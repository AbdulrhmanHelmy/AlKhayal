# Isolated acceptance fixture manifest

Database: `winforms-run-001/database/alkhayal-ui-acceptance.db`  
WebView2 profile: `winforms-run-001/webview2-profile`  
State: synthetic fixtures entered via authenticated API calls only; no GUI interaction is claimed. Migrations 001–018 are present, SQLite integrity is `ok`, and foreign-key check reports zero violations.

| Fixture | Expected / actual evidence |
|---|---|
| Product and supplier | Synthetic supplier and stock product created and read back through API. Product uses scale-4 prices. |
| Cash invoice 1 | 5 units × 100.0000 = 500.0000. Posted via API; statement contains payable 500.0000 and equal payment 500.0000, net zero before return. |
| Supplier-credit return | One unit returned; return mode is `supplier_credit`, account value 100.0000 at original purchase price. |
| Cash invoice 2 | 2 units × 25.1250 = 50.2500. Posted via API; statement contains equal payable/payment entries. |
| Cash-refund return | One unit returned; mode is `cash_refund`, value 25.1250. Ledger description explicitly says no treasury movement. |
| Inventory projection | For the complete run's product: 5 units remain, inventory value 425.1250; `costBasisComplete=true`. API inventory integrity returned true. A retained earlier setup attempt also left synthetic test rows in this disposable DB. |
| Tests not covered by this fixture | FIFO 10×100 + 10×120 issue-12, transfers/provenance, expiry/serial, excess returns, draft edits, customer collections, and permission journeys are covered by focused automated tests where applicable; they were not exercised in this API scenario or GUI. |

The test-only admin password was generated at runtime and is not written to logs, script, or results. `winforms-run-001/run-api-acceptance.ps1` is the repeatable API scenario; `winforms-run-001/api-acceptance-results.json` records its results without credentials. The desktop UI launched with the isolated profile, but no screenshot/operator GUI session was captured because desktop automation failed before window enumeration.
