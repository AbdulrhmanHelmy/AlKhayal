# Massive demo data seeding report

**Run date:** 2026-10-25  
**Database:** `%LOCALAPPDATA%\AlKhayal\alkhayal.db` (no `ALKHAYAL_DATABASE_PATH` override)  
**Final schema:** migration 020 (20 applied migrations)  
**Final file size:** 121,712,640 bytes (about 116.1 MiB)

## Safety and migration validation

- The original schema-003 SQLite database was copied with SQLite's online backup API to `%TEMP%\alkhayal-migration-validation-003.db`.
- The copy was upgraded with the repository's actual `Migrator.Run` implementation. Migrations 004–020 ran in lexical order; the host was started against the copy; migration execution and host startup were repeated to check restart/idempotency behavior.
- Copy checks passed: all 20 migrations recorded, required catalog/purchasing/inventory/FIFO/sales tables present, `PRAGMA integrity_check` returned `ok`, `PRAGMA foreign_key_check` returned no rows, and both existing users and password hashes were preserved.
- The pre-seed SQLite backup was revalidated before applying migrations to the original database: `%LOCALAPPDATA%\AlKhayal\backups\alkhayal-before-massive-demo-seed-20261025-101957.db`.
- The original database was upgraded with the same migrator only after the copy passed. Its two users were retained and their password hashes remained unchanged. Migration 006's documented normalization changed the legacy `operator` role to `cashier`; no user was deleted, reset, or created.
- No applied migration files were modified. Startup preflights for catalog, customer ledger, and supplier ledger passed. Migration 018's legacy supplier payment compatibility behavior ran as part of the normal migration sequence.

## Seeded records

| Dataset | Final records |
| --- | ---: |
| Product categories | 60 |
| Units of measure | 4 |
| Products / product details | 15,000 |
| Product variants (default variants) | 15,000 |
| Product SKUs | 15,000 |
| Barcodes | 0 |
| Customers | 3,000 |
| Suppliers | 1,000 |
| Warehouses | 1 |
| Posted purchase invoices / lines | 10,000 / 30,000 |
| Posted sales invoices / lines | 30,000 / 60,000 |
| Purchase returns / lines | 1,000 / 1,000 |
| Sales returns / lines | 1,000 / 1,000 |
| Inventory documents / lines | 47,000 / 97,000 |
| Stock movements | 97,000 |
| FIFO cost layers / allocations | 31,000 / 66,000 |
| Sales return cost allocations | 1,000 |
| Customer ledger transactions | 11,536 |
| Supplier ledger entries | 13,000 |
| Sales payment/refund events | 28,894 |

Data uses Arabic names across supermarket, clothing, electronics, home goods, stationery, hardware, personal care, and general retail categories. The 68 repeated product names found in the final review were clarified with their category names through the catalog service. No duplicate product names or active SKUs remain.

## Integrity and reconciliation

All final checks passed:

- SQLite `integrity_check`: `ok`; foreign-key violations: 0.
- Duplicate product names: 0; duplicate active SKUs: 0; duplicate active barcodes: 0.
- Negative inventory balances: 0.
- Purchase invoice, sales invoice, purchase return, and sales return totals match their lines: 0 mismatches.
- Customer balances reconcile to signed customer ledger transactions: 0 mismatches.
- Supplier payable less supplier credit reconciles to supplier ledger entries: 0 mismatches.
- Aggregate on-hand inventory reconciles to remaining FIFO layer quantity and value. Quantity: 293,739,250,000 internal scale units on both sides; value: 4,235,280,957,350 internal scale units on both sides.
- All seeded purchase and sales invoices are posted; no unfinished inventory documents remain.

Aggregate amounts (internal monetary values are scaled by 10,000): purchase invoices EGP 432,875,972.22; sales invoices EGP 12,057,012.56; ending inventory value EGP 423,528,095.74. These are synthetic demo amounts.

## Startup and performance

The WinForms application was launched against the final database and remained responsive with window title `الخيال POS`. The self-hosted API returned HTTP 200 for `/api/auth/bootstrap-status` (`required=false`) and `/login.html`. After startup/restart, the protected catalog endpoint correctly returned HTTP 401 without an authenticated session. Existing credentials were intentionally left unchanged, so authenticated endpoint timings could not be measured in this run.

Representative direct database query timings recorded during the run (SQLite query shapes used by the catalog and list pages): product search, 9.77 ms median for 25 rows; warehouse balance page, 2.86 ms for 100 rows; purchase list page, 8.25 ms for 50 rows; sales list page, 0.34 ms for 50 rows. These are local database timings, not authenticated HTTP end-to-end timings.

The solution build and test run completed successfully: 49 unit tests and 112 integration tests passed (`dotnet test AlKhayal.slnx --no-build --no-restore`).

## Feature-dependent scenarios not seeded

The current persisted feature configuration has multiple warehouses, product variants, size/color options, serial/IMEI tracking, batch/expiry tracking, multiple units, weight-based selling, and product barcode settings disabled. Therefore the seeded products use simple default variants and SKUs, the database has only its configured main warehouse and no barcode records, and no cross-warehouse transfers, lot/expiry, serial, or variant-specific scenarios were fabricated. All posted purchases, sales, returns, FIFO allocations, and stock adjustments were created through the application's business services; FIFO layers, posted documents, balances, and ledger entries were not forged with direct SQL.

The requested eight-warehouse target was not met because multi-warehouse support is disabled in the current persisted configuration. Barcode records were also not seeded because barcode configuration is disabled. These feature-gated cases are the only planned volume/scenario exceptions; other requested core counts were met.

## Restore point

To return to the database as it existed before this task, stop the application and restore `%LOCALAPPDATA%\AlKhayal\backups\alkhayal-before-massive-demo-seed-20261025-101957.db` to `%LOCALAPPDATA%\AlKhayal\alkhayal.db`. That backup contains the original schema-003 state and two users; it does not include migrations or demo data added afterward. Keep the application closed while replacing the database file.

For the later normal-desktop database selection, account, authenticated visibility, and full-warehouse inventory query verification, see [desktop-database-selection-report.md](desktop-database-selection-report.md).
