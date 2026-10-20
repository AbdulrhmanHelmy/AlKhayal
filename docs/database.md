# Database

SQLite is created under `%LOCALAPPDATA%\AlKhayal\alkhayal.db` by default or at `ALKHAYAL_DATABASE_PATH` when explicitly configured. It must remain on local storage. `SchemaVersion` records applied embedded scripts; the migrator orders scripts lexically and applies each pending migration transactionally without recreating the database.

- `001_create_products.sql` provides the original product foundation.
- `002_auth.sql` adds users, server-side sessions, indexes, and foreign keys.
- `003_auth_profile_and_csrf.sql` adds display name, role, timestamps, and per-session CSRF tokens.

Development provisioning runs only through an explicit `--provision-user [username]` command. Password reset for an existing account is a separate explicit `--reset-password [username]` command. Both read the password from the console without echoing it and never store it in an environment variable or command-line argument. Any non-empty password is accepted. Passwords use PBKDF2-SHA256 with random salts and are never stored or returned in plaintext. Existing usernames are not silently overwritten during provisioning; `--provision-demo` remains an interactive compatibility alias.

The UI's products, invoices, accounts, inventory, and other business records are mock session data and are not SQLite tables or migrations. Persisting them requires explicit money/rounding policy, transactional sale/payment/inventory rules, operation-key idempotency, audit records, and a new numbered migration.
