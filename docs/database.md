# Database

The Phase 2.3 tracking foundation reuses persisted product capability assignments. Physical batch and identifier tables are created by the atomic inventory ledger migration `011_inventory_ledger.sql` described below; see [phase-2-task-3-tracking.md](phase-2-task-3-tracking.md) for the Task 2.3 scope.

## Migration 009 — product reference purchase cost

`009_product_purchase_price.sql` additively adds nullable `ProductDetails.PurchasePriceScaled` with a non-negative check. Non-null values use the existing signed-64-bit scale-4 money policy. Existing rows remain `NULL`, explicitly meaning unknown; the migration does not invent a zero cost, calculate average inventory cost, or create stock movements. Catalog create/update transactions write this reference value alongside the existing product details.

## Migration 008 — product catalog foundation

`008_product_catalog_foundation.sql` adds `ProductCategories`, `Units`, `ProductDetails`, the internal default subset of `ProductVariants`, `ProductMigrationIssues`, and `CatalogOperations`. Root and child category names have separate normalized unique indexes because SQLite treats `NULL` values as distinct. Unit codes are normalized and unique; unit ID 1 (`PCS`, قطعة) is the protected system default. Product details reference active categories/units through Application/Data validation, store price as a non-negative signed 64-bit integer at scale 4, and carry a monotonic `Version` for optimistic concurrency.

The migrator performs the legacy price backfill in C# inside the same transaction as migration 008. It parses only invariant integer/real/text decimal representations, multiplies with checked `decimal`, rounds midpoint away from zero once, and range-checks `long`. Invalid names/types/text/negative/overflow values retain their original `Products` row and price, receive an inactive zero placeholder detail with `review_required`, and create a review issue. Startup reconciliation applies the same quarantine policy to a legacy-only row created after migration. The original `Products.Price` is recovery evidence; it is not read by the catalog API after migration.

No developer database is recreated or deleted. Tests use unique temporary files, clear SQLite pools, and delete only those files.

## Migration 007 — general settings

`007_general_settings.sql` adds singleton `CompanyProfile` and `ApplicationSettings` rows plus `SettingsAuditLog`. Defaults are Arabic, `dd/MM/yyyy`, 24-hour time, `EGP`, `ج.م`, two decimal places, and tax disabled. Updates and their non-sensitive audit entry commit in one SQLite transaction. The migration is additive, repeat-safe through `SchemaVersion`, preserves all earlier tables, and uses the configured `ALKHAYAL_DATABASE_PATH` rather than a hardcoded location.

SQLite is created under `%LOCALAPPDATA%\AlKhayal\alkhayal.db` by default or at `ALKHAYAL_DATABASE_PATH` when explicitly configured. It must remain on local storage. `SchemaVersion` records applied embedded scripts; the migrator orders scripts lexically and applies each pending migration transactionally without recreating the database.

- `001_create_products.sql` provides the original product foundation.
- `002_auth.sql` adds users, server-side sessions, indexes, and foreign keys.
- `003_auth_profile_and_csrf.sql` adds display name, role, timestamps, and per-session CSRF tokens.
- `004_business_activities.sql` adds the selected activity set, its single primary marker, and capability recommendation/override metadata. `UX_BusinessActivities_Primary` enforces at most one primary selection. Foreign keys retain the administrator responsible for each change.
- `005_feature_management.sql` adds optional-module state and an append-only feature audit log. Existing `CapabilityConfiguration` remains the only product-capability state source.
- `006_user_management.sql` adds `Users.LastLoginUtc`, preserves the legacy `administrator` role while mapping every other legacy role to the non-privileged `cashier` role, and adds `UserScreenPermissions` plus `UserAuditLog`. Existing IDs, usernames, activation state, timestamps, and password hashes are preserved.
- `004_customers.sql` adds persisted customer records, including address, balances, credit limit, subscription, and status.
- `005_customer_payments.sql` adds persisted customer payment records.
- `012_suppliers.sql` adds persisted suppliers (`Suppliers.Payable` is the amount currently owed), supplier purchase-on-credit invoices (`SupplierInvoices`, with `PaidAmount <= Total`), and supplier payments (`SupplierPayments`, optionally tied to one invoice). Foreign keys use `ON DELETE RESTRICT`, so a supplier with invoices or payments cannot be deleted.

User mutations that can remove administrator access count active administrators and apply the change in the same SQLite transaction. Role/status/password changes invalidate that user's sessions. Permission replacement and its non-sensitive audit record share a transaction; a failure rolls back the old permission set.

`BusinessActivities` stores stable registry keys, `IsPrimary`, UTC selection time, and `ChangedByUserId`. Saving runs in one transaction and replaces only rows in this table. `CapabilityConfiguration` is the Task 2 integration seam: `IsEnabled` is the effective suggested state and `OverrideState` is either `recommended` or `administrator`. Applying activity recommendations inserts/enables recommended rows but its conflict condition excludes administrator-owned rows. Removed activities never delete or disable capability rows, so the comparison service can report recommendations that no longer apply and the administrator can decide later.

`ModuleConfiguration` stores explicit or recommended optional-module state, source, UTC timestamp, and acting user. Absence means the registry default. `FeatureAuditLog` records module/capability changes and recommendation application with previous/new states. State and audit writes share a transaction. Deactivation changes configuration only and never deletes business rows.

Development provisioning runs only through an explicit `--provision-user [username]` command. Password reset for an existing account is a separate explicit `--reset-password [username]` command. Both read the password from the console without echoing it and never store it in an environment variable or command-line argument. Any non-empty password is accepted. Passwords use PBKDF2-SHA256 with random salts and are never stored or returned in plaintext. Existing usernames are not silently overwritten during provisioning; `--provision-demo` remains an interactive compatibility alias.
Products, categories, units, and optional reference purchase costs are persisted by migrations 008–010, and warehouses, stock movements, balances, batches, physical identifiers, and inventory audit are persisted by migration 011 as described below. Customer records and customer payments are also persisted by their dedicated migrations, `004_customers.sql` and `005_customer_payments.sql`. Suppliers, supplier credit invoices, and supplier payments are persisted by `012_suppliers.sql`; supplier invoices are created from the supplier account screen, not yet from the purchase-invoice editor. Invoices, purchases, accounts, and other business records outside these migrations remain session-only demonstrations and are not SQLite tables. Business activity profile tables contain configuration metadata only. Persisting any further business record requires explicit money/rounding policy, transactional sale/payment rules, operation-key idempotency, audit records, and a new numbered migration.

# Phase 2 / Task 2.4 inventory migration

## Migration 015 — customer transaction ledger

Migration 015 is additive: it leaves `Customers`, `CustomerPayments`, balances, and all historical rows untouched. It creates immutable customer transaction entries and imports a reconciled opening entry plus the recorded historical collections, so the new statement derives the same current balance rather than rewriting it. The migration preflight rejects a legacy balance or payment that cannot be represented exactly at scale 4; it never rounds a historical monetary value. New monetary entries are validated at scale 4 and use a unique operation key for retry safety.

Migration `011_inventory_ledger.sql` is additive to the Task 2.3 catalog schema. It creates the default warehouse, inventory documents/lines, immutable stock movements, batch and physical-unit identities, movement links, balance projection, and inventory audit. Partial unique balance indexes distinguish null batch from a real batch. Projection triggers compare each inserted/updated row with its ledger aggregate; posted documents, lines, movements, and audit rows are protected against edits. See [inventory-ledger.md](inventory-ledger.md) for the complete table list, scale policy, restore/rebuild process, and tracking limits.

Warehouse rows are created and edited transactionally by `SqliteInventoryStore.SaveWarehouseAsync`. `Warehouses.NormalizedCode` is the trimmed, Unicode-normalized, upper-cased code and carries a unique constraint, so codes that differ only by case or spacing are rejected as duplicates on both create and edit. The active default and a warehouse that still holds stock cannot be deactivated, only an empty nondefault warehouse can, and reactivating it is an ordinary edit of the same row. Creating a second warehouse still requires the effective `multiple-warehouses` module; editing, deactivating, or reactivating an existing warehouse does not. See [warehouse-management.md](warehouse-management.md) for the directory workflow.

## Migration 016 — FIFO cost layers

Adds persistent receipt/transfer layers, immutable movement allocations and documented opening-cost reconciliations, plus nullable selected-unit acquisition/retail-price snapshots on document lines. Existing stock becomes quantity-preserving unresolved layers; historical movements and valuations are not rewritten. Cost-sensitive withdrawals are blocked until an evidenced opening basis is established. The conservative reconciliation cannot alter retained historical value; a mismatch stays blocked for a dedicated valuation correction. See [inventory-fifo.md](inventory-fifo.md).
