# Database

## Migration 007 — general settings

`007_general_settings.sql` adds singleton `CompanyProfile` and `ApplicationSettings` rows plus `SettingsAuditLog`. Defaults are Arabic, `dd/MM/yyyy`, 24-hour time, `EGP`, `ج.م`, two decimal places, and tax disabled. Updates and their non-sensitive audit entry commit in one SQLite transaction. The migration is additive, repeat-safe through `SchemaVersion`, preserves all earlier tables, and uses the configured `ALKHAYAL_DATABASE_PATH` rather than a hardcoded location.

SQLite is created under `%LOCALAPPDATA%\AlKhayal\alkhayal.db` by default or at `ALKHAYAL_DATABASE_PATH` when explicitly configured. It must remain on local storage. `SchemaVersion` records applied embedded scripts; the migrator orders scripts lexically and applies each pending migration transactionally without recreating the database.

- `001_create_products.sql` provides the original product foundation.
- `002_auth.sql` adds users, server-side sessions, indexes, and foreign keys.
- `003_auth_profile_and_csrf.sql` adds display name, role, timestamps, and per-session CSRF tokens.
- `004_business_activities.sql` adds the selected activity set, its single primary marker, and capability recommendation/override metadata. `UX_BusinessActivities_Primary` enforces at most one primary selection. Foreign keys retain the administrator responsible for each change.
- `005_feature_management.sql` adds optional-module state and an append-only feature audit log. Existing `CapabilityConfiguration` remains the only product-capability state source.
- `006_user_management.sql` adds `Users.LastLoginUtc`, preserves the legacy `administrator` role while mapping every other legacy role to the non-privileged `cashier` role, and adds `UserScreenPermissions` plus `UserAuditLog`. Existing IDs, usernames, activation state, timestamps, and password hashes are preserved.

User mutations that can remove administrator access count active administrators and apply the change in the same SQLite transaction. Role/status/password changes invalidate that user's sessions. Permission replacement and its non-sensitive audit record share a transaction; a failure rolls back the old permission set.

`BusinessActivities` stores stable registry keys, `IsPrimary`, UTC selection time, and `ChangedByUserId`. Saving runs in one transaction and replaces only rows in this table. `CapabilityConfiguration` is the Task 2 integration seam: `IsEnabled` is the effective suggested state and `OverrideState` is either `recommended` or `administrator`. Applying activity recommendations inserts/enables recommended rows but its conflict condition excludes administrator-owned rows. Removed activities never delete or disable capability rows, so the comparison service can report recommendations that no longer apply and the administrator can decide later.

`ModuleConfiguration` stores explicit or recommended optional-module state, source, UTC timestamp, and acting user. Absence means the registry default. `FeatureAuditLog` records module/capability changes and recommendation application with previous/new states. State and audit writes share a transaction. Deactivation changes configuration only and never deletes business rows.

Development provisioning runs only through an explicit `--provision-user [username]` command. Password reset for an existing account is a separate explicit `--reset-password [username]` command. Both read the password from the console without echoing it and never store it in an environment variable or command-line argument. Any non-empty password is accepted. Passwords use PBKDF2-SHA256 with random salts and are never stored or returned in plaintext. Existing usernames are not silently overwritten during provisioning; `--provision-demo` remains an interactive compatibility alias.

The UI's products, invoices, accounts, inventory, and other business records remain mock session data and are not SQLite tables or migrations. Business activity profile tables contain configuration metadata only. Persisting business records requires explicit money/rounding policy, transactional sale/payment/inventory rules, operation-key idempotency, audit records, and a new numbered migration.
