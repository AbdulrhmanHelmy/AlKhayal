# Phase 1 QA coverage — Batch 1 (session security)

Generated 2026-10-26 by the OpenCode Batch 1 worker. Scope: authentication/session security items only.
No production code was changed in Batch 1 itself; its two QA-004 tests were intentionally left failing as
evidence (see [QA-004 execution evidence](#qa-004-execution-evidence)). A follow-up repair batch fixed the
confirmed store-level expiry materialization defect in
`src/AlKhayal.Data/Authentication/SqliteAuthStores.cs` — the QA-004 tests now pass with their original
assertions; see [confirmed-bugs.md](confirmed-bugs.md) (CONFIRMED-001) for the full before/after record.

## Numbering caveat

The repository contains no committed canonical QA-001..QA-017 checklist. Only **QA-004** (session expiry at
the 8-hour boundary) and **QA-005** (cookie security) were fixed by the Batch 1 assignment. The remaining IDs
below are reconstructed from the coverage areas stated for this batch (bootstrap, invalid/disabled login,
persisted session/logout, user role and grants, last admin, settings and features API/CSRF) and are marked
*(inferred)*. If a Codex-authored master list exists, it supersedes the inferred numbering.

## Isolation method (applies to all newly executed tests)

- Every new test creates a disposable GUID SQLite database under `%TEMP%` and passes it as an explicit
  `HostOptions.DatabasePath` to a real OWIN `AlKhayalServer`; the operator database
  `%LOCALAPPDATA%\AlKhayal\alkhayal.db` is never opened (each test asserts
  `Assert.NotEqual(Db.DefaultDatabasePath(), path)`).
- Before the test runs, the process environment variable `ALKHAYAL_DATABASE_PATH` was set to a new absolute
  temp GUID path and verified to differ from the operator default (`DIFFERS_FROM_DEFAULT=True`).
- `seeded-database.db` and all pre-existing working-tree changes were untouched.
- Expiry is exercised without sleeps: `Sessions.ExpiresUtc` is moved to an expired UTC value directly in the
  isolated database.

## Coverage map

| ID | Expected behavior | Existing exact tests | New tests (this batch) | Missing browser/native portions | Execution status |
|---|---|---|---|---|---|
| QA-001 *(inferred)* | First-run bootstrap only with zero users; blocked (`bootstrap_unavailable`/409) afterwards; exactly one admin under concurrency; no partial user on validation failure | `FirstRunBootstrapTests.Fresh_database_bootstraps_admin_logs_in_and_survives_restart`, `Any_existing_user_blocks_bootstrap`, `Concurrent_bootstrap_creates_exactly_one_admin`, `Bootstrap_api_is_public_once_then_permanently_conflicts`, `Invalid_or_mismatched_password_leaves_no_partial_user`, `Simple_non_empty_passwords_are_accepted` | — | Bootstrap UI wizard flow in WebView2 | Not re-executed in this batch; covered by committed tests |
| QA-002 *(inferred)* | Wrong username/password → 401 `invalid_credentials` at the API | `UserManagementApiTests.Admin_manages_users_cashier_is_forbidden_and_sessions_follow_account_state` (post-reset login → 401) | — | Login-screen Arabic validation messages | Not re-executed in this batch; API-level wrong-password 403/401 distinction is only indirectly covered |
| QA-003 *(inferred)* | Disabled account cannot log in (`disabled_account`); deactivating an account invalidates its live session | `UserManagementApiTests.Admin_manages_users_cashier_is_forbidden_and_sessions_follow_account_state` (deactivate → `/api/auth/me` 401) | — | Deactivated-user UX after session loss | Not re-executed in this batch; direct login-with-disabled-account status assertion exists at service level only |
| **QA-004** | Session expires exactly 8 hours after login; expired cookie rejected by `/api/auth/me` and protected endpoints; expired row removed; restart does not restore it; bound CSRF token invalid after expiry | None (no prior test referenced `ExpiresUtc`, expiry, or cookie flags) | `QaPhase1SessionSecurityTests.QA004_expired_session_is_rejected_removed_and_not_restored_by_restart`, `QaPhase1SessionSecurityTests.QA004_csrf_token_is_invalid_for_an_expired_session` | Real 8-hour wall-clock session, WebView2 cookie retention across restarts | **PASSED after repair** — Batch 1 run was 2 failed / 1 passed (real production defect found, see evidence below); repaired in `SqliteAuthStores.cs`, rerun 3/3 ([CONFIRMED-001](confirmed-bugs.md)) |
| **QA-005** | Login sets `alkhayal_session` cookie with `HttpOnly` and `SameSite=Lax` (over HTTPS also `Secure`); login/me response bodies never expose the session token or password hash | Adjacent only: `UserManagementApiTests…` asserts `passwordHash` absent from the users list — it does not cover cookie flags or login/me secret leakage | `QaPhase1SessionSecurityTests.QA005_session_cookie_sets_HttpOnly_and_SameSite_and_login_me_responses_leak_no_secrets` | `Secure` flag over a real HTTPS listener; WebView2 cookie storage inspection | **PASSED** in this batch's run |
| QA-006 *(inferred)* | Session survives a service restart against the same database; logout deletes the session | `AuthServicePersistenceTests.Auth_session_survives_new_service_instance`, `ApiEndpointTests.Health_login_me_and_logout_use_real_owin_api` | — | WebView2 restore of a cookie after desktop restart | Not re-executed in this batch |
| QA-007 *(inferred)* | Cashier role is forbidden on administrator APIs; every protected endpoint rejects anonymous access | `UserManagementApiTests…`, `BusinessActivityApiTests.Api_enforces_auth_admin_validation_and_persists_after_restart`, `CatalogApiTests.Catalog_api_enforces_authentication_csrf_screen_permissions_and_restart_persistence`, `InventoryApiTests.Inventory_api_requires_auth_csrf_and_existing_screen_permission`, `SettingsApiTests.Settings_api_authenticates_authorizes_validates_and_persists_after_restart`, `FeatureApiTests.Feature_api_reads_validates_authorizes_and_persists_after_restart` | — | Sidebar/search/direct-route gating in a real browser session | Not re-executed in this batch |
| QA-008 *(inferred)* | Per-user screen-permission grants persist and are enforced; administrator-only screens reject cashier grants | `UserManagementApiTests…`, `UserManagementPersistenceTests.Effective_access_combines_role_grants_and_enabled_modules` | — | Screen-permission editor interaction | Not re-executed in this batch |
| QA-009 *(inferred)* | Last active administrator cannot be deactivated (409) | `UserManagementApiTests…` (users/1/status → Conflict), `UserManagementPersistenceTests.Users_permissions_last_admin_and_rollback_are_persisted_transactionally` | — | Administrator UX messaging | Not re-executed in this batch |
| QA-010 *(inferred)* | Settings API authenticates, authorizes, validates, persists, audits, rolls back | `SettingsApiTests…`, `SettingsPersistenceTests.Company_general_audit_and_database_information_persist_after_restart`, `SettingsPersistenceTests.Settings_update_and_audit_are_one_transaction` | — | Settings screens in WebView2 | Not re-executed in this batch |
| QA-011 *(inferred)* | Features/modules API authenticates, authorizes, persists across restart, keeps administrator overrides | `FeatureApiTests…`, `FeaturePersistenceTests.Feature_states_overrides_audit_and_reenable_survive_real_sqlite`, `FeaturePersistenceTests.Invalid_user_rolls_back_state_and_audit_and_migration_is_repeatable` | — | Modules & Features screen interaction | Not re-executed in this batch |
| QA-012 *(inferred)* | State-changing calls without/with a wrong `X-CSRF-Token` → 403 `invalid_csrf` | `UserManagementApiTests…` (POST without CSRF → Forbidden), `ApiEndpointTests…` (logout without CSRF → Forbidden), `CatalogApiTests…`, `InventoryApiTests…` | — | Front-end CSRF refresh flow | Not re-executed in this batch |
| QA-013 *(inferred)* | `bootstrap-status` public once; login/me/logout flow over the real OWIN host with cookies | `FirstRunBootstrapTests.Bootstrap_api_is_public_once_then_permanently_conflicts`, `ApiEndpointTests.Health_login_me_and_logout_use_real_owin_api` | — | — | Not re-executed in this batch |
| QA-014 *(inferred)* | User CRUD: validation, case-insensitive uniqueness, role validation, rollback on audit failure, password reset with hash replacement | `UserManagementApiTests…`, `UserManagementPersistenceTests…`, `UserProvisionerTests.Provisioner_accepts_a_single_character_password_and_stores_only_its_hash`, `UserProvisionerTests.Password_reset_replaces_the_hash_for_an_existing_user` | — | User-management screen interaction | Not re-executed in this batch |
| QA-015 *(inferred)* | Safe serialization: no password hashes in API responses; static files served `no-store` | `UserManagementApiTests…` (list `DoesNotContain("passwordHash")`, screens.json `no-store`), `QA005` new test (login/me) | QA-005 test extends this to login/me bodies | Client-side storage inspection (no secrets in localStorage) | Partially executed this batch via QA-005 |
| QA-016 *(inferred)* | LAN/cloud hardening: rate limiting, CSRF review, TLS decisions | None — not implemented per AGENTS.md deployment requirements | — | Entirely | **Open** — required before production cookie-authenticated LAN/cloud deployment |
| QA-017 *(inferred)* | Native WinForms/WebView2 lifecycle: expired-session redirect UX, cookie retention, visible connection/runtime errors | None automated | — | Entirely manual (`docs/testing.md` native acceptance sections) | **Open** |

Items marked "Not re-executed in this batch" were not run here; their listed tests exist in the committed
suite. This document does not claim that historical pass reports are current, and the frontend registry
tests verify route→renderer wiring, not real user interaction.

## QA-004 execution evidence

Commands run from the repository root:

```powershell
dotnet build AlKhayal.slnx --no-restore
# process ALKHAYAL_DATABASE_PATH set to a fresh temp GUID path, verified != %LOCALAPPDATA%\AlKhayal\alkhayal.db
dotnet test AlKhayal.slnx --no-restore --no-build --filter "FullyQualifiedName~QaPhase1SessionSecurityTests"
```

Build: **succeeded, 0 warnings, 0 errors**. Targeted run: **Total tests: 3, Passed: 1, Failed: 2**
(13.4 s).

- `QA005_session_cookie_sets_HttpOnly_and_SameSite_and_login_me_responses_leak_no_secrets` — **Passed**.
- `QA004_expired_session_is_rejected_removed_and_not_restored_by_restart` — **Failed**: after moving
  `Sessions.ExpiresUtc` to a past UTC value, `GET /api/auth/me` with the same cookie returned
  **200 OK**, expected 401 Unauthorized (assertion at the first deviation; later steps therefore
  unverified in the failing run).
- `QA004_csrf_token_is_invalid_for_an_expired_session` — **Failed**: `POST /api/auth/logout` with the
  still-bound CSRF token after expiry returned **200 OK**, expected 403 `invalid_csrf`.

### Root-cause analysis (observation only; no production code changed)

The freshly stored `ExpiresUtc` is correct: the boundary assertion confirmed it equals `UtcNow + 8h` within
a ±2-minute tolerance, and login/me/users all succeeded before expiry. The defect is in expiry comparison.
An out-of-repository probe on the same runtime (.NET Framework 4.8, `System.Data.SQLite` from the test
output) against a scratch database showed:

```text
Machine: Egypt Standard Time, base offset +03:00
STORED=2026-10-26T10:34:01.3127056Z          (one minute before UtcNow)
GETDATETIME=2026-10-26T13:34:01.3127056 KIND=Unspecified
MATERIALIZED_GT_UTCNOW=True
```

`System.Data.SQLite` materializes the stored UTC `…Z` text as **local wall-clock ticks with
`DateTimeKind.Unspecified`** (UTC stored value shifted by the +03:00 zone). `AuthService.GetSessionAsync`
(`src/AlKhayal.Application/Authentication/AuthService.cs`, line 41) then compares that shifted value
against `DateTime.UtcNow`, so on a UTC+X machine an expired session still appears valid for an extra X
hours (sessions effectively last ~11 hours here), and direct expiry is not detected. On a machine set to
UTC the same code would behave correctly — the defect is timezone-dependent.

The failing tests were intentionally left failing as Batch 1 evidence. Expectations were not relaxed and
no production file was modified in Batch 1; the comparison had to be fixed in `AuthService` (or the store
had to normalize expiry to UTC-kind ticks) before these tests could pass.

## QA-004 repair addendum (follow-up batch)

The repair batch normalized expiry materialization in `SqliteSessionStore.FindAsync`
(`src/AlKhayal.Data/Authentication/SqliteAuthStores.cs`): the stored UTC text is parsed explicitly
(`AdjustToUniversal | AssumeUniversal`, invariant culture) and handed to `AuthService` as
`DateTimeKind.Utc`, covering current `…Z` rows, prior ISO UTC values without a zone suffix, and
rejecting invalid stored expiry by returning `null`. Login policy, cookie format, authorization rules,
schema, migrations, and the exact regression assertions are unchanged; the test file is untouched.

Repair-run results (isolated temp GUID database via `HostOptions.DatabasePath`; process
`ALKHAYAL_DATABASE_PATH` set to a fresh temp GUID path with `DIFFERS_FROM_DEFAULT=True`):

- Before repair: `QaPhase1SessionSecurityTests` → **2 failed / 1 passed of 3** (identical to Batch 1 and
  to Codex's independent rerun).
- After repair: `QaPhase1SessionSecurityTests` → **3/3 passed**; `AuthServicePersistenceTests` → 1/1;
  `UserManagementApiTests` → 1/1; `FirstRunBootstrapTests` → 10/10; combined rerun → **15/15**.
- Reproduction, probe output, the net48 `DateTimeStyles` pitfall, and residual risks are recorded in
  [confirmed-bugs.md](confirmed-bugs.md) (CONFIRMED-001).

No OpenCode call log file was available in the repository to append to this record.

## New files added by this batch

- `tests/AlKhayal.IntegrationTests/QaPhase1SessionSecurityTests.cs` — three tests (QA-004 x2, QA-005).
- `docs/qa/phase-1-coverage.md` — this document.

No other file was created, modified, staged, committed, or reverted.

The follow-up repair batch additionally created `docs/qa/confirmed-bugs.md` and modified only
`src/AlKhayal.Data/Authentication/SqliteAuthStores.cs` plus this document.
