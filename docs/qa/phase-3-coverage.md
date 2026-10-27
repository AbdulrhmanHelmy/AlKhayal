# QA Batch 4 — Phase 3 coverage (QA-040..QA-055, customers/suppliers)

Date: 2026-10-26 (UTC). Scope confirmed by the QA owner: phase 3 = customers (QA-040..QA-047)
and suppliers (QA-048..QA-055), per the `scaffoldRanges.phase3` convention already recorded
in `docs/qa/scenario-manifest.json`.

This batch only updated QA-040..QA-055 in the manifest (titles, `tests`, `mapped`, `partial`,
`manualRemainder`, `notes`), added one focused edge test, and produces this document.
No runner or production code was changed; no commits were made; the operator database
(`%LOCALAPPDATA%\AlKhayal\alkhayal.db`) and the repository `seeded-database.db` were never
opened, and pre-existing working-tree changes were left untouched.

## Mapping decisions

Every mapped scenario cites an existing direct test (exact emitter FQN verified); no
duplicate test was created for coverage that already exists. Where a mapped FQN is a
shared/multi-domain test, the scenario notes call out the exact assertions that constitute
the phase-3 evidence.

Selectors (all verified by execution in this batch):

- `AlKhayal.IntegrationTests.CustomerPersistenceTests.*`
- `AlKhayal.IntegrationTests.CustomerLedgerMigrationPreflightTests.*`
- `AlKhayal.IntegrationTests.QaPhase3EdgeTests.*` (new file, one Fact)
- `AlKhayal.IntegrationTests.InventoryPersistenceTests.Realistic_disposable_financial_dataset_reconciles_account_boundaries_and_preserves_fifo_residuals`
- `AlKhayal.IntegrationTests.SupplierPersistenceTests.*`
- `AlKhayal.IntegrationTests.SupplierLedgerMigrationTests.*`
- `AlKhayal.IntegrationTests.SupplierRestartApiTests.*`

Selector correction (important for later batches): the purchasing test file
`tests/AlKhayal.IntegrationTests/PurchasingPersistenceTests.cs` declares its tests inside
`public sealed partial class InventoryPersistenceTests`. The first Phase 3 run therefore
reported QA-044 FAIL ("mapped test absent from run results") because the source file name
suggests a `PurchasingPersistenceTests` class, but xUnit emits
`AlKhayal.IntegrationTests.InventoryPersistenceTests.Realistic_...`. The manifest was
corrected to the emitter FQN and QA-044 was re-run individually and passed.

## Scenario-by-scenario results

Execution evidence (runner `scripts/run-acceptance-tests.ps1`, exact-FQN filtered runs,
TRX in the runner's temp results directory):

1. `-Scenario QA-045`: totals PASS=1 FAIL=0 SKIP=99 BLOCKED=0, exit 0.
2. `-Phase 3` (after the initial mapping): integration-filtered run `total=15 passed=15
   failed=0`, but QA-044 was attributed FAIL because of the selector mismatch above;
   totals PASS=14 FAIL=1 SKIP=84 BLOCKED=1, exit 1.
3. `-Scenario QA-044` (after the manifest correction): totals PASS=1 FAIL=0 SKIP=99
   BLOCKED=0, exit 0.

No phase-wide rerun was repeated after the correction; the other 14 scenarios passed in
run (2), QA-044 and QA-045 each passed in their own targeted run (3)/(1).

| ID | Status (this batch) | Notes |
|----|--------------------|-------|
| QA-040 | PASS (partial) | Create/reopen persistence (balance 25.00, credit limit 500) — `CustomerPersistenceTests.Customer_address_and_fields_survive_repository_recreation`. Browser/native create screens remain manual. |
| QA-041 | PASS (partial) | Edit-ignores-balance (stays 25.00/500.00) and delete-clears-row — same shared test. Browser/native edit/delete flows remain manual. |
| QA-042 | PASS | Legacy real-typed balance 100.40 → opening `AmountScaled` 1004000, 0.10 payment → 1000, schema 20 — `CustomerLedgerMigrationPreflightTests.SQLite_numeric_real_values_at_scale_four_upgrade_without_false_failure`. |
| QA-043 | PASS | Sub-scale 1.00001 legacy balance stops the migrator with the Arabic diagnostic, original value preserved, no `CustomerTransactions` table — `CustomerLedgerMigrationPreflightTests.Genuine_sub_scale_legacy_value_stops_with_diagnostic_and_preserves_original`. |
| QA-044 | PASS (partial) | Collections/increases post exact scale-4 balances (100.50 −0.10 → 100.40; 0.00 +0.99 → 0.99) inside the shared reconciled disposable financial dataset — see selector correction above. Direct phase-3 assertions: `CustomerService.Create` + `CustomerAccountService.PostTransaction('collection'/'increase')` + exact `Customers.Balance` rows, FK clean, migrations rerun. |
| QA-045 | PASS (partial) | New focused edge test: opening transaction row, identical collection retry replays (same id, no duplicate row, balance unchanged 100.25), statement closing equals the persisted balance, delete is refused for customers with non-opening ledger rows. Browser statement screen remains manual. |
| QA-046 | PASS (partial) | Same edge test: missing code/name/phone, negative money and 1.005 credit-limit rejections persist zero rows; case-insensitive duplicate code raises the constraint the API maps to 409 `customer_conflict`; edit ignores balance/credit-limit payloads (100.50/500.00 stay). Arabic validation UX remains browser-only. |
| QA-047 | BLOCKED (unmapped) | See "Gaps documented" below. |
| QA-048 | PASS (partial) | Supplier CRUD persistence + edit-never-mutates-payable (99999 payload stays 500.00) + FK delete protection (`Supplier_fields_survive_repository_recreation_and_update_never_changes_payable`, `Supplier_with_invoices_or_payments_cannot_be_deleted`). Browser/native supplier screens remain manual. |
| QA-049 | PASS (partial) | Invoice → payable 100→500, partial (150)/full (250) payments with invoice states مسدد جزئيًا/مسدد, and an on-account payment without invoice — two direct `SupplierPersistenceTests` facts. Payment screens remain manual. |
| QA-050 | PASS (partial) | Over-payment/zero/wrong supplier/missing supplier rejections leave balances and payment rows unchanged. Browser validation messages remain manual. |
| QA-051 | PASS | Per-supplier case-insensitive duplicate invoice numbers rejected without balance change; same number allowed across suppliers; SI- auto numbering. |
| QA-052 | PASS (partial) | Statement ordering (OPEN→A→payment→B), totals 100/460/150/410, period opening 350 before 2026-10-06, closing == live payable, unknown supplier → null. Browser statement screen remains manual. |
| QA-053 | PASS (partial) | Exact 20.13 ledger payment, identical retry replays (`Replayed=true`) with no double effect, changed amount on the same key throws, legacy purchase history in the unified statement/history. Confirm-screen UX remains manual. |
| QA-054 | PASS | Legacy pre-017 history imports once (-100000/+500000/+600000) preserving exactly 100.00 payable; 39.00 mismatch stops before `SupplierLedgerEntries` exists (two `SupplierLedgerMigrationTests` facts; shared phase-4 assets, supplier-side opening-balance assertions cited). |
| QA-055 | PASS | Real OWIN supplier API restart: login cookie + `X-CSRF-Token` honored (201 posts), 15.13/5.13 payable/paid survive a full server dispose + double migration rerun + fresh `AlKhayalServer`, integrity/foreign-key checks clean (`SupplierRestartApiTests.Authenticated_supplier_list_survives_purchase_payment_and_application_restart`). |

## New test added (single focused test)

`tests/AlKhayal.IntegrationTests/QaPhase3EdgeTests.cs`
`Customer_validation_duplicate_code_and_idempotent_collections_preserve_balances_and_statement`

Justification: three customer-side phase-3 invariants had no direct test anywhere in the
suite (only indirect references), and each is a concrete financial-integrity risk because
the API maps these service behaviors to 400/409 responses:

1. `CustomerService` create/update validation (missing code/name/phone, negative money,
   >2 decimals) must refuse before any `Customers` row is written.
2. The case-insensitive unique `Customers.Code` constraint (`004_customers.sql`) is what
   surfaces as 409 `customer_conflict`; the edit path must ignore balance/credit-limit
   payloads so a stale form can never corrupt the ledger-opening balance.
3. `CustomerAccountService.PostTransaction` identical retries must replay (same transaction
   id, one `CustomerTransactions` row, unchanged balance) and the statement must close at
   exactly the persisted `Customers.Balance` (100.25).

The test uses its own disposable explicit SQLite path (`Guid` temp file, `Migrator.Run`,
pool clear + delete in `finally`) and never touches the operator database. Build and run
observed in this batch: build 0 errors/0 warnings; targeted run `Passed 1, Failed 0`.

## Gaps documented (must never be reported PASS from phase 3)

- QA-047 (BLOCKED/unmapped): `CustomersController` does implement session authentication
  and CSRF gating (verified in `src/AlKhayal.Api/Controllers/CustomersController.cs`: 401
  `unauthorized`, 403 `invalid_csrf`, 409 `customer_conflict`), but there is no automated
  API integration test for `api/customers` (no `CustomerApiTests`). Adding a second new
  test file exceeded this batch's one-test allowance. This was recorded in
  `docs/qa/testing.md`-equivalent wording here only; `docs/testing.md` was not modified.
- Changed-payload retry conflicts are FIXED for customer statement transactions as of the QA-101
  repair (docs/qa/confirmed-bugs.md CONFIRMED-002): `SqliteCustomerAccountRepository.PostTransaction`
  now replays only an identical retry (same customer, type, scale-4 integer amount, date and
  normalized notes, compared against the stored `AmountScaled` integer without floating point) and
  throws `CustomerOperationKeyConflictException` for any mismatch, which
  `CustomersController.PostTransaction` maps to HTTP 409 `operation_key_conflict` (the same contract
  as sales, purchasing, catalog and inventory). Pinned repository-level regressions:
  `QaCustomerOperationKeyTests.Customer_operation_key_reuse_with_mismatched_payload_is_conflicted`
  (6 theory cases including `changed_notes_whitespace`),
  `Customer_manual_operation_key_never_replays_payment_provenance_rows` (an actual customer-payment
  row's key never replays as a manual posting) and
  `Customer_manual_key_starting_with_cp_or_sale_prefix_replays_and_legacy_payment_row_still_conflicts`
  (no key prefix is reserved — manual `CP-…`/`sale-…` keys replay identically), plus
  `Customer_transaction_api_returns_409_operation_key_conflict_...` over the
  real OWIN API; scenario QA-101 is mapped in docs/qa/scenario-manifest.json.
- Native/browser portions remain a manual remainder for every scenario marked `partial`
  in the manifest (customer create/edit delete flows, statement/credit-payment screens,
  supplier list/payment/statement screens). "Registry verified" screen wiring in the
  frontend suite is not a claim of native verification.
- The customers API has no restart-persistence API test (the customer service-level
  restart path is covered by QA-040 repository recreation); supplier API restart is
  covered end to end by QA-055.

## Database safety

- Runner gate (each run): created a unique absolute SQLite path under the system temp
  directory, set `ALKHAYAL_DATABASE_PATH` for the runner and child test processes, and
  verified `DIFFERS_FROM_DEFAULT=True` (operator default
  `C:\Users\user\AppData\Local\AlKhayal\alkhayal.db`), `OUTSIDE_APPDATA_ALKHAYAL=True`,
  `DIFFERS_FROM_SEEDED_DATABASE=True`, non-pre-existing path. Printed effective paths:
  `...\Temp\alkhayal-qa-6c319bbbaf114adf844116e4d5b04456\alkhayal-qa.db` (QA-045 run),
  `...\Temp\alkhayal-qa-48ad48351730438aa09be9b5d99c633a\alkhayal-qa.db` (phase run),
  `...\Temp\alkhayal-qa-2784875820954226b5594fff54b34fb4\alkhayal-qa.db` (QA-044 rerun).
  For the targeted build/run of the new test, the batch additionally set and printed
  `C:\Users\user\AppData\Local\Temp\alkhayal-qa-batch4-phase3-<guid>.db` paths and verified
  they differ from the operator default and seeded database and did not exist beforehand.
- The manifest remains valid JSON with 100 unique scenario IDs (verified by a
  `ConvertFrom-Json` pass: 100 scenarios, 100 unique, no duplicates).
