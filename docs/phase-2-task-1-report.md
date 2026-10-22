# Phase 2 Task 2.1 implementation report

## Delivered

- Additive migration 008 with categories, units, product details, hidden default variants, migration issues, and idempotent create operations.
- Atomic C# legacy backfill with ID/value preservation, exact fixed-scale conversion, invalid-row quarantine, and startup reconciliation.
- Application validation and async port, transactional SQLite adapter, authenticated/CSRF/screen-aware API, optimistic versions, activation, and archive.
- Real RTL product list/add/quick-add/edit, category, and unit screens using the approved shell, themes, tabs, cards, tables, forms, dialogs, and toasts.
- Automated migration, persistence, business-rule, API authorization/restart, frontend registry, syntax, and stale-render coverage.

## Focused Task 2.1 hotfix

- Migration 009 adds nullable scale-4 `PurchasePriceScaled`; legacy products remain explicitly unknown and no stock movement or average-cost calculation is introduced.
- Product create/edit and detail contracts now carry `purchasePrice`, with non-negative server validation and invariant four-place formatting.
- Product Catalog is the visible product hub with contextual add, quick-add, edit, category-management, and unit-management actions. Inline category creation preserves the mounted product draft and selects the created category.
- The inventory sidebar now contains only Product Catalog and Warehouse Directory. Warehouse Directory is a Task 2.4 placeholder that displays no mock records; legacy action routes/renderers and cashier permission IDs remain registered for direct/contextual compatibility.

## Compatibility assumptions and remaining acceptance

The implementation remains .NET Framework 4.8/.NET Standard 2.0, Web API 2/OWIN, System.Data.SQLite/Dapper, vanilla JavaScript, same-origin cookies, and Chromium 109-compatible syntax. It is standalone/local-only for this phase. Windows 7 with the legacy Chromium 109 WebView2 runtime, real DPI matrices, and authenticated WinForms interaction require manual deployment verification; no such pass is claimed here. Tasks 2.2–2.5 remain planned and no stock, tracking, barcode, or warehouse persistence is implied.

## Validation recorded

- Release `dotnet build AlKhayal.slnx --no-restore -c Release`: succeeded with zero warnings/errors. The exact Debug solution build reached every changed backend/test project but the already-running `AlKhayal.UI` process locked its Debug output copies; no compile error was reported in changed code.
- `dotnet test AlKhayal.slnx --no-restore`: 15 unit and 23 integration tests passed against isolated databases.
- All frontend JavaScript files passed `node --check`.
- `node frontend/tests/run-tests.js`: passed with 99 registered screens.
- `git diff --check`: passed; Git emitted only the repository's expected LF-to-CRLF checkout notices.
- The current WinForms process was detected, but the Windows automation surface returned no targetable apps; authenticated desktop interaction was not claimed.
