# Stabilization Part 1 — financial integrity, cancellation, valuation

Date: 2026-10-26. Branch: `main`; no commit, push, or branch switch.

## Completed

- Investigated invoice history: it had only an Open action and no delete/cancel API. Posting deducted stock through `manual_issue`; removing a posted row would violate immutable document and ledger relationships.
- Added a draft deletion action and API. It writes an immutable tombstone, removes the draft from the normal list/detail, and leaves inventory and ledger untouched. The draft remains recoverable in the database for audit.
- Added a posted invoice cancellation action and API for **unpaid credit sales without downstream activity**. Cancellation atomically creates a reversing stock document, restores exact FIFO quantities and allocated costs in new linked layers, posts an offsetting customer ledger reduction, and records an immutable cancellation. The original invoice remains in history with projected status `canceled` and zero outstanding amount. Repeated requests use operation keys and request hashes; concurrent execution cannot create a second cancellation.
- Posted cash sales, invoices with linked payments/collections, returns, tracked physical units, or an ambiguous later customer collection are blocked with explicit messages. No refund or cash-drawer behavior was added.
- Added on-demand inventory valuation for the selected warehouse, with optional product filter at the API. It aggregates remaining FIFO layers and valid current variant selling prices server-side. Cost, potential sale value, and estimated potential gross margin are shown only when their respective inputs are complete. The calculation button has its own loading state and prevents repeated clicks.
- Sales and purchase posting now return `operation_key_conflict` when a previously used posting key is reused with a different request or invoice. Existing key uniqueness and transaction boundaries remain in place. The other audited operations retain their existing unique keys, request hashes, and transactional writes.

## Root causes and financial audit

| Operation | Existing protection / finding | Part 1 action |
|---|---|---|
| Sales post | Unique invoice key and atomic stock/ledger/payment transaction; reused key on another invoice surfaced a generic conflict | Explicit key conflict |
| Purchase post | Unique post key and atomic stock/supplier ledger transaction; same generic conflict gap | Explicit key conflict |
| Customer collections | QA-101 payload comparison against unique `CustomerTransactions.OperationKey` | Preserved and rerun |
| Supplier payments | Unique key, request hash, short transaction | No change; concurrent `database_busy` response could be improved later |
| Sales returns | Unique key/hash, atomic return, stock, payment, ledger | Preserved; cancellation blocks linked returns |
| Purchase returns | Unique key/hash, atomic return/stock/ledger | Preserved |
| Stock adjustments | Unique inventory document key/hash, atomic movement/FIFO/balance | Preserved |
| Invoice cancellation | No endpoint or UI; no cancellation record or stock reversal | Added guarded transactional operation |

## Database safety

Every new write-capable test obtains a fresh GUID `.db` path in `%TEMP%`, asserts the file did not exist, and asserts the resolved path is outside `%LOCALAPPDATA%\AlKhayal`. Existing regression fixtures also use disposable test databases. The real `%LOCALAPPDATA%\AlKhayal\alkhayal.db` was not opened or migrated by this work. `seeded-database.db` was not used.
The operator database was inspected read-only after testing: size 121,712,640 bytes; `LastWriteTimeUtc` remained 2026-10-25 14:41:42 UTC, before this work.

## Verification

- `dotnet build AlKhayal.slnx --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --filter FullyQualifiedName~SalesCancellationValuationTests --logger "console;verbosity=detailed"`: PASS, 5/5. The selected-warehouse/product valuation call over four balance rows took **26.383 ms** in the latest disposable test fixture; this is a small-fixture measurement, not a large-stock benchmark.
- Financial subset: `SalesCancellationValuationTests|SalesPersistenceTests|PurchasingPersistenceTests|QaCustomerOperationKeyTests|InventoryFifoTests`: PASS, 21/21. An initial run failed only because a preexisting test expected 20 migrations; migration-count assertions were updated to 21, with no business assertion changed.
- Sales/purchase/cancellation subset after explicit reused-key conflict assertions: PASS, 10/10.
- `node frontend/tests/run-tests.js`: PASS, 99-screen registry and existing frontend suites.
- `dotnet test AlKhayal.slnx`: PASS, unit 49/49 and integration 132/132 after updating two preflight migration-count assertions (16→17 and 17→18). An earlier full run had 49/49 unit and 130/132 integration, with only those stale count failures.
- `git diff --check`: no whitespace errors; Git reports line-ending conversion warnings for existing working-tree files.

## Blocked cases and policy decisions

- Cash cancellation would require an explicit tender/refund and cash-drawer policy. The current `cash_sale` event means paid; silently voiding it would misstate cash. The API returns `cash_refund_policy`.
- Partial/full collections and linked returns require a documented reversal/refund workflow before cancellation; the API blocks them. Later unallocated customer collections also block because allocation to this invoice cannot be proved.
- Tracked serial/IMEI stock requires a controlled return of the exact physical identifiers; cancellation blocks it to avoid inventing a physical return.
- The existing sales return path records refund events but has no cash-drawer integration. This assignment did not redefine that existing policy.
- A representative large-stock valuation benchmark and desktop/WebView2 manual confirmation are still needed. The SQL uses the existing FIFO availability index and returns one aggregate object; the inventory grid itself still loads balance rows for display.

## Changed files

`src/AlKhayal.Application/Sales/SalesContracts.cs`, `SalesService.cs`; `src/AlKhayal.Data/Sales/SqliteSalesStore.cs`, `.Lifecycle.cs`, `.Returns.cs`, new `.Cancellation.cs`; `src/AlKhayal.Api/Controllers/SalesController.cs`; `src/AlKhayal.Data/Migrations/021_sales_cancellation.sql`; inventory contracts/service/store new `.Valuation.cs` and `InventoryController.cs`; `src/AlKhayal.Data/Purchasing/SqlitePurchaseRepository.cs`; `src/AlKhayal.Data/Customers/SqliteCustomerAccountRepository.cs` (statement label); `frontend/js/modules/sales.js`, `inventory.js`; `tests/AlKhayal.IntegrationTests/SalesCancellationValuationTests.cs` and migration-count assertions in existing integration tests; this document, `docs/api-contracts.md`, `docs/database.md`, `docs/testing.md`.

## Part 2 handoff

Start with the outstanding user-facing UX work in `docs/qa/manual-feedback-improvements.md` Batch 2. Preserve migration 021 and the cancellation blocks until an accounting decision defines cash refunds, linked-payment reversals, and serial/IMEI handling. Verify the inventory valuation screen manually in WebView2 on the target resolutions and benchmark a large disposable database before changing its query or grid loading behavior. Do not treat a canceled sale's original posted row as deletable.
