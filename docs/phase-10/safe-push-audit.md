# Phase 9/10 tracking cleanup and developer handoff — 2026-10-30

## Scope and tracking cleanup

Phase 9 reports, payment instruments, migrations 074/075, pricing/FIFO and return/maintenance regressions, Phase 10 licensing/storage, WinForms activation/UAC, the production **public** verification key, owner License Manager source and their tests/docs are included. Existing solution/project files use source globs; the Data project explicitly embeds migrations and trusted public XML and runs the public-only key build guard.

Removed 300 generated files from the current index using `git rm --cached`, restricted to `.test-results-batch5/` and `test-artifacts/`. These include three POS test databases, the entire generated WebView2 profile (including extensionless SQLite databases, journals and caches), TRX files and generated acceptance-run scripts/results. Every local file's SHA-256 matched before/after untracking. No migration or source test fixture was removed. Tests create their own disposable databases; no test/source dependency on these artifact directories was found.

Root `.gitignore` now protects these generated directories, `.tmp`, WebView2 profiles, runtime SQLite databases and sidecars, `.alklic`/`.alkreq`, known private-key filenames/backups, owner registry exports/session/backups, `.bak` and dotenv overrides. Existing bin/obj/artifacts rules remain. Public XML, SQL migrations and test sources remain tracked. Ignore rules supplement content scans; they cannot detect every arbitrarily named secret.

## Historical sensitivity audit

Inspected all 1,433 distinct blobs (216,878,294 bytes) reachable through local refs and reflogs before this change, including local Codex checkpoint refs, with a read-only Git batch reader. Pattern checks covered private PEM bodies, RSA private XML components, common provider tokens, credential assignments/URLs and complete licensing request/license strings. Matching private-key header strings were parser/test assertions, without key bodies. Credential literals were disposable test-only values, not deployed defaults. No production signing key, credential or signed license was found in this scope. This is a scoped pattern/content audit, not a guarantee against every possible secret encoding.

SQLite blobs were deserialized into memory for metadata/aggregate inspection; no original database was opened for writing and no rows, hashes, tokens, contact values or license contents were printed. WAL-mode header bytes were adjusted only in the disposable in-memory copy to allow inspection without its original WAL.

The three POS databases in main's history are synthetic:

- `.test-results-batch5/gui-isolated-20261023.db`: no users, customers, suppliers, products or business transactions; default settings only.
- `test-artifacts/phase-1-to-4-acceptance-2026-10-24/database/alkhayal-acceptance.db`: empty business/account tables; default settings only.
- `test-artifacts/phase-1-to-4-acceptance-2026-10-24/winforms-run-001/database/alkhayal-ui-acceptance.db`: generated acceptance accounts, four test products and four test suppliers; all supplier contact fields match literal values in its historical scenario script. No customers or populated company/contact profile; sessions expired. The script generates its test password at runtime.

Historical WebView2 SQLite inspection found no saved logins, cookies, device-bound sessions, autofill/payment records or stored tokens. History URLs were loopback only. The ignored seeded database also appears in local checkpoint objects, but is absent from main/origin/main history and is not included in this push. Local checkpoint refs are not push targets.

Removing current tracking does **not** remove historical objects. Synthetic fixtures/cache files remain in existing main history; no history rewrite was performed. The ignored production verification input was not read or copied in this task. Production activation, signing keys, owner registry and the real POS database were left untouched.

## Verification

All 528 files in the initial cleaned index were scanned directly from staged blobs; no forbidden generated files, SQLite signatures, private key material, provider tokens or complete production licensing strings were found. Working-tree credential-assignment findings were confined to disposable tests. The public key `rsa-17b43bcb5933edf6b1e9463d.xml` is tracked and passes the public-only build guard and embedded-resource unit regression. No private key is needed to restore/build the POS.

`git diff --cached --check` passes after removing one trailing-space line and two surplus EOF blank lines in new files. Git's LF-to-CRLF warnings reflect existing checkout configuration; no bulk line-ending normalization or whole-file formatting was performed.

Built/tested a clean `git checkout-index` export under ignored `.tmp/safe-push-3039ff464ae54abba24ff74c8ef4960a/`, with no copied bin/obj/runtime/business database. NuGet restored dependencies into the normal developer cache. Node 24 was supplied externally; it is an explicit tooling dependency, not source embedded in the export. Package tests used a new temporary package assembled from the installer's allowlist, with separate temporary owner data and outbound network denial; the installed owner Desktop package was not changed.

Actual results from that clean export:

- Full solution restore/build: passed, zero warnings/errors.
- Full unit suite: 224 passed, zero failed/skipped, including trusted public-resource, activation, UAC/storage and Node/.NET interoperability coverage.
- Full integration suite: 350 passed, zero failed/skipped.
- Owner manager and disposable packaged-launch suite: 8 passed, zero failed/skipped.
- Frontend registry/behavior suite: passed, 126 screens.
- JavaScript syntax: all 62 frontend/owner source and test files passed.
- Staged whitespace validation and final staged secret/prohibited-file scan: passed.

TRX evidence is retained locally under the clean export's `artifacts/verification/results/`; generated evidence is excluded from Git. The final added audit document is documentation-only; no executable source changed after the tested export. The final index contains 529 retained source/assets/docs files, including this audit.

## Colleague prerequisites and commands

Use a supported Windows development machine with the .NET 10 SDK (for this `.slnx`), .NET Framework 4.8 developer/targeting pack, PowerShell and Node 24.15+ in the 24.x line. The verification here used SDK 10.0.402 and Node 24.22.2. Initial NuGet restore needs available package sources or a populated package cache. The owner tool uses built-in `node:sqlite`; no npm install is required. Desktop testing additionally needs a compatible WebView2 runtime. Windows 7 SP1/Chromium 109 and actual separate-account UAC remain manual deployment checks; owner Node 24 has no Windows 7 claim.

Fresh clone (choose a new destination):

```powershell
git clone --branch main https://github.com/AbdulrhmanHelmy/AlKhayal.git AlKhayal-phase910
Set-Location AlKhayal-phase910
```

For an existing clean main checkout, use `git pull --ff-only origin main` instead. Then run from the repository root:

```powershell
$ErrorActionPreference = 'Stop'
$node24 = (Get-Command node.exe).Source
& $node24 --version # Must be v24.15+ and below v25.
$env:ALKHAYAL_LICENSE_MANAGER_NODE = $node24
$env:ALKHAYAL_DATABASE_PATH = Join-Path $env:TEMP ('alk-colleague-' + [guid]::NewGuid().ToString('N') + '.db')
dotnet restore AlKhayal.slnx --artifacts-path artifacts/colleague
if ($LASTEXITCODE) { throw 'Restore failed' }
dotnet build AlKhayal.slnx --artifacts-path artifacts/colleague --no-restore -v minimal
if ($LASTEXITCODE) { throw 'Build failed' }
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/colleague --no-build --logger 'trx;LogFileName=unit.trx' --results-directory artifacts/colleague/results
if ($LASTEXITCODE) { throw 'Unit tests failed' }
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/colleague --no-build --logger 'trx;LogFileName=integration.trx' --results-directory artifacts/colleague/results
if ($LASTEXITCODE) { throw 'Integration tests failed' }
& $node24 frontend/tests/run-tests.js
if ($LASTEXITCODE) { throw 'Frontend tests failed' }
```

For all eight owner-tool tests without installing/replacing a Desktop package, prepare a disposable package from the same source allowlist:

```powershell
$ownerSource = Join-Path (Get-Location) 'tools/AlKhayal.LicenseManager'
$package = Join-Path $env:TEMP ('alk-colleague-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $package,(Join-Path $package 'public'),(Join-Path $package 'runtime') | Out-Null
$files = @('protocol.cjs','registry.cjs','server.cjs','launch.cjs','secure-storage.ps1','open-browser.vbs','error.vbs','AlKhayal License Manager.vbs','package.json')
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $ownerSource $file) -Destination (Join-Path $package $file) }
foreach ($file in @('index.html','style.css','app.js')) { Copy-Item -LiteralPath (Join-Path $ownerSource ('public/' + $file)) -Destination (Join-Path $package ('public/' + $file)) }
Copy-Item -LiteralPath $node24 -Destination (Join-Path $package 'runtime/node.exe')
$env:ALKHAYAL_LICENSE_MANAGER_PACKAGE = $package
$ownerTests = @(Get-ChildItem -LiteralPath (Join-Path $ownerSource 'tests') -Filter '*.test.cjs' | ForEach-Object { $_.FullName })
& $node24 --test @ownerTests
if ($LASTEXITCODE) { throw 'Owner tests failed' }
```

## First-time offline activation acceptance

Use a new Windows test machine/VM without an existing AlKhayal machine-wide activation. Do not delete another installation's activation to simulate first run. In the same PowerShell session, set an isolated database/profile and launch the built POS normally:

```powershell
$env:ALKHAYAL_FRONTEND_PATH = Join-Path (Get-Location) 'frontend'
$env:ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER = Join-Path $env:TEMP ('alk-colleague-webview-' + [guid]::NewGuid().ToString('N'))
Remove-Item Env:ALKHAYAL_SERVER_URL -ErrorAction SilentlyContinue
& .\artifacts\colleague\bin\AlKhayal.UI\debug\AlKhayal.UI.exe
```

The activation dialog appears before login. Copy that machine's request and transfer it privately to the owner. The owner uses the existing License Manager and existing production key to issue its matching license; return the activation code privately and paste it into POS. Accept the bounded installation UAC request, then verify login/first-admin setup and normal restart without another activation prompt. Test UAC cancellation/retry before accepting. A public key alone cannot issue activation codes; a different machine requires its own signed license. Existing production keys/licenses are neither regenerated nor included in Git.

For the exact committed path list, run `git show --name-status --format=fuller HEAD` after pulling this change.

## Included source, test, asset and documentation paths

- `.gitignore`
- `docs/api-contracts.md`
- `docs/architecture.md`
- `docs/database.md`
- `docs/design.md`
- `docs/hotfixes/sales-return-conflict.md`
- `docs/maintenance-advance-hotfix.md`
- `docs/maintenance.md`
- `docs/payment-methods.md`
- `docs/phase-10/activation-storage-hotfix.md`
- `docs/phase-10/owner-manager-changed-files.md`
- `docs/phase-10/phase-10-1-changed-files.md`
- `docs/phase-10/phase-10-1-licensing.md`
- `docs/phase-10/production-public-key-integration.md`
- `docs/phase-10/safe-push-audit.md`
- `docs/phase-9/phase-9-1.md`
- `docs/phase-9/phase-9-2.md`
- `docs/phase-9/phase-9-3.md`
- `docs/phase-9/phase-9-final-acceptance.md`
- `docs/phase-9/pre-phase-9-pricing-fifo.md`
- `docs/qa/confirmed-bugs.md`
- `docs/qa/release-readiness-audit.md`
- `docs/qa/scenario-manifest.json`
- `docs/testing.md`
- `frontend/css/modules.css`
- `frontend/data/screens.json`
- `frontend/index.html`
- `frontend/js/app.js`
- `frontend/js/modules/expense-report.js`
- `frontend/js/modules/net-profit-report.js`
- `frontend/js/modules/operational-reports.js`
- `frontend/js/modules/payment-methods.js`
- `frontend/js/modules/renderers.js`
- `frontend/js/modules/report-ui.js`
- `frontend/js/modules/sales-report.js`
- `frontend/js/modules/sales.js`
- `frontend/js/modules/stock-reports.js`
- `frontend/js/modules/transaction-reports.js`
- `frontend/tests/invoice-return-badges-tests.js`
- `frontend/tests/payment-methods-tests.js`
- `frontend/tests/pricing-fifo-tests.js`
- `frontend/tests/report-tests.js`
- `frontend/tests/run-tests.js`
- `scripts/run-acceptance-tests.ps1`
- `scripts/validate-license-public-keys.ps1`
- `src/AlKhayal.Api/ApiBootstrap.cs`
- `src/AlKhayal.Api/Controllers/PaymentMethodsController.cs`
- `src/AlKhayal.Api/Controllers/ReportsController.cs`
- `src/AlKhayal.Api/Controllers/SalesController.cs`
- `src/AlKhayal.Api/LicenseAccessHandler.cs`
- `src/AlKhayal.Application/Catalog/CatalogService.cs`
- `src/AlKhayal.Application/Catalog/CatalogVariantContracts.cs`
- `src/AlKhayal.Application/Licensing/LicenseContracts.cs`
- `src/AlKhayal.Application/Licensing/LicenseFormat.cs`
- `src/AlKhayal.Application/Licensing/LicenseInstallation.cs`
- `src/AlKhayal.Application/Payments/PaymentInstrumentService.cs`
- `src/AlKhayal.Application/Reports/BusinessOverviewService.cs`
- `src/AlKhayal.Application/Reports/NetProfitReport.cs`
- `src/AlKhayal.Application/Reports/OperationalReports.cs`
- `src/AlKhayal.Application/Reports/ReturnTaxAllocation.cs`
- `src/AlKhayal.Application/Reports/SalesReport.cs`
- `src/AlKhayal.Application/Reports/StockReports.cs`
- `src/AlKhayal.Application/Reports/TransactionReports.cs`
- `src/AlKhayal.Application/Sales/SalesContracts.cs`
- `src/AlKhayal.Application/Sales/SalesPricing.cs`
- `src/AlKhayal.Application/Sales/SalesService.cs`
- `src/AlKhayal.Data/AlKhayal.Data.csproj`
- `src/AlKhayal.Data/Catalog/SqliteCatalogConfigurationStore.cs`
- `src/AlKhayal.Data/Inventory/SqliteInventoryStore.Valuation.cs`
- `src/AlKhayal.Data/Licensing/FileLicenseStorage.cs`
- `src/AlKhayal.Data/Licensing/RsaLicenseSignatures.cs`
- `src/AlKhayal.Data/Licensing/TrustedKeys/README.md`
- `src/AlKhayal.Data/Licensing/TrustedKeys/rsa-17b43bcb5933edf6b1e9463d.xml`
- `src/AlKhayal.Data/Licensing/WindowsHardwareIdentity.cs`
- `src/AlKhayal.Data/Licensing/WindowsLicensePath.cs`
- `src/AlKhayal.Data/Maintenance/SqliteMaintenanceRepository.cs`
- `src/AlKhayal.Data/Migrations/074_payment_instruments.sql`
- `src/AlKhayal.Data/Migrations/075_expense_profit_classification.sql`
- `src/AlKhayal.Data/Payments/SqlitePaymentInstrumentStore.cs`
- `src/AlKhayal.Data/Reports/SqliteNetProfitStore.Products.cs`
- `src/AlKhayal.Data/Reports/SqliteNetProfitStore.cs`
- `src/AlKhayal.Data/Reports/SqliteOperationalReportStore.cs`
- `src/AlKhayal.Data/Reports/SqliteSalesReportStore.cs`
- `src/AlKhayal.Data/Reports/SqliteStockReportStore.cs`
- `src/AlKhayal.Data/Reports/SqliteTransactionReportStore.cs`
- `src/AlKhayal.Data/Sales/SqliteSalesStore.Returns.cs`
- `src/AlKhayal.Data/Sales/SqliteSalesStore.cs`
- `src/AlKhayal.Data/Workforce/SqliteWorkforceStore.cs`
- `src/AlKhayal.Server.Host/AlKhayalServer.cs`
- `src/AlKhayal.UI/ActivationForm.cs`
- `src/AlKhayal.UI/MainForm.cs`
- `src/AlKhayal.UI/Program.cs`
- `src/AlKhayal.UI/WindowsLicenseInstaller.cs`
- `tests/AlKhayal.IntegrationTests/ApiEndpointTests.cs`
- `tests/AlKhayal.IntegrationTests/BusinessActivityApiTests.cs`
- `tests/AlKhayal.IntegrationTests/BusinessActivityPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/CatalogApiTests.cs`
- `tests/AlKhayal.IntegrationTests/CatalogMigrationTests.cs`
- `tests/AlKhayal.IntegrationTests/CustomerInstallmentApiTests.cs`
- `tests/AlKhayal.IntegrationTests/CustomerLedgerMigrationPreflightTests.cs`
- `tests/AlKhayal.IntegrationTests/CustomerStatementApiTests.cs`
- `tests/AlKhayal.IntegrationTests/ExpenseApiTests.cs`
- `tests/AlKhayal.IntegrationTests/ExpenseReportTests.cs`
- `tests/AlKhayal.IntegrationTests/FeatureApiTests.cs`
- `tests/AlKhayal.IntegrationTests/FeaturePersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/FirstRunBootstrapTests.cs`
- `tests/AlKhayal.IntegrationTests/InventoryApiTests.cs`
- `tests/AlKhayal.IntegrationTests/InventoryFifoTests.cs`
- `tests/AlKhayal.IntegrationTests/InventoryPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/LabelsApiTests.cs`
- `tests/AlKhayal.IntegrationTests/LicenseAccessTests.cs`
- `tests/AlKhayal.IntegrationTests/MaintenancePersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/MigrationTestExpectations.cs`
- `tests/AlKhayal.IntegrationTests/MigrationTests.cs`
- `tests/AlKhayal.IntegrationTests/NetProfitApiTests.cs`
- `tests/AlKhayal.IntegrationTests/NetProfitReportTests.cs`
- `tests/AlKhayal.IntegrationTests/OperationalReportTests.cs`
- `tests/AlKhayal.IntegrationTests/OperationalReportsApiTests.cs`
- `tests/AlKhayal.IntegrationTests/PaymentInstrumentTests.cs`
- `tests/AlKhayal.IntegrationTests/PaymentMethodsApiTests.cs`
- `tests/AlKhayal.IntegrationTests/PricingFifoInvariantTests.cs`
- `tests/AlKhayal.IntegrationTests/PrintingApiTests.cs`
- `tests/AlKhayal.IntegrationTests/PurchasingPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/QaCustomerOperationKeyTests.cs`
- `tests/AlKhayal.IntegrationTests/QaPhase1SessionSecurityTests.cs`
- `tests/AlKhayal.IntegrationTests/QaPhase4EdgeTests.cs`
- `tests/AlKhayal.IntegrationTests/RefundOverdraftMigrationTests.cs`
- `tests/AlKhayal.IntegrationTests/ReportsApiTests.cs`
- `tests/AlKhayal.IntegrationTests/ReturnManagementApiTests.cs`
- `tests/AlKhayal.IntegrationTests/SalesPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/SalesReportTests.cs`
- `tests/AlKhayal.IntegrationTests/SalesReturnConflictApiTests.cs`
- `tests/AlKhayal.IntegrationTests/SalesReturnConflictTests.cs`
- `tests/AlKhayal.IntegrationTests/SalesTreasuryApiTests.cs`
- `tests/AlKhayal.IntegrationTests/SettingsApiTests.cs`
- `tests/AlKhayal.IntegrationTests/SettingsPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/StockInventoryReportTests.cs`
- `tests/AlKhayal.IntegrationTests/StockReportTests.cs`
- `tests/AlKhayal.IntegrationTests/StockReportsApiTests.cs`
- `tests/AlKhayal.IntegrationTests/SupplierAccountsApiTests.cs`
- `tests/AlKhayal.IntegrationTests/SupplierLedgerMigrationTests.cs`
- `tests/AlKhayal.IntegrationTests/SupplierRestartApiTests.cs`
- `tests/AlKhayal.IntegrationTests/TestLicenseGate.cs`
- `tests/AlKhayal.IntegrationTests/TransactionReportTests.cs`
- `tests/AlKhayal.IntegrationTests/TreasuryApiTests.cs`
- `tests/AlKhayal.IntegrationTests/TreasuryLegacyMigrationTests.cs`
- `tests/AlKhayal.IntegrationTests/UserManagementApiTests.cs`
- `tests/AlKhayal.IntegrationTests/WorkforceApiTests.cs`
- `tests/AlKhayal.UnitTests/ActivationDialogTests.cs`
- `tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj`
- `tests/AlKhayal.UnitTests/LicenseInstallationTests.cs`
- `tests/AlKhayal.UnitTests/LicensingTests.cs`
- `tests/AlKhayal.UnitTests/NetProfitServiceTests.cs`
- `tests/AlKhayal.UnitTests/NodeLicenseCompatibilityTests.cs`
- `tests/AlKhayal.UnitTests/OperationalReportServiceTests.cs`
- `tests/AlKhayal.UnitTests/PaymentInstrumentValidationTests.cs`
- `tests/AlKhayal.UnitTests/ReportPeriodTests.cs`
- `tests/AlKhayal.UnitTests/ReturnTaxAllocationTests.cs`
- `tests/AlKhayal.UnitTests/SalesPricingTests.cs`
- `tests/AlKhayal.UnitTests/StockReportServiceTests.cs`
- `tests/AlKhayal.UnitTests/TrustedPublicKeyRegistryTests.cs`
- `tools/AlKhayal.LicenseManager/.gitignore`
- `tools/AlKhayal.LicenseManager/AlKhayal License Manager.vbs`
- `tools/AlKhayal.LicenseManager/README.md`
- `tools/AlKhayal.LicenseManager/error.vbs`
- `tools/AlKhayal.LicenseManager/install-desktop.ps1`
- `tools/AlKhayal.LicenseManager/launch.cjs`
- `tools/AlKhayal.LicenseManager/open-browser.vbs`
- `tools/AlKhayal.LicenseManager/package.json`
- `tools/AlKhayal.LicenseManager/protocol.cjs`
- `tools/AlKhayal.LicenseManager/public/app.js`
- `tools/AlKhayal.LicenseManager/public/index.html`
- `tools/AlKhayal.LicenseManager/public/style.css`
- `tools/AlKhayal.LicenseManager/registry.cjs`
- `tools/AlKhayal.LicenseManager/secure-storage.ps1`
- `tools/AlKhayal.LicenseManager/server.cjs`
- `tools/AlKhayal.LicenseManager/tests/browser-fixture.cjs`
- `tools/AlKhayal.LicenseManager/tests/cross-sign.cjs`
- `tools/AlKhayal.LicenseManager/tests/manager.test.cjs`
- `tools/AlKhayal.LicenseManager/tests/offline-guard.cjs`
- `tools/AlKhayal.LicenseManager/tests/packaged.test.cjs`
