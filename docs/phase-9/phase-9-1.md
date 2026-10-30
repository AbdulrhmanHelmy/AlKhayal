# Phase 9.1 — persisted transaction reports

Implementation and automated validation: 2026-10-30. All four report implementations are present and pass the validation below. **Acceptance status: PARTIAL pending rendered Arabic RTL Light/Dark desktop inspection.** Automatic approval review rejected the temporary browser-host launch with `blocked by policy`; Browser Use subsequently rejected the local visual fixture because its URL policy permits only HTTP/HTTPS. Neither rejection is a test pass. No browser-policy workaround was attempted after that rejection.

This extends the original Sales Report service/store/renderer/tests. No Phase 9.2/9.3 calculations, charts, dependency, framework, migration, export system or printing engine were added.

## Screens and navigation

The canonical reports group contains exactly these four entries:

| Screen | Frontend route | Renderer | GET API |
| --- | --- | --- | --- |
| تقرير المبيعات | `report-sales` | `sales-report` | `/api/reports/sales` |
| تقرير المشتريات | `report-purchases` | `transaction-reports` | `/api/reports/purchases` |
| تقرير المرتجعات | `report-returns` | `transaction-reports` | `/api/reports/returns` |
| تقرير وسائل الدفع | `report-payment-methods` | `transaction-reports` | `/api/reports/payment-methods` |

All four are enabled in the shell and registered with executable renderers, normal sidebar/search, effective permissions and closable tabs. No duplicate Settings entry was added. Existing instrument management is reused; its shell registration was completed so payment-history drill-down resolves to its renderer rather than the construction screen.

`report-ui.js` shares date presets, exact string summaries, cards, permission-aware links and pagination. Scoped styles reuse card/table/theme tokens, use intrinsic sizing and four/two/one filter columns at the existing 1050/560 breakpoints, and wrap large totals. Workspace retains vertical scroll ownership. No charts, browser printing, extra viewport heights, scaling, polling or remote resources were introduced.

Each report defaults to month-to-date, with Today, Last 7 Days (today plus six previous dates), Current Month and Custom From/To. Date edits select Custom. Apply resets paging; Reset returns to month-to-date/page one. Search inputs remain mounted. All screens have loading, empty, error/retry and server pagination. Generation/active-screen checks suppress stale responses. Counts and summaries cover the entire matching set, including when the selected page is empty.

Links use existing contracts: `sales-management` with `saleInvoiceId`, `purchase-management` with `invoiceId`, `payment-history` with `instrumentId`. Returns open the original saved invoice. Purchase return references are actual saved IDs: no separate purchase return number exists. Instrument history selects the requested identity and keeps its existing invoice-date/all-dates semantics; it does not claim to share the report's payment-event dates. A report grant does not grant editor/history access: references become explanatory text when the target screen is unavailable, rather than dead buttons or permission bypasses.

## API contract

Responses retain `{ success, data, error }`. Monetary DTO fields are invariant decimal strings with four places. IDs/counts are integers. JavaScript performs no report money arithmetic or unsafe scaled-integer conversion.

Common parameters: `from`/`to` are inclusive local ISO calendar dates (`yyyy-MM-dd`); `search` is optional, maximum 120 characters; `page` defaults to 1, accepts 1..1,000,000; `pageSize` defaults to 20, accepts 1..100. Both dates omitted means server-local month-to-date. A single missing date, malformed/reversed range, unsupported type/method, nonpositive identifier, oversized filter or invalid primitive/page returns HTTP 400. Search uses parameterized SQL LIKE; `%`/`_` retain SQLite wildcard meaning. Internal UTC bounds are computed by Application, never bound from HTTP.

| API | Additional filters | Search target | Main result fields |
| --- | --- | --- | --- |
| sales | `method=cash/wallet/card/credit/unclassified`, positive `cashierId` | Invoice reference | `count,total,returns,cancellations,netEvents,cash,wallet,card,credit,unclassified,items` |
| purchases | positive `supplierId`, `party` (supplier name, max 120) | Purchase reference | `count,total,returns,netEvents,items` |
| returns | `type=sales/purchase`, `party` (customer/supplier, max 120) | Return/original invoice reference | `count,salesReturnCount,purchaseReturnCount,salesReturns,purchaseReturns,items` |
| payment-methods | `type=wallet/card` | Current instrument name/number/provider | `count,cash,wallet,card,credit,unclassified,instruments` |

The transaction result DTO is shared by the three new reports: irrelevant monetary fields are null and irrelevant collections empty. UI displays only fields defined for its report. Payment count means configured instruments, not payment transactions. Instrument rows contain `id,type,name,number,isActive,invoiceCount,total`. Transaction rows preserve `id,invoiceId,reference,invoiceNumber,eventUtc,party,type,total,status,paymentMode` where applicable.

## Date and recognition policy

Standalone single-PC reporting uses host `TimeZoneInfo.Local`; browser/server run on the same PC. UTC storage is unchanged. Each calendar boundary converts independently to `[startUtc, nextDayAfterToUtc)`. There is no fixed offset: days may contain 23/25 hours. A skipped midnight advances to its first valid instant; a repeated midnight uses its earliest UTC occurrence and includes both repetitions. No LAN timezone configuration is implied. Reproducibility depends on retaining the intended PC timezone/rules; no historical business-timezone configuration is introduced.

| Fact | Timestamp |
| --- | --- |
| Posted sale/purchase invoice value | Original `PostedUtc` |
| Sales return | `SalesReturns.CreatedUtc` |
| Posted purchase return | `PurchaseReturns.PostedUtc` |
| Sales cancellation | `SalesCancellations.CreatedUtc` |
| Immediate invoice payment | `SalesPayments.CreatedUtc`, event `cash_sale` only |
| Separate credit invoice value | Sale `PostedUtc`, never a collection |

Saved invoice business dates are separate facts: a backdated invoice posted today belongs to today's posting report. Sales displays both business date and posting time. Current cancellation status never rewrites a historical posting period. Events exactly at the exclusive end belong to the next interval.

## Sales definitions

The original `SalesReportService`/`SqliteSalesReportStore` are retained. Only `SalesInvoices.Status='posted'` contributes invoice count/value; drafts are excluded. `total` sums original saved `TotalScaled`, including saved tax/discount effects. Method cards partition gross invoice value by original settlement method. They are not collections, outstanding receivables, net-of-tax revenue or profit.

Returns use saved gross refund amounts on their event dates. Cancellation amount is the original invoice gross once per cancellation event. `netEvents = posted invoice gross - period gross returns - period gross cancellations`. Events may relate to older invoices, so period net can be negative. Original canceled invoices remain posted; the query never both removes their original posting and subtracts cancellation. Current canceled status is displayed separately.

Reference/method/API cashier filters scope original invoices and their event totals. The table shows reference, saved business date, posting time, current customer/cashier labels, method, saved total and current cancellation status; ordering is posting time descending then invoice ID descending. Current catalog prices, current tax settings, later collections and FIFO layers never join these amounts.

## Purchases definitions

Posted `PurchaseInvoices` joined to `Suppliers` provide saved invoice count/value and supplier/reference filters. Amounts use saved `TotalScaled`, never today's catalog cost. Purchasing has no separately saved tax component; labels therefore say saved document total rather than inventing purchase tax. Original cash/credit settlement mode is shown explicitly as original mode, not current supplier outstanding balance.

Posted purchase returns are recognized separately by return posting time. `netEvents = posted purchase value - period posted purchase returns`. Draft invoices/returns are excluded. No nonexistent purchase cancellation event is inferred. Ordering is posting time then ID descending. Purchase expenditure is not FIFO COGS and is not subtracted from sales for profitability.

## Returns definitions

The query UNION ALLs actual successful `SalesReturns` with posted `PurchaseReturns`. It preserves original invoice IDs/references and current customer/supplier labels. Type/reference/party filters precede database count, sum and paging. Sales/purchase counts and amounts stay separate. One actual header survives per repeated/partial return; no line/payment/allocation join multiplies header amounts. Ordering is event time descending, type ascending, return ID descending.

Values are persisted gross refund/account amounts, including any original tax embedded in the refunded amount. No returned tax is invented and no old return is recomputed with today's tax settings.

## Payment classification

Sales invoice classification prefers saved `SalePaymentSelections` (credit mode remains credit). Without selection, exactly one historical immediate payment explicitly cash/card can establish that method. Missing/ambiguous evidence and old unassigned transfers remain unclassified. Legacy invoice mode `cash` means immediate settlement and does not prove physical cash.

Payment Methods sums actual `SalesPayments.AmountScaled` for `cash_sale` events on posted immediate invoices using payment timestamps. Saved selection supplies cash/wallet/card and instrument identity; otherwise explicit cash/card payments establish their own category and transfers remain unclassified. Later `customer_collection` and refund events are excluded. Invoice totals never substitute for payment amounts. Credit is separate original posted invoice value, including invoices later canceled: not outstanding debt or collected cash. Original immediate payments are not silently netted against later refunds.

Configured wallets/terminals survive a left join, including inactive/zero-activity instruments. Rows show type, current name, string number (leading zeros preserved), active status, distinct linked invoice count and actual attributed immediate-payment total. Payments aggregate before joining the paged directory. Renames retain saved identity/amounts; earlier names remain in the existing audit, while report labels are current. No unavailable historical name snapshot is claimed.

Type/search filters scope both directory and summary. Filtered electronic instruments cannot match cash/credit/unassigned, so those cards are zero while instrument filters apply. Unfiltered summaries include all immediate categories and separate credit value. These are recorded invoice payments, never provider/bank/settlement/drawer balances. No drawer/shift accounting or treasury mutation changed.

## Architecture, security and performance

Application owns validation, timezone conversion and read ports; Data owns parameterized SQLite queries; API authenticates/checks access/maps requests/translates validation; Server.Host composes services. No business code entered WinForms. Each count/summary/page shares one short read transaction. Stores contain no DML, posting or recalculation. Empty and populated fixture tests compare complete database bytes before/after report reads.

Sales event queries now join event headers to filtered original invoices instead of the original per-invoice correlated return sum. Other one-to-many financial sources aggregate independently. Count/sum and LIMIT/OFFSET remain in SQLite. No unbounded invoices are materialized in Application/JavaScript. Existing indexes remain; no speculative index migration was added. The focused fixture with 30,000 sales/payments, 10,000 purchases and 10,000 return headers queried all four reports in **429 ms** on this development machine, not a low-resource/Windows 7 benchmark.

Every endpoint requires a server session. Admins can read all reports; cashiers require that exact effective `report-*` permission. Direct unauthenticated/denied requests return 401/403. Invalid primitive binding is checked via ModelState and returns 400. GET is the only report verb (POST returns 405). Report permission does not grant another report or document mutation. Existing sessions/roles/cookies are reused.

## Actual validation

All databases are uniquely named disposable temporary files. Test processes additionally set `ALKHAYAL_DATABASE_PATH` under repository `.tmp`. The normal `%LOCALAPPDATA%\AlKhayal\alkhayal.db` was not opened or modified by this task. Build/test outputs are isolated from the running desktop. Separate integration/unit TRX names prevent cross-assembly overwrite.

| Check | Result |
| --- | --- |
| Focused reports + HTTP | **18 passed**, 0 failed/skipped; `reports-focused-final.trx` |
| Focused timezone/date unit | **19 passed**, 0 failed/skipped; `report-period-final.trx` |
| Full integration | **279 passed**, 0 failed/skipped; `integration-full.trx`, 2m41s |
| Full unit | **130 passed**, 0 failed/skipped; `unit-full.trx`, 12s |
| All four frontend report handlers | Passed presets/custom/reset, summaries, tables, pages, loading/empty/error/retry, document state/permissions, stale results |
| Full frontend suite | Passed, **112 registered routes** |
| JavaScript syntax | Passed **33 application files** plus `report-tests.js` |
| Full solution build | Passed, **0 warnings / 0 errors**, including WinForms UI |
| Diff whitespace | Passed; existing CRLF notices on unrelated files remain |
| Rendered browser/native inspection | **Not executed: blocked as described above** |

Focused integration covers posted/drafts, all methods/credit, explicit legacy cash/card versus unassigned transfers, exact full-set/page summaries, reference/cashier/supplier/type/party filters, later returns/cancellations without historical rewrites, repeated partial returns/original links, real purchase posting/returns, real inclusive/exclusive sales tax and invoice discounts, catalog/tax edits after posting, inactive/renamed instrument identities, payment-vs-posting timestamps, exclusion of later credit collections, byte-for-byte nonmutation, reopening/migration persistence and large server pages. All four HTTP endpoints are tested with anonymous, denied cashier, granted cashier and admin, invalid parameters/binding, cross-report denial and no mutation escalation. The first HTTP attempt used a wrong test-only permission URL (`/screens`); correcting it to the existing `/screen-permissions` contract fixed the test failure.

Timezone unit tests cover leap/month/year boundaries, positive/negative offsets, 23/25-hour days, skipped/repeated midnight, invalid dates/ranges/pages and month defaults. Full suites also execute existing inventory/FIFO/pricing, treasury/shifts, purchasing, authentication, maintenance, installments and payment-instrument regressions.

Reproduce from repository root (results under `artifacts/phase-9-1/results`):

```powershell
$env:ALKHAYAL_DATABASE_PATH=Join-Path $PWD '.tmp/phase-9-1-guard.db'
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/phase-9-1 --filter 'FullyQualifiedName~SalesReportTests|FullyQualifiedName~TransactionReportTests|FullyQualifiedName~Reports_http' --logger 'trx;LogFileName=reports-focused-final.trx' --results-directory artifacts/phase-9-1/results -m:1
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/phase-9-1 --filter FullyQualifiedName~ReportPeriodTests --logger 'trx;LogFileName=report-period-final.trx' --results-directory artifacts/phase-9-1/results -m:1
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/phase-9-1 --logger 'trx;LogFileName=integration-full.trx' --results-directory artifacts/phase-9-1/results -m:1
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/phase-9-1 --logger 'trx;LogFileName=unit-full.trx' --results-directory artifacts/phase-9-1/results -m:1
node frontend/tests/report-tests.js
node frontend/tests/run-tests.js
Get-ChildItem frontend/js -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName; if($LASTEXITCODE -ne 0){throw 'JavaScript syntax failed'} }
node --check frontend/tests/report-tests.js
dotnet build AlKhayal.slnx --artifacts-path artifacts/phase-9-1 --no-restore -m:1
git diff --check
```

## Remaining acceptance and Phase 9.2 handoff

The persisted backend/API/registry and executable frontend behavior are verified. Remaining acceptance is rendered RTL Light/Dark, long-label/focus/scroll/tab inspection at 1366x768 and a narrow viewport with an isolated database/profile. DOM tests and shared theme tokens do not constitute visual inspection. Windows 7 SP1/Chromium 109, low-resource hardware and physical-device checks were not performed. Code retains net48/netstandard2.0 and vanilla Chromium 109-compatible JavaScript without new browser APIs, frameworks or remote assets. WebView2 runtime/cache/DPI policies remain intact.

CSV/export and report printing are intentionally deferred as allowed. No provider settlement, historical price repair, LAN timezone feature, COGS, valuation, top products, stock shortage, realized profit or Phase 9.3 work was added.

Before Phase 9.2 profitability, approve partial-return tax/discount allocation. Return headers persist gross refunds and original-line/FIFO links but no independent returned tax/discount components. Proposed only: allocate original saved components by cumulative returned quantities, respect previous partial returns, take exact final residuals on the final eligible return and reconcile each component to original persisted values. Do not use today's tax rates or rewrite history. The preserved [pricing/FIFO audit](pre-phase-9-pricing-fifo.md) remains the handoff. Trustworthy gross transaction/payment sources are ready; net-of-tax realized-profit policy remains unapproved.

No delegation/OpenCode/subagent, commit, push, branch creation/switch/merge, reset/discard, production reseed or real AppData database mutation occurred. Unrelated existing changes remain. Task files: Application/Data `Reports/`; `ReportsController` and API/host composition; report helper/renderers, registry/shell/script loading, scoped styles, selected payment-history route state; `TransactionReportTests`, `ReportsApiTests`, `ReportPeriodTests`, `report-tests.js` and registry checks. Relevant architecture/database/API/design/testing documents link here.
