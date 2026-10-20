# Architecture

## Solution boundaries

AlKhayal is a modular monolith with directional dependencies: `Core -> nothing`, `Application -> Core`, `Data -> Application + Core`, `Api -> Application + Data`, `Server.Host -> Api + Data`, and `UI -> Server.Host`. `Printing` is an isolated output abstraction. Business rules belong in Application; HTTP controllers and the WinForms shell remain adapters.

The implemented server boundary currently covers system health and authentication. The 92 business screens use deterministic in-memory services in the browser; they do not bypass the architecture with direct SQLite calls. A future persisted business operation must first receive an Application command/port, then a Data implementation and thin API endpoint before the frontend calls it.

## Runtime topology

Startup resolves a writable SQLite path, applies embedded migrations in lexical order, composes the authentication service, starts OWIN, and serves `frontend/` from the same origin. Standalone UI binds `127.0.0.1:5137` by default and navigates WebView2 to `/login.html`. A regular browser exercises the same frontend and HTTP contracts.

`ALKHAYAL_SERVER_URL`, `ALKHAYAL_DATABASE_PATH`, and `ALKHAYAL_FRONTEND_PATH` provide explicit deployment/test overrides. In LAN server mode SQLite remains on the server's local disk; LAN clients navigate to the configured server URL and never open a database through a network share. Cloud synchronization and an ASP.NET Core/PostgreSQL host are future work.

## Frontend composition

`frontend/data/screens.json` is the route and navigation source of truth. `app.js` authenticates, builds the shell, owns workspace tabs, and selects one renderer. Shared UI primitives live in `components/ui.js`; specialized renderers live in `modules/`; deterministic records live in `mocks/data.js`; and session-only CRUD/query behavior is exposed by `services/mock-services.js`. Dashboard and POS have focused renderers while related screen families share explicit schema-driven renderers.

The frontend has no build step, framework, CDN, remote font, or service worker. It uses relative same-origin requests, stores only the CSRF token and harmless UI preferences in web storage, and targets Chromium 109 syntax and APIs.

## Desktop compatibility

The WinForms shell owns host lifetime, WebView2 runtime detection, application-specific browser data, navigation allowlisting, connection errors, and disposal. It currently targets x86 because that is the runtime/native-loader combination verified on the development machine. Windows 7 requires a separately packaged Chromium 109-compatible WebView2 runtime and real-device testing; that deployment has not been qualified.
