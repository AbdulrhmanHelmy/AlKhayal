# Phase 2 / Task 2.4 implementation report

## Implemented

The Warehouse Directory now reads and changes SQLite data. It supports warehouse creation, editing, default selection, activation controls, search and status filtering, warehouse balances, opening stock, independent manual receipts and issues, signed adjustments, count drafts and posting, immediate transfers, and paginated movement history. The Inventory sidebar remains `products-list` and `warehouses-list`; old stock routes redirect to the directory. Product master data remains separate from stock.

All quantity changes use `InventoryService` and one `SqliteInventoryStore.PostInTransactionAsync` transaction coordinator. A posted `InventoryDocuments` source, its lines, signed `StockMovements`, `InventoryBalances` projection, physical identifier locations, and `InventoryAudit` event commit together. The unique operation key returns the original result for an identical retry and conflicts with a changed request. SQLite serializes writers with a bounded five-second busy timeout. Source and destination transfer movements post atomically. A missing source quantity rejects the entire request; negative stock is disabled.

Migration `011_inventory_ledger.sql` adds `Warehouses`, `ProductBatches`, `InventoryDocuments`, `InventoryDocumentLines`, `StockMovements`, `InventoryBalances`, `InventoryPhysicalUnits`, `InventoryIdentifiers`, `StockMovementUnits`, and `InventoryAudit`. It creates the initial main warehouse. Ledger, posted document, and audit immutability triggers reject update/delete. Balance writes must match ledger totals. The migration is additive to Tasks 2.1–2.3; previous migration files were retained. The details of schema, precision, valuation, and constraints are in [inventory-ledger.md](inventory-ledger.md).

Quantities persist as scale-6 integers and money as scale-4 integers. Unit conversion uses the saved rational numerator and denominator, rejects inexact base quantities, and snapshots the conversion on document lines. Receipts require operator-supplied actual acquisition cost; a catalog reference price is never substituted. Receipt value rounds once, away from zero, to scale 4. Issues use the current warehouse/variant/batch weighted average; a final issue removes remaining value. Transfers carry the source value unchanged.

Batch-tracked intake requires a lot; expiry-tracked intake requires its business-date expiry. Operators identify the specific lot for issue or transfer. Expired lots stay visible; manual issue and transfer are blocked, while a documented negative adjustment can dispose of them. Serial-tracked goods require one serial per whole unit. One physical device can hold multiple globally unique IMEIs and still contributes quantity one. Receipts, issues, and transfers update identifier location and movement links in the posting transaction. Tracking assignments with movement history cannot be removed.

Count drafts snapshot balance versions and quantities. Posting rejects a stale snapshot, records each counted quantity, and sends nonzero differences through the common ledger. Drafts may be canceled. The restricted automatic reversal creates an opposing document only for a positive untracked opening, manual receipt, or adjustment whose balance and value are still exactly unchanged; complex cases return a conflict or unsupported error and require a new documented adjustment. Administrator integrity checks compare balances with the ledger. Rebuild verifies tracked-unit totals, creates and checks a SQLite backup, then reconstructs the projection with an audit document and active balance guards.

The new `/api/inventory/*` routes provide warehouse management, product selection, balances, movements, posting, counts, identifier lookup, restricted reversal, integrity check, and rebuild. Every route requires a validated session. Cashiers require `warehouses-list`; warehouse management, reversal, integrity, and rebuild require an administrator. Mutations use the existing CSRF check. The frontend uses the same-origin API and stores no authoritative stock in browser state. Route and operator details are in [warehouse-management.md](warehouse-management.md) and [api-contracts.md](api-contracts.md).

## Changed areas

The principal Task 2.4 files are `src/AlKhayal.Data/Migrations/011_inventory_ledger.sql`, `src/AlKhayal.Application/Inventory/*`, `src/AlKhayal.Data/Inventory/SqliteInventoryStore.cs`, `src/AlKhayal.Api/Controllers/InventoryController.cs`, `frontend/js/modules/inventory.js`, `frontend/css/modules.css`, `frontend/data/screens.json`, `frontend/index.html`, `tests/AlKhayal.IntegrationTests/InventoryPersistenceTests.cs`, and `tests/AlKhayal.IntegrationTests/InventoryApiTests.cs`. Host composition, catalog tracking-change protection, registry tests, migration-count expectations, and architecture/API/database/design/testing documentation were also updated. The checkout already contained uncommitted Task 2.1–2.3 work; it was preserved.

## Validation

On 2026-10-22:

| Command or check | Result |
| --- | --- |
| `dotnet build AlKhayal.slnx --no-restore` | Passed, zero warnings/errors |
| `dotnet build AlKhayal.slnx --no-restore -c Release` | Passed, zero warnings/errors |
| `dotnet test AlKhayal.slnx --no-restore` | Passed: 19 unit and 51 integration tests |
| `node frontend/tests/run-tests.js` | Passed: registry coverage for 99 screens |
| `node --check frontend/js/modules/inventory.js` and checks for all frontend JavaScript files | Passed |
| `git diff --check` | Passed |

Integration coverage includes fresh migration, upgrade from migration 010, foreign-key checks, receipt/issue/transfer, exact conversion and overflow, batch/expiry/serial/dual-IMEI state, count staleness, restricted reversal, projection rebuild and backup, immutable rows, rollback on failure, operation-key replay/conflict, concurrent negative-stock prevention, feature gating, authentication, CSRF, and screen permissions. These are automated checks; they are not a substitute for an authenticated native desktop walkthrough.

## Acceptance and limits

The implemented ledger and HTTP workflows are ready for an isolated business acceptance run. **Authenticated WinForms/WebView2 visual acceptance has not been executed**, so Task 2.4 should not be signed off as fully accepted yet. A scratch database host started successfully on `127.0.0.1:5149`, but automatic approval review blocked launching the visible desktop process; the scratch host was stopped. Run the scenarios in [warehouse-management.md](warehouse-management.md) on a scratch database, including Arabic RTL layouts, narrow windows, keyboard access, and restart persistence. Windows 7 with the Chromium 109 WebView2 runtime needs validation on a target machine.

The current UI requires explicit lot selection; automatic FEFO allocation is not present. A selected zero-stock unbatched variant can be counted, but a new batch must enter as a manual receipt so its lot and expiry are captured. Automatic reversal is deliberately restricted as described above. Historical receipt cost must be entered by the operator. Projection rebuild makes a recovery backup but should be run while normal posting is quiesced, followed by a restore test. Purchase, sales, and returns posting are future integrations with this ledger boundary; this task does not represent those workflows as persisted inventory actions.
