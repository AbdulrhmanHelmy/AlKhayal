# Maintenance advance posting hotfix

## Reproduction and root cause

The exact reported test was reproduced before editing. It failed with `CHECK constraint failed: Balance >= 0` in `SqliteMaintenanceRepository.Create`, original line 40.

The constraint belongs to **Customers**, defined by migration `004_customers.sql`:

```sql
Balance NUMERIC NOT NULL DEFAULT 0 CHECK (Balance >= 0)
```

The failing SQL was:

```sql
UPDATE Customers SET Balance=@balance,UpdatedAt=@now WHERE Id=@CustomerId
```

Intake read the customer transaction balance (5 EGP = 50,000 scale-4 units), recorded a 100 EGP advance (1,000,000 units) as a customer debt collection before charging any maintenance service, and attempted to write `(50000 - 1000000) / 10000 = -95` EGP to `Customers.Balance`.

This was incorrect advance-payment handling, not missing funds, a missing opening balance, duplicate posting or a schema migration defect. The customer opening debt really was 5 EGP. The advance belongs to the uncharged maintenance ticket and cannot collect 100 EGP of that 5 EGP debt. The constraint correctly rejected the write and the transaction rolled back. Real intake operations with debt below the advance were affected; this was not limited to the test fixture.

The original test's interim `CurrentBalance == -95` assertion contradicted the established nonnegative customer-debt model. Changing only that assertion would not fix the exception. The production posting sequence was corrected first. The final credit-delivery expectation remains **505 EGP**, unchanged.

## Minimal correction

Only `src/AlKhayal.Data/Maintenance/SqliteMaintenanceRepository.cs` changes runtime behavior:

1. Intake persists the ticket, initial status and immutable advance receipt in one transaction. It no longer inserts a customer collection or changes customer debt before a service charge exists.
2. Delivery keeps its readiness and `finalCost >= advance` validation. It posts the full actual service charge and applies the recorded advance once, together with any remaining cash payment, customer debt projection and delivery status/history.
3. New advance application appears on the delivery date in the customer statement, after the charge. The original receipt retains the actual intake date and amount. No estimated charge, artificial opening balance or negative customer balance is introduced.
4. A valid advance collection already posted by the older implementation is reused unchanged. The lookup uses its existing `maintenance:{ticketId}:advance` key and validates customer, collection type and amount. Delivery does not collect that advance again or rewrite its original hash/date.
5. A mismatched existing advance or inconsistent receipt rejects delivery before charge/payment writes. No exception is hidden and no balance is clamped.

For new tickets, cash delivery records only `final cost - advance` as additional cash and leaves unrelated customer debt unchanged. Credit delivery adds only that remainder to customer debt. A fully prepaid ticket creates no additional cash receipt and adds no debt.

For legacy tickets, the already-applied advance is included in the current ledger balance; delivery adds the charge and remaining cash collection without subtracting the advance a second time. This works for cash and credit without changing historical entries.

Existing operation keys and the delivered-ticket replay check remain in place. All writes remain in the original SQLite transaction. No schema migration or CHECK/immutability-trigger change is needed.

## Preserved boundaries

Maintenance receipts remain local receipt evidence. This maintenance workflow did not post to a treasury account before the fix and still does not. No treasury funds are invented, withdrawn or deposited; no supplier transactions are created. Tests assert both boundaries. Payment Methods, Phase 7, inventory, customer collection policy and unrelated Phase 8 implementation were not changed by this hotfix.

The customer statement no longer shows a pending maintenance advance as a reduction of existing debt. The pending advance remains visible on the saved ticket. Once delivered, the statement shows the final service charge and its advance/cash settlement. Existing already-posted legacy statement entries remain readable.

## Regression coverage

`MaintenancePersistenceTests` now contains 11 executed cases:

- Original intake/status/credit delivery followed by cash delivery, statement projection and injected rollback scenario; only the disproved interim negative-debt assertion changes.
- Zero-debt and 5 EGP-debt customers with a 100 EGP advance, for both cash and credit delivery.
- Full prepayment for cash and credit, without another cash receipt.
- Legacy advance collections for cash and credit, retaining the old hash and avoiding a second collection.
- Mismatched legacy advance rejection without charge or delivery effects.
- Failed intake receipt insertion and late delivery-status failure, verifying rollback of ticket/history/receipt/ledger/balance effects and successful same-key retries.

The tests also exercise intake replay, changed-payload key conflicts, delivery replay/conflicts, delivery before readiness, final cost below the advance, rejection of collecting pending advances against unrelated debt, exact saved balances, receipt counts, zero treasury/supplier events, integrity and foreign-key checks.

All automated tests use explicit temporary SQLite paths via the existing disposable fixture. The operator database `%LOCALAPPDATA%\AlKhayal\alkhayal.db` was not modified, reset, deleted or reseeded.

## Validation commands and evidence

```powershell
dotnet test tests/AlKhayal.IntegrationTests --no-restore -m:1 -p:OutDir=D:\projects\AlKhayal\artifacts\maintenance-hotfix\ --filter FullyQualifiedName~MaintenancePersistenceTests.Intake_advance_status_and_cash_or_credit_delivery_persist_atomically_and_project_to_statement --logger 'trx;LogFileName=maintenance-reproduction.trx'
dotnet test tests/AlKhayal.IntegrationTests --no-restore -m:1 -p:OutDir=D:\projects\AlKhayal\artifacts\maintenance-hotfix\ --filter FullyQualifiedName~MaintenancePersistenceTests --logger 'trx;LogFileName=maintenance-focused-final.trx'
dotnet test tests/AlKhayal.IntegrationTests --no-build --no-restore -p:OutDir=D:\projects\AlKhayal\artifacts\maintenance-hotfix\ --logger 'trx;LogFileName=maintenance-full-integration.trx'
dotnet test tests/AlKhayal.UnitTests --no-restore -m:1 -p:OutDir=D:\projects\AlKhayal\artifacts\maintenance-hotfix\ --logger 'trx;LogFileName=maintenance-unit.trx'
node frontend/tests/run-tests.js
dotnet build AlKhayal.slnx --no-restore -m:1 -p:OutDir=D:\projects\AlKhayal\artifacts\maintenance-hotfix\
git diff --check
```

Reproduction: **1 failed, 0 passed**, with the exact reported SQLite constraint error. Focused maintenance tests after the fix: **11 passed, 0 failed**. Final validation:

| Check | Result |
| --- | --- |
| Focused maintenance class | 11 passed, 0 failed |
| Complete integration suite | 250 passed, 0 failed, 0 skipped |
| All unit tests | 105 passed, 0 failed |
| Frontend executable suite | Passed, all 108 registered routes and payment/printing regressions |
| Final complete solution build | Passed, 0 warnings and 0 errors |
| `git diff --check` | Passed; only existing Git LF/CRLF notices for preserved QA files |

TRX evidence: `tests/AlKhayal.IntegrationTests/TestResults/maintenance-reproduction.trx`, `maintenance-focused-final.trx`, `maintenance-full-integration.trx` in the same directory, and `tests/AlKhayal.UnitTests/TestResults/maintenance-unit.trx`. Build/test output is isolated under `artifacts/maintenance-hotfix/`.

## Changed files and limits

- `src/AlKhayal.Data/Maintenance/SqliteMaintenanceRepository.cs`: correct advance posting time and legacy replay validation.
- `tests/AlKhayal.IntegrationTests/MaintenancePersistenceTests.cs`: corrected interim accounting assertion plus focused financial/rollback/retry coverage.
- `docs/maintenance.md`, `docs/api-contracts.md`, `docs/architecture.md`, `docs/database.md`, `docs/testing.md`, and this report: document the corrected statement timing and actual validation.

No new banking, customer-credit-wallet, maintenance refund or other development phase is introduced. Existing inconsistent legacy advances require review and are rejected rather than silently repaired. Native UI/printer and Windows 7 runtime acceptance were not performed for this backend-only posting correction.

No delegation, commit, push, merge or branch switch occurred. Existing local work was preserved. No running desktop process was stopped.
