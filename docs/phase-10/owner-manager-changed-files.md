# Owner manager and activation redesign — changed files

Only the following files are owned by this update. Other modifications in git status predate this request.

- `tools/AlKhayal.LicenseManager/.gitignore`
- `tools/AlKhayal.LicenseManager/package.json`
- `tools/AlKhayal.LicenseManager/README.md`
- `tools/AlKhayal.LicenseManager/protocol.cjs`
- `tools/AlKhayal.LicenseManager/registry.cjs`
- `tools/AlKhayal.LicenseManager/server.cjs`
- `tools/AlKhayal.LicenseManager/launch.cjs`
- `tools/AlKhayal.LicenseManager/secure-storage.ps1`
- `tools/AlKhayal.LicenseManager/install-desktop.ps1`
- `tools/AlKhayal.LicenseManager/AlKhayal License Manager.vbs`
- `tools/AlKhayal.LicenseManager/open-browser.vbs`
- `tools/AlKhayal.LicenseManager/error.vbs`
- `tools/AlKhayal.LicenseManager/public/index.html`
- `tools/AlKhayal.LicenseManager/public/style.css`
- `tools/AlKhayal.LicenseManager/public/app.js`
- `tools/AlKhayal.LicenseManager/tests/manager.test.cjs`
- `tools/AlKhayal.LicenseManager/tests/packaged.test.cjs`
- `tools/AlKhayal.LicenseManager/tests/cross-sign.cjs`
- `tools/AlKhayal.LicenseManager/tests/browser-fixture.cjs`
- `tools/AlKhayal.LicenseManager/tests/offline-guard.cjs`
- `src/AlKhayal.UI/ActivationForm.cs`
- `tests/AlKhayal.UnitTests/ActivationDialogTests.cs`
- `tests/AlKhayal.UnitTests/NodeLicenseCompatibilityTests.cs`
- `tests/AlKhayal.UnitTests/LicensingTests.cs` (remove old issuer-specific test/dependency; crypto coverage retained)
- `tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj` (remove obsolete issuer reference; add built-in serializer reference for test bridge)
- `AlKhayal.slnx` (remove obsolete issuer project)
- Removed individually after acceptance: `tools/AlKhayal.LicenseIssuer/Program.cs`, `tools/AlKhayal.LicenseIssuer/AlKhayal.LicenseIssuer.csproj`; source archived under ignored artifacts.
- `docs/phase-10/phase-10-1-licensing.md`
- `docs/phase-10/owner-manager-changed-files.md`
- `docs/architecture.md`, `docs/design.md`, `docs/testing.md` (targeted licensing paragraphs only)

Desktop deployment and owner database paths are documented in the licensing guide. Installer copies only its explicit allowlist, excluding tests and private keys. Downloaded runtime, screenshots, TRX evidence and retired-issuer archive live under ignored artifacts; actual owner data stays outside the repository.
