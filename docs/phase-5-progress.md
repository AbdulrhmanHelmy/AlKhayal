# Phase 5 progress

## Implemented

- Persisted sales drafts and  immutable posted invoices/lines with generated invoice numbers, audit events, tax/discount/total snapshots, and version checks.
- Atomic invoice posting through the existing FIFO inventory writer. Posting rejects insufficient stock and incomplete FIFO basis, snapshots tracking data, and uses an operation key for safe retry.
- Standard `فواتير البيع` and dedicated `الكاشير اللمسي` screens backed by the same sales API. The touch screen supports catalog/barcode lookup, variant/unit/batch selection, serial/IMEI capture, customer/payment mode, discounts, totals, and posting.
- Append-only cash sale, customer collection, refund, and credit-adjustment events. Credit invoices update customer balances and customer statements include invoice/return references. Account events do not create treasury/cashbox movements.
- Partial/full returns capped at remaining sold quantities. A return restores the exact original FIFO layer quantities and values, restores selected serial units, and applies the cash refund/eligible credit reduction with customer and inventory changes in one transaction.
- Invoice receipt and reprint view with compact thermal CSS and the browser/WebView2 print flow.

## Automated validation

- `dotnet build AlKhayal.slnx --no-restore` passed with zero warnings and errors.
- Focused `SalesPersistenceTests` passed 3/3, including rollback/retry, credit collection, partial return, and serial/IMEI/batch/expiry restoration.
- `node frontend/tests/run-tests.js` passed across 99 registered screens.
- `node --check frontend/js/modules/sales.js` and `node --check frontend/js/app.js` passed.
- `git diff --check` passed; Git only reported line-ending normalization notices for edited LF files.
- `dotnet test AlKhayal.slnx --no-restore` passed 49 unit tests and 111 integration tests, including concurrent attempts to sell or return the last unit.

## Verification limits

The app process could not be started for a native Windows WebView2 smoke test because the environment rejected the process-launch request by policy. Consequently the 1366×768 no-page-scroll layout and actual system print dialog/hardware output were not visually verified. Automated route/renderer checks and the CSS layout rules pass, but they do not replace that native check. Browser print does not report paper or printer failures back to the app.

The automated sales matrix covers persistence, idempotency, stock, ledger, return, rollback, restart, concurrent last-unit sale/return attempts, and tracked-unit paths. Every rounding boundary, cash-sale customer credit limits (cash sales do not increase customer debt), and physical printer disconnect/paper errors do not have dedicated automated or hardware coverage.
