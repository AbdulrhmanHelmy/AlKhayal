# Phase 10.1 — offline activation and owner License Manager

Storage/UAC update: normal-user activation now invokes a narrowly scoped elevated install mode and revalidates persisted storage. Already activated machines keep their license without UAC. The manual whole-POS elevation workaround below is historical; follow [activation storage hotfix](activation-storage-hotfix.md) for current behavior, ACL policy and unverified manual acceptance.

## Current delivery

The customer retains mandatory free perpetual offline activation using the original RSA-3072 / SHA-256 / PKCS#1 v1.5 protocol, hardware binding, embedded trusted public keys, ProgramData persistence and API gating. This update replaces only the owner C# issuer with a standalone HTML/CSS/vanilla JavaScript + Node.js + SQLite manager and simplifies the native customer dialog. No production signing key was generated, imported or overwritten. At completion of the manager redesign the owner registry was empty and its key was uninitialized. The owner subsequently initialized the production key; its public export rsa-17b43bcb5933edf6b1e9463d is now embedded in POS. See [production public-key integration](production-public-key-integration.md) for actual verification and required restart/activation steps. Historical issuance tests below used disposable directories, fake hardware and test keys.

## Installed Desktop tool

Resolved Windows Known Folder Desktop (including redirection): `C:\Users\user\Desktop`.

- Tool: `C:\Users\user\Desktop\AlKhayal License Manager\`
- Shortcut: `C:\Users\user\Desktop\AlKhayal License Manager.lnk`
- Internal launcher: `AlKhayal License Manager.vbs`
- Runtime: `runtime\node.exe`, Node **v24.22.2 LTS**, x64.
- Owner database: `%LOCALAPPDATA%\AlKhayalLicenseManager\registry.sqlite`, currently `C:\Users\user\AppData\Local\AlKhayalLicenseManager\registry.sqlite`.
- Encrypted private key: the same owner data directory, `signing-key.pem`; public export metadata is `public.xml`.

The shortcut starts bundled Node invisibly and opens the dashboard in the default browser. It requires no npm, terminal, internet, server account or service installation. Only `127.0.0.1` is bound, on an OS-selected port. Reopening the shortcut reuses an authenticated running instance rather than starting another backend. Closing a browser tab does not stop the backend; one owner Node process remains for subsequent opens. Tests restart only their own disposable manager child processes, never the running POS.

`tools/AlKhayal.LicenseManager/install-desktop.ps1` deploys an explicit source allowlist, creates the shortcut via WScript.Shell and resolves DesktopDirectory through Windows. It copies no data, private keys, tests or customer POS files. Re-running it preserves the owner registry/key and skips copying an identical running runtime. A future different-runtime update must be performed after the owner manager process exits; it must never delete the data directory. No customer installer was implemented. The owner manager/runtime must be excluded from all POS customer/update packages.

The official Node archive was downloaded from the vendor and checked against official SHA-256 sums: `node-v24.22.2-win-x64.zip`, SHA-256 `ca1bff5d8ead13c9f2ea404a176db46bd430704d5b5113734049fa365e7799e2`. Runtime sources and checksums remain in ignored `artifacts/license-manager-runtime/`. The [Node 24 SQLite documentation](https://nodejs.org/download/release/v24.22.2/docs/api/sqlite.html) marks `node:sqlite` as release candidate, not fully stable. Its actual DatabaseSync/parameterized-statement APIs were probed and exercised with this bundled runtime; no external SQLite/native npm dependency is needed. Owner Node 24 targets supported modern Windows, independently of the customer's .NET/Windows 7 target.

## Owner frontend and backend

Sources live in `tools/AlKhayal.LicenseManager/`. `public/` contains only HTML5, CSS3 and vanilla JavaScript. There is no framework, Electron, CDN, external font, analytics or network service. It is a separate owner utility, not a business route in `frontend/data/screens.json`.

- Dashboard: customer/device/license totals and signing key status.
- Customers: add a customer with optional phone/notes; search by name, phone, device label or normalized fingerprint. Registered devices and license counts are shown; the history action opens that customer's saved licenses.
- Issue License: select an existing customer or enter a name/optional phone/notes, paste the original request, optionally label the device, validate, and issue a free perpetual license with the key password.
- History: customer, fingerprint reference, LicenseId, local display date, full signed text, re-copy and `.alklic` export. Search matches customer name, fingerprint, LicenseId or exact customer ID. Lists are bounded to 500 matching rows and the history states that limit.
- Settings: key status, public XML export, encrypted local key backup, registry JSON backup/merge restore, explicit first-time owner key initialization and encrypted PEM restoration.

The frontend uses Arabic RTL and navy/gold. Cryptographic text uses LTR monospace without changing its bytes. User content is rendered via textContent/DOM properties, not HTML concatenation. Password inputs are cleared after each attempt; passwords/private keys are never put in browser storage. Browser copy exports exactly the persisted full license, never a newly generated replacement.

## Local HTTP security

Every `/api/*` request, including reads, requires a random 256-bit per-process bearer session. The launcher gives the default browser a URL fragment; the frontend moves the token to sessionStorage and immediately removes the fragment from the address bar. It is never stored in localStorage or sent in query strings. The owner-only ACL-protected `session.json` lets a second launcher reuse that running session. Restarting the backend creates a new token and port.

Host must exactly match `127.0.0.1:<port>`. Any supplied Origin must exactly match; POST additionally requires that Origin. Cross-site Sec-Fetch-Site is rejected. No CORS wildcard or LAN binding exists. Mutations require authenticated JSON; inputs and backup bodies are bounded. Static serving is an exact three-file allowlist, not directory serving. Key/database/session/backend sources cannot be downloaded through HTTP. CSP prohibits remote resources, inline scripts, framing, base URL changes and objects; responses are no-store and nosniff. Errors do not include request bodies, passwords, private bytes or raw SQL errors.

Owner API routes: GET `status`, `customers?search=`, `devices?search=`, `history?search=`, `public-key`, `backup`; POST `customers`, `validate`, `issue`, `restore`, `key/initialize`, `key/import`, `key/backup`. All are under `/api/` and require the session. No private-key export endpoint exists. Key backup copies encrypted PEM locally and returns only its path; even encrypted key bytes never traverse HTTP. Direct unauthenticated signing fails with 401, hostile origins/hosts with 403.

Loopback authentication does not defeat malicious software already running as the owner, administrator-level tampering or browser extensions with access to the owner page. Protect the owner Windows account and offline backups. Offline signatures provide authentic issuance and practical binding, not tamper-proof DRM or protection against cloned/spoofed SMBIOS.

## SQLite registry, append-only issuance and backup

The owner database is wholly separate from `%LOCALAPPDATA%\AlKhayal\alkhayal.db`. No POS migration, operational-data mutation or database-path move is introduced.

- Customers: CustomerId, CustomerName, Phone, Notes, CreatedUtc.
- Devices: DeviceId, CustomerId FK, DeviceRequestCode, FingerprintReference (unique), FirstSeenUtc, DeviceLabel.
- Licenses: LicenseId, CustomerId FK, DeviceId FK, SignedLicenseText, IssuedUtc, SigningKeyId, LicenseType constrained to Perpetual.

All dates are UTC, displayed locally by the frontend. Foreign keys, WAL, FULL synchronous writes, lookup indexes and short explicit transactions are used. Values use prepared parameters. Update/delete triggers protect license history. Device fingerprint reference is SHA-256 of the original canonical claims string. Reissuing an already registered device returns its saved license and `reused=true`, without creating new customer/device/license rows. Conflicting customer ownership is rejected. Signing/key failure leaves no partial customer/device records.

Registry backup is a versioned JSON snapshot containing customers/devices/licenses only. Restore merges transactionally, preserves identical records, rejects conflicts and rolls everything back on malformed input, FK conflicts, invalid requests or invalid signatures. Restore the matching encrypted owner key first when restoring licenses: every license is verified against the locally restored public key. This manager currently manages one signing key; keep historical trusted public keys in POS builds for any future key rotation. Registry snapshots do not authorize arbitrary public keys in customer software. Backups are limited to 4 MiB per HTTP request and 10,000 records per table; larger archives need an explicit future migration workflow.

## Private key initialization and restoration

The application does **not** initialize a key automatically during build, installation, startup or tests against the real owner data directory. First-time initialization is an explicit owner-only Settings form with password confirmation and a required approval checkbox. The owner must choose this action; no production key was created during this task.

1. Double-click the Desktop shortcut.
2. Open `المفتاح والنسخ الاحتياطي` → `تهيئة مفتاح المالك لأول مرة`.
3. If no earlier owner key exists, choose a unique strong password of at least 16 characters, confirm it and explicitly approve key creation. Do not create a replacement for a lost existing key.
4. Native Node crypto generates RSA-3072 in memory and writes only AES-256-CBC encrypted PKCS#8 PEM. No plaintext private key is written. The launcher protects the owner data directory and its files with a current-owner-only Windows ACL; keys also use exclusive CreateNew semantics. Private-key storage inside a Git repository is rejected.
5. Export the **public** XML. The downloaded filename is the actual SigningKeyId, `rsa-<24 lowercase hex>.xml`.
6. Copy only this public file to `src/AlKhayal.Data/Licensing/TrustedKeys/<SigningKeyId>.xml`, then build the customer application. The existing build guard accepts only RSA-3072 public Modulus/Exponent and rejects private parameters/DTD content. Customer code never trusts a public key supplied in a license file.

For an existing encrypted PKCS#8 key, enter its owner-side local `.pem` path and password in the restoration form; bytes stay backend-side and it is re-encrypted before protected storage. Existing key files are never silently overwritten. This JavaScript manager does not directly import PFX; an owner with a prior PFX must convert it offline to encrypted PKCS#8 while preserving the same RSA key (and export matching public XML), not generate a replacement. No real prior production PFX was created in the preceding implementation.

SigningKeyId is `rsa-` plus the first 24 hex characters of SHA-256 of canonical .NET-compatible RSA public XML. The Node implementation exports exactly `<RSAKeyValue><Modulus>…</Modulus><Exponent>…</Exponent></RSAKeyValue>`, matching .NET ToXmlString(false). It uses Node native crypto.sign with SHA-256 and RSA_PKCS1_PADDING, not PSS. No key material, universal password or signing secret is shipped in the POS.

## Owner backup and restore walkthrough

1. In Settings, click `حفظ نسخة من السجل`; store the JSON on controlled offline media.
2. Click `إنشاء نسخة محلية مشفرة من المفتاح`; the UI displays the protected local `backups\signing-key-<timestamp>.pem` path. The matching `.public.xml` is saved alongside it. Copy these files through Explorer to two protected offline media. Store the key password separately in an owner password manager. The PEM remains password encrypted; the browser never receives it.
3. On a new owner machine, install the manager and open Settings. Restore the existing encrypted PEM from its local path with the original password before restoring signed registry records.
4. Select the saved registry JSON using `استعادة سجل محفوظ`. Identical rows merge safely; conflicting historical records are rejected without overwriting anything.
5. Confirm counts and re-copy an existing license. Verify a disposable restored-key issuance with the archived public XML before relying on a backup.

Losing the private key does not invalidate existing perpetual licenses whose public key remains embedded, but prevents further issuance using that key. Keep backups and previous public keys. Offline clients have no online revocation channel if a key is compromised. Do not store backups beside customer installers, publish them, or commit them to Git.

## Original canonical protocol and device policy (unchanged)

Maximum request/license size: 8192 characters/bytes of canonical ASCII UTF-8, no BOM or internal whitespace. UI import/paste trims surrounding whitespace only. No property-order-dependent JSON is signed.

```text
ALKREQ|1|AlKhayalPOS|1|uuid:<hash>;board:<hash>;bios:<hash>|<SHA256 of preceding bytes>
ALKLIC|1|AlKhayalPOS|<GUID D>|Perpetual|1|<claims>|<yyyy-MM-ddTHH:mm:ssZ>|<SigningKeyId>|RSA3072-SHA256-PKCS1|<384-byte signature in base64>
```

Claim order is uuid, board, bios; missing claims are omitted. At least two good components are required. The request checksum also serves as a non-sensitive request ID; it detects copying errors, not malicious requests. The license signature covers exactly the payload from ALKLIC through the fixed algorithm, excluding the last separator/signature. No ExpiresUtc, feature tiers, trial or app assembly version exists. IssuedUtc is audit-only. The parser rejects duplicates, extra/unknown fields, unsupported versions/algorithms, wrong products/keys, malformed IDs/dates/base64, oversize and modified signatures/payloads.

The .NET hardware provider reads WMI system UUID, motherboard serial and BIOS serial once per service startup, normalizes invariantly and rejects generic placeholders. Each component is hashed with `AlKhayalPOS|binding-1|<component>|<normalized value>`; requests do not expose raw serials. At least two signed components must match. Three available components tolerate one change; when only two exist both must remain. Another device, substantial changes or insufficient reliable WMI identity require support/free replacement; no shared disk/MAC/IP/hostname/user fallback exists. Cloned/spoofed hardware identities remain an offline limitation.

Cross-language tests generate the request in .NET, issue and persist through Node/SQLite, export public XML, verify/activate with the existing .NET code, reopen the service from disk, accept matching fake hardware, reject other fake hardware and reject modified fields/signatures. They use disposable keys, never real machine/customer identifiers.

## Simplified customer WinForms activation

Window title: `تفعيل برنامج الخيال`. Description: `التفعيل مجاني ودائم لهذا الجهاز.` Base client size is 620×365, with DPI-aware native controls and a compact light navy/gold appearance. It has exactly **three action/edit controls** in its tab sequence:

1. `نسخ كود الجهاز` — copies the complete original request.
2. Multiline `كود التفعيل` input — normal Ctrl+V/paste, full 8192-character capacity and LTR codes.
3. `تفعيل البرنامج` — validates/persists and returns to existing startup/login.

The compact readonly request display is selectable but excluded from the action/tab sequence (TabStop=false, static-text accessible role). There are no save/import/paste/exit/trial/demo/skip buttons. Only the title-bar X closes startup; minimize/maximize are removed. Invalid activation keeps the window and pasted text intact and shows a compact Arabic error. Clipboard implementation uses standard .NET WinForms APIs available on the Windows 7 target; no browser activation page was added.

## Customer access, persistence and updates (unchanged)

Native activation precedes MainForm, WebView2, local server and database startup. The service rereads and verifies persisted storage before the shell is enabled. Both the host OWIN middleware and the direct Web API handler fail closed for all routes while unlicensed, including login/bootstrap/health/static files/unknown routes and previously authenticated sessions. No licensing HTTP signing endpoint or production bypass exists. Normal cookies/roles/screen grants/CSRF remain enforced after activation. Explicit test gates remain confined to test composition.

Customer license stays at `%PROGRAMDATA%\AlKhayal\Licensing\license.alklic`. An elevated administrator runs `AlKhayal.UI.exe --initialize-license-storage` once and performs first activation. Administrators/SYSTEM write; cashiers read. Atomic sibling-file durable flush + replace/move is unchanged. Ordinary cashiers can copy their request without elevation; installing/replacing the machine license needs administrator rights. No SQLite storage/path/ACL migration was made.

Every API check rereads bounded file content; only verification of identical full content is cached. Deletion/corruption/denied reads lock even an existing session. WMI is not repeatedly queried per request. The existing desktop file watcher/focus check returns to activation after storage changes; watcher failure closes the shell visibly. These security paths were preserved.

Customer walkthrough: copy the request → send to owner offline → owner selects customer, validates and issues → owner copies/exports the signed license → administrator pastes it into the single input and activates → existing login opens. A changed app version never requires a new license. Compatible patch/minor/major updates, repair and reinstall must retain the ProgramData license/ACL and old trusted public keys and must exclude owner tools/keys/data. The installer is outside scope.

## Old issuer cleanup and compatibility limits

After successful Node tests, .NET cross-acceptance and Desktop shortcut launch, the obsolete `tools/AlKhayal.LicenseIssuer/Program.cs` and `.csproj` were removed individually, along with their solution/test references and specifically named ignored issuer executables/config/PDB outputs. Source is preserved in ignored `artifacts/license-manager-redesign/audit/retired-csharp-issuer-source.zip`; the former phase document is archived beside it. No broad recursive repository deletion was used. Shared contracts, verification, public keys, WMI, ProgramData storage, owner registry and unrelated user edits were preserved.

Customer RSA verification still uses .NET Framework 4.8 RSACryptoServiceProvider with explicit PROV_RSA_AES (24), ephemeral containers and SHA-256. Native tests exercise copy/paste, invalid/valid activation, title-bar close and scaled layout geometry at 100/125/150% within 1366×768. These are actual Windows native-control tests and logical scale simulations, not execution on physical Windows 7/150% display hardware. Actual Windows 7 SP1/Chromium 109 and full WebView2 reactivation acceptance remain deployment checks. The new owner Node 24 tool does not claim Windows 7 support. No production owner key was generated or automatically trusted.

## Validation for this update

Final counts and exact commands follow below. All POS databases and keys in automated tests are disposable. The normal POS AppData database and the existing running POS application were not opened, modified, migrated or stopped. The actual owner registry was created empty by the Desktop launch; no production key was initialized.

### Final executed results — 2026-10-30

| Check | Result |
|---|---|
| Focused .NET licensing, native UI, scale geometry and Node/.NET acceptance | 40 passed; zero failed/skipped |
| Full integration suite (existing role/CSRF/business guards and licensing API checks) | 350 passed; zero failed/skipped; 3m39s |
| Final full unit suite after issuer removal and final native layout | 190 passed; zero failed/skipped; 11s |
| Node manager/protocol/registry/security plus installed-package tests | 8 passed; zero failed/skipped |
| Full POS frontend registry/behavior suite | 126 screens passed |
| JavaScript syntax, POS frontend and new owner sources/tests | 62 files passed |
| Full solution build, without the old issuer project | Success, zero warnings/errors |
| git diff --check | Passed; existing line-ending notices only |

The Desktop shortcut was started through the Windows shell, successfully opening the bundled backend/default-browser workflow with an empty real owner registry and no signing key. The packaged-launch test then uses a disposable owner data directory and preloads an outbound-denial guard, exercises authenticated issuance, launches a second instance (which reuses the first), stops only its own child process and restarts it. SQLite customers/devices/licenses and the encrypted test key survive, with the exact same signed license text. The guard rejects non-loopback HTTP/fetch destinations. The browser acceptance uses the installed package against separate fake keys/data: validate request → issue → copy complete text (exact equality) → export `.alklic` → issue again (existing license reused) → search customer/device → view saved history. Its only static resources are loopback app.js/style.css, and no horizontal overflow was observed. The disposable fixture/tab were closed; the real empty owner manager remains available through the Desktop shortcut.

Actual native activation images were rendered and inspected at `artifacts/license-manager-redesign/activation.png` and `activation-dpi-100/125/150.png`. Dashboard preview with **disposable test records/key only**: `artifacts/license-manager-redesign/owner-dashboard.png`. The temporary browser export was moved to `artifacts/license-manager-redesign/audit/browser-export.alklic`; no fake license was installed into ProgramData.

Initial failures were corrected before the final results: a seven-column INSERT placeholder count, a test that incorrectly relied on fetch preserving a custom Host header (replaced by an actual HTTP request), PowerShell module autoload inherited from a different PowerShell edition (ACL setup now uses Windows .NET filesystem APIs directly), and Windows quoting in the test-only offline preload path. These did not change license serialization or customer verification. No production key/registry or POS data was used for these fixes.

Commands from repository root (Node is the installed bundled runtime):

```powershell
$node = 'C:\Users\user\Desktop\AlKhayal License Manager\runtime\node.exe'
powershell -NoProfile -ExecutionPolicy Bypass -File tools/AlKhayal.LicenseManager/install-desktop.ps1
& $node --test tools/AlKhayal.LicenseManager/tests/*.test.cjs
$env:ALKHAYAL_LICENSE_MANAGER_NODE = $node
$env:ALKHAYAL_DATABASE_PATH = Join-Path ([IO.Path]::GetTempPath()) ('alk-manager-tests-' + [guid]::NewGuid().ToString('N') + '.db')
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/license-manager-redesign --filter 'FullyQualifiedName~LicensingTests|FullyQualifiedName~NodeLicenseCompatibilityTests|FullyQualifiedName~ActivationDialogTests' --logger 'trx;LogFileName=focused-final.trx' --results-directory artifacts/license-manager-redesign/results
dotnet build AlKhayal.slnx --artifacts-path artifacts/license-manager-redesign -v minimal
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/license-manager-redesign --no-build --logger 'trx;LogFileName=integration-full.trx' --results-directory artifacts/license-manager-redesign/results
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/license-manager-redesign --no-build --logger 'trx;LogFileName=unit-final.trx' --results-directory artifacts/license-manager-redesign/results
node frontend/tests/run-tests.js
rg --files frontend tools/AlKhayal.LicenseManager -g '*.js' -g '*.cjs' | ForEach-Object { & $node --check $_; if ($LASTEXITCODE) { throw 'Syntax failed' } }
git diff --check
```

Each actual POS suite invocation used its own unique temp fallback path. Evidence files are under `artifacts/license-manager-redesign/results/`, including `node-final.txt`, successful `unit-final.trx`, `integration-full.trx` and `focused-final.trx`.

### Production setup and confirmation

Implementation, packaged launch, crypto interoperability, registry persistence, UI and automated regressions are complete. Before production use, the owner must explicitly initialize/import their permanent encrypted key, retain encrypted backups, export/embed its public XML in a customer build, and complete deployment acceptance on the intended OS. These actions were deliberately not performed against real owner keys in this task. Hardware Windows 7/Chromium 109 and real OS DPI acceptance remain unverified deployment tests, as before. [Changed files for this update](owner-manager-changed-files.md).

No delegation/subagents/OpenCode, commit, push, merge, reset or branch switch. Existing unrelated changes were preserved. Approved issuer removal was limited to individually identified files with an archive; no registry, keys, public keys, shared verifier, live POS AppData database or running POS process was deleted/modified/stopped. Only disposable test processes/data were cleaned.
