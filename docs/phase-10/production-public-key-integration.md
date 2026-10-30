# Production public-key integration — 2026-10-30

The public-key fix remains valid. Its former manual administrator activation instructions are superseded by the [storage/UAC hotfix](activation-storage-hotfix.md): normal POS remains asInvoker; only new-license installation requests UAC. Existing activated machines need no reactivation.

## Root cause and minimal fix

The owner initialized an RSA-3072 signing key after the owner-manager implementation. Its canonical public export is `C:\Users\user\AppData\Local\AlKhayalLicenseManager\public.xml`, with SigningKeyId **rsa-17b43bcb5933edf6b1e9463d**. POS `src/AlKhayal.Data/Licensing/TrustedKeys` contained no XML resources. The already running POS build also had zero embedded trusted public keys. It correctly rejected the otherwise valid signed license.

Added only the existing **public** export as `src/AlKhayal.Data/Licensing/TrustedKeys/rsa-17b43bcb5933edf6b1e9463d.xml`. Existing/future trusted keys are preserved. No signing algorithm, license/request grammar, hardware binding, API protection, authentication, persistence or owner-tool code was changed. The public-key build guard remains enabled. Private-key files and the signing password were never opened, copied, read, displayed or requested; no production keypair was generated/replaced and no new production license was signed.

## Actual verification

The owner SQLite registry was opened read-only to select its existing signed license (without customer contact/notes). Its one license used this KeyId and passed native Node verification using only the public export. A disposable .NET Framework diagnostic then referenced the **actual freshly built UI's AlKhayal.Data/Application/Core DLLs** and invoked the existing RsaLicenseSignatures.Embedded and LicenseService implementations:

- Existing production license signature: valid in both Node and .NET.
- Format/product/algorithm/key ID: accepted unchanged.
- Signed matching device: accepted.
- Actual current Windows computer, checked via the existing WMI provider: matched and accepted (one matching license). No raw identifiers were printed or saved.
- Different injectable hardware identity: rejected.
- Tampered payload with a changed, structurally valid LicenseId: rejected.
- Tampered signature: rejected.
- Activation/recreated-service persistence: passed using a disposable temp license file only, never ProgramData.
- Empty-trust negative control: rejected the same signature, reproducing the original root cause.
- Old UI build inspected without loading/executing business startup: zero trusted public-key resources.

Added `TrustedPublicKeyRegistryTests.Embedded_trusted_resources_are_public_RSA3072_and_named_by_their_canonical_key_id`: **1 passed, 0 failed/skipped**. It verifies embedded resource discovery, canonical key ID/filename, public-only RSA-3072 and canonical XML. Full solution build: **success, 0 warnings/errors**. `git diff --check`: passed. Full unrelated suites were not rerun for this public-resource-only change. Earlier task counts are historical, not new results.

The ignored diagnostic initially encountered default recursive source-glob collisions with generated assembly files; it was restricted to its own Program.cs, then executed successfully. This did not affect the POS build or licensing implementation. Production verification JSON and TRX evidence are under `artifacts/production-license-trust/results/`; actual signed-license diagnostic input remains ignored and is not committed as a test fixture.

Executed build/test commands:

```powershell
dotnet build AlKhayal.slnx --artifacts-path artifacts/production-license-trust -v minimal
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --artifacts-path artifacts/production-license-trust --no-build --filter FullyQualifiedName~TrustedPublicKeyRegistryTests --logger 'trx;LogFileName=trusted-public-key-final.trx' --results-directory artifacts/production-license-trust/results
```

## Exact activation steps

The running process currently uses `D:\projects\AlKhayal\src\AlKhayal.UI\bin\Debug\net48\AlKhayal.UI.exe`, whose old Data DLL has no trusted key. It was not stopped or overwritten. **A restart is required, and it must launch the rebuilt executable**; reopening the unchanged old executable does not load the new resource.

A ready updated executable is:

`D:\projects\AlKhayal\artifacts\production-license-trust\bin\AlKhayal.UI\debug\AlKhayal.UI.exe`

Its default frontend resolution was checked and points to `D:\projects\AlKhayal\frontend`. The existing configured/default database path remains unchanged; no POS was launched by this verification.

1. Close the currently open POS activation window using X.
2. The ProgramData licensing directory does not yet exist on this computer. Open PowerShell **as Administrator** and run the following once. Click OK on the storage-initialization message before continuing (the first command waits):

```powershell
$pos = 'D:\projects\AlKhayal\artifacts\production-license-trust\bin\AlKhayal.UI\debug\AlKhayal.UI.exe'
Start-Process -FilePath $pos -ArgumentList '--initialize-license-storage' -Wait
Start-Process -FilePath $pos
```

3. Open the existing owner License Manager → `سجل التراخيص`, find this computer's already-issued license and copy its complete activation code. **No reissuance, new key or password is needed.**
4. Paste it into `كود التفعيل` in the new POS window, then click `تفعيل البرنامج`. The verified same-device license should persist and proceed to the existing login.
5. Later launches can run normally without elevation. Continue using the updated executable. To return to the original source/bin launch path, close POS first, run a normal `dotnet build D:\projects\AlKhayal\AlKhayal.slnx`, then reopen that rebuilt original executable. Future compatible builds automatically include this trusted public resource.

The storage initializer only prepares licensing ACLs; it does not create/change business data. It was not executed during this task. ProgramData activation is left for the operator. Existing POS and owner processes, POS business database, owner registry and private key were not modified or stopped. No delegation, commit, push, reset, merge or branch switch was performed.

## Files changed

- `src/AlKhayal.Data/Licensing/TrustedKeys/rsa-17b43bcb5933edf6b1e9463d.xml` — existing production public export, newly trusted.
- `src/AlKhayal.Data/Licensing/TrustedKeys/README.md` — current provisioning/rebuild instructions.
- `tests/AlKhayal.UnitTests/TrustedPublicKeyRegistryTests.cs` — embedded public-resource regression.
- `docs/phase-10/phase-10-1-licensing.md` — current production trust status/link.
- `docs/phase-10/production-public-key-integration.md` — this investigation, evidence and activation walkthrough.
