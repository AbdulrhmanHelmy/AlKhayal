# Payment methods: wallets and card terminals

Implemented directly in Codex, without delegation, commits, pushes or branch changes. This is local sales-payment record keeping. No gateway, provider connection, independent financial movements, banking ledger, settlement, fees, balances, branches or synchronization were added.

## Business rules and compatibility

- Immediate checkout selects exactly one of كاش, محفظة إلكترونية or فيزا. The existing آجل choice remains separate.
- Wallet/card selection requires one active instrument of the matching type. Cash/credit cannot carry an instrument. Validation occurs in Application, at draft persistence, and again inside the posting transaction.
- Posting records the persisted server-calculated final `TotalScaled`, after discounts and tax. Client totals are ignored. Explicit payment amounts and allocation arrays in the draft API are rejected. Additional collections remain possible only through the existing credit-sale workflow.
- Existing zero-value-sale rejection remains unchanged. The current checkout has no cash-tender/change input; no tender amount or change is added to revenue.
- Credit posting creates no immediate payment. Historical payments, partial collections, amounts and operation identities are unchanged. No historical instrument IDs are backfilled.
- No separate browser write follows posting. The saved selection, existing payment, invoice paid projection, FIFO, stock issue, treasury effect when applicable, and audit commit through the existing sales transaction. A late failure rolls back posting; the editable draft/selection remains available for retry.
- Instrument selection is part of the posting request hash for electronic sales. Legacy cash/credit hashes remain unchanged. Retry returns the existing invoice/payment without adding history. The frontend also checks saved status before resubmitting a draft after an uncertain posting response.

### Existing SQL classification

An additive selection table avoids rebuilding immutable financial tables and their CHECK constraints. `SalesInvoices.PaymentMode` retains its legacy values: `cash` means immediate settlement in this compatibility contract, and `credit` means deferred settlement. Consumers must use `immediateMethod` to distinguish cash, wallet and card.

`SalesPayments.EventType='cash_sale'` remains the existing immediate-sale event discriminator. Its `PaymentMethod` is `cash` for physical cash, `card` for terminals, and legacy-compatible `transfer` for wallets. `SalePaymentSelections.Method='wallet'` distinguishes a configured wallet from an old unassigned transfer. Payment history resolves these together; it never infers an instrument for historical transfers. Do not derive physical drawer cash from the event discriminator or the old invoice mode alone.

## Schema and migration

New additive migration: **074_payment_instruments.sql**. The inspected tree ended at 073; the migration set now contains 37 embedded scripts. Existing migrations were not edited.

| Table | Purpose |
| --- | --- |
| `PaymentInstruments` | ID; immutable type (`wallet`/`card`); name; string number; provider; notes; active flag; optimistic version; UTC creation/update timestamps; normalized identity |
| `SalePaymentSelections` | One row per invoice: method and nullable instrument FK; editable only while the invoice is a draft |
| `PaymentInstrumentAudit` | Append-only snapshot of each configuration save, actor, version and UTC timestamp |

There is no new money ledger or duplicate invoice-total column. Existing `SalesPayments.AmountScaled` and `SalesInvoices.TotalScaled` remain authoritative, using scale-4 signed integers and the existing piastre rounding policy. Referenced instruments cannot be deleted; no delete endpoint is provided.

Active identity uniqueness is `(type, normalized provider, normalized number)`. Normalization uses Unicode Form KC, invariant uppercase and removal of whitespace. The provider is length-prefixed to prevent ambiguous concatenation. Leading zeros are retained; numbers never pass through numeric conversion. Different providers may reuse a number; inactive instruments may share an identity, but reactivation fails if an active duplicate exists. Edits cannot change instrument type. Required name/number and field lengths are validated in Application; audit and configuration save are transactional.

Indexes cover active identities, type/status, and instrument-to-invoice lookups. Existing invoice date/status and payment-to-invoice indexes are reused. Instrument history pushes its parameterized instrument filter into the source query. SQL performs sums/counts; only the requested history page is materialized. Count, sum and page share a read transaction.

## Management, history and overview

The administrator sidebar section **وسائل الدفع** contains:

| Route | Screen |
| --- | --- |
| `payment-overview` | نظرة عامة |
| `payment-wallets` | المحافظ الإلكترونية |
| `payment-terminals` | ماكينات الفيزا |
| `payment-history` | سجل العمليات |

All four resolve to the `payment-methods` renderer. These are explicit administrator-only operational navigation entries requested for this module, without duplicating Settings hub links. They use the canonical registry, search, workspace tabs and SVG sidebar rail.

Wallets/terminals have add/edit/status dialogs, inline validation, version conflict detection, duplicate-save protection, initial focus and shared modal close/scroll/keyboard handling. Directory search stays mounted and filters name, number and provider; status filtering and 20-row pagination are provided. Inactive instruments remain in management and saved invoice details.

Details show name, number, provider, status, notes, linked-invoice count, recorded-payment sum and invoice history. History initially covers all dates. Filters support date range, invoice number, cashier name (and cashier ID through the API), and invoice lifecycle status. Totals and counts reflect the selected filters. Pages have 20 rows, with a server maximum of 100. Each row shows reference/date, final invoice amount, recorded amount, resolved method/instrument, cashier, invoice status and current payment status. Clicking a reference opens the existing Sales editor/details using `saleInvoiceId` route state. Request-generation checks prevent stale responses repainting another route or instrument.

History is a read projection of existing posted sales/payment records, not manually created transactions. Legacy unassigned payments remain visible in the general history. Renaming an instrument displays its current saved name without changing its ID or recorded amount; earlier configuration names remain in the append-only audit. Deactivation does not hide historical payments.

The compact overview shows active wallet/terminal counts and recorded invoice payments for cash, wallets and cards. General method totals include existing invoice collections of that method; instrument totals include only attributed immediate-sale payments. **These sums are recorded invoice payments, not wallet account balances, bank balances or settlement balances.** Refund events are not silently netted against original payments.

## Standard Sales and Touch POS

Both screens use the existing shared `sales.js` renderer. Cash, wallet and Visa choices appear alongside credit. Wallet/card reveal a compact active-instrument selector; no checkout configuration action is exposed. Empty/loading-failure conditions explain the absence of available instruments in Arabic. Changing the method clears the previous instrument and updates only the selector region, retaining the mounted barcode/search input. Busy guards prevent repeated checkout submissions. The server rechecks activity if configuration changed after options were loaded.

Layout uses the shared RTL cards, forms, tables and theme tokens. The cart header wraps to accommodate the selector, without adding a viewport owner or scale transform. The new code uses APIs available in Chromium 109.

## HTTP contracts and permissions

All endpoints use the existing envelope, authenticated session, same-origin requests and origin middleware. Configuration writes require the existing CSRF token and administrator role. No unrelated optional module must be enabled.

| Endpoint | Access and behavior |
| --- | --- |
| `GET /api/payment-methods/active` | Administrator or existing effective Sales/POS screen access; returns active ID/type/name/string number/provider only |
| `GET /api/payment-methods/instruments?type=wallet` | Administrator directory; optional `wallet`/`card` type |
| `POST /api/payment-methods/instruments` | Administrator and CSRF; create instrument |
| `PUT /api/payment-methods/instruments/{id}` | Administrator and CSRF; edit/status with current version |
| `GET /api/payment-methods/history` | Administrator; `instrumentId`, `cashierId`, `cashierName`, `from`, `to`, `invoiceNumber`, `status`, `page`, `pageSize` |
| `GET /api/payment-methods/overview` | Administrator aggregates |

Instrument bodies contain `type`, `name`, `number`, optional `provider`/`notes`, `isActive`, and `version` for edits. IDs and timestamps are server-managed. They contain no PIN, OTP, passwords, card number, CVV or track data. Cashiers cannot read the management/history/overview endpoints or mutate configuration even with a valid CSRF token; direct routes are also gated by administrator-only registry metadata.

The existing sales draft create/update contract adds:

```json
{"paymentMode":"cash","immediateMethod":"wallet","paymentInstrumentId":12}
```

Use `immediateMethod:"card"` for terminals. Cash uses `cash` and a null instrument; credit uses `paymentMode:"credit"`, `immediateMethod:"credit"`, a valid customer and a null instrument. Omission of the new method preserves legacy cash/credit callers. Do not send `amount`, `paymentAmount` or `payments` to draft create/update. Existing credit collection endpoints and historical allocations are unchanged. Invoice responses add `immediateMethod`, `paymentInstrumentId` and the resolved server-side instrument name. Posting still uses the original `/api/sales/{id}/post` and operation key.

## Returns and shifts

Original cash/credit returns retain their existing FIFO/customer/treasury behavior. Electronic-sale returns fail with `electronic_reversal_unsupported` before stock/payment writes because no traceable electronic reversal exists. Existing paid-sale cancellation restrictions remain in force. No wallet withdrawal, terminal refund or bank reversal workflow was introduced. Original payments remain intact; history reports existing lifecycle status.

Cash checkout retains the existing treasury deposit and `cash_sale` shift event. Wallet/card checkout creates neither a treasury cash deposit nor a physical cash shift event. Credit retains its existing informational shift event and customer debt. Tests open a real shift, post each electronic method and verify the expected drawer balance remains at its opening amount, with no extra shift event, customer transaction or treasury receipt. Existing shift reconciliation tests run in the regression suite. The optional shift sales count continues to count its existing cash/credit events; electronic sales are visible in the payment module, not added to that legacy count.

## Validation and actual results

All test databases are explicit disposable paths under the temporary directory. The normal `%LOCALAPPDATA%\AlKhayal\alkhayal.db` was not modified by tests. No running desktop process was stopped.

Commands (isolated output avoids desktop DLL locks):

```powershell
dotnet build AlKhayal.slnx --no-restore -m:1 -p:OutDir=D:\projects\AlKhayal\artifacts\payment-build\
dotnet test AlKhayal.slnx --no-restore -m:1 -p:OutDir=D:\projects\AlKhayal\artifacts\payment-build\ --logger 'trx;LogFileName=payment-full-final.trx'
dotnet test tests/AlKhayal.IntegrationTests --no-build --no-restore -p:OutDir=D:\projects\AlKhayal\artifacts\payment-build\ --filter 'FullyQualifiedName~PaymentInstrumentTests|FullyQualifiedName~Payment_methods_http' --logger 'trx;LogFileName=payment-focused-final.trx'
node frontend/tests/run-tests.js
Get-ChildItem frontend/js -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName }
git diff --check
```

Observed full-suite run: **105 unit tests passed; 237 integration tests passed, 1 failed (238 total)**. The sole remaining failure is `MaintenancePersistenceTests.Intake_advance_status_and_cash_or_credit_delivery_persist_atomically_and_project_to_statement`: `CHECK constraint failed: Balance >= 0` in `SqliteMaintenanceRepository.Create`, line 40. Maintenance implementation and its schema were not changed. This prevents claiming an entirely green solution test run; it is an integration blocker for a full release, not permission to change the parallel maintenance work.

The final focused payment run includes 12 persistence/API tests: both instrument types; create/edit/deactivation; normalized duplicates; leading zeros; restart; immutable audit; missing/wrong/inactive instruments; explicit partial/split rejection; tax included/excluded and discount totals; full saved amounts; authoritative HTTP total despite a forged client total; repeated post; late rollback and retry; migration of existing posted records; no legacy backfill; history count/sum/filter/pagination; rename/deactivation; shift cash exclusion; refund/cancellation restriction; and administrator/session/CSRF/effective cashier permissions.

The added 8 unit cases cover normalization, required/type validation and bounded/date-valid history requests. Frontend VM tests execute both checkout handlers and management: selectors, correct payload, empty instruments, focus-node retention, busy guard, lost-response replay, inline save errors, history navigation/filter parameters and stale responses. The full frontend suite verifies **108 routes**. These are automated DOM/contract checks, not a claim of native visual acceptance.

Migration-count assertions now derive the number of embedded SQL resources rather than retaining an obsolete hardcoded count. Legacy migration fixtures exclude the new migration; two old cutoff fixtures also exclude scripts newer than their intended snapshot so rollback counts remain meaningful. Existing reconciliation/financial assertions remain in place.

Final validation after implementation review:

| Check | Actual result |
| --- | --- |
| Full solution build, isolated output | Passed: 0 warnings, 0 errors |
| All unit tests, final binaries | Passed: 105/105 |
| Focused payment persistence/API tests, final binaries | Passed: 12/12 |
| Full integration-suite run before the two final upgrade/HTTP additions | 237 passed, 1 maintenance failure, 238 total; final additions are included in the 12 focused passing tests |
| Frontend executable suite | Passed, including payment handler tests and all 108 registered routes |
| Syntax validation of every production JavaScript file | Passed |
| `git diff --check` | Passed; Git emitted only its existing LF/CRLF notices for preserved QA files |

TRX evidence is in `tests/AlKhayal.IntegrationTests/TestResults/payment-full-final.trx`, `tests/AlKhayal.IntegrationTests/TestResults/payment-focused-final.trx` and `tests/AlKhayal.UnitTests/TestResults/payment-unit-final.trx`. These test outputs and `artifacts/payment-build/` are ignored build artifacts. No standalone native/manual acceptance is claimed.

## Remaining manual acceptance and limitations

- Native WebView2 Light/Dark, narrow-window layout, modal keyboard trapping, real scanner focus and actual printer hardware require manual verification. Existing saved-invoice printing paths and automated printing regressions are retained.
- Windows 7 SP1 / Chromium 109 execution and packaging are unverified; no compatibility guarantee is claimed beyond retaining the approved stack and API compatibility.
- No real bank/card/wallet authorization occurs. Staff must confirm external payment independently.
- Instrument directory data is small configuration data loaded once per directory; historical invoices are always paginated in SQL.
- Names in history resolve to the current instrument name; immutable configuration snapshots preserve earlier names. No historical payment is rewritten.
- Electronic refunds remain intentionally unavailable until a proper persisted reversal exists.

## Phase 8 integration and changed files

Phase 8 business modules were not modified. Shared integration points are sales contracts/posting, the controller resolver/host composition, screen registry, shell script list/icon map, shared module CSS and test registry. Check whether the other branch also claimed migration **074** before integrating; renumber this new unapplied migration if necessary. Do not renumber an already applied migration. Merge the additive route/script/composition changes rather than replacing those shared files. Preserve the `immediateMethod` distinction in future reports and shift enhancements.

Pre-existing work preserved: `docs/qa/confirmed-bugs.md`, `docs/qa/scenario-manifest.json`, `scripts/run-acceptance-tests.ps1`, untracked `docs/qa/release-readiness-audit.md` and `tests/AlKhayal.IntegrationTests/QaPhase4EdgeTests.cs`.

The implementation file manifest follows below; all paths are relative to the repository root.

```text
docs/api-contracts.md
docs/architecture.md
docs/database.md
docs/design.md
docs/payment-methods.md
docs/testing.md
frontend/css/modules.css
frontend/data/screens.json
frontend/index.html
frontend/js/app.js
frontend/js/modules/payment-methods.js
frontend/js/modules/sales.js
frontend/tests/invoice-return-badges-tests.js
frontend/tests/payment-methods-tests.js
frontend/tests/run-tests.js
src/AlKhayal.Api/ApiBootstrap.cs
src/AlKhayal.Api/Controllers/PaymentMethodsController.cs
src/AlKhayal.Api/Controllers/SalesController.cs
src/AlKhayal.Application/Payments/PaymentInstrumentService.cs
src/AlKhayal.Application/Sales/SalesContracts.cs
src/AlKhayal.Application/Sales/SalesService.cs
src/AlKhayal.Data/Migrations/074_payment_instruments.sql
src/AlKhayal.Data/Payments/SqlitePaymentInstrumentStore.cs
src/AlKhayal.Data/Sales/SqliteSalesStore.Returns.cs
src/AlKhayal.Data/Sales/SqliteSalesStore.cs
src/AlKhayal.Server.Host/AlKhayalServer.cs
tests/AlKhayal.IntegrationTests/BusinessActivityPersistenceTests.cs
tests/AlKhayal.IntegrationTests/CatalogMigrationTests.cs
tests/AlKhayal.IntegrationTests/CustomerLedgerMigrationPreflightTests.cs
tests/AlKhayal.IntegrationTests/FeaturePersistenceTests.cs
tests/AlKhayal.IntegrationTests/InventoryFifoTests.cs
tests/AlKhayal.IntegrationTests/InventoryPersistenceTests.cs
tests/AlKhayal.IntegrationTests/MigrationTestExpectations.cs
tests/AlKhayal.IntegrationTests/MigrationTests.cs
tests/AlKhayal.IntegrationTests/PaymentInstrumentTests.cs
tests/AlKhayal.IntegrationTests/PaymentMethodsApiTests.cs
tests/AlKhayal.IntegrationTests/PurchasingPersistenceTests.cs
tests/AlKhayal.IntegrationTests/RefundOverdraftMigrationTests.cs
tests/AlKhayal.IntegrationTests/SalesPersistenceTests.cs
tests/AlKhayal.IntegrationTests/SettingsPersistenceTests.cs
tests/AlKhayal.IntegrationTests/SupplierLedgerMigrationTests.cs
tests/AlKhayal.IntegrationTests/SupplierRestartApiTests.cs
tests/AlKhayal.IntegrationTests/TreasuryLegacyMigrationTests.cs
tests/AlKhayal.UnitTests/PaymentInstrumentValidationTests.cs
```
