# Phase 4 progress

## Approved business rules

- Purchase invoice totals are the sum of purchase-line quantities times actual purchase prices, using scale-4 money. Discounts and taxes are deferred.
- Cash purchase posting records an equal supplier payable and payment atomically. This is a supplier-account event, not a treasury posting.
- Historical supplier invoices and payments remain intact. The migration imports each reconciled opening balance as a labeled entry and separately imports each old document once. It does not count old documents again as opening balance. A failed reconciliation aborts before migration 017 writes.
- Returns explicitly choose `cash_refund` or `supplier_credit`. They remain distinct supplier-ledger entry types; neither creates a treasury movement or balance.
- Return supplier-account value uses original invoice purchase prices. Inventory value comes from actual FIFO allocations and any variance is recorded on each return line.
- Return quantity is limited by the original line quantity less posted returns and eligible remaining stock from that original receipt. Provenance follows transferred FIFO fragments. Serial-tracked lines require selecting the matching original serial currently present in the return warehouse.
- Authorized purchasing users can edit drafts. Creation, draft edits, and posting record actor/time audit data. Posted invoices and returns are immutable.
- Invoice posting, inventory movements, cost layers, supplier ledger, and immediate payment use one SQLite transaction and stable operation identities.

## Group 1 — foundation and atomic posting

Completed. Added migrations 017–018, the supplier ledger schema/import, purchasing invoice/draft schema, scale-4 line totals, immutable posting/audit constraints, and draft/list/get/post APIs. Purchase posting invokes the existing inventory posting implementation using the same SQLite connection and transaction; no second inventory/FIFO path was introduced. Cash posting creates equal supplier payable and payment entries. The UI provides Arabic RTL draft entry, tracking fields, draft editing, and posted invoice listing.

Migration preflight verifies exact scale-4 representability, invoice paid totals against recorded payments, supplier ownership, and nonnegative reconciled opening balances before 017 executes. It has regression coverage for both preservation/no-double-counting and abort-before-write on mismatch.

## Group 2 — supplier ledger and payment integration

Completed. Added a unified SQLite ledger repository and service for dated statements, movement history, and idempotent supplier payments. Legacy invoice/payment records remain available; new payments use scale-4 amounts and stable operation keys. Supplier balance projections support a supplier-credit balance. Supplier payment screens explicitly state that the recorded entry does not create a cash/bank/treasury movement; the optional reference is text only.

## Group 3 — traceable purchase returns and employee UI

Completed. Added return posting, return audit, immutable return records, explicit refund-versus-credit ledger treatment, original-line account values, FIFO inventory values, and stored variances. Returns restrict FIFO issue to the original receipt's movement origin and reject insufficient/ambiguous allocations. Eligible stock and serialized identities remain traceable through transfers. A dedicated RTL return screen requires an invoice and warehouse, asks for quantities/serials, and displays account value, FIFO value, and variance.

## Files and migrations changed

- `017_integrated_purchasing.sql`: purchasing and supplier-ledger foundation, historical opening/document imports, immutable ledger/audit rules.
- `018_purchasing_provenance_and_credit.sql`: return provenance, scale-compatible supplier credit projection, invoice-line stock provenance, supplier-payment idempotency, legacy invoice payment linkage, and return audit/immutability.
- Core/Application/Data/API/Host purchasing and supplier-ledger contracts, services, SQLite repositories, controllers, and composition.
- Shared inventory posting/FIFO integration, Arabic RTL purchase and return views, registry/renderer tests, and database/API/FIFO/testing/screen coverage documentation.

## Focused verified tests

- Legacy supplier migration preserves invoice/payment rows, creates the reconciled opening, and does not double-count; a mismatch leaves migration 017 unapplied and source rows unchanged.
- Purchase draft persistence, exact scale-4 totals, atomic FIFO posting, stable replay, cash payable/payment equality without treasury rows, and rollback after an injected late inventory failure.
- Purchase return account-vs-FIFO values, return idempotency, transfer-origin traceability, and original serial selection/retirement.
- Unified supplier ledger payment replay/conflict, invoice balance, history, statement, and payment listing.
- Frontend registry resolves all 99 screen renderers; purchasing renderer and tracking/refund distinctions are covered.

## Final verification

- `dotnet build AlKhayal.slnx --no-restore` — passed with 0 warnings and 0 errors.
- `dotnet test AlKhayal.slnx --no-build --no-restore --logger "console;verbosity=minimal"` — passed: 37 unit and 92 integration tests, 0 failed (includes WebView2 profile resolver and supplier restart API coverage).
- `node --check` on every `frontend/js/**/*.js` file — passed.
- `node frontend/tests/run-tests.js` — passed, including purchasing renderer registration and all 99 screens.
- `git diff --check` — passed; Git emitted only configured LF-to-CRLF working-copy notices.

No real treasury integration, tax/discount treatment, or historical FIFO reconstruction is claimed. Supplier-payment and cash-refund labels are supplier-account records only. The UI has automated registry/behavior checks but has not had a manual Windows WebView2 visual smoke test in this environment.

## Startup compatibility follow-up (2026-10-24)

An isolated copy of the default user database (SchemaVersion 001–003, no supplier ledger) upgraded successfully with the current binaries, so the reported screenshot's exact database state was not reproduced from that file. A representative recorded-017 schema missing `SupplierLedgerEntries.LegacyPaymentId` did reproduce the exact 018 `UPDATE` failure. The migrator now repairs that specific missing link transactionally only when every historical payment maps uniquely; unresolved history stops without writing. No applied migration file was edited. The WinForms startup error state now shows Arabic guidance while retaining the exception and stack trace in the local diagnostic log. Manual normal-database UI click-through remains unverified.
