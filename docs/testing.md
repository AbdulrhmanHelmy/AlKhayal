# Testing and verification

Run from the repository root:

```powershell
dotnet build AlKhayal.slnx --no-restore
dotnet test AlKhayal.slnx --no-restore
node frontend/tests/run-tests.js
Get-ChildItem frontend/js -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName }
```

The .NET unit suite covers authentication result handling and password/session behavior. Integration tests use isolated temporary SQLite files, apply real migrations, exercise the OWIN endpoints, verify cookie/CSRF behavior, and clean up only their own files. The frontend test asserts unique screen routes, complete navigation references, known renderers, required metadata, invoice totals, installment schedules, filtering, and sorting.

The maintained screen matrix is `docs/ui-screen-coverage.md`. “Registry verified” means metadata/renderer/navigation consistency was checked automatically; it does not claim a manual browser test. “Browser smoke” records a route actually exercised in the browser.

For a manual smoke run, provision an isolated development user, start the host, then test login, session restoration, dashboard, POS, sales, purchase editor, customer/supplier lists and search, low stock, financial movements, reports, maintenance tracking, settings, theme switching, compact sidebar navigation, and logout. Confirm that business actions identify themselves as session-only and that logout returns to `login.html`.

The WinForms project uses the same URL and assets, but desktop lifecycle checks require an installed matching WebView2 Runtime. Verify missing-runtime messaging, unavailable-server messaging, application-origin navigation allowlisting, and clean host/browser disposal separately. The current x86 target matches the development runtime. Windows 7/Chromium 109 packaging, actual Windows 7 execution, printer drivers, barcode hardware, backups, and LAN firewall/TLS behavior remain unverified deployment work.

The frontend test also performs static shell-contract checks for the single workspace scroller, intrinsic grid sizing, absence of the old POS viewport calculation, and the six responsive target sizes. These checks do not replace real browser layout inspection. A visual/DPI pass must be recorded only after an authenticated shell is opened at 100%, 125%, 150%, and 175% Windows scaling; this environment was able to inspect the real WinForms/WebView2 login window but did not automate credentials.
