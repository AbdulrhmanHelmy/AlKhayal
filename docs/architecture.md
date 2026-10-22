# Architecture

Phase 2.3 product tracking configuration and the deferred physical-stock boundary are specified in [phase-2-task-3-tracking.md](phase-2-task-3-tracking.md).

## Phase 2.1 catalog vertical slice

`CatalogService` in Application owns product/category/unit validation, fixed-scale price conversion, idempotent create request hashes, and optimistic version rules. `ICatalogStore` is the port; `SqliteCatalogStore` owns short transactions and parameterized SQL. `CatalogController` translates authenticated HTTP contracts, validates CSRF on mutations, checks effective cashier screen access, and keeps activation, archive, migration review, category writes, and unit writes administrator-only where specified. The host composes these dependencies; the frontend never opens SQLite directly.

Migration 008 is additive. It retains `Products.Id`, `Products.Name`, and the original `Products.Price`, while `ProductDetails.SellPriceScaled` becomes the authoritative price. A hidden default variant gives every product a future-compatible identity without exposing incomplete variant setup. No inventory ledger, stock quantity, warehouse, barcode, or tracking persistence is implemented by Task 2.1. The five-task future boundary is defined in `docs/phase-2-architecture-blueprint.md`.

## General settings boundary

`Application.Settings` owns typed company/general contracts, validation, effective-value caching, and the database-information port. `Data.Settings` owns the SQLite implementation and transactional audit writes. `SettingsController` translates authenticated HTTP requests only, and the host composes the service with the configured database path. Business-wide settings never depend on web storage; the existing theme controller intentionally keeps appearance as a user/device preference.

## Solution boundaries

AlKhayal is a modular monolith with directional dependencies: `Core -> nothing`, `Application -> Core`, `Data -> Application + Core`, `Api -> Application + Data`, `Server.Host -> Api + Data`, and `UI -> Server.Host`. `Printing` is an isolated output abstraction. Business rules belong in Application; HTTP controllers and the WinForms shell remain adapters.

The implemented server boundary covers system health, authentication, user management, cashier screen access, business activity profiles, and feature management. The remaining business screens use deterministic in-memory services in the browser; they do not bypass the architecture with direct SQLite calls. User lifecycle/access rules live in `Application.Users`; activity rules live in `Application.BusinessActivities`; feature resolution and dependency rules live in `Application.Features`; SQLite implements their ports in Data; controllers only translate session/role/CSRF and HTTP concerns. A future persisted business operation must follow the same Application-port/Data-adapter/API flow.

## Runtime topology

Startup resolves a writable SQLite path, applies embedded migrations in lexical order, composes authentication, business activity, and feature services, starts OWIN, and serves `frontend/` from the same origin. Standalone UI binds `127.0.0.1:5137` by default and navigates WebView2 to `/login.html`. A regular browser exercises the same frontend and HTTP contracts.

`ALKHAYAL_SERVER_URL`, `ALKHAYAL_DATABASE_PATH`, and `ALKHAYAL_FRONTEND_PATH` provide explicit deployment/test overrides. In LAN server mode SQLite remains on the server's local disk; LAN clients navigate to the configured server URL and never open a database through a network share. Cloud synchronization and an ASP.NET Core/PostgreSQL host are future work.

## Frontend composition

`frontend/data/screens.json` is the route and navigation source of truth. `app.js` authenticates, builds the shell, owns workspace tabs, and selects one renderer. Shared UI primitives live in `components/ui.js`; specialized renderers live in `modules/`; deterministic records live in `mocks/data.js`; and session-only CRUD/query behavior is exposed by `services/mock-services.js`. Dashboard and POS have focused renderers while related screen families share explicit schema-driven renderers.

`business-activities.js` is a real API-backed renderer. After authentication the shell reads the saved selection and opens this route only when setup is incomplete. It never stores activity configuration in browser storage.

`feature-state.js` loads one effective server snapshot and is the frontend route-visibility decision point. `feature-management.js` edits real state. Sidebar, quick access, global search, collapsed navigation, direct navigation, and workspace tabs all consult that service; source routes stay registered in `screens.json` with optional `featureKey` metadata.

The operational sidebar and administration hub share that same registry and access pipeline. `settings-program` renders Settings entries from `settingsGroup` metadata; it is not a second navigation registry. Hidden legacy aliases resolve through centralized `redirectTo` handling before access and tab creation. Static no-store headers plus a one-time desktop network-cache clear prevent a cached shell from loading a newer registry without its renderer scripts.

`access-state.js` loads the authenticated account's effective screen IDs, deduplicates concurrent reads, and emits only when the role/access signature changes. The shell requires both feature and screen access for sidebar, quick access, search, dashboard shortcuts, direct routes, and open tabs. Feature changes refresh access without a recursive renderer event.

The frontend has no build step, framework, CDN, remote font, or service worker. It uses relative same-origin requests, stores only the CSRF token and harmless UI preferences in web storage, and targets Chromium 109 syntax and APIs.

## Desktop compatibility

The WinForms shell owns host lifetime, WebView2 runtime detection, application-specific browser data, navigation allowlisting, connection errors, and disposal. It currently targets x86 because that is the runtime/native-loader combination verified on the development machine. Windows 7 requires a separately packaged Chromium 109-compatible WebView2 runtime and real-device testing; that deployment has not been qualified.
# Phase 2 / Task 2.4 inventory boundary

The standalone host now composes `InventoryService` with `SqliteInventoryStore`. The Warehouse Directory and browser clients use authenticated `/api/inventory/*` calls; neither WinForms nor JavaScript owns stock quantities. `InventoryService` validates command shape, fixed-scale decimal input, feature gates, and request identity. `SqliteInventoryStore` owns the single SQLite posting transaction, tracking identity changes, immutable movements, derived balances, and audit events. See [inventory-ledger.md](inventory-ledger.md) for invariants and limits.
