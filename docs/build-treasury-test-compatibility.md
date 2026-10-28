# Treasury test compatibility build repair

The reported compiler errors came from the separate checkout at `D:\AlKhayal`, which contains retained legacy Core treasury types and an untracked legacy `TreasuryServiceTests.cs`. The current host uses Application `TreasuryService`/`ITreasuryStore`; the legacy tests expected a repository/clock constructor, IBAN/opening fields, transfers and reporting methods that this service does not expose. Type aliases alone would therefore leave unsupported-method errors.

The repair ports the active treasury unit test to the current store contract and gives `PurchasingTreasuryTests.cs` an explicit Application `TreasuryAccountCommand` alias. The original unit test is preserved verbatim as `docs/legacy-tests/TreasuryServiceTests.pre-build-fix.cs.txt`, outside the C# compile glob. The same active test/alias and backup are retained in the working checkout `D:\k\AlKhayal`. No test project was excluded and no production implementation, migration or operator database changed.

The replacement tests exercise both account types, metadata normalization, required fields, edit/delete identity/version validation, exact amounts, idempotency hash canonicalization and actor/payload binding, invalid monetary input and list balance formatting. Legacy transfer/IBAN/report assertions are retained in the backup rather than falsely claimed as supported behavior.

Validation on 2026-10-29 was executed in the user's `D:\AlKhayal` checkout:

- `dotnet build AlKhayal.slnx --no-restore -m:1`: succeeded, zero errors and warnings.
- `dotnet test AlKhayal.slnx --no-build --no-restore -m:1 --filter 'FullyQualifiedName~TreasuryServiceTests|FullyQualifiedName~Cash_purchase|FullyQualifiedName~Late_purchase_failure|FullyQualifiedName~Draft_can_change_cash_account' --logger 'console;verbosity=minimal'`: all 14 selected unit and 6 selected integration tests passed. This was focused validation, not a claim that all tests in the separate checkout ran.
- Working-checkout `git diff --check`: passed. A preliminary sandboxed test-host attempt was stopped; successful selected tests ran outside the sandbox.

All external edits were authorized through the sandbox approval flow. Other staged, untracked and legacy files in the separate checkout were preserved. No framework/package/runtime or Windows 7/Chromium compatibility claim changed. Native UI startup was not part of this compiler repair.
