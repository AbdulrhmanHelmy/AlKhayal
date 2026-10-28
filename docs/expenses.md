# Expense management and disbursement vouchers

The Expenses and Vouchers section exposes exactly two destinations: `expenses-list` (إدارة المصروفات) manages saved expense types, and `expense-add` (سند صرف) records an expense against one of them. Old `expense-categories` and `financial-movement` routes are hidden compatibility redirects. Quick access opens the real voucher page. No mock expenses are imported.

## Workflow

Add an expense type by name, then choose it on the voucher together with a cashbox/bank, amount, business date, optional payee and notes. The account balance is visible. Unknown/free-text types cannot be posted. Category creates repeated with the same trimmed, FormKC/uppercase-normalized name return the existing category. Category editing/deleting was not requested.

The voucher page shows the latest 200 saved vouchers, newest creation first, with number, business date, category, account, amount, payee and notes. Posted vouchers are immutable. Success clears amount/payee/notes, retains type/account/date, refreshes balance/history and displays the document number. Loading failures have retries. Drafts freeze uncertain submissions for retry with the same payload/key, including tab changes during a pending request; definitive 4xx responses release fields for correction.

## Persistence boundary

`ExpenseService` owns validation, exact monetary conversion and canonical actor-bound SHA-256 request hashes. `IExpenseStore` is its port; `SqliteExpenseStore` owns short writer transactions. `ExpensesController` handles session, screen grants, CSRF and HTTP errors. Host composition injects dependencies; the frontend uses same-origin APIs only.

Additive embedded migration `028_expenses.sql` creates `ExpenseCategories` and `ExpenseVouchers` without changing historical rows. Categories retain creator/UTC creation. Vouchers retain category/account name snapshots, foreign keys, actor, UTC posting time, date-only local business date, unique operation identity/hash and unique treasury movement source. Amounts are scale-4 integers restricted to two fractional user digits. Voucher rows reject update/delete; category/account indexes support foreign-key lookup.

Posting acquires the writer lock before checking identity, sources and funds. It reuses the treasury transaction writer for the withdrawal/audit/account version, then inserts the voucher. All changes commit together; insufficient funds, source conflicts and late failures roll back everything. Identical actor-bound retries replay after restart; changed payload or actor conflicts. Treasury operation keys have an `expense:` prefix; a preexisting independent movement cannot be claimed by a new voucher.

This feature records the expense and cash/bank effect. Dashboard sample totals, tax/accounting reports, reversals and printing retain separate existing boundaries.

## API

All routes use `{ success, data, error }` and require a session. Mutations require `X-CSRF-Token`.

- `GET /api/expenses/categories`: saved `{ id, name }`; requires `expenses-list` or `expense-add`.
- `POST /api/expenses/categories`: `{ name }`, required and at most 160 characters; requires `expenses-list`, returns 201.
- `GET /api/expenses/vouchers`: latest 200 documents; requires `expense-add`.
- `POST /api/expenses/vouchers`: `{ categoryId, treasuryAccountId, amount, expenseDate, payee?, notes?, operationKey }`; requires `expense-add`. Amount is positive invariant decimal text, at most two fractional digits; date is `yyyy-MM-dd`. Payee max 200, notes max 1000, operation key required/max 100. Returns 201 saved/replayed voucher.

Responses include `id`, `number`, category/account IDs and name snapshots, `treasuryMovementId`, `amount` text, `amountScaled`, date, payee, notes, actor, UTC creation and `replayed`. Request hashes are not returned. Missing sources return 404, invalid input 400, insufficient funds/identity conflicts 409, and logged SQLite failures 503 with a same-request retry message. `expense-add` grants treasury lookup without manual deposit/withdrawal or account-management access.

## Validation and files

The shell also gates completed screens through `implementedRoutes` in `frontend/js/app.js`. Both `expenses-list` and `expense-add` must be included or the shell displays the construction placeholder despite a registered renderer. The expense handler suite now resolves both canonical screens through the actual shell dispatcher before executing them; it reproduced this failure before the route-list fix and passed afterward. This regression requires frontend changes only; refresh/reopen the page to load the updated shell.

On 2026-10-28:

- `dotnet test AlKhayal.slnx --no-restore -m:1 --logger 'console;verbosity=minimal'`: 184 integration + 50 unit tests passed. New `ExpensePersistenceTests` and `ExpenseApiTests` cover restart, category replay, exact money, immutable history, actor/payload identity, permissions/CSRF, missing sources, insufficient funds, rollback and concurrency. Migration-count assertions/legacy-fixture exclusions include 028.
- `node --check frontend/js/modules/expenses.js` and `node frontend/tests/run-tests.js`: passed, including new `expense-tests.js` and all 101 routes.
- `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj --no-restore -m:1 -p:OutDir=D:/k/AlKhayal/.tmp/expense-validation/`: succeeded, zero warnings/errors, separate output to avoid running-desktop DLL locks.
- `git diff --check`: passed. Tests report NU1900 because NuGet vulnerability metadata was unavailable.

Changed implementation files are the new Application service/port, SQLite adapter/migration, API controller, API/host composition and treasury lookup permission; frontend `expenses.js`, `screens.json` and `index.html`; executable tests, migration fixtures and architecture/design/database/API/testing docs. No new package/runtime target. Chromium 109-compatible JS is retained; native visual/DPI and Windows 7 runtime checks were not performed. Tests use disposable databases; the operator database was not opened or modified.

Close the running desktop/server and run `dotnet run --project src\AlKhayal.UI` to copy updated binaries and apply migration 028 through normal startup.
