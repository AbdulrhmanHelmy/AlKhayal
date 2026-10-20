# AlKhayal POS — Engineering Instructions

## Project identity

AlKhayal POS is a lightweight Arabic-first point-of-sale and ERP foundation. The repository implements the platform foundation, real SQLite-backed authentication, and a complete route-driven UI prototype covering 92 business screens. Business datasets and mutations are still deterministic, session-only demonstrations unless an endpoint explicitly states otherwise. It is offline-first for standalone deployments, targets Windows 7 SP1 through Windows 11 subject to the runtime caveats below, and must remain usable on low-resource hardware. The codebase is deliberately AI-agent-first: changes must be discoverable, small, explicit, testable, and documented.

Arabic is the primary user-facing language. HTML uses `dir="rtl"`, labels and validation are Arabic, and layouts must remain usable with long Arabic strings. Store dates as UTC and display local business time at the presentation boundary.

## Approved stack and deployment

-  C#, .NET Framework 4.8 for the desktop, API, host,data, printing, and tests; .NET Standard 2.0 for Core and Application where compatible.
- Windows Forms shell with Microsoft WebView2. The frontend is HTML5/CSS3/vanilla JavaScript, with no React, Angular, Electron, runtime CDN, or remote-font dependency.
- ASP.NET Web API 2 on OWIN Self-Host. The API serves the same `frontend/` files and is the business-operation boundary.
- SQLite with Dapper/plain parameterized SQL and ordered embedded migrations. No PostgreSQL implementation yet.
- Standalone mode binds `127.0.0.1`; LAN server mode binds a configured server interface and keeps SQLite on that server's local disk; LAN clients navigate to the configured server URL and do not start a local API. Cloud mode is future work on a separate modern host (ASP.NET Core/PostgreSQL).

Do not reintroduce WPF or replace the approved stack without an explicit architecture decision record. Never use SQLite over a network share. The desktop may be replaced later, but the shared frontend and HTTP contract should remain portable.

## Architecture rules

This is a modular monolith with directional dependencies:

`Core -> (nothing)`; `Application -> Core`; `Data -> Application, Core`; `Api -> Application, Data`; `Server.Host -> Api, Data`; `UI -> Server.Host`; `Printing` is an isolated output abstraction. Core contains domain models and interfaces only. Application contains use cases and ports, not HTTP, UI, or concrete infrastructure. Data owns SQLite/Dapper repositories, migrations, and hashing. API controllers are thin adapters. WinForms owns startup, WebView2 lifecycle, navigation policy, and user-visible connection states; it must not contain sales, accounting, or authentication rules. Printing must be invoked through an abstraction.

Keep local/LAN/cloud boundaries explicit. A local API is the same business boundary used by the browser and WebView2. A future cloud host may reuse Core/Application contracts but must not force cloud concerns into the local host.

## Frontend standards

Use `frontend/css` for tokens, themes, shared app styles, and page styles; `frontend/js` for the API client and page behavior. Prefer semantic HTML, small modules, stable IDs, and Arabic labels. Use navy/gold tokens, light/dark themes, keyboard navigation, visible focus, clear loading/error/empty states, and responsive desktop-first layouts. Do not add a framework or CDN.

`frontend/data/screens.json` is the canonical screen/navigation registry. Every new route must have unique metadata, a registered renderer, a module navigation reference or documented deep-link purpose, and coverage in `frontend/tests/run-tests.js`. Shared deterministic sample records belong in `frontend/js/mocks/data.js`; business-screen mutations must remain behind `frontend/js/services/mock-services.js` until a real Application/API use case exists. Never present session-only mock actions as persisted accounting, inventory, printing, backup, or sales operations.

All API calls use relative URLs in the same-origin browser and `credentials: same-origin`; the client never stores passwords or bearer tokens in localStorage. Validate in JavaScript for usability, then validate again in Application/API. Keep JavaScript compatible with Chromium 109: avoid browser APIs unavailable there, transpilation assumptions, and excessive animation. Design for low memory and fast first render.

### UI shell and desktop UX contract

The shell has one viewport owner: `.app-shell` fills the WebView2 viewport, `.app-main` owns application chrome, and `.workspace` owns page-level vertical scrolling. The sidebar navigation scrolls independently and its footer remains accessible. Every grid/flex boundary that can contain long Arabic content must use `min-width: 0` and `min-height: 0`. Never add a second module-level `100vh` or `calc(100vh - ...)`; use intrinsic responsive sizing and bounded local scrolling for genuinely wide tables, POS panes, search results, and modal bodies.

Shared responsive breakpoints are 1180px, 1080px, 1050px, 760px, and 560px. At 1080px the sidebar becomes a persisted 64px SVG icon rail without overwriting the user's saved preference. Quick-access tiles and workspace tabs must remain bounded and keyboard reachable. Mock notices use the compact wording `وضع تجريبي — البيانات غير محفوظة فعليًا`.

The WinForms host uses `src/AlKhayal.UI/app.manifest` for Per-Monitor DPI awareness and `AutoScaleMode.Dpi`; WebView2 is dock-filled, starts at zoom 1.0, disables built-in zoom/pinch, and blocks only desktop-host browser zoom gestures. Do not use CSS `zoom`, `transform: scale()`, or global browser-zoom suppression. See `docs/ui-audit.md` and `docs/design.md` before changing shell behavior.

## Backend standards

Use PascalCase for public C# symbols and camelCase JSON. Controllers must translate HTTP to application commands and contracts only. Business rules belong in Application services. Use dependency injection/composition at the host boundary; do not make infrastructure a hidden global dependency. Validate all input, return the consistent `{ success, data, error }` envelope, log actionable failures without secrets, and never swallow exceptions silently.

Use async I/O for database and HTTP work, short-lived connections, short transactions, and cancellation tokens. Financial operations must later be idempotent and require a client operation key. Authentication uses server-side session validation, HttpOnly SameSite cookies, strong salted password hashes, and authorization checks at every protected endpoint. Add rate limiting and CSRF protection before production cookie-authenticated LAN/cloud deployment.

## Database standards

Schema is in `src/AlKhayal.Data/Migrations/NNN_name.sql` and is applied in lexical order by `Migrator`. Before first release migrations may be edited/merged; after release never edit an applied migration—add a new numbered migration. Use Dapper or ADO.NET with parameters such as `@Name`; never concatenate values into SQL. Enable foreign keys, add indexes for lookup/expiry paths, use UTC ISO timestamps, and do not recreate or delete the database at startup.

The writable database belongs in LocalApplicationData or an explicitly configured data directory outside Program Files. Back up the SQLite file while the application is quiesced and verify restores periodically. Monetary values require explicit decimal/minor-unit policy before sales work; do not use floating point for money. Keep persistence boundaries explicit so PostgreSQL can be introduced later without leaking SQLite SQL into Application.

## POS-specific rules

Future sales work must preserve an auditable, immutable financial trail. Checkout needs a unique operation key to prevent duplicates, atomic sale/payment/inventory transactions, explicit refund/reversal records, and deterministic rounding. Inventory changes must be tied to a source document and remain explainable. Receipts go through `IReceiptPrinter`; printer disconnects, paper errors, and retries must not corrupt the sale. Multi-terminal LAN behavior needs terminal identity, server-side concurrency control, and clear offline-versus-server semantics. These are requirements, not claims that the prototype already implements.

## Security and performance

Never commit plaintext credentials, universal demo passwords, password hashes in responses, or secrets in frontend code. Explicit development-user provisioning reads a hidden password from the console and stores only its salted PBKDF2 hash; it is not an automatic production feature. Passwords may be any non-empty string because deployment policy is operator-controlled, though strong passwords remain recommended. Default local binding is loopback. LAN mode requires explicit binding, firewall/TLS decisions, authentication, and CSRF/rate-limit review. WebView2 allows only the configured application origin and must not load arbitrary external resources. Use an application-specific user-data directory and dispose the host/browser correctly.

Keep background processes and dependencies minimal. Avoid polling when events or one request suffice. Index real query predicates, measure startup/login on legacy hardware, and dispose streams, database connections, WebView2 environments, and OWIN handles. Do not claim Windows 7 compatibility without testing the legacy WebView2/Chromium 109 runtime; modern Evergreen is for supported Windows versions.

The shell detects an available WebView2 runtime before creating its application-specific user-data directory. If no runtime or server is available it shows a visible connection/runtime error and does not silently navigate elsewhere. Windows 7 packaging and legacy Chromium 109 runtime distribution remain deployment work, not an implicit guarantee.

## Mandatory AI-agent workflow

1. Read this file and the relevant docs.
2. Inspect the existing code, project references, git status, and tests before editing.
3. Plan the smallest safe implementation and preserve useful existing decisions.
4. Follow the dependency direction and existing conventions; avoid unnecessary packages.
5. Keep the change focused and do not build unrelated functionality.
6. Update or add tests for changed behavior.
7. Build and run the relevant tests; report anything that could not run.
8. Update architecture/design/database/API/testing docs when behavior or boundaries change.
9. Report changed files, validation commands, results, and explicit compatibility assumptions.

Prohibited: blind rewrites, unrequested technology migrations, duplicate services, business logic in UI/controllers/SQL, hardcoded credentials, silent error swallowing, claiming unexecuted tests passed, or unrelated features.

## Definition of done

A task is done only when the requested behavior works, relevant projects build, relevant tests pass or blockers are stated, boundaries remain intact, documentation is updated, and compatibility assumptions are explicit.

## Decision records

- WPF was replaced because a shared HTML surface served by the local API can run in both browser and desktop shell while keeping WinForms lightweight.
- HTML is shared so desktop and browser smoke tests exercise the same user interface and HTTP contracts.
- HTTP API is the business-operation boundary so UI technologies do not own domain behavior and LAN clients have one server path.
- SQLite was chosen for local deployment because it is embedded, durable, low-resource, and requires no service; it must stay on local disk.
- Cloud deployment will use a separate modern host to avoid forcing legacy desktop/runtime constraints into the cloud platform.
- Windows 7 requires Chromium 109-compatible WebView2 distribution and explicit runtime testing; this repository does not silently guarantee that runtime is present.

See [docs/architecture.md](docs/architecture.md), [docs/design.md](docs/design.md), [docs/api-contracts.md](docs/api-contracts.md), [docs/database.md](docs/database.md), and [docs/testing.md](docs/testing.md).
