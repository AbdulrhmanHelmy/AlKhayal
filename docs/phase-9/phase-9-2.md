# Phase 9.2 — Financial and inventory reports; final acceptance

Phase status: **COMPLETE AUTOMATED IMPLEMENTATION, PARTIAL VISUAL ACCEPTANCE**. All seven Phase 9.2 reporting capabilities are implemented: preserved Net Profit and Expense Voucher reports plus Product Profit, Inventory Valuation, Top Sellers, Availability and Inventory Movements. Full final regression passed **309 integration + 143 unit tests** with zero failed/skipped; frontend passed **119 screens**. Native rendered Light/Dark, low-resource and Windows 7/Chromium 109 acceptance remain **UNVERIFIED**. Existing Phase 9.1 definitions and scoped Net Profit accounting are preserved; this is not a complete company-wide P&L or release-readiness claim.


## Final five-report implementation — 2026-10-30

This section supersedes the earlier statements that the five reports remain undelivered; the previous financial audit, Net Profit implementation, expense classification and historical validation records below are retained. Final automated acceptance results are recorded separately below. Rendered/native acceptance remains outstanding.

| Report | Frontend route | GET endpoint |
| --- | --- | --- |
| Product gross realized profit | `report-product-profit` | `/api/reports/product-profit` |
| Current inventory valuation | `report-inventory-valuation` | `/api/reports/inventory-valuation` |
| Top-selling products | `report-top-products` | `/api/reports/top-products` |
| Current availability / zero stock | `report-low-stock` | `/api/reports/low-stock` |
| Saved inventory movements | `report-inventory-movements` | `/api/reports/inventory-movements` |

### Contracts, validation and access

`StockReportQuery`, `StockReportService` and `IStockReportStore` live in Application; `SqliteStockReportStore` and the product partial of the existing `SqliteNetProfitStore` live in Data. Host composition supplies the service to the existing thin `ReportsController`. The frontend uses only same-origin `AlKhayalApi.request`. No dependency, writer, schema migration or Phase 9.3 feature was added.

All endpoints accept `search` (product/current variant/SKU, max 120), optional positive `categoryId`, `page` (1..1,000,000, default 1) and `pageSize` (1..100, default 20). Category is the **current catalog category**, not a historical category snapshot. Movement/current reports support positive `warehouseId`; product reports reject it because no warehouse attribution is claimed. Product reports accept `sort=quantity|amount`, descending net base quantity by default, or saved net gross amount; stable variant ID breaks ties. Movement filters accept `type` from the actual source types below and `reference` (max 120). Current reports accept `status=zero|available|incomplete`; availability defaults to zero-stock in the UI and all its assigned active rows at the API. Primitive binding and validation failures return 400. Report counts and summaries cover the complete matching scope, independent of the requested page. Empty pages preserve full-scope totals.

`StockReportResult` contains `count`, `summary`, `items` and Arabic `scope`. Applicable monetary strings retain four decimal places; quantities are decimal strings in base units with up to six places. Unknown monetary fields remain null (omitted by the existing API null serializer); the frontend displays `غير مكتمل`. `summary.complete` and `incompleteCount` disclose missing evidence. Each product row identifies stable `productId`/`variantId`, current labels, active SKU, base unit, sold/returned/cancelled/net quantities, original gross recorded amount, tax-exclusive recognized revenue, FIFO cost, gross profit and margin where applicable. No heterogeneous quantity grand total is shown.

Every endpoint authenticates the server session and checks its **own** `report-*` effective screen grant. No default cashier grant was introduced. Product Profit and Inventory Valuation remain separate sensitive permissions. Application strips cost/profit/revenue/price/valuation fields from Top Sellers and Availability responses, including unused JSON fields; granting either report therefore does not disclose the other financial reports. Top Sellers retains only its required original recorded gross amount. Report grants do not grant invoice viewing/editing. Links require the destination permission; otherwise references are readable text. Real HTTP tests exercise independent grants for each of the five reports, cross-report denial, anonymous rejection, admin success, malformed filters, GET-only verbs and read-only bytes.

### Product gross profit and Top Sellers

The product query reuses the existing Net Profit **same SQL evidence CTE and same streamed return allocation implementation**, rather than a second revenue/tax/cost algorithm. Saved line gross less original saved tax supplies recognized revenue. Independently aggregated `InventoryCostAllocations` supply original issue cost and lineage checks. Original return history uses `ReturnTaxAllocation` and saved `SalesReturnCostAllocations`; earlier partials affect cumulative allocation but are not recognized again. Only relevant original-line return history is streamed, restricted by SQL product filters. Financial totals, grouping, sorting, count and visible identities are computed in SQLite; only at most one page of product aggregates is retained while the shared return stream updates page figures and full-scope summary.

Sales are recognized at original `PostedUtc`, returns at actual return `CreatedUtc`, eligible cancellations at actual cancellation `CreatedUtc`. Cancellations reverse original revenue/cost once and do not remove the original posting from an earlier period. Original snapshot conversions supply base quantities, including box sales and partial box returns. Grouping uses saved variant identity, never a current price/name. Archived/inactive historical sellers remain in historical reports. Current labels/categories are explicitly disclosed as filters/labels. Filtered scope warns that the general unfiltered Net Profit total includes other products and cannot be compared directly. Unattributable no-line sale/return headers are counted separately and suppress a falsely complete product total.

Product profit is **gross realized revenue minus actual FIFO COGS before general expenses**. Rent/salary/electricity are not allocated to products. Margin is absent for zero/negative net revenue, while zero and negative profits remain valid. Unknown revenue/cost suppresses the affected row/summary amount rather than substituting zero. A missing original inventory-unit snapshot also makes the quantity columns unknown, orders the unknown net quantity after known quantities and discloses incomplete coverage; it never fabricates zero sales. The saved original gross amount remains independently available. Product summaries reconcile with the sales-derived revenue/COGS/gross component of Net Profit for the same unfiltered period. Top Sellers uses sold minus actual returns minus cancellations, ordered by net base quantity, with saved net gross sales amount including original tax. Collections, installments, stock popularity and current catalog prices do not generate sales.

The approved example passes: 10 at 100 + 10 at 120, sale of 12 at 200 -> revenue 2400, FIFO COGS 1240, realized gross profit 1160; stock 8, remaining cost 960, current selling value 1600, expected gross margin 640. Subsequent catalog edits change current valuation only, never original product profit. Repeated inclusive/exclusive discounted cash returns and later-only periods reconcile with the unchanged Net Profit allocator; an 11-unit return restores 1120 across the original layers. Converted units, variants, stable equal-quantity paging, amount ordering and category/search scopes have executable coverage.

### Current inventory valuation and availability

Valuation is a **current snapshot**, not an as-of historical report. There are no date controls; supplying `from`/`to` returns 400. Rows group actual persisted balance batches by warehouse and variant. Shared `SqliteInventoryStore.ValuationEvidence`, `CostComplete` and `SellingScaled` are also used by the existing inventory valuation endpoint. Each batch's exact remaining layer quantity/value must reconcile to its saved balance; unresolved or null acquisition cost makes that basis incomplete. Current applicable variant override/base price determines potential selling value using the existing decimal conversion and piastre-rounding policy per balance batch. Rounded displayed unit cost is never multiplied back into actual inventory cost. Streaming sums use exact decimal arithmetic and bounded memory; SQL filters/counts/pages precede presentation. Current unit prices are per product base unit.

Expected gross margin = current potential selling value minus exact remaining FIFO value. It is **not realized profit** and is never added to Net Profit. If any matching row has an incomplete cost basis, the full matching cost/profit total is unknown; complete-looking partial sums are not advertised as complete totals. The row and summary expose completeness. Valuation retains actual owned stock even if its catalog/warehouse is now inactive; availability intentionally restricts to active, unarchived stock products/variants and active warehouses. No feature toggles, stock configuration or balances are modified by reports. Existing data remains visible by stable identity rather than manufacturing assignments from feature configuration.

There is **no authoritative minimum/reorder threshold in the current schema**. Availability therefore honestly reports zero-stock or available status, not below-minimum/shortage quantities. Its scope contains only previously recorded `InventoryBalances` warehouse/variant assignments (including balances now zero). It does not cross join all products with all warehouses and does not claim to include products never stocked anywhere. The legacy hidden `low-stock` route redirects to the real report, and the existing quick-access tile targets the new canonical report. A future threshold editor would be an explicit separately designed enhancement, not a speculative migration here.

### Inventory movement source policy

The report reads actual `StockMovements.CreatedUtc`, saved signed base quantity and saved movement value. It joins unique inventory-document source links to classify purchase receipts (`purchase`), sales issues (`sale`), sales returns (`sales-return`), purchase returns (`purchase-return`) and cancellations (`cancellation`); remaining valid inventory types retain their saved `opening`, `manual_receipt`, `manual_issue`, `adjustment`, `count`, `transfer`, `reversal` or `projection_rebuild` identity. No events are manufactured. Transfer sides remain separate outgoing/incoming warehouse rows; their signed recorded values net to zero across both warehouses, not new revenue/purchases. Missing cost support is disclosed; absent display unit cost alone does not erase saved FIFO allocation/return values.

SQL filters, count, signed value sum and stable `CreatedUtc DESC, Id DESC` pagination share the same read snapshot. No universal in/out quantity total is shown across heterogeneous base units. References navigate only to existing sales-management (`saleInvoiceId`) or purchase-management (`invoiceId`) contracts; return/cancellation references open their original invoice. Generic inventory/count/adjustment documents have readable inventory references because a document-specific route is not supported. The separate raw inventory document type remains visible alongside the business movement classification. No report revalues historical movements with current catalog purchase prices.

### Frontend and compatibility acceptance

`stock-reports.js` registers all five routes with the canonical reports sidebar group and existing closable tabs. It reuses `report-ui.js` date presets, cards, pager, escaped labels and permission-aware links. Historical screens retain today/yesterday/last seven days/current week/current month/previous month/custom ranges. Application reuses local `ReportPeriod` DST conversion, never a fixed UTC offset. Current screens have no misleading date range. Mounted search/filter nodes survive requests; generation and active-screen checks discard stale successes and failures. Loading/error/retry/empty states and full-scope summaries are covered by executable frontend tests. Search, category/warehouse identifiers, status/type/order dropdowns, compact tables and cards are intentionally simple. No charts, graph libraries, framework, export, print dialog, remote assets, extra viewport owner or new browser APIs were added.

The current automated environment targets installed Windows/.NET Framework 4.8 and the existing Chromium-109-compatible frontend syntax. Actual rendered 1366×768 Arabic RTL Light/Dark inspection, WebView2 native behavior, real low-resource measurements and Windows 7/Chromium 109 distribution/runtime qualification remain **UNVERIFIED**. No running desktop was stopped. Phase 9.3 has not started; its handoff must retain those manual acceptance items, scoped Net Profit coverage and the known discounted credit-return `sales_conflict` limitation. Failed/unposted return attempts create no recognized reporting event. This task does not alter the unrelated return writer or claim a complete company-wide P&L.

## Final automated validation

All final runs completed on 2026-10-30. TRX counters were read after completion: zero failed, skipped, aborted, pending or not-executed tests. Focused totals overlap the full suites and must not be added to them.

| Executed check | Final result | Evidence under `artifacts/phase-9-2-final/results` |
| --- | --- | --- |
| Five reports + HTTP + converted-unit case + preserved Net Profit integration | **29 passed**, 0 failed/skipped | `stock-profit-focused-final-quantity.trx` |
| New Application query rules + preserved original-tax/Net Profit service unit tests | **13 passed**, 0 failed/skipped | `stock-unit-focused-accepted.trx` |
| Full integration suite | **309 passed**, 0 failed/skipped, about 3m14s | `integration-full-final-quantity.trx` |
| Full unit suite | **143 passed**, 0 failed/skipped, about 12s | `unit-full-final-current.trx` |
| Complete frontend registry/behavior suite | **119 screens passed** | `node frontend/tests/run-tests.js` |
| All production/test JavaScript syntax | **51 files passed** | `node --check` loop below |
| Full solution including WinForms, isolated output | **0 warnings, 0 errors** | final build command below |
| Working-tree whitespace | **passed** | `git diff --check`; only existing unrelated line-ending notices |

The 29 focused integration cases comprise 13 new product/stock/HTTP cases, the extended converted-unit pricing/FIFO case, and 15 preserved Net Profit/HTTP cases. Product/valuation/availability/movement checks overlap within those lifecycle cases; they are not five independent suites whose counts can be summed. Coverage includes all five endpoints under actual OWIN HTTP authorization and server-side removal of sensitive fields from lower-privilege report contracts. Full integration reruns Phase 9.1 reports, payment instruments, original tax returns, expense classification/audit, daily/monthly Net Profit, sales returns/cancellations, FIFO, purchases, maintenance and installments. New report reads compare saved database bytes or projection/source evidence to establish nonmutation. Catalog edits and test-only event redating occur only in isolated disposable fixtures, never through report GETs.

Initial focused failures exposed the cancellation table's `SalesInvoiceId` key (not `Id`) in the new movement query/fixture; this was corrected. Later fixture setup was corrected to respect disabled transfer UI features and piastre input policy while exercising scale-4 residuals with fractional quantity. A test compilation namespace error was corrected. Fresh isolated build outputs initially needed package restore. One overlapping final-build attempt produced six transient copy retries while the isolated testhost held its xUnit DLL; after that test completed, the full solution was built again with **zero warnings/errors**. These intermediate results are not counted as passing acceptance. No running desktop process was stopped.

Commands executed from the repository root (the initial build also restored the new artifact directory):

```powershell
$env:ALKHAYAL_DATABASE_PATH=Join-Path $PWD '.tmp/phase-9-2-final-guard.db'
dotnet build AlKhayal.slnx --artifacts-path artifacts/phase-9-2-final -m:1

dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/phase-9-2-final --no-build --no-restore --filter 'FullyQualifiedName~StockReportTests|FullyQualifiedName~Stock_reports|FullyQualifiedName~Five_stock_reports_HTTP|FullyQualifiedName~Converted_sale_and_partial_return|FullyQualifiedName~NetProfitReportTests|FullyQualifiedName~Net_profit_HTTP' --logger 'trx;LogFileName=stock-profit-focused-final-quantity.trx' --results-directory artifacts/phase-9-2-final/results -m:1

dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/phase-9-2-final --no-build --no-restore --filter 'FullyQualifiedName~StockReportServiceTests|FullyQualifiedName~ReturnTaxAllocationTests|FullyQualifiedName~NetProfitServiceTests' --logger 'trx;LogFileName=stock-unit-focused-accepted.trx' --results-directory artifacts/phase-9-2-final/results -m:1

dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/phase-9-2-final --no-build --no-restore --logger 'trx;LogFileName=integration-full-final-quantity.trx' --results-directory artifacts/phase-9-2-final/results -m:1
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/phase-9-2-final --no-build --no-restore --logger 'trx;LogFileName=unit-full-final-current.trx' --results-directory artifacts/phase-9-2-final/results -m:1
node frontend/tests/run-tests.js
$reportJsFiles=@(Get-ChildItem -LiteralPath frontend/js -Recurse -Filter *.js)+@(Get-ChildItem -LiteralPath frontend/tests -Recurse -Filter *.js)
foreach($reportJsFile in $reportJsFiles) { node --check $reportJsFile.FullName; if($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
dotnet build AlKhayal.slnx --artifacts-path artifacts/phase-9-2-final -m:1 --no-restore
git diff --check
```

Main files changed for final completion (the substantial pre-existing working tree was preserved):

- `src/AlKhayal.Application/Reports/StockReports.cs`: query/result contracts, validation, independent lower-privilege report projections.
- `src/AlKhayal.Data/Reports/SqliteNetProfitStore.cs` and new `.Products.cs` partial: shared original evidence/return allocation with paged stable product aggregation.
- `src/AlKhayal.Data/Reports/SqliteStockReportStore.cs`; shared evidence/calculation extracted in `Inventory/SqliteInventoryStore.Valuation.cs`.
- `src/AlKhayal.Api/Controllers/ReportsController.cs`, `ApiBootstrap.cs`, `src/AlKhayal.Server.Host/AlKhayalServer.cs`: five adapters and service composition.
- `frontend/js/modules/stock-reports.js`, `frontend/data/screens.json`, `frontend/index.html`, `frontend/js/app.js`: five renderers/routes, canonical navigation and availability compatibility route.
- `tests/AlKhayal.IntegrationTests/StockReportTests.cs`, `StockInventoryReportTests.cs`, `StockReportsApiTests.cs`, extended `PricingFifoInvariantTests.cs`; `tests/AlKhayal.UnitTests/StockReportServiceTests.cs`; `frontend/tests/report-tests.js`, `run-tests.js`.
- This document and applicable additions to `docs/api-contracts.md`, `database.md`, `architecture.md`, `design.md`, `testing.md`.

The final missing-unit-snapshot regression passed before rerunning the complete integration suite; the frontend renders null quantities as unknown using its existing value formatter.

No additional migration, historical financial rewrite or speculative threshold schema. No OpenCode/GLM/Gemini, subagent delegation, Git commit/push/branch creation/switch/merge/reset, destructive existing-work changes or normal AppData SQLite access. Tests use uniquely named temporary SQLite fixtures and an additional process database override. `%LOCALAPPDATA%\AlKhayal\alkhayal.db` is untouched. All seven Phase 9.2 capabilities now meet automated acceptance; native/manual acceptance and documented wider financial-source limitations remain explicit. Phase 9.3 was not started.

## Original return allocation integration

`SqliteNetProfitStore` now calls the existing `ReturnTaxAllocation` helper. It loads the original line quantity, final gross, tax, line discount, allocated invoice discount and original inventory unit conversion. It restricts history loading to original lines with returns in the selected period, streams rows ordered by original line / return timestamp / return ID / line ID, and retains earlier partials up to the selected end. Earlier partials determine cumulative allocations but do not enter the current period's totals. Future events never change the selected period's allocation.

Original final gross already incorporates the saved inclusive/exclusive treatment. Original tax is subtracted from that gross; current tax settings, rates, catalog prices and discounts are never consulted. A zero-tax original line remains distinct from a taxable line on the same invoice. Header/line gross, tax, invoice discount and subtotal reconciliation must hold before original revenue is accepted. Missing lines or inconsistent components are incomplete evidence, rather than zero tax. This cannot prove facts that were never recorded or detect a fully self-consistent falsification of historical records.

Allocation uses cumulative proportional floor at internal scale 4. It constrains each tax reversal by the actual gross refund and remaining original revenue/tax, carries discount residuals cumulatively, and assigns the final residual when all eligible quantity is returned. Full quantity with a mismatched saved gross is incomplete. No original return, invoice or stock fact is rewritten. Restored cost comes from saved `SalesReturnCostAllocations`, not from the tax helper or a new costing algorithm.

## Net Profit vertical slice

- Frontend route: `report-net-profit`, title `تقرير صافي الأرباح`, renderer `net-profit-report`.
- Read API: `GET /api/reports/net-profit?from=yyyy-MM-dd&to=yyyy-MM-dd&breakdown=daily|monthly&page=1&pageSize=20`.
- Classification API: `POST /api/reports/net-profit/expenses/{voucherId}/classification`.
- Metric scope: **صافي ربح المبيعات بعد المصروفات المسجلة**.

Saved posted sale revenue excluding original saved tax minus actual original FIFO issues equals realized gross sales profit. Later return events reverse their allocated original tax-exclusive revenue and actual restored FIFO costs. Eligible cancellation events reverse original revenue and cost on their own event date; the original posting remains in its original period. Cancellation cost must reconcile with its reversing inventory movement and cannot coexist with returns. Eligible recorded voucher expenses are then deducted once to obtain scoped net profit. Net margin is net profit / positive net revenue; zero or negative revenue has no displayed percentage.

The report excludes stock acquisition invoices, supplier payments, customer collections, installment contract/principal receipts, maintenance advances and maintenance income/cost, treasury transfers and shift balances. They do not enter the query. No business-wide net profit, unrecorded expense coverage or maintenance profitability is claimed. Unsold stock and potential selling margins never enter realized profit.

## Expense classification and audit

Additive migration `075_expense_profit_classification.sql` creates **metadata only**, `ExpenseProfitClassifications`. Every change appends voucher identity, revision, explicit `included` / `excluded` / `unclassified`, required reason, authenticated actor, UTC timestamp and unique operation key. Update/delete triggers preserve audit events. Original vouchers, amounts, dates and linked treasury movements remain immutable. Old vouchers have no classification event and default to unclassified; names are never used to infer eligibility. Latest revision supplies the current accounting classification. Corrections restate the old posting period using current audited classification; this is not a locked accounting-period or historical-as-of reporting system.

Only administrators may write, with authenticated session and existing CSRF validation. Expected revision prevents stale corrections. An identical operation key retries without a second event; changed intent under the same key conflicts. The report lists server-paged vouchers with original business `ExpenseDate`, recognition `CreatedUtc`, current classification and latest actor/reason/time. Admins can classify or correct each voucher there. Cashiers granted the report can read it, but cannot classify. The existing Expense Voucher Report is reused unchanged. Vouchers contribute once; treasury withdrawals are never separately deducted. Unclassified count and amount and excluded totals are exposed independently.

## Recognition, buckets and completeness

Sales use `PostedUtc`, returns and cancellations their actual `CreatedUtc`, and vouchers `CreatedUtc` to reconcile with the existing expense report; `ExpenseDate` stays visible separately. `ReportPeriod` converts every local calendar boundary independently using the server timezone, including DST, ambiguous and skipped midnight handling. Application supplies nonoverlapping UTC bucket boundaries; SQLite aggregates posting/cancellation/expense events into those ranges. Only return component allocation requires streaming saved history in Data; no invoice collection or financial arithmetic enters the browser. Month boundaries are clipped to the selected inclusive dates. Zero-activity buckets are retained; negative values are valid. Summary is the exact decimal sum of all buckets before voucher pagination, so daily and monthly breakdowns reconcile independently of the visible voucher page.

`summary` and each `items` bucket expose `revenue`, `cogs`, `grossProfit`, included `expenses`, `netProfit`, `netMargin`, `complete`, unclassified count/amount, excluded expense total, missing revenue/cost event counts, affected absolute saved gross, and event count. Event count is posting headers + cancellation headers + return lines + vouchers, not a unique invoice count. Monetary values are invariant four-place decimal strings. Missing revenue makes revenue null; missing cost makes COGS null; either makes gross profit null. Either missing evidence or any unclassified voucher makes **net profit null**. The frontend independently suppresses net profit/margin when incomplete and displays coverage counts/amounts. It never substitutes zero for unknowns. Completion refers only to the stated recorded-sales scope.

FIFO allocations aggregate independently per issue movement before line/invoice joins. Saved quantity, value, document, variant and unit links must reconcile. Unresolved/missing cost layers or allocations produce a warning. Restored return quantities use original conversion snapshots, cost lineage must point to originally issued layers, and cumulative restored quantity/value cannot exceed original issue facts; a fully restored line must reconcile its exact original cost. Catalog reference purchase prices never enter reporting.

## Bounds, UI and permissions

`from`/`to` default to server-local month-to-date. Both are inclusive local dates, and custom ranges are capped at 3660 days to bound bucket generation. All seven existing presets are reused. `breakdown` accepts daily/monthly only. The last month is clipped before advancing its boundary, including the final supported December in year 9999; a valid filter must not cause a date-overflow exception. `page`/`pageSize` apply to the expense review rows (default 20, maximum 100); summary and all bounded calendar buckets are independent of this pagination. Values and range boundaries use parameterized SQL. Narrow posting-time and original-line indexes support the actual predicates. One read snapshot covers each report response; GET creates no documents, classifications, stock or financial writes. A 30,000-sale fixture, expense paging and the maximum 3660-day range are executable tests, not a claim about legacy hardware.

The registry, operational reports group and shell enable `report-net-profit`. API and frontend enforce effective screen access. Administrators can read; cashiers receive no default profit grant and must be explicitly granted this exact screen. Another report grant does not grant profit access, classification rights or sales mutations. The RTL Light/Dark renderer uses existing tokens, mounted date controls, compact summary cards/tables, loading/error/retry/empty states and stale-response guards. There are no charts, new frameworks, remote assets or browser APIs newer than Chromium 109. Native rendered 1366×768/Windows 7/hardware acceptance has not been performed.

## This delivery's validation

Resumed validation on 2026-10-30 used the existing implementation without replacing the return allocation algorithm. Focused Net Profit SQLite + real HTTP tests: **15 passed, 0 failed/skipped**, `artifacts/net-profit-completion/results/net-profit-focused-final.trx`. They cover the approved two-layer example and eligible expense, changed prices/settings, inclusive/exclusive original tax and discounts, repeated/final partial returns, mixed saved tax treatment, later cancellation, classification/replay/conflict/audit, unknown evidence, daily/monthly/custom reconciliation, DST, zero/negative profit, excluded collections/installments/maintenance advances, restart/nonmutation, expense paging, 30,000 saved sales and the maximum period. Added disposable SQLite regressions explicitly verify an 11-unit return spanning both original FIFO layers (1120 cost), its final one-unit residual (120 cost), and separate incomplete results for missing saved tax or missing return restoration allocations.

The supported final calendar month exposed an `ArgumentOutOfRangeException` in monthly bucket generation. `net-profit-boundary-before.trx` records the reproduced failure; the final-month clipping fix is covered by the passing focused and full unit runs. Original tax/classification/service tests: **7 passed**, `unit-profit-tax-focused.trx`. Full unit suite: **137 passed**, `unit-full.trx`. The complete frontend suite passed for **114 registered screens**, and `node --check` passed all **50** application/test JavaScript files. Full solution build, including WinForms: **0 warnings, 0 errors**, isolated in `artifacts/net-profit-completion-solution`.

The **full integration run completed: 296 passed, 0 failed, 0 skipped**, approximately **4 minutes 26 seconds**, `artifacts/net-profit-completion/results/integration-full.trx`. The 15 focused integration tests completed in approximately 43 seconds; all 137 unit tests completed in approximately 14 seconds. Every passing test run has zero failed/skipped tests. `git diff --check` passed (Git printed only existing unrelated LF/CRLF normalization notices). No unfinished run is counted as successful.

Commands executed from the repository root (each test command has a distinct TRX name):

```powershell
$env:ALKHAYAL_DATABASE_PATH=Join-Path $PWD '.tmp\net-profit-completion-guard.db'
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/net-profit-completion --no-restore --filter 'FullyQualifiedName~NetProfitReportTests|FullyQualifiedName~Net_profit_HTTP' --logger 'trx;LogFileName=net-profit-focused-final.trx' --results-directory artifacts/net-profit-completion/results -m:1
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/net-profit-completion --no-restore --filter 'FullyQualifiedName~ReturnTaxAllocationTests|FullyQualifiedName~NetProfitServiceTests' --logger 'trx;LogFileName=unit-profit-tax-focused.trx' --results-directory artifacts/net-profit-completion/results -m:1
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/net-profit-completion --no-build --no-restore --logger 'trx;LogFileName=integration-full.trx' --results-directory artifacts/net-profit-completion/results -m:1
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/net-profit-completion --no-build --no-restore --logger 'trx;LogFileName=unit-full.trx' --results-directory artifacts/net-profit-completion/results -m:1
node frontend/tests/run-tests.js
$files = @(Get-ChildItem -LiteralPath frontend/js -Recurse -Filter *.js) + @(Get-ChildItem -LiteralPath frontend/tests -Recurse -Filter *.js)
foreach ($file in $files) { node --check $file.FullName; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
dotnet build AlKhayal.slnx --artifacts-path artifacts/net-profit-completion-solution -m:1
git diff --check
```

All integration fixtures use unique disposable SQLite files in the OS temporary directory. The process database override is an additional guard for child hosts. `%LOCALAPPDATA%\AlKhayal\alkhayal.db` was not accessed. No commit, push, merge, branch switch, reset, OpenCode or subagent was used. Automated verification targets the installed Windows/.NET runtime; native 1366×768 Light/Dark rendering, low-resource measurements and Windows 7/Chromium 109 runtime qualification remain manual acceptance work.

Slice files (existing unrelated working-tree changes were preserved):

- Application: `Reports/NetProfitReport.cs`, existing `Reports/ReturnTaxAllocation.cs` and shared `Reports/TransactionReports.cs` period contract.
- Data: `Reports/SqliteNetProfitStore.cs`, additive `Migrations/075_expense_profit_classification.sql`.
- API/host: `Controllers/ReportsController.cs`, `ApiBootstrap.cs`, `Server.Host/AlKhayalServer.cs`.
- Frontend: `modules/net-profit-report.js`, shared `modules/report-ui.js`, `data/screens.json`, `index.html`, `js/app.js`, existing `css/modules.css` report styles.
- Tests: `NetProfitReportTests.cs`, `NetProfitApiTests.cs`, `NetProfitServiceTests.cs`, existing `ReturnTaxAllocationTests.cs`, `frontend/tests/report-tests.js`, `frontend/tests/run-tests.js`, migration fixture compatibility/expectations.
- Documentation: this specification and the Net Profit sections in `architecture.md`, `api-contracts.md`, `database.md`, `design.md` and `testing.md`.

Historical Net Profit checkpoint: Product Profit, Inventory Valuation, Top-Selling Products, Low Stock/Availability and Inventory Movements were still outstanding at that time. The final completion section above now delivers them. Phase 9.3 must respect the documented scope and unresolved-source semantics.

Fixture exploration also encountered the existing return writer's `sales_conflict` response during repeated discounted credit returns (three units at 10, line discount 0.02 and invoice discount 0.01). The same repeated-return reporting scenarios pass using persisted cash refunds. This report delivery does not modify that posting workflow or establish the cause of its conflict. Failed/unposted returns have no financial event to recognize; this edge case requires separate return-workflow investigation.

## Financial sources and limitations

The following source audit is retained from the earlier checkpoint. The implementation sections above supersede its forward-looking tasks for Net Profit; its wider business-coverage limitations still apply.

| Source | Evidence and reporting treatment needed |
| --- | --- |
| Sales | Posted invoices retain original total and tax and must be recognized at `PostedUtc`; customer payments and later collections are not additional revenue. |
| FIFO | Original issue allocations are linked through the sales line's inventory document line and movement. Aggregate allocations independently; use their saved values, not current catalog prices. |
| Sales returns | Return headers and lines contain saved gross amounts and original line links; cost allocations retain restored FIFO value. Returned tax components are not saved. Implement and verify the approved cumulative allocation and residual policy before claiming exact net revenue. |
| Cancellations | Append-only cancellation and inventory documents represent later events. Reverse the original sale and cost on the cancellation date without removing the posting from its original period. |
| Expenses | `ExpenseVouchers` is an authoritative immutable, idempotent record linked uniquely to one treasury withdrawal. Use the voucher once; do not also count the linked movement. Its business date and creation time are distinct. The present workflow always pays from a treasury account, and has no unpaid or reversal workflow. |
| Treasury, shifts | Movements and shift events can represent transfers, balances, sales or settlements. They are not independently classified as operating expenses or new revenue. |
| Purchases and supplier payments | Stock purchases and settlement of liabilities are not operating expenses; FIFO acquisition cost becomes COGS on sale. |
| Workforce | Employee salary metadata and attendance do not establish a posted payroll expense; only a separately recorded eligible voucher can currently represent such an expense. |
| Maintenance | Advances and collection receipts are not automatically earned revenue. Delivery stores a final charge, but the inspected ticket schema does not establish actual attributable service costs; do not claim complete maintenance profitability. |
| Installments | Contract and payment records are separate facts; do not treat collections as new sales or infer earned charges from generic payments. Recognition and linkage need further verification. |

`ExpenseVouchers.ExpenseDate` can be backdated, whereas Phase 9.1 reports recognize sales and returns by posting event timestamps. A financial report must specify and test its chosen expense recognition policy and later reversal period explicitly; the existing schema alone does not support an auditable void. No missing cost or expense should be represented as a known zero.

## Earlier date-preset checkpoint (historical)

The shared Phase 9.1 date selector now includes yesterday, the current week starting Saturday, and the previous calendar month alongside its existing presets. Dates are computed as local calendar dates, not fixed UTC offsets. The server continues to convert date boundaries using `ReportPeriod` and `TimeZoneInfo.Local`. No new API routes, frontend routes, migrations or financial writes were introduced.

## Earlier checkpoint validation (historical)

- `node frontend/tests/report-tests.js`: passed, including the three new date presets on each existing report.
- `node frontend/tests/run-tests.js`: passed, 112 routes.
- `node --check frontend/js/modules/report-ui.js` and `node --check frontend/tests/report-tests.js`: passed.
- Existing focused reporting and expense integration tests: 23 passed, 0 failed, 0 skipped, using `ALKHAYAL_DATABASE_PATH=.tmp/phase-9-2-guard.db`, isolated artifacts and `focused.trx`. These do not test the missing Phase 9.2 reports.
- `git diff --check`: passed with existing unrelated line-ending notices.

At this earlier checkpoint no Phase 9.2 financial acceptance had run and the other Phase 9.2 reports were undelivered. Final automated acceptance is now recorded in the final completion section above. Native UI, low-resource hardware and Windows 7 qualification remain outstanding. No operator database was opened.

## Expense voucher report — recovery implementation

`GET /api/reports/expenses` and frontend route `report-expenses` list persisted paid `ExpenseVouchers`. The report uses voucher `CreatedUtc` for recognition, with the Phase 9.1 local-calendar boundary conversion; `ExpenseDate` is displayed as a separate business date. This avoids silently changing the reporting period when a voucher is backdated. Date, category ID and text filters apply to the complete count, amount, category totals and paged rows. Search matches category, payee and notes. Each voucher contributes once; the linked treasury withdrawal is not separately summed. Category summaries are bounded to the largest 100 categories; the overall count and total include all matching vouchers.

Category names are operator-defined and cannot prove that each voucher is a qualifying operating expense. The expense report displays recorded vouchers, not verified eligible expense or net profit. There is no unpaid voucher or reversal support. No report provides a document drill-down because the current expense screen does not expose a supported voucher-specific navigation contract. At this recovery checkpoint there was no net-profit reconciliation; the Net Profit slice described above now supplies it with explicit voucher eligibility.

The endpoint enforces authentication and `report-expenses` effective screen access, and accepts `from`, `to`, `search`, `categoryId`, `page`, and `pageSize`, with the same period and paging validation as Phase 9.1. The frontend uses the same registry and shared controls, with bounded server-side pagination and no chart. No migration or financial write was added.

Recovery validation: `ExpenseReportTests` and `Reports_http` focused integration run: 6 passed, 0 failed, 0 skipped; isolated TRX `artifacts/phase-9-2-recovery/results/expense-report-focused.trx`. It covers voucher idempotency, date basis, filtering, pagination, exact totals, database read-only behavior, and HTTP access including the new report. The expense report frontend handler test covers rendering, paging, category filter, error/retry and empty state.

Full recovery validation, using isolated `artifacts/phase-9-2-full` and a guarded database path: integration 281 passed, unit 130 passed, full solution build 0 warnings/0 errors; separate integration and unit TRX files are under `results`. Full frontend suite passed for 113 routes; application JavaScript and report test syntax checks passed. These regressions do not cover the unimplemented profit and inventory reports.
