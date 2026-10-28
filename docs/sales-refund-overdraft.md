# Sales refund overdraft and debt-first credit settlement

On 2026-10-29 the operator authorized negative balances for sales cash refunds from `مرتجعات البيع`. Cash sales still deposit into `إيراد الوردية`, which is not touched by return posting. The refund store passes an explicit internal capability to the shared treasury writer. The writer verifies the mapped sales-returns account, withdrawal type and refund operation-key namespace before enabling checked signed arithmetic. Ordinary withdrawals still reject overdraft, including manually submitted withdrawals from the returns account. Deposits may partially replenish a negative refund balance.

`SalesRefundPolicy.CapDebtReduction` implements credit-return settlement in Application: reduce current customer debt first, by at most the return total; refund the exact remainder in cash. Existing invoice-linked payments do not take priority over remaining debt. This supports payments recorded on the customer account without an invoice link and prevents negative customer debt. Cash sale return limits, quantity/serial/FIFO checks and actor-bound idempotency are unchanged. Return, payment, customer balance/ledger, stock and treasury changes remain one transaction with full rollback.

## Migration 031

The old `TreasuryMovements.BalanceAfterScaled >= 0` constraint cannot be changed with SQLite ADD COLUMN. New embedded migration `031_sales_refund_overdraft.sql` uses the standard table-rebuild procedure: copy every ledger row/ID/value to a replacement table, replace the table, recreate the account index and immutable update/delete triggers, and restrict negative post-balances with an insert trigger. Negative rows are allowed only for the mapped sales-returns account, with the sales-refund withdrawal namespace or a replenishing deposit. Positive amount and exact piastre constraints remain.

`Migrator.ApplyRefundOverdraft` disables FK enforcement on its migration connection before beginning this transaction, runs the rebuild and `PRAGMA foreign_key_check`, records the migration and commits. On any failure the entire rebuild/metadata rolls back. FK enforcement is restored in `finally` outside the transaction. All existing movement IDs, post-balances, audit and source links remain unchanged; the old applied migration is not edited. No operator data is reset or backfilled.

## Validation

Changed files: Treasury Application rules; new Sales settlement policy; shared treasury/return and sales-return writers; migration 031 and its migrator branch; returns-page explanation; integration/unit/frontend tests and existing migration fixtures/counts; linked architecture/API/database/design/testing docs.

`RefundOverdraftMigrationTests` builds populated pre-031 databases with real account/deposit/expense/audit/source links, verifies exact preservation and repeated startup, injects a late migration failure and verifies full old-schema rollback before retry. It checks FK integrity, immutable history and rejection of unrelated negative postings. Returns tests cover negative refunds from zero, partial replenishment, replay, late rollback, unchanged shift revenue, credit debt below/equal/above the return amount, general-account collections, debt-first priority even with invoice payments, and no negative customer debt. Unit tests cover exact residual allocation and signed integer underflow protection. Existing ordinary cashbox/bank, purchase, supplier and treasury rules remain in the full suite.

Commands: `dotnet test AlKhayal.slnx --no-restore -m:1 --logger 'console;verbosity=minimal'`, `node frontend/tests/run-tests.js`, `git diff --check`, and isolated-output desktop build `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj --no-restore -m:1 -p:OutDir=D:/k/AlKhayal/.tmp/returns-validation/`. Tests use disposable files; the operator database was not modified. No dependency or runtime target changed; native visual/DPI and Windows 7/Chromium 109 runtime qualification remain unperformed.

Close the running desktop/server and run `dotnet run --project src\AlKhayal.UI` to copy current binaries and apply migration 031 during normal startup.

Final verification on 2026-10-29: all 200 integration and 52 unit tests passed, all frontend tests passed for 102 routes, the isolated desktop build succeeded with zero warnings/errors, and `git diff --check` passed. NU1900 reports unavailable NuGet vulnerability metadata. No operator database was opened or modified by the tests.
