# AlKhayal release-readiness audit

Date: 2026-10-29
Scope: focused Windows 7, standalone browser-host, native printing, and release-packaging architecture audit. No implementation, migration, database write, commit, push, branch change, or broad test run was performed.

## Classification

- **VERIFIED** — established from repository source or existing build artifacts.
- **LIKELY** — supported by the design and known platform behavior, but not demonstrated on the target deployment.
- **UNVERIFIED** — requires a clean package, target machine, vendor documentation, or hardware test.
- **BLOCKED** — release claim cannot be made until the stated work is completed.

Windows 7 *source compatibility* means the code targets APIs/frameworks that can plausibly run there. It does not prove the actual application, WebView2 runtime, SQLite native binaries, printer driver, fonts, installer, or signing chain works on Windows 7 SP1.

## Executive conclusion

The code is structurally suitable for an x86 Windows 7 build and for browser access through the existing OWIN console host. It is **not commercially release-ready for a Windows 7 claim**. There is no installer/publish definition, no qualified Chromium 109-compatible WebView2 distribution, no clean-package dependency manifest or installation test, no physical printer result, and no Windows 7 SP1 test evidence. These are release blockers, not source-code compilation blockers.

For a loopback standalone release on currently supported Windows, the same packaging gaps remain. For LAN release, HTTP binding, firewall/TLS policy, rate limiting, and deployment security require a separate production gate.

## 1. Windows 7 compatibility blockers

| Status | Finding | Evidence / consequence |
|---|---|---|
| VERIFIED | Desktop/API/data/printing/host target .NET Framework 4.8; Core/Application target .NET Standard 2.0. | `src/*/*.csproj`. This is source-level alignment with the documented Windows 7 SP1 baseline, but the .NET 4.8 prerequisite must be installed and tested. |
| VERIFIED | Desktop is deliberately x86. | `src/AlKhayal.UI/AlKhayal.UI.csproj` sets `PlatformTarget=x86` and `Prefer32Bit=true`; `docs/architecture.md` says only the development x86 combination was verified. Release packaging must not silently produce an AnyCPU/x64 desktop. |
| BLOCKED | WebView2 on Windows 7 has no qualified runtime package or target-machine result. | `MainForm.InitializeAsync` requires an installed/detectable runtime. The repository documents the need for a separately packaged Chromium 109-compatible runtime, but contains no installer definition or fixed-runtime payload. Modern Evergreen must not be presented as Windows 7 support. |
| UNVERIFIED | `Microsoft.Web.WebView2` 1.0.2792.45 SDK with the chosen Chromium 109 fixed runtime on Windows 7. | The NuGet SDK and loader are present, but the exact SDK/runtime/OS combination was not executed. Confirm against vendor lifecycle documentation and on a clean Windows 7 SP1 VM. |
| UNVERIFIED | `System.Data.SQLite.Core` 1.0.119 native interop on Windows 7 SP1. | `project.assets.json` includes x86/x64 `SQLite.Interop.dll`; existing outputs contain both. The x86 process should load x86, but actual loader/VC runtime behavior on a clean target is untested. |
| LIKELY | OWIN/Web API packages are source-compatible with net48. | Host uses Web API 5.2.9 and OWIN 4.2.2 on `HttpListener`. Runtime binding/URL ACL/firewall behavior must be tested under a standard user. Loopback normally avoids LAN reservation concerns; LAN binding may not. |
| UNVERIFIED | Arabic fonts, RTL rendering, DPI, TLS/signing validation, and legacy hardware performance. | Manifest requests Per-Monitor DPI awareness and the UI uses WebView2. None proves Windows 7 display quality or acceptable startup/operation on release hardware. |

No inspected source uses a new Windows-only P/Invoke. The main OS-sensitive boundaries are WebView2, `HttpListener`, WinForms/System.Drawing printing, printer drivers, and SQLite native interop.

## 2. Standalone Browser Mode feasibility

**VERIFIED — feasible and substantially present at the host layer, but not packaged as a polished browser product.**

- `src/AlKhayal.Server.Host/AlKhayalServer.cs` starts OWIN with `WebApp.Start`, maps Web API before static files, serves a physical frontend directory with default files, adds JSON content type handling, and applies no-store headers.
- The same file contains a console `Program.Main` that starts the server without WinForms or WebView2 and remains alive until Ctrl+C. This is a genuine non-desktop launcher.
- `frontend/js/api-client.js` uses relative API URLs, `fetch`, `credentials: "same-origin"`, session storage for the CSRF token, and cookies. No desktop bridge is required for ordinary API access.
- No frontend dependency on `window.chrome.webview`, host objects, or a WinForms JavaScript bridge was found. Printing is submitted to HTTP APIs; the server constructs the native printer backend.

Gaps:

- **VERIFIED:** `HostOptions.FrontendPath` defaults to a five-level development-relative path. An installed console host must set `ALKHAYAL_FRONTEND_PATH` or change the default to a packaged, executable-relative location.
- **VERIFIED:** the host does not open a browser, manage a tray/service lifecycle, or provide installer-managed configuration. Users can navigate to the configured URL manually; a commercial Browser Mode UX is not complete.
- **LIKELY:** loopback same-origin authentication should work in a normal browser. It still needs clean-browser acceptance covering bootstrap/login, cookie persistence, CSRF mutations, refresh/direct routes, shutdown, and cache behavior.
- **BLOCKED for LAN commercial release:** binding beyond loopback needs explicit URL ACL/firewall/TLS configuration and the documented CSRF/rate-limit/security review. SQLite must remain on the server's local disk.
- **UNVERIFIED:** static fallback behavior for arbitrary deep URLs. The application appears hash/route driven, but direct-route acceptance must be included in browser testing.

## 3. Native printing compatibility concerns

| Status | Finding |
|---|---|
| VERIFIED | Printing uses .NET `System.Drawing.Printing.PrintDocument`, `PrinterSettings`, `PaperSize`, `Margins`, and GDI+ drawing. These are conventional Win32-era APIs rather than a modern Windows-only print stack. |
| VERIFIED | `WindowsPrinterBackend` validates printer names and sends native print jobs; `NativePrintRenderer`/`NativeLabelRenderer` rasterize receipts and labels. ZXing.Net 0.16.11 supplies managed barcode encoding and its Apache notice is configured for output. |
| LIKELY | The API choices are source-compatible with Windows 7 and common vendor drivers. Exact printer capabilities, custom paper sizes, margins, Arabic glyph shaping, barcode density, spooler behavior, and driver bitness vary by device/driver. |
| BLOCKED | Physical receipt and label printing is not proven. No claim of working physical output, cutter behavior, scanner readability, paper alignment, retry behavior, or disconnect recovery is permitted until hardware tests pass. |
| UNVERIFIED | Whether all target printer drivers install and operate on Windows 7 SP1, especially current USB/thermal models. Driver availability is a procurement/deployment constraint, not something the managed code proves. |

Preview/raster tests are useful evidence for layout and barcode generation only; they do not replace printer and scanner acceptance.

## 4. Installer and runtime prerequisites

No `.wixproj`, Inno Setup, NSIS, MSIX, publish profile, or root installer directory was found. This is a **BLOCKED** commercial-release item.

The release definition must explicitly handle:

1. Windows 7 SP1 baseline and required servicing prerequisites, then .NET Framework 4.8 detection/install guidance.
2. x86 application identity and a tested Chromium 109-compatible WebView2 fixed-runtime strategy for Windows 7; modern supported Windows may use a separately qualified Evergreen strategy.
3. `WebView2Loader.dll`, managed WebView2 assemblies, `System.Data.SQLite.dll`, **x86** `SQLite.Interop.dll`, OWIN/Web API/Newtonsoft/Dapper dependencies, `zxing.dll`, frontend assets, embedded migrations, and third-party notices.
4. Writable data outside Program Files. `Db.DefaultDatabasePath()` correctly uses `%LOCALAPPDATA%\AlKhayal\alkhayal.db`; WebView2 profile and startup logs also use LocalApplicationData. Installer upgrades must preserve these directories.
5. An explicit frontend path. The development-relative default in `HostOptions` is not install-safe.
6. Standard-user install/run tests, clean install, upgrade, repair, uninstall-with-data-preservation, rollback, long Arabic Windows username/path, low disk space, antivirus, and locked-file behavior.
7. Executable/installer signing, timestamping, certificate-chain behavior on offline Windows 7, publisher identity, version metadata, and reproducible release hashes.
8. Separate packaging decisions for desktop standalone, console/browser standalone, and LAN server/client roles. Do not install SQLite on a network share.

Existing `bin/Release` contents are not accepted as a release package: their provenance is unknown and the inspected listing is not a clean, installer-driven manifest.

## 5. Code protection and obfuscation

**LIKELY compatible only with conservative rules and a post-obfuscation acceptance suite.** Strong renaming can break behavior even when compilation succeeds.

Preserve or explicitly configure:

- Web API controllers, action methods, route attributes, request/response DTO public members, and JSON property names (`ApiBootstrap` uses reflection-based Web API and Newtonsoft camel-case contracts).
- Types and members mapped by Dapper from SQL column names.
- the OWIN startup assembly attribute and startup type.
- embedded migration resource names and order (`Migrator` discovers `GetManifestResourceNames()`).
- WinForms entry points, project references, native DLL names/paths, and WebView2 loader assets.
- exception diagnostics useful for support; keep a private symbol/map archive per signed release.

Do not obfuscate native third-party binaries. Sign after protection. Validate that protection does not alter deterministic financial behavior, serialization contracts, migrations, stack traces needed by support, barcode output, or printer rendering. Licensing/DRM is a separate architecture decision and should not be mixed into this release-hardening batch.

## 6. Critical release blockers

1. **BLOCKED:** no Windows 7 SP1 physical/VM qualification with the exact x86 package, .NET 4.8, SQLite native library, and Chromium 109-compatible WebView2 runtime.
2. **BLOCKED:** no installer/publish pipeline or clean-machine dependency manifest.
3. **BLOCKED:** no physical receipt/label printer and scanner acceptance; printer-driver availability is unknown.
4. **BLOCKED:** installed frontend path and runtime layout are not defined; the current fallback path is development-relative.
5. **BLOCKED for LAN scope:** production binding/firewall/TLS/rate-limit/security deployment is not qualified.
6. **UNVERIFIED:** code signing/timestamping and any obfuscation profile have not been designed or tested.

## 7. Ordered implementation plan

1. Define supported release profiles and minimum OS matrix: Windows 7 desktop standalone, modern-Windows desktop standalone, browser standalone, LAN server, and LAN client. State which are in the first commercial release.
2. Build an x86 release packaging project/script that stages a deterministic file manifest, frontend assets, notices, and native dependencies without touching user data.
3. Replace/configure the development-relative frontend lookup with an install-safe executable-relative/configured path; add startup diagnostics for missing/mismatched assets.
4. Select and document the Windows 7 WebView2 Chromium 109 fixed-runtime distribution and license/update policy; keep it isolated from the modern-Windows strategy.
5. Add package smoke tests on clean VMs: standard user, fresh database, bootstrap/login, restart, API/static assets, migration, upgrade, and uninstall data preservation.
6. Run the Windows 7 SP1 matrix on x86 hardware/VM with legacy performance measurements and Arabic/DPI checks.
7. Run the physical printer/scanner matrix and record model, driver, connection, receipt width, label stock, barcode scan, cutter, error/retry, and spooler results.
8. Add signing/timestamping. Only then pilot a conservative obfuscation profile and rerun the full packaged acceptance suite.
9. Complete LAN security/deployment work only if LAN mode is in the initial commercial scope.

## 8. Exact files likely to require changes

Existing files:

- `src/AlKhayal.Server.Host/HostOptions.cs` — installed frontend/config path and release-profile configuration.
- `src/AlKhayal.Server.Host/AlKhayalServer.cs` — packaged startup diagnostics and possibly explicit browser/server lifecycle behavior; business logic must remain outside the host.
- `src/AlKhayal.Server.Host/AlKhayal.Server.Host.csproj` — deterministic frontend/content staging and release metadata.
- `src/AlKhayal.UI/AlKhayal.UI.csproj` — deterministic x86/runtime content and version/signing metadata.
- `src/AlKhayal.UI/MainForm.cs` and `WebView2UserDataFolderResolver.cs` — only if the selected fixed-runtime path or clearer runtime diagnostics require it.
- `src/AlKhayal.Data/AlKhayal.Data.csproj` — only if native SQLite asset selection must be constrained to the release architecture.
- `src/AlKhayal.Printing/AlKhayal.Printing.csproj` — release notice/content staging and any package-layout fixes.
- `docs/architecture.md`, `docs/testing.md`, `docs/database.md`, `README.md`, and `docs/third-party/README.md` — supported matrix, prerequisites, packaging, backup/upgrade, and verified limitations.

New files/directories are likely required for an installer/package definition, signing configuration template (without secrets), release manifest, and targeted package/VM smoke tests. The installer technology should be selected in an explicit small decision record rather than embedded ad hoc in an application project.

## 9. Manual verification checklist

- [ ] Clean Windows 7 SP1 x86/x64 machine/VM with required servicing prerequisites.
- [ ] Install/detect .NET Framework 4.8; run as a standard non-administrator user.
- [ ] Install/use the exact approved Chromium 109-compatible WebView2 runtime; verify its version and offline startup.
- [ ] Confirm x86 process, x86 `WebView2Loader.dll`, and x86 `SQLite.Interop.dll` load; verify no accidental AnyCPU/x64 dependency.
- [ ] Fresh install creates database, browser profile, and logs only under approved LocalApplicationData paths.
- [ ] Bootstrap administrator, login/logout, restart, cookie/session/CSRF mutations, direct navigation, and cache refresh.
- [ ] Console Browser Mode serves `login.html`, frontend assets, `screens.json`, and APIs without starting WinForms; verify configured frontend path from installed location.
- [ ] Upgrade over real data, migration, backup/restore, repair, uninstall while preserving data, and reinstall.
- [ ] Arabic text, RTL, keyboard/focus, 96/120/144 DPI, minimum window size, and low-resource startup/login timing.
- [ ] Receipt printers: 58/80 mm as supported, Arabic, logo, long lines, copies, margins, cutter, offline/paper-out/spooler/retry.
- [ ] Label printers: supported stocks, start cells, multi-page jobs, Code128/EAN13 scan using an independent physical scanner.
- [ ] Browser Mode print job submission reaches the server-side printer and reports failures without corrupting business records.
- [ ] Signed installer/executables validate on online and offline targets; timestamp remains valid after certificate expiry.
- [ ] If obfuscated, rerun API contracts, Dapper reads/writes, all migrations, printing, error logging, and upgrade tests against the protected package.
- [ ] If LAN is shipped, test URL ACL/firewall/TLS, server identity, concurrent terminals, authentication/CSRF/rate limiting, and local-only SQLite storage.

## 10. Findings requiring further research

- Exact vendor-supported WebView2 SDK version paired with the last Windows 7 Chromium 109 fixed runtime, redistribution rights, download provenance, and security/update policy.
- .NET Framework 4.8 and signing/timestamp chain prerequisites for the exact Windows 7 SP1 patch baseline, including offline roots/TLS behavior.
- `System.Data.SQLite.Core` 1.0.119 Windows 7 and native runtime prerequisites from authoritative package/vendor documentation.
- OWIN `HttpListener` URL ACL behavior for each proposed LAN binding and installer elevation model.
- Supported thermal/label printer models and Windows 7 driver availability in the target market.
- Commercial obfuscator support for net48 SDK-style projects, Web API 2, Newtonsoft, Dapper, embedded resources, WebView2, and mixed managed/native output.
- Code-signing certificate storage, CI signing boundary, timestamp service, revocation/offline policy, and installer reputation strategy.

## Audit method and limitations

Three permitted OpenCode calls were attempted with the verified economical model `opencode-go/mimo-v2.6-flash`: the Windows dependency call failed before receiving a prompt; the Browser Mode and packaging calls reached file inspection but did not return findings within their bounded execution windows. They were not retried. Therefore no delegated assertion was accepted as evidence; important findings above were independently checked against source, project assets, and existing output listings.

No build or tests were run because this was a read-only architecture audit and the repository already contained unrelated modified/untracked work. Existing work was preserved. Windows 7, clean installer behavior, and physical printers remain untested.
