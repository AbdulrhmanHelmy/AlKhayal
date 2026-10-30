# Phase 10 — activation storage / UAC hotfix

## Status and proven cause

Implementation is complete; production acceptance is **PARTIAL** because complete credentialed UAC installation under a separate standard Windows account remains **UNVERIFIED**. No production activation was reset, overwritten or used as a test fixture. Its initial/final SHA-256 remained identical. The running POS was not stopped or relaunched.

Previously the dialog called LicenseService.Activate → FileLicenseStorage.Write in the normal caller token. The real Licensing DACL correctly grants Users read/execute and Administrators/SYSTEM full control. Privileged installation orchestration was missing.

Reproduction used a disposable directory with that policy and the actual current Windows **medium-integrity filtered administrator token** (Administrators deny-only). The failing operation was FileStream CreateNew/Write for the sibling GUID.tmp file: **UnauthorizedAccessException, HRESULT 0x80070005**, before flush/rename/replacement. No partial/temp license remained. This is actual Windows ACL enforcement, not a fake exception; it does not establish complete standard-account/UAC success. A separate actual sharing failure at atomic replacement preserved the previous file and cleaned only the operation's own temp file, demonstrating that storage failures have other causes too.

## New privilege flow

Normal startup remains **asInvoker**. Existing activation uses the original read-only validation and starts the shell without UAC or rewriting the license. A new activation follows:

1. Application LicenseActivationCoordinator validates schema/version/product, trusted KeyId, RSA and hardware locally. Invalid input never starts a helper. Existing valid activation skips the installer.
2. UI WindowsLicenseInstaller launches **Application.ExecutablePath only**, using ShellExecute/runas and a fixed working directory. Process creation/wait runs outside the UI thread. An already elevated caller runs just the same bounded installation operation directly.
3. The elevated **command mode of the same executable** exits before visual initialization, MainForm/WebView2, API host or database composition. It is not an elevated business session.
4. The helper requires an actual enabled administrator token, constructs fresh embedded-key verification and WMI identity, and independently validates the full candidate before provisioning.
5. MachineLicenseStorage prepares the fixed protected target and atomically installs. An existing valid entitlement is retained, including on repeated installation.
6. The normal parent rereads and validates storage. A success exit without valid persisted content cannot unlock the application.

No service, scheduled task, permanent elevation, signing endpoint or installer was introduced. No Windows credentials are collected by AlKhayal.

## Helper input and error contract

The only install command is `--install-license-v1 <canonical-base64-envelope>`, with exactly two arguments. The envelope contains only the signed license and has explicit bounded length, strict UTF-8/base64 canonicalization and the unchanged license schema. Base64 transports bytes; RSA remains the authority. No path, executable, output file, custom key, environment bypass, shell command, registry or SQL argument is accepted. No caller-controlled staging file/response file is used. Signed licenses are not secrets, but full command arguments/payloads must not be recorded in support logs.

Exit codes: 0 success; 10 cancelled; 11 denied/no enabled administrator token; 12 invalid command/license; 13 storage failure; 14 unsafe path; 15 helper unavailable. The client maps Win32 1223 separately for UAC cancellation; 5/740/1326/1314 indicate denial/elevation/credential failure. Unexpected codes fail closed.

The same three-control native dialog remains: copy request, license input, activate. Concise Arabic messages distinguish cancellation, denial, unsafe path and storage/helper failure. Pasted text remains available, activation is disabled only while pending, and retry is re-enabled afterward. Title-bar close still works. An already approved helper may finish its limited operation after the parent closes; it never starts business operations.

## Storage, ACL and redirection policy

Target stays `%PROGRAMDATA%\AlKhayal\Licensing\license.alklic`. Business SQLite stays at its original configured LocalApplicationData path. The helper has no SQLite composition.

Only the reserved application parent AlKhayal and its Licensing child may be created; existing system/common-data ancestors are inspected but never created or re-ACL'd. Local fixed-drive paths are required.

- Every ancestor is inspected through native OPEN_REPARSE_POINT/BACKUP_SEMANTICS handles. Junctions, symlinks, reparse objects and non-directory ancestors fail closed.
- Read handles without share-delete are retained to prevent rename. Application directories are hardened under separate no-share-write handles before child creation, then retained with read/write sharing so intended child writes can proceed.
- The app parent must contain only Licensing; that child must contain only the intended license. Unexpected contents are preserved and rejected for support review, never recursively cleaned. Orphan temp files after a machine crash consequently require explicit review.
- The Licensing child is preflighted/protected before parent ACL inheritance can propagate. Existing license targets must be ordinary bounded single-link files; directory, hard-link, reparse and oversized targets are rejected before their ACLs are changed. Inspection handles remain open during ACL changes.
- App directories and installed files are owned by Builtin Administrators, with protected DACLs: SYSTEM and Administrators full control, Users read/execute. No Everyone/Authenticated Users write grants are added. The reserved parent is secured because an untrusted writable/owned ancestor would undermine its protected child. Unexpected unrelated contents cause rejection before this parent change.
- Temporary files use exclusive CreateNew with an explicit protected admin-owned file descriptor, durable flush and atomic same-directory replace/move. Caught failures clean only that operation's temporary file. Persistence diagnostics record stage, exception type and HRESULT without payloads.

Actual production ACLs were inspected but not changed by tests. The real license bytes were never written by this task.

Microsoft documents [CreateFile directory/sharing/reparse flags](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) and [reparse point operations](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-points-and-file-operations). Tests caught an initial zero-access metadata handle that did not enforce rename sharing, and then an overly restrictive retained share mode that prevented child writes. The final design separates exclusive hardening from retained no-share-delete handles; actual native tests verify rename locking/release, junction rejection and hard-link rejection.

## Future Inno Setup contract

The installer itself remains outside scope. It must:

1. Install the UI/helper executable and **all dependencies** in a protected application directory writable only by administrators/SYSTEM. Keep the main manifest asInvoker. A user-writable development build is not a tamper-resistant UAC boundary; authentic distribution and protected executable/DLL locations are deployment requirements.
2. Reserve/provision ProgramData AlKhayal/Licensing with the owner/DACL policy above and preflight unexpected contents/reparse objects before ACL propagation. The existing elevated maintenance command `--initialize-license-storage` uses this guarded policy but displays a completion dialog; unattended setup can implement the documented ACL contract directly. Customers no longer need that manual command before activation.
3. Preserve installed license bytes/ACLs and old trusted public keys across compatible update/repair/reinstall. Preserve the business database/configuration/user data independently. Never deploy a replacement license over an installed valid one.
4. Leave first activation in the normal user session. UAC authorizes only the fixed helper operation. Alternate administrator credentials must not change the machine target. Credentials belong to Windows.
5. Exclude owner License Manager, production private keys, PEM/PFX backups, signing passwords, tests and test composition. Include only the intended trusted public XML and customer binaries.
6. Verify normal-user read access, first credentialed activation, cancellation/retry, policy denial and already activated startup from a protected install location.

## Compatibility and manual acceptance

.NET Framework 4.8, WinForms, ShellExecute runas, administrator-token checks, CreateFile/GetFileInformationByHandle, NTFS ACLs and File.Replace/Move are used. No modern-only UI framework, signing server or PowerShell runtime dependency was introduced. RSA-3072/SHA-256/PKCS#1 v1.5, the production public key and APIs' licensing guards are unchanged.

Actual Windows 7 SP1, credentialed UAC on that OS and Chromium 109 remain **UNVERIFIED**. Helper mode does not initialize WebView2. Local fixed-drive/NTFS semantics and usable administrator credentials are assumptions requiring deployment validation.

Actual Windows verification: medium-token CreateNew denial (0x80070005), replacement sharing failure/old-byte retention, directory rename lock/release, temporary junction/hard-link rejection, native three-control dialog events and retry, plus child-process CLI rejection for malformed payload (12), non-elevated authority (11) and extra path arguments (12).

Simulated verification: elevated authority is an explicit fake target, and UAC cancellation/denial/missing credentials/helper failures are injected result codes. These tests exercise clean/missing/initialized/corrupt temporary storage, independent signature/key/product/schema/hardware rejection, no partial writes, success-exit-without-storage rejection and repeated-install preservation. They are not labeled actual UAC tests.

**UNVERIFIED:** complete separate standard-account → real consent/admin credentials → elevated fixed-target ACL/write → normal parent validation/login; alternate-admin behavior; actual consent cancellation/policy denial; protected Program Files deployment; Windows 7. No production target override was added just to ease testing.

Perform those remaining checks in a clean Windows VM with a disposable POS database and a test build containing disposable trusted keys. Include missing/initialized directories, success/cancel/retry/denial/no credentials, normal restart without prompts, roles/API protection and unsafe/corrupt targets. Preserve the user's currently active real installation throughout.

## Rollout and preserved activation

The production license's initial/final content hash is identical. It was not used as a fixture. No production private key/password was accessed; owner License Manager and real POS business data were untouched. The running POS was left running.

New executable: `D:\projects\AlKhayal\artifacts\activation-storage-hotfix-final\bin\AlKhayal.UI\debug\AlKhayal.UI.exe`. No running executable was overwritten. Existing activated machines use it on their next planned normal restart without new licenses/reactivation or a normal-start UAC prompt. New unlicensed machines use the approved three-control dialog normally; only a locally valid candidate requests limited elevation.

## Changed files

- Application/Licensing/LicenseInstallation.cs: port/result/envelope, independent helper use case and caller orchestration.
- Data/Licensing/WindowsLicensePath.cs: fixed target, native inspection/locks and secure provisioning.
- Data/Licensing/FileLicenseStorage.cs: protected temp ACL overload, diagnostics and guarded maintenance provisioning.
- UI/WindowsLicenseInstaller.cs: fixed-executable UAC client/helper entry and result mapping.
- UI/Program.cs and UI/ActivationForm.cs: early helper mode, async activation/retry; manifest/layout unchanged.
- UnitTests/LicenseInstallationTests.cs, ActivationDialogTests.cs and test csproj: focused native/simulated tests and explicit test installer composition.
- This guide and targeted architecture/design/testing/Phase 10 documentation links.

No authentication/role logic, business modules/migrations, production trust material or owner tool code changed.

## Final validation

Final counts/commands follow below. Outputs are isolated under artifacts/activation-storage-hotfix; tests use disposable storage/keys and a unique temporary SQLite fallback. Production storage is never a test target.

| Executed check | Actual result |
|---|---|
| Focused installation/native activation checks | 37 passed, zero failed/skipped |
| Full unit suite, final build | 224 passed, zero failed/skipped, 11 seconds |
| Full integration suite, including licensing API and role/CSRF regressions | 350 passed, zero failed/skipped, 3m42s |
| POS frontend registry/behavior | 126 screens passed |
| JavaScript syntax (POS and owner utility) | 62 files passed |
| Actual final helper child-process rejection cases | 3 passed (12/11/12 exit codes), no elevation or production writes |
| Final full solution build | Success, zero warnings/errors |
| git diff --check | Passed; Git line-ending notices only |
| Existing production activation SHA-256 comparison | Identical |

Commands executed from repository root:

```powershell
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/activation-storage-hotfix --filter 'FullyQualifiedName~LicenseInstallationTests|FullyQualifiedName~ActivationDialogTests' --logger 'trx;LogFileName=focused-latest.trx' --results-directory artifacts/activation-storage-hotfix/results
dotnet build AlKhayal.slnx --artifacts-path artifacts/activation-storage-hotfix -v minimal
# A unique temp ALKHAYAL_DATABASE_PATH was set for each full suite invocation.
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --artifacts-path artifacts/activation-storage-hotfix --no-build --logger 'trx;LogFileName=integration-full.trx' --results-directory artifacts/activation-storage-hotfix/results
dotnet build AlKhayal.slnx --artifacts-path artifacts/activation-storage-hotfix-final -v minimal
$env:ALKHAYAL_LICENSE_MANAGER_NODE = 'C:\Users\user\Desktop\AlKhayal License Manager\runtime\node.exe'
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/activation-storage-hotfix-final --no-build --logger 'trx;LogFileName=unit-final.trx' --results-directory artifacts/activation-storage-hotfix-final/results
node frontend/tests/run-tests.js
rg --files frontend tools/AlKhayal.LicenseManager -g '*.js' -g '*.cjs' | ForEach-Object { node --check $_; if ($LASTEXITCODE) { throw 'Syntax failed' } }
powershell -NoProfile -ExecutionPolicy Bypass -File artifacts/activation-storage-hotfix/verify-cli.ps1
git diff --check
```

The ignored CLI script generates only a disposable in-memory test RSA key and invokes only rejection paths in the actual final UI executable, never normal startup or elevated production installation. Successful final evidence is `artifacts/activation-storage-hotfix-final/results/unit-final.trx`, `actual-helper-cli.txt`, plus `artifacts/activation-storage-hotfix/results/integration-full.trx` and `focused-latest.trx`. Earlier failed test evidence is retained; those runs are not counted as passing.

No delegation, OpenCode, commit, push, merge, reset or branch switch. No production license was removed/replaced and no production database changes occurred. No running POS was stopped. The production trusted public key, owner private key/password, License Manager and all unrelated existing modifications were preserved. Only disposable test fixtures/child processes were cleaned.
