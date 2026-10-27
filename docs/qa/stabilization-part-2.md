# Stabilization Part 2 — global input and Touch POS

Status: implemented and automated regressions passing on 2026-10-26. Native WebView2, physical scanner, and target-size visual acceptance remain open. No commit, push, branch switch, or delegated worker was used.

## Completed work and root causes

- Preserved the Part 1 entity-list and other in-place search fixes. Audited global keyboard handlers, workspace tab rendering, search handlers, and delayed responses. POS search now invalidates an outstanding request as soon as typing begins, debounces human input, keeps the input mounted, and ignores results from an old query, warehouse, or screen. Rapid per-character scanner input of at least eight characters triggers exact barcode lookup after a short quiet period even without Enter; Enter also performs exact lookup, and a shared guard blocks a trailing Enter from adding the same scan twice. Purchase and inventory product pickers now debounce and reject stale responses while keeping their inputs mounted.
- Touch POS previously left the grid empty until a search and fetched the entire customer directory at startup. It now loads a 40-product page immediately, with a loading/empty/error state and a server-side “more” page. A 30-day net-selling aggregate subtracts returned quantity in base units and sorts with name/ID ties; products with no sales follow and remain browsable. POS reads include only active, unarchived, ready stock products with active variants and sale units. Current warehouse quantity is shown on each card; zero-stock cards are disabled. Name, SKU, and barcode searches use the existing catalog predicate.
- Added all-products and active-category buttons. The selected category constrains each server page and search. Six subtle, stable category tints use CSS supported by Chromium 109 in both themes.
- Touch POS customer selection searches name, code, or phone through a bounded 20-row endpoint. “Without customer” remains selectable. Selected IDs are carried in the draft payload for cash or credit sales; the sales service continues to reject credit sales without a customer. The warehouse selector appears when the multiple-warehouses feature is enabled and more than one active warehouse exists; otherwise the default warehouse name is shown. Switching warehouses invalidates the old product request and reloads availability. Sales posting retains server-side stock validation.
- Invoice discount mode is explicit (fixed amount or percent). Percentage accepts 0–100 with two decimals and is converted using integer arithmetic to one rounded two-decimal amount. Totals recalculate on input without replacing the focused field. Existing sales draft/posting logic persists that amount, recalculates tax, updates the customer balance, and prints the posted amount. Reopened invoices show the stored fixed amount because the original percentage choice is not stored separately.
- Investigated Supplier List navigation. No idle timer or automatic `navigate("suppliers-list")` path was found. The earlier asynchronous repaint guards remain. A separate confirmed route-state gap existed: switching or closing workspace tabs did not update `alkhayal_last_route`, so a later shell reload could restore an older Supplier List route. Tab activation and active-tab close now persist the actual active route. The reported idle jump itself was not reproduced; a WebView2 session trace is still needed to determine whether a reload or another event occurred.

## Main changed files

- POS and search frontend: `frontend/js/modules/sales.js`, `frontend/css/modules.css`, `frontend/js/modules/purchasing.js`, `frontend/js/modules/inventory.js`, `frontend/js/app.js`.
- Browse and customer API: `src/AlKhayal.Api/Controllers/InventoryController.cs`, `src/AlKhayal.Api/Controllers/CustomersController.cs`, `src/AlKhayal.Application/Catalog/{CatalogContracts,CatalogPorts,CatalogService}.cs`, `src/AlKhayal.Application/Customers/CustomerService.cs`, `src/AlKhayal.Core/Interfaces/ICustomerRepository.cs`, `src/AlKhayal.Data/Catalog/SqliteCatalogStore.cs`, `src/AlKhayal.Data/Customers/SqliteCustomerRepository.cs`, `src/AlKhayal.Data/Migrations/022_pos_browse_indexes.sql`.
- Regressions and migration-fixture counts: `frontend/tests/run-tests.js`, `tests/AlKhayal.IntegrationTests/{SalesCancellationValuationTests,CustomerPersistenceTests,ApiEndpointTests}.cs`, and existing integration migration fixtures that expect the embedded-script count.
- Contracts and test guidance: `docs/api-contracts.md`, `docs/database.md`, `docs/testing.md`, `docs/qa/manual-feedback-improvements.md`.

## Tests and actual results

| Check | Result |
|---|---|
| `dotnet build .\src\AlKhayal.UI\AlKhayal.UI.csproj --no-restore -v:q` | Passed, 0 warnings, 0 errors after backend changes. |
| `node frontend/tests/run-tests.js` | Passed, 99 registered screens. Focus and paste retention, stale POS/customer search, default page, category/warehouse filters, pagination, customer selection, rapid scan without Enter and late Enter de-duplication, and discount rounding assertions pass. |
| `dotnet test .\tests\AlKhayal.UnitTests\AlKhayal.UnitTests.csproj --no-restore -v:q` | Passed, 49/49. |
| `dotnet test .\tests\AlKhayal.IntegrationTests\AlKhayal.IntegrationTests.csproj --no-restore -v:q` | Passed, 137/137, including the authenticated OWIN route test. The three POS browse tests and UI build also passed after the final active-product predicate change. |
| Focused POS browse, customer search, 15,000 product page, and discount posting tests | Passed individually after corrections. The first 15,000-product page query measured **190.977 ms** on this host, returning 40 records; no claim for older hardware or a heavy 30-day sales ledger. |

The 15,000-product test uses a disposable SQLite file, inserts the products in one transaction, then checks page 1 and page 375. It does not download the full product list to JavaScript. The smaller POS fixture posts real credit sales and a real return, then verifies net-sale order, category filtering, pagination, and a second warehouse's zero availability.

## Limits and decisions for handoff

- Native authenticated WinForms/WebView2 interaction was **not** executed. Verify continuous Arabic/English typing, caret/selection/paste, scanner rapid input with and without Enter, customer/warehouse selectors, Light/Dark, and checkout at 1366×768 and about 900×600 using an isolated database and WebView2 profile. The physical scanner has not been exercised.
- The Supplier List idle jump was not reproduced. If it recurs, record route, active tab, URL/hash, `alkhayal_last_route`, WebView2 navigation/reload events, and network timing. Do not treat the fixed stale route value as proof that every idle trigger is gone.
- The percentage input mode is transient UI state. Accounting persists the exact rounded discount amount. Persisting original percentage as additional financial-document metadata would need a separate explicit contract/schema decision; it is not necessary for the current total, tax, balance, or receipt amount.
- The 30-day popularity query is on demand per browse page and has indexes for invoice/return line joins. The 15,000-product benchmark had no large sales history. Benchmark a realistically populated 30-day ledger on target hardware before choosing a maintained popularity projection.
- This part does not implement Part 3. Preserve the Part 1 cancellation policy blocks for cash, paid, returned, and tracked invoices.

## Database safety

Every new write-capable integration test uses a unique, previously nonexistent file under the system temp directory. The POS/financial test helper asserts the path is outside the operator directory; the new customer test repeats that assertion. No test uses `%LOCALAPPDATA%\AlKhayal\alkhayal.db`. Read-only inspection after the tests showed `C:\Users\user\AppData\Local\AlKhayal\alkhayal.db`, 121,712,640 bytes, last modified **2026-10-25 14:41:42 UTC**, unchanged from the Part 1 handoff. Migration 022 was applied only to disposable files by tests.

## Exact Part 3 handoff

Start from the existing uncommitted workspace without reset, commit, push, or branch switch unless the user later authorizes it. Perform an authenticated WebView2 pass with `ALKHAYAL_DATABASE_PATH` and `ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER` set to disposable locations. Review the Part 3 request before coding; keep Part 1 financial policy blocks and Part 2 POS paging/discount semantics intact.
