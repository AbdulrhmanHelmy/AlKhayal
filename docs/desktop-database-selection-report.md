# Desktop database selection and visibility report

**Run date:** 2026-10-25 (Africa/Cairo)  
**Normal app database:** `C:\Users\user\AppData\Local\AlKhayal\alkhayal.db`  
**Final schema:** 020; **final size:** 121,712,640 bytes

## Findings and cause

The massive seeded database was already at the normal desktop path. The app and standalone host both construct `HostOptions`; absent environment overrides, that uses `Db.DefaultDatabasePath()`, which resolves to `%LOCALAPPDATA%\AlKhayal\alkhayal.db`. `ALKHAYAL_DATABASE_PATH`, `ALKHAYAL_SERVER_URL`, `ALKHAYAL_FRONTEND_PATH`, and `ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER` were unset at Process, User, and Machine scope. The authenticated live API's `settings/database-info` returned the same absolute path, 20 applied migrations, migration 020, and the expected database size. No database relocation, merge, or environment change was needed.

The data-visibility cause was authentication. The catalog API is protected; an earlier anonymous catalog request returned 401. With the existing active administrator account authenticated, the same API returned the seeded products and other business data. The inventory screen also had a separate scale problem: it requested every warehouse balance, while the API ran multiple FIFO-layer subqueries per balance row. On this database, the full authenticated balance request timed out after 90 seconds. The API query now aggregates available FIFO layers once and joins that summary to balances. The same full request now returns all 14,795 rows (4,579,983 response bytes) in 0.35 seconds.

A historical `startup.log` entry at `2026-10-25T01:29:20` reports a local port 5137 listener collision. It predates this run's startup test; after confirming no AlKhayal process was running, the normal desktop started successfully and responded. It was not the cause of the current database selection.

## Database candidate inventory

Business database candidates in the known `%LOCALAPPDATA%\AlKhayal`, migration-validation, and repository test-artifact locations were inspected with SQL and SQLite checks. A dash means the corresponding business table does not exist in that candidate schema.

| Absolute path | Size (bytes) | Schema | Products | Customers | Suppliers | Purchases | Sales | Inventory docs | Integrity / FK |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `C:\Users\user\AppData\Local\AlKhayal\alkhayal.db` | 121,712,640 | 020 | 15,000 | 3,000 | 1,000 | 10,000 | 30,000 | 47,000 | ok / 0 |
| `C:\Users\user\AppData\Local\AlKhayal\before-password-reset-20261020-213331.db` | 45,056 | 003 | 0 | — | — | — | — | — | ok / 0 |
| `C:\Users\user\AppData\Local\AlKhayal\backups\alkhayal-before-massive-demo-seed-20261025-101957.db` | 45,056 | 003 | 0 | — | — | — | — | — | ok / 0 |
| `C:\Users\user\AppData\Local\AlKhayal\backups\alkhayal-before-legacy-payment-id-20261024.db` | 45,056 | 003 | 0 | — | — | — | — | — | ok / 0 |
| `C:\Users\user\AppData\Local\AlKhayal\recovery-20261020-211623\alkhayal.db` | 45,056 | 003 | 0 | — | — | — | — | — | ok / 0 |
| `C:\Users\user\AppData\Local\AlKhayal\startup-repro-20261024\alkhayal-upgrade-repro.db` | 749,568 | 018 | 0 | 0 | 0 | 0 | — | 0 | ok / 0 |
| `C:\Users\user\AppData\Local\AlKhayal\startup-repro-20261024\fresh-startup.db` | 749,568 | 018 | 0 | 0 | 0 | 0 | — | 0 | ok / 0 |
| `C:\Users\user\AppData\Local\AlKhayal\startup-repro-20261024\phase4-existing-restart.db` | 749,568 | 018 | 4 | 0 | 4 | 5 | — | 7 | ok / 0 |
| `C:\Users\user\AppData\Local\Temp\alkhayal-migration-validation-003.db` | 872,448 | 020 | 0 | 0 | 0 | 0 | 0 | 0 | ok / 0 |
| `D:\projects\AlKhayal\test-artifacts\phase-1-to-4-acceptance-2026-10-24\database\alkhayal-acceptance.db` | 749,568 | 018 | 0 | 0 | 0 | 0 | — | 0 | ok / 0 |
| `D:\projects\AlKhayal\test-artifacts\phase-1-to-4-acceptance-2026-10-24\winforms-run-001\database\alkhayal-ui-acceptance.db` | 749,568 | 018 | 4 | 0 | 4 | 5 | — | 7 | ok / 0 |

WebView2 profile and GPU-cache SQLite-looking files were excluded from business candidates because they are browser cache/profile storage and contain no AlKhayal business tables. No database candidate besides the default path had the seeded record counts.

## Backups and preservation

At the start of the change window, no AlKhayal desktop or server process was running and the default database had no WAL/SHM sidecars. Free disk space was about 18 GiB. Because the seeded source and normal destination are the same file, two separate SQLite online-backup snapshots were created before API login checks or code rollout:

- Normal database backup: `%LOCALAPPDATA%\AlKhayal\backups\alkhayal-normal-before-selection-account-20261025-164237.db`
- Seeded source backup: `%LOCALAPPDATA%\AlKhayal\backups\alkhayal-seeded-source-before-selection-account-20261025-164237.db`
- Backup manifest: `%LOCALAPPDATA%\AlKhayal\backups\alkhayal-selection-fix-backups-20261025-164237.json`

Both backups are 121,712,640 bytes, have SHA-256 `9cead546d91229c1c26dedf4bfa3407069cd677ca456bbfb0d3e25161e04adc2`, schema 020, the expected seeded counts, `integrity_check=ok`, and zero foreign-key violations. Their hashes match each other. The original database's pre-backup SHA-256 was `b289ef411490755784bedcaeb50f0e25726e6468cb52f7335124fe28bc5ebd0a`. The backup SHA differs from the source hash; SQL counts, integrity, migration state, and users were verified against the source. The original database was never replaced.

The requested username `1` already existed as user ID 2, with role `administrator` and active status. A real local API login with the requested password succeeded (HTTP 200), so this is the intended development account. It was verified rather than recreated or reset. Both original users remain; their stored password hashes match the pre-change backup. Bootstrap reports `required=false`. The authenticated administrator can list users (HTTP 200) and receives 71 effective screen permissions. Password hashes and session tokens were not printed.

## Final database checks

After the app was restarted and authenticated, the database still has schema 020 and the expected counts: 15,000 products, 3,000 customers, 1,000 suppliers, 10,000 purchase invoices, 30,000 sales invoices, 1,000 purchase returns, 1,000 sales returns, 47,000 inventory documents, and 97,000 stock movements. `integrity_check` is `ok`; foreign-key violations are 0. Duplicate product names, active SKUs, active barcodes, purchase/sales invoice numbers, purchase/sales/inventory operation keys, and logical stock movements are all 0. Purchase/sales/return totals, customer and supplier ledger projections, and FIFO quantity/value projections have 0 mismatches. The existing users remain two, and the development admin account remains active.

## Live desktop and API verification

The normal WinForms app was started and confirmed responsive with title `الخيال POS`. The live host reported database path `C:\Users\user\AppData\Local\AlKhayal\alkhayal.db`, schema migration count 20, and the expected file size. The desktop was closed through its window and restarted; authenticated API login again succeeded and catalog total remained 15,000.

The final persistence check used the user's exact command from `D:\projects\AlKhayal`: `dotnet run --project .\src\AlKhayal.UI`. No database-path override was supplied. The command was run twice with a clean desktop shutdown between runs. On both runs the authenticated live API selected `C:\Users\user\AppData\Local\AlKhayal\alkhayal.db`; the second run reported 121,712,640 bytes, 20 migrations, and migration 020. Login as username `1` with the requested development password succeeded, the process was responsive, and no new top-level database file was created in `%LOCALAPPDATA%\AlKhayal`.

Authenticated endpoint results: catalog page 200 (15,000 total); cashier product search 200 (230 matches for `LED`); customers 200 (3,000); suppliers 200 (1,000); purchase history 200 (500 returned by the endpoint); sales history 200 (500 returned by the endpoint); warehouses 200 (1); filtered inventory balance 200 (1); inventory movement page 200 (97,000 total); administrator user list 200 (2). The whole-warehouse balance endpoint now returns 14,795 rows in 0.35 seconds after the query optimization.

WinForms startup and responsiveness were exercised, and login/data visibility were verified through the real authenticated local API. Login was not typed into the native WebView2 window; API authentication is the evidence for the credential result.

## Code and validation

The default path resolution already matched the required local path, so no machine-specific path or persistent environment setting was added. No database was relocated, merged, reseeded, or overwritten. The inventory balance SQL now groups FIFO layer totals/statuses once and joins them to balances. A regression assertion covers unresolved and reconciled cost-basis fields.

- `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj --no-restore` — passed, 0 warnings/errors.
- `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~Fifo_migration_preserves_legacy_ledger_and_blocks_unknown_cost_until_documented_opening_basis` — passed.
- `dotnet test AlKhayal.slnx --no-restore` — 49 unit and 112 integration tests passed.

No commit or push was made.
