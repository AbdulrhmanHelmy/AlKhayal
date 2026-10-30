# Sales return `sales_conflict` hotfix — 2026-10-30

Status: **FIXED AND VERIFIED**. Reproduced before the correction; focused and complete regressions passed afterward. This is a posting-writer correction after Phase 9, not a reporting formula change.

## Reproduction and preserved before-fix evidence

All runs use disposable SQLite files created by `NetProfitReportTests.Fixture` / `TreasuryPersistenceTests.Fixture`, migrated through the existing inventory (001–033, 071–075). No migration or production database access is needed.

1. Create a real catalog stock product priced at EGP 10, a customer with zero opening debt, and receipt layers (1 unit at cost 2, then 2 units at cost 3).
2. Through `SalesService.CreateDraftAsync` and `PostAsync`, post 3 units as credit with **fixed whole-line** discount 0.02 and invoice discount 0.01. These are monetary discounts, not per-unit discounts. Saved subtotal after line discount is 29.98; saved eligible gross is 29.97 = **299700 scale-4 units**, zero tax.
3. Call `SalesService.ReturnAsync` with original line ID, the listed quantities, and a distinct operation key for each step. Retry every successful step with its original key and identical request.
4. Compare the same workflows using a cash sale. Also test a single full credit return.

Executed before changing production code: `reproduction-before.trx` and `reproduction-constraint-before.trx` in `artifacts/sales-return-hotfix/results/`. Each run executed **8 cases: 3 failed, 5 passed, 0 skipped**. TRX output preserves the exact step results and balance diagnostics. `artifacts/sales-return-hotfix/SqliteSalesStore.Returns.before.cs` preserves the pre-hotfix writer including existing user modifications.

| Original mode | Sequence | Actual before-fix result |
| --- | --- | --- |
| Credit | 1 + 1 + 1 | First two 99900 debt reductions succeeded; third 99900 failed `sales_conflict` |
| Credit | 2 + 1 | First 199800 debt reduction succeeded; final 99900 failed `sales_conflict` |
| Credit | 1 + 2 | First 99900 debt reduction succeeded; final 199800 failed `sales_conflict` |
| Credit | 3 | Single 299700 debt reduction succeeded |
| Cash | All four sequences above | Every step succeeded, with matching persisted cash refunds |

For 1+1+1, the balance before the last request prints as `9.989999999999997`. The exact scale-4 balance read is still 99900. Running the old subtraction in a rolled-back diagnostic transaction gives **-3.552713678800501e-15** and SQLite explicitly reports `CHECK constraint failed: Balance >= 0`. For 2+1 the equivalent residue is -1.77635683940025e-15. No failure is inferred merely from the public error code.

## Exact root cause and production correction

`Customers.Balance` is a legacy SQLite **NUMERIC** projection with `CHECK (Balance >= 0)` (`004_customers.sql`). NUMERIC affinity stores nonintegral amounts as binary REAL; it does not implement decimal arithmetic. The return writer already reads debt as an integer with `CAST(ROUND(Balance*10000) AS INTEGER)` and uses Application `SalesRefundPolicy.CapDebtReduction` to select the exact ledger reduction. It then used a different arithmetic path to persist the result: `Balance=Balance-@Amount`.

Repeated subtraction of 9.99/19.98 produces a tiny negative REAL residue on the final settlement, violating the nonnegative-balance constraint. The catch for SQLite `Constraint` maps that real constraint violation to `sales_conflict`. It is a **false business conflict caused by projection arithmetic**, not over-return, duplicate posting, discount/tax allocation drift, FIFO failure, or an optimistic invoice-version rejection. Cash refunds bypass this customer projection update, which explains the observed difference.

Changed production file: `src/AlKhayal.Data/Sales/SqliteSalesStore.Returns.cs` only. Calculate `balanceAfterCredit = checked(balance - credit)` from the already-read scale-4 debt and exact settlement integers. Bind `balanceAfterCredit / 10000m` as the replacement projection value in the same transaction. The final reduction writes exact zero; intermediate projections no longer accumulate SQL subtraction residues. No epsilon, `MAX(0,...)` clipping, new rounding policy, report-helper reuse, relaxed constraint, schema change, or historical rewrite is introduced. The existing scale-4 projection read remains unchanged.

The writer's electronic-sale rejection was already present before this task and is preserved. Other pre-existing working-tree changes are outside this hotfix.

## Accounting and integrity

- Posted sale price, quantity, discounts, tax, status, version and original costing facts remain unchanged. Existing posted returns remain append-only. A regression seeds the exact old REAL projection residue after two historical partials and proves the remaining quantity can finish without rewriting either prior return.
- Return gross allocation remains the existing contract: nonfinal quantities receive proportional saved line gross floored to piastres; the final eligible quantity receives **saved original gross minus all actual previously returned amounts**. For 29.97/3, each unit receives 9.99. With invoice-only 0.01 discount, the returns are 9.99, 9.99 and 10.01. Quantity and saved remaining amount are still authoritative. No discounts are reapplied from current configuration.
- Return headers/lines do not independently save tax/discount components. Reports continue their approved cumulative scale-4 allocation from original snapshots and actual saved gross; final components reconcile through the existing `ReturnTaxAllocation` tests. Inclusive/exclusive tax, changed current tax settings and catalog prices, mixed taxable/exempt saved snapshots, and event dates remain covered. Mixed tax snapshots use the existing explicit legacy fixture; this task introduces no mixed-tax editor.
- Credit returns follow **existing current-customer-debt-first policy**, including debt/collections outside the original invoice. Credit reduction is capped by current debt, with a cash refund for the remainder through the sales-returns cashbox. A prior 5.00 cash collection on the example gives cumulative credit reduction **24.97** and cash refund **5.00**, never a negative customer balance. Another invoice's legitimate 30.00 debt survives a full multiline return. There is no new overpayment liability or invented refund mode.
- Pure debt adjustments create no physical-cash movement. Cash refunds retain their separate sales-return treasury source; original shift-revenue receipt sources are not rewritten. Existing sales-return cashbox overdraft policy remains intact. Electronic wallet/card **sale** returns remain `electronic_reversal_unsupported`; no undocumented electronic reversal is introduced. Legacy invoice collections retain their existing payment/treasury behavior.
- Original issued FIFO allocations restore exact saved quantities/costs. The two-layer example restores 3 units and cost **8.0000**, independent of current purchase/sale prices. Replay leaves layer, stock movement, return, ledger and treasury snapshots identical; no duplicate restoration is created. Existing cross-layer/final-residual and transferred-stock tests run in the full suite.
- All effects remain in the existing SQLite writer transaction. A test forces a late treasury INSERT constraint, after inventory, return and customer effects have been attempted. The complete relevant state snapshot remains unchanged, the failed operation produces no profit event, and dropping the test-only fault permits retry with the same operation key.
- Operation-key lookup/hash checks, quantity limits, lifecycle checks, SQLite busy/locked handling and constraint handling remain intact. Two competing 2-unit requests for a 3-unit sale produce exactly one success and one quantity rejection (a busy result is retried with its same key). A changed request under the successful key still returns `operation_key_conflict`. Returns have no caller-supplied expected-version field: stale return requests are revalidated against current quantities inside the existing transaction; draft/post version handling is untouched.

## Coverage and compatibility limits

New tests: `SalesReturnConflictTests.cs` (20 cases) and `SalesReturnConflictApiTests.cs` (2 cases, in the existing HTTP test partial class). They cover all four credit/cash sequences, each successful request's replay, over-return, no/line-only/invoice-only/both discounts, minimum accepted 0.01 discounts and final residuals, inclusive/exclusive tax, changed price/tax configuration, prior cash collection, exact FIFO restoration, ledger/projection reconciliation, immutable originals/history, concurrent/stale quantity, changed-key rejection, late rollback, multiline credit and other debt, zero net profit/product profit after full returns, and HTTP posting through **both** `/api/sales/{id}/returns` and `/api/returns`.

There is no caller-specified refund amount to overstate: refund amount is derived from original saved gross and validated quantity. Over-quantity tests therefore exercise the supported excessive-refund request. User discount inputs are restricted to two decimals; sub-piastre input discounts are unsupported, not silently rounded. Internal money remains scale 4. Existing small-discount scenarios exercise residual allocation without inventing unsupported precision.

Full regression includes `SalesPersistenceTests`, customer ledger/idempotency tests, `InventoryFifoTests`, `PricingFifoInvariantTests`, `SalesReportTests`, `TransactionReportTests` (returns/payment methods/historical dates/no multiplied headers), `NetProfitReportTests`, `StockReportTests` (Product Profit), and `OperationalReportTests` (customer receivables/cashier shifts). Existing mixed-tax, event-period, FIFO and report permissions/HTTP tests remain unchanged. No report query or frontend code changed; existing Arabic errors, retry identity, RTL and theme behavior are retained.

## Executed validation

Run from the repository root, using isolated artifacts and unique TRX names:

```powershell
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/sales-return-hotfix --filter FullyQualifiedName~SalesReturnConflictTests --logger 'trx;LogFileName=reproduction-before.trx' --results-directory artifacts/sales-return-hotfix/results -m:1
# Second pre-fix run adds direct constraint diagnostics; production writer still unchanged:
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/sales-return-hotfix --no-restore --filter FullyQualifiedName~SalesReturnConflictTests --logger 'trx;LogFileName=reproduction-constraint-before.trx' --results-directory artifacts/sales-return-hotfix/results -m:1
# After production correction and regression expansion:
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/sales-return-hotfix --no-restore --filter 'FullyQualifiedName~SalesReturnConflictTests|FullyQualifiedName~Discounted_credit_returns_HTTP' --logger 'trx;LogFileName=focused-final.trx' --results-directory artifacts/sales-return-hotfix/results -m:1
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/sales-return-hotfix --no-build --no-restore --logger 'trx;LogFileName=integration-full.trx' --results-directory artifacts/sales-return-hotfix/results -m:1
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/sales-return-hotfix --no-build --no-restore --logger 'trx;LogFileName=unit-full.trx' --results-directory artifacts/sales-return-hotfix/results -m:1
node frontend/tests/run-tests.js
$syntaxFiles = @(Get-ChildItem frontend -Recurse -Filter *.js)
foreach ($syntaxFile in $syntaxFiles) { node --check $syntaxFile.FullName; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
dotnet build AlKhayal.slnx --artifacts-path artifacts/sales-return-hotfix-solution -m:1
git diff --check
```

| Check | Actual result |
| --- | --- |
| Before-fix reproduction (each of two runs) | 8 executed: 3 failed / 5 passed / 0 skipped |
| Final focused SQLite + real HTTP | 22 passed / 0 failed / 0 skipped (`focused-final.trx`) |
| Full integration | 348 passed / 0 failed / 0 skipped (`integration-full.trx`, 3m47s) |
| Full unit | 150 passed / 0 failed / 0 skipped (`unit-full.trx`) |
| Frontend executable suite | Passed, 126 registered screens |
| JavaScript syntax | Passed, 52 files (all frontend JS, including tests) |
| Final solution build | Passed, 0 warnings / 0 errors (`artifacts/sales-return-hotfix-solution`) |
| Diff whitespace | Passed; pre-existing LF/CRLF notices in confirmed-bugs and acceptance script |

Intermediate expanded tests exposed a test-only incorrect treasury table name and duplicate product/unit fixture lines; both were corrected to the actual schema/contracts before the final 22-case passing run. One earlier solution build overlapped an active test in the same artifact directory and emitted MSB3026 retry for the testhost-held xUnit assembly; final validation builds in a separate `sales-return-hotfix-solution` artifact directory. These intermediate attempts are not counted as final passing validation.

Compatibility: installed Windows / .NET Framework 4.8, existing SQLite provider and Chromium-109-compatible frontend. No running desktop host is stopped. Native visual/hardware validation, Windows 7/legacy WebView2 runtime qualification, low-resource measurements and Phase 10 packaging remain separate unverified work. This resolves the reproduced discounted-credit-return release blocker under the passing automated regression; it does not establish general packaging readiness or repair unrelated legacy projection writers.

No delegation, OpenCode/Gemini/GLM, commit, push, branch creation/switch, merge or reset. Existing work preserved. Normal `%LOCALAPPDATA%\AlKhayal\alkhayal.db` untouched; tests only use their own disposable databases.
