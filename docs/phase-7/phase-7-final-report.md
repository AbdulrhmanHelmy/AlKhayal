# Phase 7 — Final engineering and acceptance report

Evidence date: 2026-10-29. Phase 7 software implementation is present and the complete automated regression passed. **Release/hardware acceptance remains open**: no physical printer or scanner was available, and Windows 7 SP1 was not exercised. No commit, push, merge, branch switch, external coding worker or delegation was used. The existing uncommitted 7.1/7.2 work was preserved.

## 1. Phase 7.1 status

Implemented: persisted employees with immutable allocated codes, optional salary/payday information, independently optional attendance with overlap prevention and correction audit, cashier shift ownership/register locking, opening/cash adjustments/closing and exact reconciliation. Source-linked cash sale/collection events remain atomic with their original posting transactions. Attendance never opens a shift; salaries do not implement payroll or create payments. Current full regressions include WorkforceServiceTests, WorkforcePersistenceTests, WorkforceApiTests and workforce frontend handlers. See [Phase 7.1](phase-7-1.md) for supported drawer sources and deliberate exclusions. Physical drawer reconciliation and full desktop interaction remain manual acceptance.

## 2. Phase 7.2 status

Implemented and regression-tested: native direct receipt/document printing, configurable profiles/templates, separate saved printer settings, sample native preview, opt-in idempotent sale auto-print, explicit reprints, saved shift summary/closing reports, durable diagnostics and interrupted-job review. Existing persisted sale/purchase/return/customer/supplier/shift projections remain the source. New label output reuses this infrastructure. See [Phase 7.2](phase-7-2.md). Receipt hardware behavior remains unverified.

## 3. Phase 7.3 status

Implemented: transactional product/variant/unit barcode management; internal generation and uniqueness; Code 128/EAN-13 native rendering; independent label profiles; custom/roll/sheet geometry; Arabic numeric visual designer and native PNG preview; compact persisted-only product printing; bounded bulk search/selection/quantities/start cell; shared jobs/idempotency/reprints; existing POS lookup integration. See [Phase 7.3](phase-7-3.md). Scope is implemented at software level; physical scanning/feeding and target-machine acceptance are not marked complete.

## 4. Migrations

Phase 7 adds **071_phase7_1_workforce.sql**, **072_phase7_2_printing.sql** and **073_phase7_3_labels.sql**, in the reserved ordered range. Only 073 was introduced in this implementation step; 071/072 were already in the working tree. 073 adds LabelConfiguration/LabelConfigurationAudit, BarcodeOperations/BarcodeSymbologies and nullable PrintJobs RequestHash/BatchSummaryJson. Existing barcode provenance remains separate from symbology. No new financial tables or financial recalculation. The embedded migration total is 34; old upgrade fixtures exclude the later migrations until upgrade. Tests use temporary files; the operator database was not opened or modified by tests.

## 5. Printer architecture

Application owns validation/contracts/services; Data owns catalog/configuration/claim/audit persistence. Thin API controllers enforce session, permission, feature, CSRF and origin boundaries. One host-composed WindowsPrinterBackend implements receipt and label ports. System.Drawing/PrintDocument submits directly through an explicitly selected Windows driver with StandardPrintController, paper capability/DEVMODE checks, DPI and hardware margins. PrintJobs is the shared queue identity/diagnostic boundary. Printing never runs financial commands. No browser/PDF/WebView2 printing path was added. No hardware completion status is inferred from successful submission.

## 6. Barcode formats

Code 128 supports printable ASCII and actual checksum/module encoding with quiet zones; generated ten-digit internal values are not GS1 allocations. EAN-13 verifies thirteen digits and calculates the check digit for explicitly entered twelve-digit source values. Invalid length/content/checksum is rejected. Existing normalized case-insensitive uniqueness and retired identities are preserved. ZXing.Net 0.16.11 is pinned, Apache 2.0, using its net48 encoder assembly only (531,968 bytes); the optional presentation/WPF assembly is excluded. See [dependency/license inspection](../third-party/README.md). Decoder tests establish raster round-trip evidence, not physical certification.

## 7. Label template capabilities

Create/edit/copy/rename/delete-unused/reset, default profile/template assignment, thirteen allowed elements (name/company/product reference/SKU/barcode/readable code/price/unit/category/size/color/static/logo), visibility, positions/dimensions, alignment, font/bold, module scaling, margins/gaps and stock geometry. The selected element is outlined over a native sample raster with actual proportions and stale-response protection. Arabic text uses Tahoma/RTL. Overflow and overlap with barcode quiet zones are rejected. Numeric element editing is implemented; drag/drop and arbitrary element rotation are outside this implementation.

## 8. Paper configuration

Six label presets, A4 sheets and custom dimensions are configurable. Label bounds: 10–420 × 10–1000mm; page bounds: 10–420 × 10–3000mm before orientation. At most 20 columns/100 rows/500 cells. Driver capability and actual printable bounds remain authoritative. Roll mode uses one driver page per cell; page pitch includes label, margins and trailing vertical gap. Sheets use label-size-plus-gap pitch and configurable initial used-cell offset. An unsupported form produces an explicit error; there is no universal printer-support claim.

## 9. Individual workflow

Directory barcode management handles entered/generated/copied/replaced codes. Directory Print Labels opens saved codes; saved add/edit/details provide a collapsed inline selector for barcode/profile/template/quantity. It preserves existing modal fields and reads authoritative persisted data only. New products must be saved first. Editing a barcode retires its old row; historical line snapshots and references remain unchanged. Feature flags hide operational print controls when the label module is off.

## 10. Bulk workflow

The contextual product-labels route searches twenty products per server page, fetches only a selected detail, adds active variant/unit codes, edits quantity/symbology, removes entries and selects saved profile/template/starting cell. Limits: 200 distinct codes, 1–1000 each, 2000 labels including profile copies. Jobs store compact barcode-ID/count/format summary, actor, printer, template, settings version, copies and status. Lost responses retain a frozen key/payload; identical requests replay the durable result. Intentional reprints use a new key. No automatic requeue after uncertain output or interrupted accepted jobs.

## 11. Scanner/POS integration

Management writes the real ProductBarcodes table used by the existing catalog lookup, including exact variant/sale-unit mapping. Integration tests execute actual POS lookup for ordinary and distinct size/color variants. Native raster decoding validates Code 128/EAN-13 content. Standard/Touch POS continue using the common catalog/sales boundary. A keyboard-wedge scanner and paper-label scan are UNVERIFIED and must be tested on the target system; no scanner hardware or new device protocol was emulated as proof.

## 12. Security and permissions

Same-origin authenticated cookies and existing effective screen permissions apply. Administrator-only configuration/device/diagnostic/history/review/sample-preview; output requires effective catalog access and barcode-label-printing. Barcode management requires product list/edit access, independently from the optional print/variant switches. All POST/PUT requests validate existing CSRF and supplied Origin authority. Actor identity comes from the session; templates accept only known structure/elements and bounded PNG. The browser never supplies production price/company fields. LAN rate-limit/TLS and final deployment hardening remain the existing release obligations.

## 13. Build results

The complete solution build passed with **zero errors and zero warnings**. The first recorded full build took 9.72s; final build evidence is under local `test-artifacts/phase73/build-final.log`. Output is redirected away from any running desktop binaries. Restore passed with the pinned net48 dependency. Inspection of solution output found `zxing.dll` and no `zxing.presentation.dll`; the Apache license is copied to ThirdPartyNotices. No framework/runtime upgrade or WPF dependency was introduced.

## 14. Test results and measured evidence

| Check | Actual result |
|---|---|
| Complete unit suite | **97 passed**, 0 failed/skipped; 10s reported duration |
| Complete integration suite | **224 passed**, 0 failed/skipped; 2m 4s reported duration |
| Frontend behavior/registry | Passed for **105 registered routes**, including redirect/renderer load-order validation |
| All frontend JS syntax | Passed with Node `--check` |
| Whitespace check | `git diff --check` passed; Git emitted existing LF/CRLF normalization notices |
| Native label images | All six presets, custom 73×37 and A4 generated; 30×20 and A4 visually inspected |
| Label fake-backend timing | One persisted-data batch submission: **131.34ms** in full suite; excludes driver/spooler/device |
| Existing receipt fake-backend timing | 20 persisted-document submissions: **2242.28ms total / 112.11ms mean**; excludes hardware |
| Existing 15,000-product regression | First 40 POS results **88.652ms**; bounded pages/last page verified, inventory search **46.833ms** |

Timings are local samples under regression load, not legacy-hardware throughput promises. The label UI test uses a 15,000-result count with bounded page requests; the real 15,000-row test remains the persisted catalog/inventory regression. New tests cover barcode format/uniqueness/replay/retirement/audit rollback, variant mapping, authoritative label fields, independent settings/restart, bulk limits/concurrency/failure/uncertainty, native layout/DPI/quiet zones/overflow, real HTTP authorization/CSRF/origin/features, draft survival and stale responses. Financial snapshots compare sales/payments/purchases/stock/FIFO/customer/supplier/treasury/shift tables around barcode/label operations. Existing receipt auto-print/reprints and workforce tests passed as part of the full run.

During development a sheet raster test created Graphics before setting bitmap DPI; corrected to set DPI first. A new feature-state use exposed incomplete frontend test stubs; those now implement the real isEnabled contract. A native roll pitch validation and test were added. The final focused label run and build cover the subsequent label-test request fingerprint refinement and catalog identifier error mapping. Review found unsupported input in the old aggregate form could throw ArgumentException instead of its normal catalog validation result; it now returns identifier_invalid and a regression asserts that behavior. No failed run is represented as a pass.

```powershell
dotnet restore AlKhayal.slnx -v:q
dotnet build AlKhayal.slnx --no-restore -p:OutDir=D:\projects\AlKhayal\test-artifacts\phase73\solution\ -v:q
dotnet test tests/AlKhayal.UnitTests --no-restore -p:OutDir=D:\projects\AlKhayal\test-artifacts\phase73\unit\ --logger "trx;LogFileName=unit.trx"
dotnet test tests/AlKhayal.IntegrationTests --no-restore -p:OutDir=D:\projects\AlKhayal\test-artifacts\phase73\integration\ --logger "trx;LogFileName=integration.trx"
node frontend/tests/run-tests.js
Get-ChildItem frontend/js -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName }
git diff --check
```

Logs/builds/previews are locally excluded under test-artifacts/phase73; TRX results are in the test projects' ignored TestResults directories. All write-capable fixtures use unique disposable SQLite files and injected recording output backends. No real print submission or operator-database test was performed.

## 15. Hardware verification and manual acceptance matrix

Use an isolated database and a physical test printer/profile. VERIFIED below always names its evidence and must not be read as paper-output verification. Update statuses only after observing the real scenario.

| Scenario | Status | Required acceptance / available evidence |
|---|---|---|
| 58mm receipt device | UNVERIFIED | Select physical driver/form, print Arabic/mixed long receipt, measure margins/total/feed; raster regression passed |
| 80mm receipt device | UNVERIFIED | Repeat with its own independent profile, copies, long invoice pagination |
| A4 office device | UNVERIFIED | Print receipt and sticker grid; measure first/last rows/columns at 100% scale |
| Dedicated label device | UNVERIFIED | Install exact stock, print 1/10/200 labels, inspect quiet zones, count/feed |
| Custom label rolls | UNVERIFIED | Validate custom form and sensing; measure label+gap pitch over 100 labels; reject unsupported sizes |
| Multi-column physical sheets | UNVERIFIED | Use partially consumed sheet, selected start cell and multi-page batch; measure alignment |
| Arabic receipt paper output | UNVERIFIED | Inspect shaping, mixed direction, long names, exact totals on 58/80/A4 |
| Arabic product label paper output | UNVERIFIED | Inspect small stock, names, SKU, price, size/color without clipping |
| Scan physical printed label | UNVERIFIED | Scan Code 128 and EAN-13 into standard/Touch POS; confirm variant/unit/price |
| Cash drawer / cutter | BLOCKED | No raw ESC/POS drawer/cutter port implemented; driver-controlled feed/cut requires separate target-driver acceptance |
| Physical disconnect | UNVERIFIED | Disconnect before and during output; inspect job state and saved sale; recording missing-device/failure tests passed |
| Out of paper | UNVERIFIED | Exhaust stock during batch, inspect Windows queue and uncertain result; no automatic retry |
| Spooler errors/restart | UNVERIFIED | Stop/restart service on isolated target; inspect partial pages and explicit review; deterministic failure/status regressions passed |
| Physical reprint | UNVERIFIED | Reprint intentionally with new key; verify count and unchanged financial values |
| WinForms/WebView2 interaction | UNVERIFIED | Open settings/product draft, preview/print, return to draft; verify focus, no browser dialogs and error recovery |
| Windows 7 SP1 target | UNVERIFIED | See section 16; run installed x86 application, printers and legacy WebView2 |
| Actual desktop restart | UNVERIFIED | Restart executable after saved profiles and interrupted output; compare settings/queue; repository/service restart tests passed |
| Light/Dark actual UI | UNVERIFIED | Inspect designer controls, focus/error states and native preview at both themes |
| DPI / scaling actual UI/device | UNVERIFIED | Test 100/125/150/200% and 203/300DPI drivers; confirm geometry/scannability |
| Software native raster | VERIFIED | Native PNG generation, decoder round trips, bounds/integer dots and visual 30×20/A4 inspection; no physical output |
| Software persistence/security/replay | VERIFIED | Temporary SQLite and actual OWIN API tests plus production frontend VM handlers |

## 16. Windows 7 compatibility

UNVERIFIED. Targets remain .NET Framework 4.8 / compatible .NET Standard 2.0, native System.Drawing printing and Chromium 109-compatible JavaScript. Desktop stays the existing x86 WinForms/WebView2 shell; managed encoder introduces no modern Windows-only printing API. Source/target compatibility is not runtime validation. A packaged Windows 7 SP1 machine must have .NET 4.8, supported legacy WebView2 Chromium 109 distribution, Arabic fonts, functioning spooler and the actual x86 driver. Win7 packaging/runtime distribution and x86/x64 device acceptance remain release work; current Evergreen is not a Win7 solution.

## 17. Remaining limitations

Physical acceptance in the matrix is outstanding. Generic Windows drivers cannot establish paper completion; submitted_unknown is deliberate. Arbitrary Unicode/control bytes are not Code 128 content. Extremely long identifiers/text require larger labels or template adjustment; the system rejects overflow. Roll sensing/cutter/proprietary printer commands are driver concerns. Preview is first sample page with a bounded raster, not a physical-printer simulator. No drag/drop, arbitrary element rotation, GS1 assignment, optional receipt-reference barcode, per-item design overrides, historical template-body archive, job retention policy or multi-terminal identity was introduced. Existing single-register/standalone and LAN security limitations remain documented.

## 18. Before release

Complete and record the target-device matrix; scan labels from the smallest intended stock at each actual DPI; calibrate driver paper/margins/sensing and repeat a long roll run. Validate backup/restore and upgrade using a copy of a representative business database. Test interrupted accepted-job review with Windows queue inspection. Verify low-memory performance and authenticated desktop theme/keyboard/DPI behavior. Package third-party notices and the correct WebView2/runtime/driver prerequisites. Decide operational job/audit retention and whether device-specific drawer/cutter support is needed. These checks are release gates, not claims already satisfied by unit compilation.

## 19. Full changed-file summary

Phase 7.3 adds BarcodeService, SqliteCatalogBarcodes, LabelContracts/LabelService/SqliteLabelRepository, migration 073, NativeLabelRenderer, WindowsPrinterBackend.Labels, LabelsController, label JS/CSS and label unit/integration/API/frontend tests. It extends catalog projections/identifier validation, the shared print claim/lifecycle/contracts/backend, composition, package selection, product actions, the settings integration, route registry/load order, tests and documentation.

The complete working-tree inventory below includes **preserved earlier Phase 7.1/7.2 changes**, not just files changed in 7.3. Prior workforce/features/sales/receipt/financial projection work belongs to those phases; 7.3 does not rewrite their financial logic. Migration-fixture edits account for additive 073. Paths are repository-relative; M means modified tracked file and ?? means new untracked file. Local build logs, images and test results are excluded. The local Git exclude file only records test-artifact directories; no repository Git action was taken.

```text
 M docs/api-contracts.md
 M docs/architecture.md
 M docs/database.md
 M docs/design.md
 M docs/sales-shift-revenue.md
 M docs/testing.md
 M docs/ui-screen-coverage.md
 M frontend/css/modules.css
 M frontend/data/screens.json
 M frontend/index.html
 M frontend/js/app.js
 M frontend/js/modules/catalog.js
 M frontend/js/modules/purchasing.js
 M frontend/js/modules/renderers.js
 M frontend/js/modules/returns.js
 M frontend/js/modules/sales.js
 M frontend/tests/run-tests.js
 M src/AlKhayal.Api/ApiBootstrap.cs
 M src/AlKhayal.Application/Catalog/CatalogContracts.cs
 M src/AlKhayal.Application/Catalog/CatalogPorts.cs
 M src/AlKhayal.Application/Catalog/CatalogService.cs
 M src/AlKhayal.Application/Catalog/CatalogVariantContracts.cs
 M src/AlKhayal.Application/Features/FeatureContracts.cs
 M src/AlKhayal.Application/Features/FeatureRegistry.cs
 M src/AlKhayal.Application/Features/FeatureService.cs
 M src/AlKhayal.Application/Sales/SalesContracts.cs
 M src/AlKhayal.Core/Models/CustomerStatement.cs
 M src/AlKhayal.Core/Models/PurchaseInvoice.cs
 M src/AlKhayal.Data/AlKhayal.Data.csproj
 M src/AlKhayal.Data/Catalog/SqliteCatalogConfigurationStore.cs
 M src/AlKhayal.Data/Catalog/SqliteCatalogStore.cs
 M src/AlKhayal.Data/Customers/SqliteCustomerAccountRepository.cs
 M src/AlKhayal.Data/Features/SqliteFeatureStore.cs
 M src/AlKhayal.Data/Sales/SqliteSalesStore.Lifecycle.cs
 M src/AlKhayal.Data/Sales/SqliteSalesStore.cs
 M src/AlKhayal.Printing/AlKhayal.Printing.csproj
 M src/AlKhayal.Server.Host/AlKhayal.Server.Host.csproj
 M src/AlKhayal.Server.Host/AlKhayalServer.cs
 M tests/AlKhayal.IntegrationTests/BusinessActivityPersistenceTests.cs
 M tests/AlKhayal.IntegrationTests/CatalogMigrationTests.cs
 M tests/AlKhayal.IntegrationTests/CustomerLedgerMigrationPreflightTests.cs
 M tests/AlKhayal.IntegrationTests/CustomerStatementTests.cs
 M tests/AlKhayal.IntegrationTests/FeaturePersistenceTests.cs
 M tests/AlKhayal.IntegrationTests/InventoryFifoTests.cs
 M tests/AlKhayal.IntegrationTests/InventoryPersistenceTests.cs
 M tests/AlKhayal.IntegrationTests/MigrationTests.cs
 M tests/AlKhayal.IntegrationTests/PurchasingPersistenceTests.cs
 M tests/AlKhayal.IntegrationTests/RefundOverdraftMigrationTests.cs
 M tests/AlKhayal.IntegrationTests/SalesPersistenceTests.cs
 M tests/AlKhayal.IntegrationTests/SettingsPersistenceTests.cs
 M tests/AlKhayal.IntegrationTests/SupplierLedgerMigrationTests.cs
 M tests/AlKhayal.IntegrationTests/SupplierRestartApiTests.cs
 M tests/AlKhayal.IntegrationTests/TreasuryLegacyMigrationTests.cs
 M tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj
?? docs/phase-7/phase-7-1.md
?? docs/phase-7/phase-7-2.md
?? docs/phase-7/phase-7-3.md
?? docs/phase-7/phase-7-final-report.md
?? docs/third-party/README.md
?? docs/third-party/ZXing-Apache-2.0.txt
?? frontend/css/labels.css
?? frontend/css/printing.css
?? frontend/js/modules/labels.js
?? frontend/js/modules/printing.js
?? frontend/js/modules/workforce.js
?? frontend/tests/label-tests.js
?? frontend/tests/printing-tests.js
?? frontend/tests/workforce-tests.js
?? src/AlKhayal.Api/Controllers/LabelsController.cs
?? src/AlKhayal.Api/Controllers/PrintingController.cs
?? src/AlKhayal.Api/Controllers/WorkforceController.cs
?? src/AlKhayal.Application/Catalog/BarcodeService.cs
?? src/AlKhayal.Application/Printing/LabelContracts.cs
?? src/AlKhayal.Application/Printing/LabelService.cs
?? src/AlKhayal.Application/Printing/PrintContracts.cs
?? src/AlKhayal.Application/Printing/PrintDocumentProvider.cs
?? src/AlKhayal.Application/Printing/PrintingService.cs
?? src/AlKhayal.Application/Workforce/WorkforceContracts.cs
?? src/AlKhayal.Application/Workforce/WorkforceService.cs
?? src/AlKhayal.Data/Catalog/SqliteCatalogBarcodes.cs
?? src/AlKhayal.Data/Migrations/071_phase7_1_workforce.sql
?? src/AlKhayal.Data/Migrations/072_phase7_2_printing.sql
?? src/AlKhayal.Data/Migrations/073_phase7_3_labels.sql
?? src/AlKhayal.Data/Printing/SqliteLabelRepository.cs
?? src/AlKhayal.Data/Printing/SqlitePrintPaymentReader.cs
?? src/AlKhayal.Data/Printing/SqlitePrintRepository.cs
?? src/AlKhayal.Data/Workforce/SqliteWorkforceStore.cs
?? src/AlKhayal.Printing/NativeLabelRenderer.cs
?? src/AlKhayal.Printing/NativePrintRenderer.cs
?? src/AlKhayal.Printing/WindowsPrinterBackend.Labels.cs
?? src/AlKhayal.Printing/WindowsPrinterBackend.cs
?? tests/AlKhayal.IntegrationTests/LabelPersistenceTests.cs
?? tests/AlKhayal.IntegrationTests/LabelsApiTests.cs
?? tests/AlKhayal.IntegrationTests/PrintingApiTests.cs
?? tests/AlKhayal.IntegrationTests/PrintingPersistenceTests.cs
?? tests/AlKhayal.IntegrationTests/WorkforceApiTests.cs
?? tests/AlKhayal.IntegrationTests/WorkforcePersistenceTests.cs
?? tests/AlKhayal.UnitTests/LabelLayoutTests.cs
?? tests/AlKhayal.UnitTests/PrintingLayoutTests.cs
?? tests/AlKhayal.UnitTests/WorkforceServiceTests.cs
```
