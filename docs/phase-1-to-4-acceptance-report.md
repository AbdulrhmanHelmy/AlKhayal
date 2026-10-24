# Phases 1–4 acceptance test report

Date: 2026-10-24  
Result: **Not ready for full acceptance sign-off; API-backed scenarios passed, while visual/native UI interaction remains unverified.**  
Scope: acceptance work and a narrowly scoped WebView2 profile-isolation override with focused tests. No commit or push was created.

## Executive summary

The working tree builds cleanly. The final automated suites pass: 37 unit tests, 92 integration tests, all 99 registered frontend screens, and JavaScript syntax checks for 23 files. The isolated WinForms process started with a dedicated database and WebView2 profile; its embedded API and static frontend responded successfully. Authenticated API-only scenarios created synthetic suppliers/products and posted cash purchases and both return classifications. A post-restart `GET /api/suppliers` failure was reproduced, traced to SQLite result typing in the supplier repository, fixed without schema changes, and then returned HTTP 200 against the same persisted database. The supplier list includes existing balances, and SQLite integrity/foreign-key checks remain clean. The default application database's SHA-256 was unchanged.

This is not a full real-world acceptance pass. The `ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER` override now isolates the native WebView2 profile and preserves the prior default when unset; invalid/overlapping paths fail startup. MainForm was launched against that isolated profile and `ALKHAYAL_DATABASE_PATH`, and the embedded API responded. However, the desktop automation helper failed twice before window enumeration (`failed to write kernel assets: The system cannot find the path specified`), so no screenshot or visual/click-through GUI check was possible. UI login was not automated. Synthetic account authentication and workflows below were exercised over HTTP and are explicitly API-only, not GUI coverage.

The confirmed supplier API defect is repaired and regression-tested. Visual/click-through UI acceptance remains unverified because the desktop automation helper was unavailable; Windows 7 / Chromium 109 also remains unverified.

## Environment and data safety

- Repository: `D:\projects\AlKhayal`; existing Phase 4 changes and the pre-existing `.test-results-batch5\` directory were preserved.
- Test database: `D:\projects\AlKhayal\test-artifacts\phase-1-to-4-acceptance-2026-10-24\winforms-run-001\database\alkhayal-ui-acceptance.db`.
- Test API URL: `http://127.0.0.1:5137`; the listener was checked before launch. The actual `AlKhayal.UI.exe` process (MainForm) is the host owner for this run.
- Startup isolation: `ALKHAYAL_DATABASE_PATH` and `ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER` both pointed inside `winforms-run-001`; `ALKHAYAL_FRONTEND_PATH` pointed to this checkout. MainForm created the separate database/profile; health and login page returned HTTP 200.
- Migration/database evidence: `SchemaVersion` contains migrations 001–018. `PRAGMA integrity_check` returned `ok`; `PRAGMA foreign_key_check` returned zero rows. The ledger contains synthetic acceptance entries (including a retained earlier setup attempt/draft), not historical production documents.
- Normal database: `C:\Users\user\AppData\Local\AlKhayal\alkhayal.db`; SHA-256 before and after: `A7E9AC4D5BB832A6FDC3CFE4ED26E0B0C8B2D8ABCAEDB732C9A440EF032A6CDC`. It was not opened by this task's application process or modified by the isolated host.
- Platform/build: Windows 11 (build 22621), .NET SDK 10.0.402, WebView2 runtime 155.0.4283.62. MainForm/WebView2 startup succeeded; interactive native UI inspection did not.
- Test artifacts and API script/results are in `test-artifacts\phase-1-to-4-acceptance-2026-10-24\`. No screenshots were captured because the desktop automation helper could not enumerate windows.

## Acceptance matrix

“PASS — automated” means an automated unit, integration/API, database, or frontend test passed. It does not mean an operator completed the scenario in the GUI.

| Phase / area | Result | Evidence and limit |
|---|---|---|
| 1 — Authentication, Admin/Cashier, sessions, permissions, last-admin safeguards | PASS — automated; UNVERIFIED — GUI | .NET unit/integration suites passed (28 + 91). Valid/invalid sign-in, logout, persistence, and route behavior were not clicked through in the native UI. |
| 1 — Company/general/activity/features/user settings and persistence | PASS — automated; UNVERIFIED — GUI | Persistence, validation, authorization, and restart coverage is in the integration suite. No settings were changed in the test database by this acceptance run. |
| 1 — Navigation, themes, search, tabs, RTL and compact layout | PASS — registry/renderer automated; UNVERIFIED — visual | Frontend suite exercised registry/load behavior across 99 screens. No native screenshot, resize, theme-switch, focus, or DPI inspection was performed. |
| 2 — Catalog, categories/units, variants/barcodes, activation and persistence | PASS — automated; partial API fixture; UNVERIFIED — GUI | Synthetic product created and read back via API; no visual form was exercised. |
| 2 — Warehouse receipts, movements, tracking and restart | PASS — automated; UNVERIFIED — GUI | Inventory persistence/API tests passed. No operator receipt was made through the UI. |
| 2 — Exact FIFO example (10×100 + 10×120, issue 12) | PASS — automated; UNVERIFIED — GUI | FIFO regression coverage asserts remaining 8 units at value 960; the requested operator interaction and screenshot were not performed here. |
| 2 — Counts, adjustments, transfers, unit conversion, expiry, serial/IMEI and retry safety | PASS — automated; UNVERIFIED — GUI | Inventory persistence/FIFO/API suites cover these backend rules, including transfer cost-origin preservation, stale counts, tracked units, and idempotency. No native form was exercised. |
| 3 — Customers, balances, collections, statements and history | PASS — automated; UNVERIFIED — GUI | Customer persistence/API tests pass. No customer records or collections were created in the acceptance database. |
| 3 — Suppliers, legacy invoices/payments, statements and balances | PASS — automated; partial API fixture; UNVERIFIED — GUI | Synthetic supplier statement/ledger verified via API; no production history imported or invented. |
| 3.5 — Clear Arabic, mock-data disclosure, under-construction labels, forms/modals, errors, focus and visual usability | PASS — static/renderer checks; UNVERIFIED — hands-on | Registry/renderer tests pass. The registry explicitly labels six routes under construction. No native visual audit was possible, so visual clarity, click burden, focus, overlap, and theme styling are not signed off. |
| 4 — Drafts, edits/audit, exact totals, immutable posting and invoice search | PASS — automated; partial API fixture; UNVERIFIED — GUI | Two invoices were posted over API: 500.0000 and 50.2500. Draft editing and UI invoice search were not exercised in this scenario. |
| 4 — Atomic invoice + inventory + FIFO + supplier ledger + cash payment | PASS — automated; API scenario verified; UNVERIFIED — GUI | API ledger showed a 500.0000 payable matched by a 500.0000 payment, and a 50.2500 payable matched by payment. Inventory balances/movements were created and integrity endpoint returned true. No treasury posting is claimed. |
| 4 — Supplier payments, partial balance, statement and retry | PASS — automated; UNVERIFIED — GUI | Supplier ledger integration tests cover scale-4 payment, replay/conflict, invoice remaining, statement and history. |
| 4 — Return refund vs supplier credit, original-price account value, actual FIFO value/variance, provenance and limits | PASS — automated; API scenario verified; UNVERIFIED — GUI | API recorded a 100.0000 supplier-credit return and a distinct 25.1250 cash-refund record. Statement labels cash refund “without treasury movement”; inventory integrity passed. Transfer-origin edge cases, limits, serial provenance, and variance remain covered by focused automated tests only. |
| Cross-phase scenarios A–F | PASS — component coverage only; partial API scenario; UNVERIFIED — end-to-end UI | Supplier/catalog/purchase/return/inventory connected journey ran over API; customer workflows, permissions, and the complete six GUI journeys were not run. |
| Legacy supplier migration and representative mismatch stop | PASS — automated fixture; PASS — fresh migration | Migration tests cover historical invoices/payments preserved once, derived opening balance, and abort-before-write on unreconciled data. The actual fresh test DB migrated 001–018 twice without duplicate schema versions. No real database was migrated. |
| Restart persistence in an operator workflow | PASS — isolated DB and authenticated API | Reproduced the HTTP 500 against the original persisted fixture, fixed the supplier projection, rebuilt WinForms dependency output, then restarted MainForm against that same DB/profile. Auth login, `/api/auth/me`, and `GET /api/suppliers` all returned 200; the API returned all 4 persisted suppliers and their payable/credit/paid values. No data was migrated or rewritten by the fix. |
| Windows 7 / Chromium 109 compatibility | UNVERIFIED | This machine runs Windows 11 and WebView2 155.0.4283.62; no Windows 7 or Chromium 109 runtime was available/exercised. |

## Interactions and expected versus actual

1. Confirmed no AlKhayal process or listener on port 5143; verified the normal database path and recorded its SHA-256.
2. Built the solution with `dotnet build AlKhayal.slnx --no-restore`. Expected: clean build. Actual: success, 0 warnings and 0 errors.
3. Ran the focused supplier/purchase set (`SupplierRestartApiTests`, `SupplierPersistenceTests`, and purchase posting tests). Actual: 15/15 passed.
4. Ran `dotnet test AlKhayal.slnx --no-build --no-restore --logger "console;verbosity=minimal"`. Expected: all current unit/integration tests pass. Actual: 37/37 unit and 92/92 integration tests passed.
5. Ran `node frontend/tests/run-tests.js`. Expected: registry and behavior suite passes. Actual: pass, all 99 screens.
6. Ran `node --check` on every `frontend/js/**/*.js` file. Expected: valid supported syntax. Actual: 23 files passed.
7. Added and focused-tested `ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER`; confirmed the existing default is unchanged when unset and invalid/overlapping paths fail safely.
8. Launched actual `AlKhayal.UI.exe` with `ALKHAYAL_DATABASE_PATH` and the WebView2 override inside `winforms-run-001`. Actual: process remained alive, isolated profile populated, and embedded health/login assets returned 200.
9. Attempted desktop automation twice during the original acceptance run. Actual: helper failed before window enumeration with `failed to write kernel assets: The system cannot find the path specified`; no UI interaction or screenshot is claimed.
10. Ran `winforms-run-001\run-api-acceptance.ps1` using generated test-only credentials. Actual: synthetic supplier/product created; 500.0000 and 50.2500 cash invoices posted; supplier-credit and cash-refund returns recorded distinctly; statement, movements, balances and inventory integrity read back. Results are saved without credentials in `api-acceptance-results.json`. A prior setup attempt left an unposted draft in the isolated test DB.
11. Reproduced the restart defect on the same DB/profile with login 200, `/api/auth/me` 200, and `GET /api/suppliers` 500. The actual exception and stack trace are preserved in `winforms-run-001/restart-http-failure-reproduction.json`.
12. Fixed the query/result mapping, rebuilt the full solution, and reran MainForm against that same database. Actual: login 200, `/api/auth/me` 200, and authenticated `GET /api/suppliers` 200 with all four persisted suppliers and balances. No migration or record rewrite was needed.
13. Rechecked the default database hash. Expected: unchanged. Actual: same SHA-256 as previously recorded.

## Focused supplier API repair

- Reproduction used the existing `winforms-run-001` SQLite file after restarting MainForm. Synthetic login returned 200, `/api/auth/me` returned 200, and authenticated `GET /api/suppliers` returned 500. The captured response identifies `System.Data.DataException: Error parsing column 9 (Paid=0 - Int64)`, with inner `System.InvalidCastException: Specified cast is not valid.` The stack reaches Dapper materialization in `SqliteSupplierRepository.GetAll()`. Full sanitized request/response exception details are in `winforms-run-001/restart-http-failure-reproduction.json`.
- Root cause: the supplier query's `COALESCE(subquery / 10000.0, 0)` returned a SQLite `Int64` when the aggregate was empty. Dapper attempted to materialize that branch directly as `Supplier.Paid` (`decimal`). This was not migration/schema initialization, auth/session setup, API composition, or JSON serialization; login and current-user requests succeeded, and the exception originated in the repository query.
- Fix: query the signed payment aggregate as integer `PaidScaled`, then map it to `Supplier.Paid` with exact decimal division by `10000m`. No schema change, migration, or data rewrite was required.
- Regression: `SupplierRestartApiTests.Authenticated_supplier_list_survives_purchase_payment_and_application_restart` creates an isolated database, supplier, posted purchase, and linked scale-4 payment; closes the server and pooled SQLite handles; runs migrations twice; starts a new server; authenticates and verifies `GET /api/suppliers` and exact payable/paid values. It also verifies no duplicate supplier/purchase/ledger/FIFO rows and clean database/FK checks after repeated startup.
- Validation: focused supplier/purchase integration filter passed 15/15; full build passed with 0 warnings/errors; full suite passed 37/37 unit and 92/92 integration tests; `git diff --check` passed. No frontend code changed, so frontend tests were not rerun for this repair.
- Same-fixture confirmation after fix: the restarted WinForms process returned 200 for login, `/api/auth/me`, and `GET /api/suppliers`; all four existing supplier records and their balances were present. Final read-only checks still show 5 purchase invoices, 3 purchase returns, 11 supplier-ledger entries, 7 stock movements, 4 FIFO layers, and 3 FIFO allocations; integrity `ok`, zero foreign-key violations. Default user DB hash remains unchanged.

## Findings and severity

**Acceptance blocker (desktop automation environment, not a confirmed business-rule defect):** profile isolation is now implemented and MainForm/WebView2 startup succeeded, but the available desktop automation helper failed before window enumeration. The resolver is unit-tested and documented. Complete visual and click-through checks with an operator when desktop automation is available; do not bypass the failure by automating UI authentication through another mechanism.

**Resolved high-priority defect — supplier list after restart:** `SqliteSupplierRepository.GetAll()` projected `Paid` using `COALESCE((SELECT SUM(...)/10000.0 ...),0)`. For suppliers with no qualifying payment ledger rows, SQLite returned the integer `0` (`Int64`) from `COALESCE`; Dapper attempted to assign it directly to `Supplier.Paid` (`decimal`) and threw `InvalidCastException`, wrapped as `DataException: Error parsing column 9 (Paid=0 - Int64)`. Migration/schema, auth/session, startup/dependency composition, and JSON serialization were not the cause: login and `/api/auth/me` returned 200, and the stack pointed to Dapper materialization in `SqliteSupplierRepository.GetAll()`.

The repository now selects the exact integer `PaidScaled` aggregate, maps it to `decimal` using `/ 10000m`, and handles the zero aggregate consistently. This avoids SQLite's dynamic `COALESCE` result type and does not introduce floating-point monetary conversion. There is no migration and no data rewrite. The same persisted test DB now returns HTTP 200 after MainForm restart.

Manual UI-only defects remain unknown, not “none.” In particular, authenticated permission discovery, Arabic form comprehension, focus/scroll behavior, save/cancel clarity, overlap at 1366×768 or smaller size, and UI restart workflows need hands-on evidence.

**Known scope limits, not defects:** no real POS/sales posting; no tax/discount calculation; no treasury integration; no exact historical FIFO reconstruction; six registry routes are explicitly under construction; Windows 7/Chromium 109 is unqualified.

## Recommended fixes / next checks before Phase 5

1. Have an operator manually inspect/authenticate to the isolated app and complete phase 1, 2, 3, 3.5, and 4 GUI journeys; record outcomes/screenshots under the artifacts directory.
2. Separately qualify Chromium 109/WebView2 and supported DPI/window sizes on intended deployment hardware.

## Readiness assessment

**Not accepted as “full real-world acceptance tested.”** The code is ready for continued development based on passing automated regressions, but Phase 5 should not be treated as having a complete manual Phases 1–4 sign-off until the isolated native UI journey and evidence are supplied. No production records were touched, no treasury/cash/bank balance was fabricated, and no commit or push was made.
