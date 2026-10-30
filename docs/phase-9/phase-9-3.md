# Phase 9.3 — Operational reports and business overview

Implementation date: 2026-10-30. **COMPLETE AUTOMATED IMPLEMENTATION / PARTIAL VISUAL ACCEPTANCE.** Current-run full regression passed 326 integration and 150 unit tests, zero failed/skipped. Native visual acceptance remains unverified. This phase adds read models, not another ledger or posting workflow.

## Functional screens and endpoints

All seven routes use `operational-reports.js`, existing `report-ui.js`, the التقارير registry module, normal sidebar/search/workspace tabs and independent effective screen permissions. All APIs retain the standard `{ success, data, error }` envelope.

| Report | Frontend route | GET endpoint | Source module feature |
| --- | --- | --- | --- |
| العملاء والمديونيات | `report-customers` | `/api/reports/customers` | Core customers |
| الموردين والمستحقات | `report-suppliers` | `/api/reports/suppliers` | Core suppliers |
| الورديات والكاشير | `report-shifts` | `/api/reports/shifts` | `advanced-shifts` |
| الموظفين والحضور | `report-attendance` | `/api/reports/attendance` | `employee-attendance` |
| الصيانة | `report-maintenance` | `/api/reports/maintenance` | `maintenance-management` |
| التقسيط | `report-installments` | `/api/reports/installments` | `installments` |
| ملخص النشاط | `report-overview` | `/api/reports/overview` | Per-metric gates |

Existing module screens provide statements, per-shift detail, attendance corrections, ticket operations and installment servicing, but lack full-scope SQL summary cards for this reporting requirement. Those workflows remain in place; the new screens add only missing read-only aggregate/filter/paging capabilities. Existing `shift-reports` remains the workforce list; the new report adds whole-scope separate shortage/overage summaries. No source writer, financial posting rule, schema migration, package, framework or chart was added.

## Query and response contract

The six operational endpoints accept `from`, `to`, `search`, positive optional `personId`, `status`, `page` and `pageSize`. `personId` identifies the customer, supplier, cashier user or employee appropriate to the report. Search targets current names/phones for directories, cashier/employee names, or ticket/contract reference and customer name. `%` and `_` retain parameterized SQLite LIKE wildcard semantics. Common limits: search 120 characters, page 1..1,000,000, size 1..100 (default 20). Unknown status, malformed/reversed dates, missing one date, invalid primitive binding and out-of-range paging return 400. Dates omitted means local month-to-date.

Statuses: directories `debt`; shifts/attendance `open|closed`; maintenance the six saved Arabic statuses; installments `active|completed|due|overdue`. Omitted status means all. Installment `due` means a saved scheduled obligation in the selected dates, including obligations already paid; it does not mean current overdue. Status and person/search filters scope both summary and page.

Operational results contain `count`, `summary` and `items`. Dictionary keys use camelCase. Monetary values, including fields ending `Scaled`, are **formatted exact decimal strings with four places**, not raw SQLite integers. Counts in summary are invariant strings; row IDs/counts/duration seconds remain integers. Missing checkout, unknown final charge and unknown remaining ticket charge remain null/omitted and render `غير متاح`, never invented zero. Summary totals span the whole matching scope, even on an empty later page. `count` is the SQL full-scope count. Ordering is event UTC descending then stable ID descending; LIMIT/OFFSET is server-side. No unbounded history is materialized in Application or the report frontend.

Overview accepts only inclusive local `from`/`to`. It returns a bounded list of `{ key, value, route, current, complete }` metrics, not underlying report rows. A missing/disabled/denied source contributes no metric, rather than a misleading zero.

## Sources, recognition and accounting boundaries

| Report | Authoritative records | Snapshot and event recognition |
| --- | --- | --- |
| Customers | `CustomerTransactions`, `Customers`, posted credit `SalesInvoices` | Current debt = full signed ledger: opening/increase positive, other transaction types negative. Positive balances sum to receivables; zero balances remain visible with all. Collections include `collection` and `legacy_payment` by `CreatedUtc`; credit invoice value uses `PostedUtc`. Last activity is maximum ledger `CreatedUtc`. |
| Suppliers | signed `SupplierLedgerEntries`, `Suppliers`, posted credit `PurchaseInvoices` | Current net balance = signed ledger sum, not invoice reconstruction or decimal compatibility projection. Positive balances sum to payables; negative credit balances remain visible per supplier. `payment|legacy_payment` are separate period settlement amounts by ledger `CreatedUtc`; purchase returns are not payments. Credit invoices use `PostedUtc`. |
| Shifts | existing workforce `ShiftSql`, `CashierShifts`, `CashierShiftEvents` | Selected dates select `OpenedUtc`; included rows represent the entire shift, including later closure/activity. Closed expected is frozen `ClosingExpectedScaled`; open expected uses the same opening + cash sale + collection + in − out source as Workforce. Counted/expected summary includes closed shifts only. Shortage and overage aggregate independently and never cancel one another. |
| Attendance | `EmployeeAttendance`, `Employees` | Rows selected by `CheckInUtc`. Current active employee count is independently scoped by employee/search, not invented attendance. Recorded duration includes completed rows only, rounded to whole seconds from persisted UTC endpoints. Open rows show unknown duration. No scheduled absence, present-day employee inference, lateness or payroll expense is created. |
| Maintenance | `MaintenanceTickets`, immutable `MaintenanceTicketReceipts`, delivery `MaintenanceTicketStatusHistory` | Table is current matching ticket inventory across all intake dates. New intake counts use `CreatedUtc`; advance receipts use receipt `CreatedUtc`; delivered count and recognized charge use saved delivery history `ChangedUtc`. Current ready/open counts are explicitly snapshots. Remaining delivered-ticket amount = final charge minus recorded ticket receipts, bounded at zero. Pending final charges remain unknown. |
| Installments | immutable contracts, actual schedules and allocated `CustomerInstallmentPayments` | Current outstanding = positive schedule amount minus allocated payment. Overdue uses saved due date strictly before current local business date; today's unpaid installment is due, not overdue. Next due is minimum unpaid date. Scheduled due amount selects saved `DueOn` inclusively; collections select payment `CreatedUtc`, excluding down payments. Contract active/completed derives from remaining schedule. |

Collection/settlement recognition uses registration timestamps consistently with recorded events; operator-entered `OccurredOn`, `PaidOn`, invoice business date, intake/delivery business date remain separate facts and do not backdate registration reports. UTC bounds reuse `ReportPeriod`: inclusive local From/To convert independently to `[startUtc, dayAfterToUtc)` in host `TimeZoneInfo.Local`, retaining DST and month/year transitions. Single-PC standalone browser/host timezone assumptions remain unchanged. Current debt/payables/overdue/status never become historical as-of balances when dates change.

Customer collections (including installment/down-payment/maintenance account movements) are not new sales revenue. Supplier settlements are not a second inventory cost or operating expense. Opening/counting cash is not revenue. Wallet/card receipts do not enter the physical drawer; they remain in the existing Payment Methods report. The legacy shift event sales count/value covers saved cash/credit events, excludes electronic sales, and is labeled accordingly. No unsupported exact electronic-to-shift association or all-method shift sales count is invented.

Maintenance advances remain payments held against tickets. Only the saved delivery workflow establishes final charges; no maintenance costs/net profit are inferred. Ticket remaining is **before unallocated general customer collections** and is not a second customer balance ledger. General account collection does not carry reliable ticket allocation. Contract remaining similarly reflects dedicated schedule allocation, not arbitrary later general account collections; the installment screen always displays an explicit allocation coverage warning for that limitation. Existing installment servicing only accepts full scheduled payments; partial persisted evidence is calculated from its allocated amount and produces `coverageCount` and a visible warning. No new partial-payment workflow, interest, late fee or penalty is introduced. Existing saved contract terms remain unchanged. No accepted report read counts failed/unposted operations as financial events.

## Overview definitions and permissions

`BusinessOverviewService` calls the existing Sales, Returns, Payment Methods and Net Profit services and the same operational report service. It contains no independent money formula. Cards expose original posted gross sales; actual saved sales returns; the exact scoped Net Profit summary; cash/wallet/card/unclassified immediate invoice receipt totals and separate original credit sales; current receivables/payables; current overdue schedules; current open maintenance tickets. It does not turn collections into sales, purchases into expense, or standalone installments/maintenance into the standard sales P&L.

The Net Profit label remains **صافي ربح المبيعات بعد المصروفات المسجلة**. Its `complete=false` suppresses the numeric net value and displays an incomplete state. Unclassified expense, missing FIFO basis or missing original tax evidence remains unknown; company-wide profitability is not claimed. Source report links remain permission-aware.

Every endpoint authenticates the existing cookie session and checks its exact `report-*` permission. Cashiers get no new default report grants. Administrators can access reports for enabled modules. Feature-disabled operational endpoints return 403, including administrators. Overview first requires `report-overview`, then authorizes **each** source report and module server-side before calling its service. An overview-only cashier receives an empty metric list and cannot retrieve debt/profit through aggregate leakage. Report access never grants statement/editor/mutation permissions; links become text when destination access is absent. Existing session/origin controls remain unchanged; this adds no roles or authentication mechanism.

## Frontend reuse and record links

The renderer reuses shared period presets, cards, pager, theme/table CSS and workspace scrolling. Dates, search, person and status inputs stay mounted. Apply/reset restart at page one; request generation plus active-tab checks suppress stale responses. Loading, empty, error/retry and unknown states are explicit. No browser money arithmetic, charts, canvas, remote resources, animations or extra viewport sizing was added.

Original record links reuse customer statement (`customerStatementId`), supplier statement (`supplierId`), shift details (`shiftId`), employee attendance query (`employeeId`), maintenance tracking (customer/ticket identity) and installment payment (customer/contract identity). Existing supplier, maintenance and installment renderers now consume that route state and select the intended record; their mutation contracts are unchanged. Historical inactive customers remain eligible for linked installment servicing. Existing module views retain their own paging/read contracts; only the new report payloads are bounded by this report query contract.

## Performance and independent review

Full counts/sums/filtering and stable paging execute in SQLite, in a short read transaction shared by each report's summary/page. Independent event sources aggregate before joining directory/header rows, avoiding duplicate totals. Existing customer/supplier ledger, shift-event covering, attendance-date and schedule/payment identity indexes are reused. Workforce's original shift source is shared through an internal constant; no second reconciliation source was introduced. No speculative index migration or changed historical financial fact is required. Existing full-regression volume tests cover 30,000 posted sales and other Phase 9 workloads; no Windows 7 or low-resource performance claim follows from development-machine execution.

Review covers query parameterization, all exact monetary conversion, snapshot/date labels, source multiplicity, denied direct requests, feature gates, incomplete coverage, drill-down state, shared renderer registration and absence of source DML. The normal AppData database was never opened by this task. All test databases are disposable temporary fixtures, with explicit fallback-path guards and isolated build outputs.

## Actual validation

All commands below completed on 2026-10-30. Earlier historical Phase 9.1/9.2 figures remain historical; these are fresh current-run results.

| Check | Executed result | Evidence under `artifacts/phase-9-3` |
| --- | --- | --- |
| Focused operational SQLite + real HTTP | 17 passed, 0 failed/skipped | `results/operational-focused-final.trx` |
| Full integration, including all Phase 9.1/9.2/9.3 and source workflow regressions | 326 passed, 0 failed/skipped; 3m 11s | `results/integration-full-phase93.trx` |
| Full unit, including 7 new operational validation/DST cases | 150 passed, 0 failed/skipped; 12s | `results/unit-full-phase93.trx` |
| Full frontend including seven new report exercises | Passed, 126 registered screens | `frontend-final-accepted.log` |
| JavaScript syntax | All 52 frontend application/test files passed | `node --check` over `rg --files frontend -g '*.js'` |
| Full solution including WinForms | Succeeded, 0 warnings, 0 errors | `.tmp-phase93-build-final.log` |
| Whitespace | `git diff --check` passed | Existing unrelated LF/CRLF normalization notices only |

Focused coverage includes all six empty reports; populated customer/supplier ledger reconciliations, credit invoice posting and exclusion of drafts; frozen shift cash/counts, open/closed status and separate shortage/overage; cross-midnight attendance and missing checkout; maintenance advance/intake/delivery recognition; full/partial schedule allocation and future/overdue obligations; exact overview source reconciliation and incomplete profit classification; independent direct HTTP grants, malformed filters, 401/403/405, disabled module and overview nonleakage. Repeated read tests compare the complete SQLite file bytes. Frontend exercises each new route's loading/filters/paging, mounted search, exact figures, escaping, record identity, denied links, retry/reset and stale/left-tab responses. The full suite also reruns existing FIFO, sales/returns, ledgers, payment instruments, shift, maintenance advance hotfix, installments, expense audit and daily/monthly profit tests.

An initial focused run was 14 passed/1 failed: an introduced encoding error in the new maintenance SQL status literal produced the wrong delivered count. The file was repaired and the completed final focused/full runs pass. `results/operational-focused.trx` retains the failed evidence. The intermediate 16-case run also passed; final counts include the added incomplete-profit test. The initial compile attempt lacked dynamic binder support; the store now uses typed `QueryAsync<object>` results and explicit dictionary casts without adding a package/reference. No unfinished or failed run is counted as passed.

Commands (PowerShell, repository root):

```powershell
$env:ALKHAYAL_DATABASE_PATH='D:\projects\AlKhayal\.tmp\phase93-test-guard.db'
dotnet build AlKhayal.slnx --artifacts-path artifacts/phase-9-3 -m:1 --no-restore
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/phase-9-3 --no-restore --filter 'FullyQualifiedName~OperationalReportTests|FullyQualifiedName~Operational_reports_HTTP' --logger 'trx;LogFileName=operational-focused-final.trx' --results-directory artifacts/phase-9-3/results -m:1
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/phase-9-3 --no-build --no-restore --logger 'trx;LogFileName=integration-full-phase93.trx' --results-directory artifacts/phase-9-3/results -m:1
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/phase-9-3 --no-build --no-restore --logger 'trx;LogFileName=unit-full-phase93.trx' --results-directory artifacts/phase-9-3/results -m:1
node frontend/tests/run-tests.js
$files=rg --files frontend -g '*.js'
foreach($file in $files){node --check $file; if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}}
git diff --check
```

The full unit process used a separate `.tmp/phase93-unit-guard.db` fallback. Every SQLite fixture independently creates its own unique temporary database; no normal AppData path is used. Solution artifacts avoid locks on the running desktop. Initial restore/build also succeeded after the described code correction.

## Changed files for this task

New: `Application/Reports/OperationalReports.cs`, `Application/Reports/BusinessOverviewService.cs`, `Data/Reports/SqliteOperationalReportStore.cs`, `frontend/js/modules/operational-reports.js`, `OperationalReportTests.cs`, `OperationalReportsApiTests.cs`, `OperationalReportServiceTests.cs`, this phase document and `phase-9-final-acceptance.md`.

Extended: existing `ReportsController`, `ApiBootstrap`, `AlKhayalServer`, workforce store constant visibility, frontend registry/index/app registration, original module `renderers.js` drill-down state, frontend report/runtime registry tests, and API/architecture/database/design/testing docs. Many other modified/untracked paths were already present at task start; they are preserved and are not claimed as this phase's changes.

## Known limitations and Phase 10 handoff

The earlier repeated discounted credit-sale return `sales_conflict` rounding edge remains **not proven fixed**. Existing successful return tests are preserved; rejected/unposted returns are absent from reports. No sales/refund posting change was made for this phase. Treat the demonstrated root cause and correction as a separate pre-release hotfix unless release acceptance establishes otherwise.

**VISUAL ACCEPTANCE UNVERIFIED** for the full native shell and all seven reports/states. Limited actual browser rendering was inspected using `artifacts/phase-9-3/visual-fixture.html`, a clearly labeled synthetic layout fixture served temporarily on loopback, not the operator database: customers/light at 1366×768; installments/dark at 1366×768; maintenance/dark and incomplete overview/dark at 760×768. Long Arabic names and large exact totals remained visible; the narrow maintenance table scrolled locally (706px wrapper, 1254px table), with document width equal to the 760px viewport. Screenshots were inspected during the browser session. This is representative browser layout evidence, not native authenticated end-to-end acceptance. The temporary server/tab were stopped/closed and viewport override reset. Full native Arabic RTL, both themes, all routes, tab/focus/scroll/loading/error inspection is still required. DOM/VM tests are not native visual proof. Windows 7 SP1, Chromium 109/WebView2 distribution and low-spec hardware runtime/startup/performance tests are **UNVERIFIED**. No claim of full release readiness or Phase 10 implementation is made.

Phase 10 should perform native visual/runtime qualification, packaging/runtime handling, printer/error-path qualification, backup/restore release verification, deployment/security review and resolution/triage of the known return-rounding issue. Maintain the documented P&L and payment-allocation limitations in operator-facing guidance.

Safety: direct implementation only; no subagents, OpenCode, GLM or Gemini, no commit/push/branch creation or switching/merge/reset, no discarded existing work, no operator database modification.
