# الخيال POS

AlKhayal is an Arabic-first, offline-first point-of-sale and ERP foundation for Windows. The repository contains a functional shared HTML interface for 96 registered screens, real SQLite-backed authentication, business activity profiles, feature configuration, company/general settings, an OWIN Web API 2 host, and a lightweight WinForms/WebView2 desktop shell.

## What is implemented

- Real local authentication with PBKDF2 password hashes, HttpOnly SameSite session cookies, CSRF-protected logout, roles, disabled-account handling, and persisted SQLite sessions.
- Real multi-activity business profiles with one primary activity, deterministic capability recommendations, administrator override preservation, and first-run/settings UI.
- Real SQLite-backed company information, currency/date/time/tax defaults, administrator audit, and device-local Light/Dark/System appearance.
- A responsive RTL shell with navy/gold Light, Dark, and System themes; top navigation; quick access; a right sidebar; workspace tabs; status bar; dialogs; toasts; tables; forms; charts; and keyboard focus states.
- The full requested navigation inventory across customers, sales, suppliers, purchases, returns, printing, expenses, treasury, alerts, inventory, employees, permissions, analytics, installments, maintenance, branches, cash services, and settings.
- Deterministic Egyptian Arabic demonstration data and session-only interactions for business screens.
- Registry and calculation tests plus .NET unit/integration tests.

Business screens are intentionally a realistic UI prototype: their records and workflow mutations live in memory for the current browser session. Checkout, inventory posting, financial posting, hardware printing, backup execution, and other production side effects are not persisted and are labelled as demonstrations in the UI.

## Build and test

```powershell
dotnet build AlKhayal.slnx --no-restore
dotnet test AlKhayal.slnx --no-restore
node frontend/tests/run-tests.js
```

## Run the local host

Provisioning is explicit. The host reads the password interactively without echoing it, then stores only a salted PBKDF2 hash in SQLite:

```powershell
dotnet run --project src/AlKhayal.Server.Host -- --provision-user demo
dotnet run --project src/AlKhayal.Server.Host -- --reset-password demo
dotnet run --project src/AlKhayal.Server.Host
```

Passwords may be any non-empty string; use a strong password for real deployments. `--reset-password [username]` replaces the hash for an existing account without deleting its data. The legacy `--provision-demo` spelling remains an interactive alias and no longer reads a password from an environment variable.

Then open `http://127.0.0.1:5137/login.html`. The desktop project serves and navigates to the same frontend. Configuration overrides are available through `ALKHAYAL_SERVER_URL`, `ALKHAYAL_DATABASE_PATH`, and `ALKHAYAL_FRONTEND_PATH`.

## Documentation

- [Architecture](docs/architecture.md)
- [Design system](docs/design.md)
- [API contracts](docs/api-contracts.md)
- [Database](docs/database.md)
- [Testing](docs/testing.md)
- [UI screen coverage](docs/ui-screen-coverage.md)
- [Implementation progress](docs/implementation-progress.md)

Windows 7 support remains a deployment qualification target, not a tested guarantee. It requires a separately distributed Chromium 109-compatible WebView2 runtime and testing on actual Windows 7 SP1 hardware.
