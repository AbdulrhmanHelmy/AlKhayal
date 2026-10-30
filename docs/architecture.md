# Architecture

Pre-Phase 9 pricing keeps Application `SalesPricing` responsible for exact selected-unit sale-price conversion; Data validates the current default during draft save and retains persisted prices/costs at posting. Catalog edits preserve variant overrides and only release an evidenced sole-variant receipt mirror when the simple product price is explicitly changed. See [pricing/FIFO audit and reporting contracts](phase-9/pre-phase-9-pricing-fifo.md). No reporting boundary or migration is introduced.

Sales refund overdraft is an explicit scoped treasury-writer capability. Application `SalesRefundPolicy` prioritizes reducing current credit-customer debt to zero and allocates any remaining return value to cash. Return transactions never touch shift revenue. See [sales-refund-overdraft.md](sales-refund-overdraft.md).

Unified Returns uses `ReturnService`/`IReturnReadStore` and existing Sales/Purchase posting services. Both return writers share their transaction with treasury cash effects, source links and sales-return withdrawal solely from the returns cashbox. See [returns-management.md](returns-management.md).

Cash Sales posting now shares its writer transaction with the treasury deposit, account provisioning and immutable source link for `إيراد الوردية`; credit sales retain the customer-ledger boundary. See [sales-shift-revenue.md](sales-shift-revenue.md).

Expenses follow `ExpenseService` → `IExpenseStore` → `SqliteExpenseStore` with authenticated HTTP adapters. Posting a voucher shares the treasury writer transaction for source withdrawal, audit and account version. See [expenses.md](expenses.md).

Phase 2.3 product tracking configuration and the deferred physical-stock boundary are specified in [phase-2-task-3-tracking.md](phase-2-task-3-tracking.md).

## Persisted treasury boundary

Customer Statement now owns the customer account-operation UI for persisted increases and collections. It reuses `CustomerAccountService`/`ICustomerAccountRepository`, requires effective statement access, preserves transactional/idempotent posting, and distinguishes full current debt from a period closing balance. Its existing unfiltered-ledger double-counting and missing render-context defects are fixed without changing historical data. See [customer-statement.md](customer-statement.md).

Installments use `InstallmentService` and `ICustomerInstallmentRepository`. Contract, immutable due schedule, initial customer-ledger debit/down-payment credit, per-installment payment, customer balance projection and statement movements are committed in SQLite transactions. Operation keys make contract creation and payments replay-safe. See [installments.md](installments.md).

Supplier settlement now shares the treasury writer in the supplier-ledger transaction. Application `SupplierPaymentAllocator` orders invoice allocations by due date (invoice date fallback), then original creation time. Immutable allocation and treasury-link tables let reads project invoice paid/remaining without modifying posted purchase invoices. See [supplier-payment-treasury.md](supplier-payment-treasury.md).

Supplier Accounts reuses the existing Supplier Application services and SQLite account/ledger ports. The directory is unchanged; `supplier-payment` groups each supplier's invoices and exposes new debt invoices and payments. Invoice creation atomically records an idempotent, actor-bound ledger operation with the supplier balance/credit projection. Source invoice identity separates account invoices from purchase invoices, and posted returns reduce payment eligibility. See [supplier-accounts.md](supplier-accounts.md).

`TreasuryService` -> `ITreasuryStore` -> `SqliteTreasuryStore` implements cashbox/bank CRUD and atomic idempotent manual deposits/withdrawals. `TreasuryController` enforces authentication, CSRF and treasury screen permissions. The frontend calls only this HTTP boundary. Immutable ledger movements derive balances; metadata, deletion tombstones and postings retain transactional audit. Cash purchase posting shares the treasury ledger writer and the purchase SQLite transaction; its withdrawal, stock/FIFO, supplier settlement and audit commit atomically. Other sales/customer/supplier financial events keep their existing separate boundaries. See [purchase-treasury.md](purchase-treasury.md). See [treasury.md](treasury.md).

## Phase 2.1 catalog vertical slice

`CatalogService` in Application owns product/category/unit validation, fixed-scale price conversion, idempotent create request hashes, and optimistic version rules. `ICatalogStore` is the port; `SqliteCatalogStore` owns short transactions and parameterized SQL. `CatalogController` translates authenticated HTTP contracts, validates CSRF on mutations, checks effective cashier screen access, and keeps activation, archive, migration review, category writes, and unit writes administrator-only where specified. The host composes these dependencies; the frontend never opens SQLite directly.

Migration 008 is additive. It retains `Products.Id`, `Products.Name`, and the original `Products.Price`, while `ProductDetails.SellPriceScaled` becomes the authoritative price. A hidden default variant gives every product a future-compatible identity without exposing incomplete variant setup. No inventory ledger, stock quantity, warehouse, barcode, or tracking persistence is implemented by Task 2.1. The five-task future boundary is defined in `docs/phase-2-architecture-blueprint.md`.

## General settings boundary

`Application.Settings` owns typed company/general contracts, validation, effective-value caching, and the database-information port. `Data.Settings` owns the SQLite implementation and transactional audit writes. `SettingsController` translates authenticated HTTP requests only, and the host composes the service with the configured database path. Business-wide settings never depend on web storage; the existing theme controller intentionally keeps appearance as a user/device preference.

## Solution boundaries

AlKhayal is a modular monolith with directional dependencies: `Core -> nothing`, `Application -> Core`, `Data -> Application + Core`, `Api -> Application + Data`, `Server.Host -> Api + Data`, and `UI -> Server.Host`. `Printing` is an isolated output abstraction. Business rules belong in Application; HTTP controllers and the WinForms shell remain adapters.

The implemented server boundary covers system health, authentication, user management, cashier screen access, business activity profiles, feature management, and persisted customer CRUD. The customer workflow follows Application service -> Core repository interface -> SQLite Data repository -> authenticated API -> frontend API client. Other business screens use deterministic in-memory services in the browser; they do not bypass the architecture with direct SQLite calls. User lifecycle/access rules live in `Application.Users`; activity rules live in `Application.BusinessActivities`; feature resolution and dependency rules live in `Application.Features`; customer business logic lives in `Application.Customers`; SQLite implements their ports in Data; controllers only translate session/role/CSRF and HTTP concerns. A future persisted business operation must follow the same Application-port/Data-adapter/API flow.

## Runtime topology

Startup resolves a writable SQLite path, applies embedded migrations in lexical order, composes authentication, business activity, and feature services, starts OWIN, and serves `frontend/` from the same origin. Standalone UI binds `127.0.0.1:5137` by default and navigates WebView2 to `/login.html`. A regular browser exercises the same frontend and HTTP contracts.

`ALKHAYAL_SERVER_URL`, `ALKHAYAL_DATABASE_PATH`, and `ALKHAYAL_FRONTEND_PATH` provide explicit deployment/test overrides. In LAN server mode SQLite remains on the server's local disk; LAN clients navigate to the configured server URL and never open a database through a network share. Cloud synchronization and an ASP.NET Core/PostgreSQL host are future work.

## Frontend composition

`frontend/data/screens.json` is the route and navigation source of truth. `app.js` authenticates, builds the shell, owns workspace tabs, and selects one renderer. Shared UI primitives live in `components/ui.js`; specialized renderers live in `modules/`; deterministic records live in `mocks/data.js`; and session-only CRUD/query behavior is exposed by `services/mock-services.js`. Dashboard and POS have focused renderers while related screen families share explicit schema-driven renderers.

`business-activities.js` is a real API-backed renderer. After authentication the shell reads the saved selection and opens this route only when setup is incomplete. It never stores activity configuration in browser storage.

`feature-state.js` loads one effective server snapshot and is the frontend route-visibility decision point. `feature-management.js` edits real state. Sidebar, quick access, global search, collapsed navigation, direct navigation, and workspace tabs all consult that service; source routes stay registered in `screens.json` with optional `featureKey` metadata.

The operational sidebar and administration hub share that same registry and access pipeline. `settings-program` renders Settings entries from `settingsGroup` metadata; it is not a second navigation registry. Hidden legacy aliases resolve through centralized `redirectTo` handling before access and tab creation. Static no-store headers plus a one-time desktop network-cache clear prevent a cached shell from loading a newer registry without its renderer scripts.

Maintenance is a persisted workflow behind the local API: Application validates intake/status/delivery commands, `IMaintenanceRepository` owns the persistence boundary, and SQLite stores tickets, status history and advance/delivery receipt records. Intake advances are ticket prepayments rather than collections of existing customer debt. Delivery posts the final service charge and applies the saved advance plus any remaining cash settlement to the customer's statement in one database transaction, reusing matching legacy advance entries without duplication. These maintenance entries do not post to treasury/cashbox.

`access-state.js` loads the authenticated account's effective screen IDs, deduplicates concurrent reads, and emits only when the role/access signature changes. The shell requires both feature and screen access for sidebar, quick access, search, dashboard shortcuts, direct routes, and open tabs. Feature changes refresh access without a recursive renderer event.

The frontend has no build step, framework, CDN, remote font, or service worker. It uses relative same-origin requests, stores only the CSRF token and harmless UI preferences in web storage, and targets Chromium 109 syntax and APIs.

## Desktop compatibility

The WinForms shell owns host lifetime, WebView2 runtime detection, application-specific browser data, navigation allowlisting, connection errors, and disposal. It currently targets x86 because that is the runtime/native-loader combination verified on the development machine. Windows 7 requires a separately packaged Chromium 109-compatible WebView2 runtime and real-device testing; that deployment has not been qualified.
# Phase 2 / Task 2.4 inventory boundary

The standalone host now composes `InventoryService` with `SqliteInventoryStore`. The Warehouse Directory and browser clients use authenticated `/api/inventory/*` calls; neither WinForms nor JavaScript owns stock quantities. `InventoryService` validates command shape, fixed-scale decimal input, feature gates, and request identity. `SqliteInventoryStore` owns the single SQLite posting transaction, tracking identity changes, immutable movements, derived balances, and audit events. See [inventory-ledger.md](inventory-ledger.md) for invariants and limits.

FIFO extends the same posting boundary: Application `InventoryCosting` owns exact selected-unit money conversion and allocation rounding; Data atomically persists FIFO layers/allocations, catalog variant pricing, physical identities, movements and projections. The existing catalog variant override model is reused inside the inventory transaction. The administrator reconciliation port records a documented legacy opening basis without reconstructing or changing historical movements. See [inventory-fifo.md](inventory-fifo.md).

## Phase 7.1 workforce boundary

Employees, optional attendance and cashier shifts now use Application/Workforce ports and a SQLite store, with thin authenticated HTTP adapters. Existing sale posting and invoice cash collections append owner-scoped shift events inside their original transactions. The WinForms shell remains unchanged. See [Phase 7.1](phase-7/phase-7-1.md) for authorization, lifecycle and excluded financial sources.

## Native printing — Phase 7.2

Application owns validated print contracts, document projections and orchestration; Data persists printing settings/templates and job diagnostics; the isolated Printing project implements native System.Drawing/PrintDocument output. Server.Host composes the backend through IPrinterBackend. No WebView2/browser print engine is used. See [Phase 7.2 architecture and verification](phase-7/phase-7-2.md).

## Barcode and native label extension — Phase 7.3

BarcodeService extends the existing catalog aggregate through IProductBarcodeStore; SQLite owns atomic replacement/retirement/version/audit/operation replay. LabelService/ILabelRepository load authoritative catalog snapshots. LabelBatch/ILabelRenderer/ILabelPrinterBackend are nonfinancial contracts implemented by the same WindowsPrinterBackend and shared PrintingService/PrintJobs lifecycle as receipts. No browser/PDF output or second printing subsystem. See [Phase 7.3](phase-7/phase-7-3.md) and [final Phase 7 acceptance](phase-7/phase-7-final-report.md).


## Sales payment instruments

Configured wallets and card terminals, atomic full-invoice attribution, administrator-only management/history, legacy contract mapping, electronic refund restrictions and physical cash shift exclusion are documented in [payment-methods.md](payment-methods.md). Migration 074 is additive; original financial records and credit collection workflows remain intact.

## Phase 9.1 read-only reporting

Application Reports query contracts/services own input and business-calendar validation; SQLite reporting stores own parameterized aggregates and server pages; ReportsController owns session/effective-screen authorization and HTTP mapping. Server.Host composes both reporting services. Original financial/header/event sources remain authoritative and no posting/repricing/FIFO logic is invoked by a report. All four screens use the shared frontend and existing navigation/permission contracts. See [report boundaries, verification and remaining visual acceptance](phase-9/phase-9-1.md).

The Phase 9.2 expense voucher report extends the existing reporting Application port and SQLite adapter. It reads voucher records without posting or interpreting treasury movements as additional expenses. See [Phase 9.2](phase-9/phase-9-2.md#expense-voucher-report--recovery-implementation).

Net Profit uses `NetProfitService`/`INetProfitStore`, `SqliteNetProfitStore`, the existing `ReportsController`, and host composition. Application validates dates/breakdown/classification and builds timezone-aware calendar boundaries. Data performs authoritative SQL aggregation and invokes the existing Application return-allocation helper over streamed immutable histories. Classification is separate append-only metadata, protected by administrator/CSRF checks, optimistic revisions and operation identity. Reports never invoke a posting or new FIFO algorithm; incomplete accounting remains explicit through API/UI nulls and coverage warnings. See [the completed vertical slice and remaining Phase 9.2 scope](phase-9/phase-9-2.md).

## Phase 9.2 product and inventory reporting

`StockReportService` / `IStockReportStore` extend the existing Reports Application boundary. Data `SqliteStockReportStore` reuses the product partial of `SqliteNetProfitStore` and shared existing inventory valuation evidence/calculation; controllers remain session/permission/HTTP adapters and Server.Host owns composition. Top Sellers/Availability receive server-side financial-field removal to preserve independent profit/valuation permissions. The five frontend screens use the existing same-origin API client, registry, report controls and workspace. No new writer, migration, dependency or UI-owned financial arithmetic. [Full contracts and accounting scope](phase-9/phase-9-2.md#final-five-report-implementation--2026-10-30).

## Phase 9.3 operational reports

Application adds `OperationalReportService`/read port plus `BusinessOverviewService`; Data supplies read-only SQLite aggregates. Existing `ReportsController` authenticates each report and each overview source, including module availability; Host composes these services. Overview reuses authoritative reporting services and does not own a second profit formula. The existing workforce shift SQL source is shared internally. Frontend adds a renderer using `report-ui.js` and original module record links; no posting/domain dependency enters WinForms. [Contracts and acceptance](phase-9/phase-9-3.md).

## Mandatory offline activation

Application licensing ports/policy, Data RSA/WMI/file adapters, native pre-WebView2 activation, and API/OWIN fail-closed gates are implemented. Customer binaries trust embedded public keys only; owner issuance is a separate owner-only HTML/CSS/JavaScript + Node.js/SQLite utility with a bundled runtime and Desktop launcher, excluded from customer packages. The POS approved stack and directional dependencies are unchanged. See [Phase 10.1](phase-10/phase-10-1-licensing.md) for provisioning, trust rotation, hardware/cache policy and deployment limits.

## License installation privilege boundary

Normal activation validates in Application, invokes a fixed-executable UAC helper mode through ILicenseInstaller, and rereads persisted content before continuing. Helper mode exits before host/database/WebView2 composition and independently validates with embedded keys/fresh hardware identity. Data owns protected fixed-path provisioning and atomic writes. Normal startup remains asInvoker and an installed valid entitlement skips elevation. [Storage hotfix and installer contract](phase-10/activation-storage-hotfix.md).
