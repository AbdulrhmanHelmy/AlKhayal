# Phase 2 Products and Inventory Architecture Blueprint

## Scope and architectural decisions

Phase 2 is delivered through the five approved tasks below. Task 2.1 is the only implemented Phase 2 slice described as current in this repository: it persists the product catalog, categories, units, fixed-scale catalog prices, and legacy-review state. Variants/identifiers, tracking, warehouses/inventory, and integration hardening remain planned until Tasks 2.2–2.5 are delivered. The architecture keeps the existing Application-port/Data-adapter/API pattern. Controllers translate authenticated HTTP requests; Application services validate features, permissions, invariants, and commands; one SQLite Data operation owns each write transaction. No UI, controller, or SQL script owns business rules.

Deployment scope for Phase 2 is the standalone Windows application with the local API bound to `127.0.0.1` and SQLite on the same machine. LAN and cloud deployment are outside this phase; the transaction and idempotency design remains suitable for later reuse without claiming that a network deployment exists.

The following decisions are fixed for Phase 2:

1. `Products.Id`, `Products.Name`, and the original `Products.Price` remain in place. A one-to-one `ProductDetails` table supplies the new required catalog fields. This avoids rebuilding a populated SQLite table and preserves every legacy ID and value.
2. Money is a signed 64-bit integer at fixed scale 4 (`value × 10,000`); quantity is a signed 64-bit integer at fixed scale 6 (`value × 1,000,000`). Floating-point values are never authoritative. Display precision remains a presentation setting.
3. In planned Task 2.4, `StockMovements` becomes the immutable stock source of truth. `InventoryBalances` is only a transactionally maintained derived projection: repository APIs expose no arbitrary mutation, triggers reject unsupported updates/deletes, every supported change occurs inside the ledger-posting transaction, and rebuild is an administrator-only, audited, quiesced operation verified against the ledger.
4. Every stock post has a caller-generated operation key and request hash, and commits its business document, ledger rows, serial links, balance changes, and audit event on one open SQLite connection in one transaction.
5. Global `CapabilityConfiguration` says whether a behavior is available. `ProductCapabilityAssignments` says whether an enabled behavior applies to one product. Disabling a global capability prevents new configuration but does not erase assignments or relax tracking of existing stock.

The Task 2.2 form now sends one aggregate command for basic details, exact base-unit mapping, optional conversions, free-text size/color values, capability assignments, and generated variants. The base unit in ProductDetails is authoritative and always has one `1/1` ProductUnits row; Multiple Units is an optional product capability, not a prerequisite for that row. SQLite commits the aggregate and operation key together. Existing variant IDs and identifiers are reconciled in place, and invalid changes roll back.
6. Products receive one default variant even when variants are not enabled. All SKUs, barcodes, batches, serials, balances, and movements therefore target a uniform sellable `ProductVariant` identity without forcing the user to manage variants.
7. Posted stock history is corrected only by a linked reversal and, when needed, a replacement transaction. Posted ledger and audit rows are never updated or deleted.

Dates/times are UTC ISO-8601 text except expiration dates, which are business-calendar dates stored as `YYYY-MM-DD`. IDs are SQLite integer primary keys. User-entered identifiers also store a trimmed, normalized comparison value; Arabic display text is preserved separately.

## Implemented and proposed relational model

Migration 008 implements `ProductCategories`, `Units`, `ProductDetails`, the internal compatibility subset of `ProductVariants`, and `ProductMigrationIssues`. Hotfix migration 009 adds only nullable scale-4 `ProductDetails.PurchasePriceScaled` as a configurable reference/default acquisition cost. It is not an inventory average cost and does not post stock. Every other table in this section is an explicit future schema contract, not a claim of current persistence or UI behavior.

### Catalog

| Table | Important columns and relationships | Purpose |
|---|---|---|
| `ProductCategories` | `Id`, nullable `ParentCategoryId -> ProductCategories`, `Name`, `NormalizedName`, `IsActive`, `ArchivedUtc`, timestamps | Arbitrary category/subcategory tree. The Application service prevents self-parenting and cycles. |
| `Units` | `Id`, `Code`, `Name`, `Symbol`, `QuantityKind`, `IsActive`, `ArchivedUtc`, timestamps | Shared units such as piece, kilogram, and box. `QuantityKind` prevents incompatible conversions. |
| `ProductDetails` | `ProductId PK/FK -> Products`, nullable `CategoryId`, `ProductType`, `BaseUnitId`, `SellPriceScaled`, nullable `PurchasePriceScaled`, `MoneyScale=4`, `IsActive`, nullable `ArchivedUtc`, `MigrationState`, `Version`, timestamps | Required one-to-one extension of the legacy product. Purchase price is an optional reference cost; types are `stock`, `service`, or `non_stock`, and only `stock` will participate in inventory. |
| `ProductUnits` | `Id`, `ProductId`, `UnitId`, positive `ToBaseNumerator`/`ToBaseDenominator`, sale/purchase flags, timestamps | Exact product-specific conversion to its base unit. The base-unit row is always `1/1`. |
| `ProductAttributes` | `Id`, `AttributeKey`, Arabic `Name`, `Kind` (`size`, `color`, `custom`), active state | Definitions available for variants. Registry-backed size/color capabilities gate their use. |
| `ProductAttributeValues` | `Id`, `AttributeId`, `Value`, `NormalizedValue`, optional sort/color metadata, active state | Allowed values such as size XL or a color. |
| `ProductVariants` | `Id`, `ProductId`, `Name`, `NormalizedName`, `IsDefault`, `IsActive`, `ArchivedUtc`, timestamps | Inventory-bearing identity. Non-variant products have exactly one default row. |
| `ProductVariantValues` | `VariantId`, `AttributeValueId`; composite PK | Defines a variant combination. One value per attribute is enforced by the service and a denormalized combination key on the variant. |
| `ProductSkus` | `Id`, `VariantId`, `Sku`, `NormalizedSku`, `IsPrimary`, `RetiredUtc`, timestamps | One or more internal codes; retired values stay reserved. |
| `ProductBarcodes` | `Id`, `VariantId`, nullable `ProductUnitId`, `Barcode`, `NormalizedBarcode`, `BarcodeType`, `IsPrimary`, `RetiredUtc`, timestamps | Multiple global barcodes per variant and optionally per selling unit. |
| `ProductCapabilityAssignments` | `ProductId`, `CapabilityKey`, `IsEnabled`, timestamps, `ChangedByUserId`; composite PK | Per-product applicability. Keys must exist in the in-process feature registry and may be enabled only when globally enabled. |

`ProductVariantValues` combinations are canonicalized by ordered `AttributeId:ValueId` pairs into `ProductVariants.CombinationKey`; the unique product/combination index prevents duplicate size/color combinations. Empty combination is reserved for the default variant.

### Planned tracking identities (Task 2.3)

Task 2.3 defines tracking configuration and identities (`ProductBatches` and `InventorySerials`) plus their validation and uniqueness. It does not create or mutate on-hand quantities. Actual receipt, issue, transfer, adjustment, and count quantities are posted only through the shared ledger introduced by Task 2.4.

### Planned warehousing and tracking relationships (Tasks 2.3–2.4)

| Table | Important columns and relationships | Purpose |
|---|---|---|
| `Warehouses` | `Id`, `Code`, Arabic `Name`, `IsDefault`, `IsActive`, `ArchivedUtc`, timestamps | Stock locations. One active default is required; without `multiple-warehouses`, only that warehouse may be used. |
| `ProductBatches` | `Id`, `VariantId`, `LotNumber`, `NormalizedLotNumber`, nullable `ExpirationDate`, `ManufacturedDate`, timestamps | Lot identity and expiry. Lot/expiry become immutable after the first posted movement. |
| `InventorySerials` | `Id`, `VariantId`, `IdentifierType` (`serial`/`imei`), display and normalized identifier, nullable `BatchId`, `Status`, nullable current warehouse, timestamps | Individual serialized stock. IMEI is exactly 15 digits and passes Luhn validation. Current location/status is a rebuildable projection of movement links. |
| `InventoryTransactions` | `Id`, unique `OperationKey`, `RequestHash`, `TransactionType`, `Status`, optional `ReversesTransactionId`, `OccurredUtc`, `PostedUtc`, `PostedByUserId`, note | Immutable posting header and idempotency boundary. Status is `posted` or `reversed`; drafts live in their source document table. |
| `StockMovements` | `Id`, `TransactionId`, `LineNumber`, `WarehouseId`, `VariantId`, nullable `BatchId`, signed `QuantityBaseScaled`, entered unit/quantity and conversion snapshot, nullable `UnitCostScaled`, `CorrelationLineKey`, timestamp | Immutable signed base-unit ledger. Positive adds stock and negative removes it. Conversion snapshots make history independent of later catalog edits. |
| `StockMovementSerials` | `MovementId`, `SerialId`; composite PK | Connects each serial/IMEI to an inbound or outbound movement. |
| `InventoryBalances` | `Id`, `WarehouseId`, `VariantId`, nullable `BatchId`, `QuantityBaseScaled`, `Version`, `UpdatedUtc`; partial unique keys `(WarehouseId, VariantId)` for unbatched rows and `(WarehouseId, VariantId, BatchId)` for batched rows | Derived balance projection at warehouse/variant/batch granularity. It is never a write API or independent source of truth; serial balances are additionally verified from serial state. |
| `InventoryTransactionAudit` | `Id`, `TransactionId`, `EventType`, `ActorUserId`, `EventUtc`, `Details`, `PreviousEventHash`, `EventHash` | Append-only lifecycle evidence. Hash chaining detects accidental history modification; it is not presented as external cryptographic attestation. |
| `InventoryPolicy` | singleton `Id=1`, `AllowNegativeStock=0`, updated user/time | Explicit global stock policy. Phase 2 defaults to rejecting negative stock. |

### Inventory documents

Each document header has `Id`, human-readable document number, `Status` (`draft`, `posted`, `cancelled`), timestamps/users, `RowVersion`, and nullable unique `PostedTransactionId -> InventoryTransactions`. Posted/cancelled documents are read-only.

| Header and lines | Line content and posting behavior |
|---|---|
| `OpeningStockDocuments`, `OpeningStockLines` | Warehouse, variant, optional batch/expiry, unit and quantity, optional unit cost; posts positive movements. One variant/batch/warehouse opening line is allowed per document, but later corrections use adjustments rather than editing the opening. |
| `StockAdjustments`, `StockAdjustmentLines` | Warehouse, variant, optional batch, signed counted difference, mandatory reason code and note; posts signed movements. |
| `InventoryCounts`, `InventoryCountLines` | Warehouse and scope, snapshot time; each line stores expected and counted quantities plus the resulting signed difference. Posting rechecks the current balance and rejects a stale count unless explicitly restarted. |
| `WarehouseTransfers`, `WarehouseTransferLines` | Distinct source/destination warehouses, variant, optional batch, unit and quantity; posting creates paired negative/positive movements with the same correlation key. Phase 2 transfers are atomic, with no in-transit state. |

Serial IDs required by a document line are stored in corresponding line-serial join tables. Batch selection is stored directly on each line. This permits validation before posting and avoids polymorphic foreign keys.

## Constraints, indexes, and lifecycle rules

- Every Data connection executes `PRAGMA foreign_keys=ON`. Foreign keys to posted/history data use `RESTRICT`; cascade is limited to unposted child/value rows whose parent is deleted in the same draft operation.
- Required strings are trimmed, length-bounded in Application, and protected by `NOT NULL` plus `CHECK(length(trim(...)) > 0)`. Boolean integers are checked as `0/1`; enum text has explicit checks.
- Unique normalized indexes cover category siblings, unit code, SKU, barcode, warehouse code, product/variant combination, variant/lot, document number, and operation key. Case-insensitive normalization is invariant uppercase plus trimming for Latin codes; barcode/IMEI normalization removes only explicitly allowed separators.
- SQLite treats `NULL` values as distinct in unique indexes. `InventoryBalances` therefore uses two partial unique indexes: `(WarehouseId, VariantId)` where `BatchId IS NULL`, and `(WarehouseId, VariantId, BatchId)` where it is not. The same partial-index approach controls primary SKU/barcode/default warehouse/default variant rows.
- At least one active warehouse and one default variant per product are transaction-level invariants enforced by Application services. A default warehouse or variant cannot be archived until another is selected in the same transaction.
- Products, categories, units, variants, warehouses, SKUs, and barcodes are archived/retired, not hard-deleted after reference. Posted documents, movements, serial identifiers, batch identities, and audit records are never deleted. Drafts may be deleted only when unreferenced.
- A retired barcode remains in its table under the global unique normalized index and is never reused. This prevents old labels from resolving to a different product. SKU values follow the same policy.
- Serial/IMEI normalized identifiers are globally unique across both identifier types so a scan is unambiguous. A serial-tracked movement's absolute base quantity must be a whole number and equal its distinct serial-link count. A serial cannot be outbound from a warehouse/status it does not currently occupy.
- A batch is mandatory when either `batch-tracking` or `expiry-dates` applies to the product. Expiration is mandatory when `expiry-dates` applies. Expired stock is visible but cannot be sold or transferred; explicit disposal, return, reversal, and adjustment operations remain allowed with permission and reason.
- Expiration occurs at the end of the stored business date in configured local business time. No UTC conversion is applied to the date itself. FEFO is the default picking order for expiring stock.
- A product capability cannot be newly assigned unless its global capability is effective. Global disablement hides setup and blocks new assignments, but existing assignments and their validation remain effective until all dependent stock/history is safely resolved and the assignment is explicitly removed.
- Products with inventory history cannot change base unit or tracking mode. A conversion may be deactivated but not edited once used; a replacement conversion row is created instead.
- Database triggers reject `UPDATE`/`DELETE` on `StockMovements` and `InventoryTransactionAudit`, and reject deletion of posted `InventoryTransactions`. Application repositories expose no such methods.

Indexes required for operational paths include movement `(VariantId, WarehouseId, PostedUtc)`, movement `(TransactionId, LineNumber) UNIQUE`, batch `(VariantId, ExpirationDate)`, serial normalized identifier, balance warehouse/variant, document status/date, audit transaction/time, and transaction reverse link. Indexes are added with the slice that introduces the query.

## Monetary, quantity, and conversion policy

- `MoneyScale` is permanently 4. C# converts `decimal` using `decimal.Round(value * 10000m, 0, MidpointRounding.AwayFromZero)`, range-checks, then stores `long`. API amounts are decimal JSON strings, not binary JSON numbers; responses include scale where a raw scaled value is exposed.
- `QuantityScale` is permanently 6. C# applies the same away-from-zero rule at six places and rejects a non-zero input that rounds to zero. Absolute stored quantities and intermediate products must fit signed 64-bit integers.
- Unit conversions are reduced positive rational numbers: `base quantity = entered quantity × numerator / denominator`. Application code uses checked `decimal`/integer arithmetic, requires an exact result at quantity scale, and rejects rather than silently rounds a conversion remainder.
- `ProductDetails.SellPriceScaled` is the catalog sale price. Historical movement cost is copied into `StockMovements.UnitCostScaled`; later price changes cannot alter history. Taxes and invoice totals remain outside this phase, but will use the same scale and rounding rule.
- `ApplicationSettings.DecimalPrecision` controls display only and never changes stored scale or historical amounts.

## Ledger and transaction rules

The Application command validates authentication, effective feature state, screen permission, document state, normalized operation key, and request payload. The Data store then opens one `SQLiteConnection`, enables foreign keys, sets a bounded busy timeout, and starts an immediate write transaction (`BEGIN IMMEDIATE`, using the verified equivalent supported by `System.Data.SQLite`). SQLite therefore serializes stock writers before they read balances.

Within that transaction the store:

1. Inserts `InventoryTransactions(OperationKey, RequestHash, ...)`. A unique-key conflict loads the existing transaction: an equal hash returns the original successful result; a different hash returns HTTP 409 `operation_key_conflict`.
2. Revalidates document version/status, feature-dependent tracking data, serial state, batches, conversions, and current balances.
3. Inserts uniquely numbered `StockMovements` and serial links. Unique `(TransactionId, LineNumber)`, one posted transaction per document, and the operation key jointly prevent duplicates.
4. Upserts each `InventoryBalances` row with checked integer arithmetic. When negative stock is disabled, the update condition requires the resulting quantity to be non-negative; affecting zero rows is a conflict and rolls back everything.
5. Updates rebuildable serial location/status, marks the source document posted, appends the audit event, and commits. Any error rolls back the complete operation.

Busy/locked errors receive a short bounded retry with jitter at the Application boundary only when no commit result is known. Retrying always uses the same operation key. This is required for local robustness and future reuse; no LAN deployment is implemented or assumed.

A reversal is a new transaction with its own operation key and `ReversesTransactionId`, exactly negated movement lines, and serial transitions validated against current state. A transaction can be reversed once. A correction posts a reversal and a replacement as separately auditable operations; no original row changes. Transfers post paired movements atomically. Physical counts post only their difference after a current-balance recheck. Opening balances use the same ledger and are not direct balance inserts.

A scheduled/manual integrity check compares `InventoryBalances` and serial projections with a ledger aggregation and reports discrepancies without repairing silently. Application ports expose balance queries and ledger posting, never a generic balance setter. Triggers reject direct update/delete except through a transaction-scoped maintenance guard owned by the Data rebuild routine. An administrator-only rebuild requires a verified backup, quiesces writes, records before/after hashes and counts in the audit log, replaces only the derived projection in one transaction, and fails closed if the rebuilt totals do not equal the ledger.

## Safe migration strategy (008 onward)

Migrations 001–007 are immutable. All Phase 2 migrations are additive, embedded, lexically ordered, and recorded by the existing `SchemaVersion` mechanism in the same transaction as their SQL.

Migration 008 creates the catalog foundation and `ProductDetails`; it does not rename, drop, or rebuild `Products`. It inserts a stable default piece unit, then creates one detail row and one default variant for every legacy product while retaining the same product ID. The original `Products.Price` remains untouched as recovery evidence and is no longer authoritative after successful upgrade.

Legacy price conversion uses fixed scale 4 and round-half-away-from-zero. Integer/real values and strictly valid invariant decimal text within the `long` scaled range become `SellPriceScaled`. Currency symbols, grouping separators, blobs, non-finite/out-of-range values, empty names, and other invalid data are never guessed: the product is preserved, set inactive with `MigrationState='review_required'`, assigned a zero placeholder price, and recorded in `ProductMigrationIssues` with a non-secret reason and original SQLite storage type/text representation. The UI requires administrator correction before activation. Fresh databases pass through migration 001 then 008 and receive the same defaults.

Before deployment, the operator quiesces the host and takes a verified copy of the SQLite database plus its WAL/SHM state using the SQLite backup API or a clean checkpointed copy. Each migration is atomic; a failure leaves its `SchemaVersion` row absent. There are no down migrations. Recovery after a committed migration is restore-from-backup plus the previous application version. Running an older binary against a Phase 2 database is unsupported because it can create legacy-only rows; Phase 2 startup detects any `Products` row lacking `ProductDetails`, quarantines it through the same reconciliation policy, and records an issue.

Hotfix migration 009 adds nullable `PurchasePriceScaled` and deliberately leaves all existing values unknown (`NULL`). Later slices therefore start at 010 and remain additive. If a future constraint genuinely requires a table rebuild, it is deferred to its own migration with preflight validation, copy/count checks, foreign-key verification, and atomic rename; none is currently required.

## Feature flags, permissions, and API boundary

The existing `product-catalog` and `basic-inventory` core modules gate their respective APIs/screens. `multiple-warehouses` gates creating a second warehouse and transfer screens. Existing capabilities gate only product-level opt-in: `product-variants`, `sizes`, `colors`, `multiple-units`, `weight-based-products`, `serial-numbers`, `imei-tracking`, `batch-tracking`, `expiry-dates`, `product-barcode-settings`, `barcode-scanning`, and `inventory`. Dependencies continue to come from `FeatureRegistry`; Phase 2 adds no second feature registry.

`screens.json` remains the screen registry. Existing routes such as `products-list`, `product-add`, `unit-add`, `warehouse-add`, `stock-adjustment`, `warehouse-transfer`, and `stock-movements` are converted from mock to API-backed slices without duplicating Settings navigation. Product capability administration remains administrator-only. Operational reads/writes require authentication and a server-side check of the matching effective screen permission; hiding a route is not authorization. Mutations require CSRF and return the standard envelope. Conflict cases use HTTP 409 with stable codes such as `operation_key_conflict`, `insufficient_stock`, `stale_document`, and `serial_state_conflict`.

List APIs are paged and filterable; mutations accept command DTOs and never expose Data models. Posting endpoints require `operationKey`. Arabic validation messages are returned safely, while stable English error codes drive UI behavior. UI pages remain RTL, keyboard reachable, Chromium 109 compatible, and replace mock notices only after their corresponding persistence slice is complete.

## Approved five-task implementation plan

Each task is independently deployable and must not make a later screen appear persisted early.

| Order | Slice and migration | Full-stack work and acceptance criteria |
|---|---|---|
| 2.1 | Product Catalog Foundation — migrations 008–009 | Implement units, nested categories, `ProductDetails`, hidden default variants, migration issues, exact legacy sale-price conversion, nullable reference purchase cost, catalog Application/Data/API flows, authenticated and permission-checked Arabic RTL list/add/edit/category/unit screens, contextual catalog actions, and a no-data Warehouse Directory placeholder. No stock quantity, SKU, barcode, average cost, or tracking persistence is introduced. |
| 2.2 | Product Variants & Identifiers — migration 010 | Add sizes, colors, variant combinations, SKUs, multiple barcodes, scanner lookup, product units/conversions, variant pricing where justified, and per-product capability assignments. A simple product keeps its hidden default variant and requires no variant setup. |
| 2.3 | Product Tracking — migration 011 | Add batch/lot identities, manufacturing batch identifiers, expiration dates, serial numbers, IMEI, per-product tracking configuration, uniqueness, and lifecycle validation. This task defines identities/configuration only; quantity posting waits for the shared Task 2.4 ledger. |
| 2.4 | Warehouses & Inventory Operations — migrations 012 onward as needed | Replace the Warehouse Directory placeholder with manual warehouse creation/editing and default warehouse, opening balances, manual stock additions independent of purchasing, manual reductions, adjustments, physical counts, warehouse transfers, immutable movement history, ledger-backed balances, idempotent posting, audit history, and serial/IMEI plus batch/expiry validation. Every operation uses the same Inventory Ledger. Future purchase invoices call that ledger service with a distinct source type; they never mutate balances directly or through a second balance path. |
| 2.5 | Integration & Stabilization — additive hardening migration only if required | Complete regression, ledger integrity/rebuild, concurrency/idempotency, permission/feature/tracking consistency, migration, performance, desktop, and failure-injection verification. This task does not expand business scope or introduce a second persistence path. |

Dependencies are strictly sequential: Task 2.2 extends Task 2.1 identities; Task 2.3 defines tracking identities/configuration; Task 2.4 establishes the only quantity-posting path and connects those identities to ledger movements; Task 2.5 hardens the integrated result. Sales and purchasing may later call the same posting port but are not implemented by Phase 2.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Dynamic legacy `Price` values cannot all be interpreted safely | Preserve the original column, convert only strict values, quarantine and report the rest, and require explicit administrator correction. |
| Fixed-scale overflow or conversion drift | Checked integer/decimal arithmetic, rational conversions, fixed rounding, range tests, and conversion snapshots on ledger rows. |
| SQLite writer contention on the standalone host | Use immediate short transactions, bounded busy retry, paging, and no work/UI calls inside a transaction. |
| Balance projection diverges from ledger | Atomic updates, immutable ledger, integrity checks, and an audited quiesced rebuild path. |
| Feature disablement strands tracked stock | Never delete assignments/data or relax existing tracking; disable only new setup until dependencies are resolved explicitly. |
| Variant/batch/serial complexity burdens simple businesses | Default hidden variant, base `1/1` unit, optional per-product capabilities, and progressive UI disclosure. |
| Old clients bypass new invariants | Same-origin deployment is versioned as one unit with no-store/cache clearing; reject unsupported API payload versions and do not support old-binary writes after upgrade. |
| A posting outcome is unknown after connection loss | Same operation key/request hash safely resolves retries to the committed result or a clear conflict. |

## Phase 2 Definition of Done

Phase 2 is complete only when all five tasks meet their acceptance criteria and:

- All migrations introduced by Tasks 2.1–2.4 apply on a fresh database and representative 001–007 databases, preserve every legacy product ID/record, and pass foreign-key/integrity checks; migration recovery is exercised from a verified backup.
- Catalog, units, variants, identifiers, warehouses, opening stock, batches/expiry, serials/IMEI, adjustments, counts, transfers, reversals, and audit queries are real Application/API/Data flows with no direct frontend persistence or duplicate business logic.
- All stock-affecting operations are idempotent, atomic, permission checked, ledger backed, projection verified, and correct under concurrent server requests; posted history cannot be silently changed.
- Global features and per-product applicability behave independently and safely, including disablement with existing data.
- Relevant unit, repository, migration-upgrade, API integration, concurrency, failure-injection, frontend registry/behavior, and accessibility tests pass; no unexecuted test is reported as passed.
- Arabic RTL pages have loading, empty, error, conflict, and success states; long Arabic text, keyboard use, the shared viewport contract, Chromium 109 syntax, browser use, and WinForms/WebView2 use are manually verified.
- Architecture, database, API-contract, feature-management, design, and testing documentation distinguish delivered behavior from future contracts. Phase 2 remains standalone/local-only, and no Windows 7 compatibility claim is made without the required legacy runtime testing.

No unresolved architecture blocker is known. The fixed storage scales, default `AllowNegativeStock=0` rejection policy, line-level checked monetary multiplication followed by one round-half-away-from-zero conversion to scale 4, atomic transfer model, derived-balance protection, barcode non-reuse rule, and legacy-price quarantine policy are decisions of this blueprint rather than deferred choices. Taxes are calculated from unrounded decimal line bases at the tax boundary, rounded once to scale 4 per documented tax component, and summed without further intermediate rounding; invoice-level reconciliation rules remain for the future sales phase.
# Implementation update — Task 2.4

This blueprint predates the implemented warehouse ledger. Its tables and workflows marked *planned* are design history. Migration 011 and the actual service/API/UI now implement the scope described in [inventory-ledger.md](inventory-ledger.md) and [warehouse-management.md](warehouse-management.md). Where the blueprint differs, those implementation documents and source code are authoritative. In particular, one generic `InventoryDocuments` header/line pair distinguishes source types, rather than a separate physical table for every source type.

