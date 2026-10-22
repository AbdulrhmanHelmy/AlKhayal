# Warehouse management — Phase 2 / Task 2.4

`دليل المخازن` is the only warehouse entry in the Inventory sidebar. It lists SQLite-backed warehouses and a selected warehouse's real stock balance. Administrators can create/edit warehouses, change the active default, and deactivate an empty nondefault warehouse. Codes are unique after normalized comparison. A warehouse with stock or the active default cannot be deactivated. Existing references are retained. The first migration creates a usable default warehouse; adding another warehouse requires the effective `multiple-warehouses` module.

The directory provides contextual actions for opening stock, independent manual receipt, manual issue, signed adjustment, physical count, immediate transfer, and read-only movement history. Operation forms search real products through `/api/inventory/products`, then use stable variant and product-unit IDs. A size/color combination is selected explicitly. Receipt forms collect cost, lot/date, and per-unit serial/IMEI lines where the product requires them. Forms review the request before posting, retain drafts on API errors, and reuse the operation key for retries. The count screen captures real balances and blocks stale posting. History supports warehouse, product, type, date, and reference filtering and pagination; eligible administrator reversals are requested as new documents.

The existing `warehouse-add`, `stock-adjustment`, `warehouse-transfer`, and `stock-movements` routes redirect to the directory for compatibility. The Inventory sidebar still has exactly `products-list` and `warehouses-list`. The Product Catalog keeps one row per product and its compact optional-capability badges. Stock remains separate from product master fields.

## Authorization

Every `/api/inventory/*` route requires a server-validated session. Cashiers need the existing `warehouses-list` screen permission to query or post stock. Warehouse CRUD, reversal, integrity, and rebuild require an administrator. Mutations require the existing CSRF token. Product selection endpoints under `/api/inventory/products` use the warehouse screen permission, so a cashier can select stock without receiving catalog-management permission. There is no new role registry. The current host remains loopback only.

## Operator checks

On an isolated test database: create a stock product, manually receive 10 units at an actual acquisition cost, issue 3, and check balance 7 plus two movement documents. Enable multiple warehouses, create another warehouse, transfer 2, and check source 5, destination 2, company total 7. Repeat the transfer with the same operation key and verify no second effect. Repeat with a changed payload and expect conflict. Count physical stock and verify posted difference; create a second count, change stock, then verify its stale posting fails. For a batch/expiry product, receive to a named lot; for an IMEI product, receive one physical unit with its serial and two IMEIs, then transfer it and verify the location and quantity remain one. Restart and inspect warehouses, balances, and history. Keep a separate verified backup of any real business database before upgrade.

Authenticated native WinForms/WebView2 acceptance and Windows 7 Chromium 109 validation must be performed on the deployment machines; automated HTTP and frontend registry tests do not prove that runtime combination.

