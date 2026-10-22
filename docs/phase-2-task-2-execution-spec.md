# Phase 2 / Task 2.2 — Product Variants and Identifiers Execution Specification

## 1. Authority, repository baseline, and scope

This document is the authoritative implementation contract for Phase 2 / Task 2.2. The implementation agent must follow it mechanically and must not invent alternative schemas, routes, business rules, or UI patterns.

Repository facts verified before this specification was written:

- The applied migration sequence ends at `009_product_purchase_price.sql`; Task 2.2 therefore uses additive migration **010**.
- Migration 008 created `ProductDetails`, the compatibility form of `ProductVariants`, categories, units, migration issues, and `CatalogOperations`. Migration 009 added nullable `ProductDetails.PurchasePriceScaled`.
- Every existing product has one `ProductVariants` row with `IsDefault=1`; `CatalogLegacyBackfill` also creates that row for late legacy products.
- `ProductDetails.Version` is the catalog aggregate concurrency token. Money is stored as signed 64-bit scale-4 integers and sent as invariant decimal strings.
- `CatalogService` is the Application owner, `ICatalogStore` is the port, `SqliteCatalogStore` is the Data adapter, and `CatalogController` is the HTTP adapter.
- The real frontend is `frontend/js/modules/catalog.js`. Its add/edit forms already contain the approved basic fields and a `futureDetails()` placeholder. That placeholder, not the surrounding design, is the Task 2.2 integration seam.
- `FeatureRegistry` already contains all relevant global capabilities. Its current `product-variants -> sizes + colors` dependency is too restrictive for size-only, color-only, or custom-attribute products and must be changed as specified below.
- The catalog endpoints already enforce authenticated cookies, server-side cashier screen permissions, and CSRF on mutations. Task 2.2 must preserve those conventions.

Task 2.2 includes global attribute definitions and values, product attribute selections, variant combinations, the hidden default variant compatibility path, one active SKU per variant with permanent identifier history, multiple barcodes, scanner lookup, product-specific units and exact rational conversions, variant price/reference-cost overrides, per-product capability assignments, UI integration, audit evidence, and focused tests.

Task 2.2 does **not** create stock balances, stock quantities, reorder levels, warehouses, movements, average cost, purchase documents, batches, expiry records, serials, IMEI records, or posting logic. No Task 2.2 response or screen may imply that any of those datasets exists.

## 2. Fixed architectural and business decisions

### 2.1 Ownership and aggregate boundary

- Attribute definitions are **global catalog metadata**. `size` and `color` are seeded system attributes; administrators may add global `custom` attributes. Attribute values are global children of an attribute. Products select a subset of those global values; values are not copied into product-owned text fields.
- A product, its capability assignments, selected attribute values, units, variants, SKUs, and barcodes form one optimistic-concurrency aggregate governed by `ProductDetails.Version`.
- Global attributes and attribute values use their own `Version` columns because they can be edited independently of products.
- Business validation belongs in `CatalogService` and pure Application rule helpers. Data repeats relational/integrity checks inside the write transaction. Controllers do not implement catalog rules.
- One SQLite connection and one transaction must persist each aggregate create/update, its identifier retirements, the version increment, and its audit rows. A partial product/configuration save is forbidden.

### 2.2 Attributes and combination generation

- The seeded system attributes are `SIZE` (`kind=size`, Arabic name `المقاس`) and `COLOR` (`kind=color`, Arabic name `اللون`). Their IDs are fixed at 1 and 2.
- A custom attribute uses a caller-supplied ASCII key matching `^[A-Z][A-Z0-9_-]{1,31}$` after invariant uppercase normalization. Keys are globally unique and never reused. System keys/kinds cannot be changed or deactivated.
- Attribute names are trimmed, 1–80 characters. Values are trimmed, 1–80 characters. Normalization is Unicode Form KC, trimmed, and invariant uppercase. Duplicate normalized values within one attribute are forbidden, including inactive values.
- `ColorHex`, when present, is exactly `#RRGGBB` uppercase and is allowed only for the color attribute. It is display metadata, never a key.
- A variant-enabled product has 1–3 assigned attributes and at least one selected active value per assigned attribute. At most 50 values may be selected for one attribute and at most 200 active combinations may result for one product.
- The UI generates the Cartesian product in attribute `SortOrder, AttributeId` order and value `SortOrder, AttributeValueId` order. It displays every generated combination, then permits the administrator to deactivate unwanted combinations before save. At least one active combination is required.
- The canonical `CombinationKey` is the pipe-joined list of invariant decimal `AttributeId:AttributeValueId` pairs sorted by `AttributeId`, for example `1:7|2:12`. The server always recomputes it and ignores any client-supplied key.
- Two unarchived variants of one product may not have the same combination. Duplicate combinations return `duplicate_combination`; no exception is made for inactive variants.
- An active variant must have exactly one value for every product-assigned attribute, no value from an unassigned attribute, and only product-selected values. The database composite keys reinforce these relationships.

### 2.3 Hidden default variant and transitions

- Every product always has exactly one `IsDefault=1` variant. `IsDefault` means canonical/default sellable identity; hidden versus visible is derived from `ProductDetails.VariantMode`.
- A simple product has `VariantMode=0`. Its sole unarchived default variant has `CombinationKey=NULL`, has no `ProductVariantValues`, is hidden in ordinary UI, and receives the simple product's SKU and barcodes.
- Existing products upgrade without changing `Products.Id`, `ProductDetails.ProductId`, or the existing default `ProductVariants.Id`. Migration 010 only extends that row.
- Enabling variants requires an active `product-variants` product assignment, 1–3 valid axes, and an explicit default combination. The existing hidden default variant is converted in place to that default combination: update its name, combination key, values, optional overrides, SKU, and barcodes. This preserves its ID and every future reference. All other combinations receive new IDs.
- On a create request that starts in variant mode, the store first creates the compatibility default row exactly as Task 2.1 does, then converts it to the requested default combination in the same transaction.
- Once any non-default variant row has ever been created, variant mode is irreversible. Removing the `product-variants` assignment or setting `VariantMode=false` returns `variant_mode_locked`. This avoids merging identifiers or future inventory history into a different identity. A setup may be cancelled back to simple mode only before a non-default variant has been persisted and only after the default row has no variant values.
- Changing the selected axes/values does not silently delete variants. Every now-invalid unarchived variant must be explicitly archived in the same request or the request fails with `variant_set_incomplete`.
- Variant deactivation is reversible and sets `IsActive=0`. Archival is explicit, irreversible, sets `IsActive=0` and `ArchivedUtc`, and preserves all IDs, values, SKUs, and barcodes. The default variant cannot be archived until another unarchived variant is made default in the same transaction. At least one unarchived variant must remain.
- Archiving a variant does not retire its identifiers. Lookup continues to identify the historical product/variant but returns `isSellable=false` and a reason. Retirement is a separate explicit action.

### 2.4 SKU policy

- A variant has zero or one active SKU. `ProductSkus` stores history rather than placing a mutable string on `ProductVariants`.
- SKU input is Unicode Form KC, trimmed, invariant-uppercase for comparison, 1–64 characters, and may contain no whitespace or Unicode control character. Display text preserves the trimmed Form-KC spelling.
- `NormalizedSku` is globally unique across active and retired rows. Retired SKUs are never deleted, reused, moved to another variant, or reactivated.
- Replacing or clearing a SKU retires the active row (`RetiredUtc`, `RetiredByUserId`) and, for replacement, inserts a new row. The complete operation is atomic and audited.
- Simple-product SKU edits target the hidden default variant. List search matches active SKU in addition to product name/description.

### 2.5 Barcode and scanner policy

- A variant may have multiple barcodes. Every barcode points to both a variant and one `ProductUnits` row of the same product, so a scan resolves the sellable identity and entered unit without guessing.
- Barcode input is Unicode Form KC, trimmed, invariant-uppercase for comparison, 1–64 characters, and may contain no whitespace or Unicode control character. No separator is stripped. Thus `123-45` and `12345` are distinct; scanners must send the stored code exactly. This avoids unsafe normalization collisions.
- `BarcodeType` is one of `gtin`, `internal`, or `supplier`. Task 2.2 does not parse variable-weight/price barcodes.
- `NormalizedBarcode` is globally unique across active and retired rows. Retirement is irreversible; a retired code can never be reused or reactivated, even for the same variant. This prevents an old physical label from resolving to a new identity.
- At most one active primary barcode exists per `(VariantId, ProductUnitId)`. When the first barcode is added to that pair it becomes primary. Retiring a primary promotes the oldest active barcode for the same pair; if none remains there is no primary.
- Lookup is exact after normalization. It never falls back to prefix matching, product-name search, SKU search, or numeric coercion.
- Lookup of an active code returns HTTP 200 even if the product, variant, or unit is inactive/archived; the response has `isSellable=false` plus one of `product_inactive`, `product_archived`, `variant_inactive`, `variant_archived`, or `unit_inactive`. A retired code returns HTTP 410 with `barcode_retired`; an unknown code returns HTTP 404 `barcode_not_found`.
- Scanner lookup is catalog identity resolution only. It returns no stock quantity, warehouse, availability, or average cost.

### 2.6 Product units and exact conversions

- Every product has one active base `ProductUnits` row whose `UnitId` equals `ProductDetails.BaseUnitId`, `IsBase=1`, and ratio is `1/1`.
- Other product units are product-specific mappings to a global `Units` row. The exact rule is: `base quantity = entered quantity × ToBaseNumerator / ToBaseDenominator`.
- Numerator and denominator are positive 64-bit integers, each at most 1,000,000,000, and must be reduced (`gcd=1`). Floating-point conversion fields are forbidden.
- At most one active row exists for `(ProductId, UnitId)`. A row declares at least one of `IsSaleUnit` or `IsPurchaseUnit` true. The base row declares both true.
- A conversion is locked when `FirstUsedUtc` is non-null **or any active/retired barcode has ever referenced it**. Locked rows may be deactivated but their `UnitId`, ratio, and flags may not be edited. A changed conversion is represented by deactivating the old row and creating a new row.
- Task 2.2 never sets `FirstUsedUtc`; Task 2.4 will set it in the same transaction as the first posted reference and will snapshot the ratio on the movement.
- The base unit may change only when no product unit has `FirstUsedUtc`, no barcode row exists for the product, and no future stock/document reference exists. Task 2.2 checks the first two conditions; the port is deliberately named so Task 2.4 can extend it. Otherwise return `base_unit_locked`.
- Conversion arithmetic for future quantity work uses quantity scale 6 and checked integer/decimal math. A converted scaled quantity must divide exactly; it is rejected rather than rounded. Task 2.2 validates ratios but stores no quantities.

### 2.7 Price and reference-cost inheritance

- `ProductDetails.SellPriceScaled` and nullable `PurchasePriceScaled` remain the product defaults in base-unit terms.
- `ProductVariants.SellPriceOverrideScaled` and `PurchasePriceOverrideScaled` are nullable. Effective variant values are `override ?? product default`. A null effective purchase price remains “unknown”; zero is a real explicit value.
- Overrides use the existing scale-4, non-negative, round-half-away-from-zero parsing and invariant four-decimal API strings. They are not inventory average cost.
- Changing a product default immediately changes effective values of inheriting variants; explicit overrides do not change.
- A simple hidden default variant may not carry price overrides; its effective prices are always the product defaults.
- Alternate-unit display may show `effective base-unit price × numerator / denominator`, rounded once to scale 4 away from zero. Task 2.2 does not persist per-unit prices and does not post sales.

### 2.8 Product capabilities and global feature state

The Task 2.2 assignable product-capability allowlist is:

`product-variants`, `sizes`, `colors`, `multiple-units`, `weight-based-products`, `product-barcode-settings`, `barcode-labels`, `wholesale-pricing`, `inventory`, and `sales`.

Tracking keys (`serial-numbers`, `imei-tracking`, `batch-tracking`, `expiry-dates`, `warranty-tracking`) remain reserved for Task 2.3. Task 2.2 APIs reject assigning them with `capability_not_supported`; their existing global flags remain unchanged.

- A capability can be newly assigned only when the matching global capability is effective. Otherwise return `capability_disabled`.
- `product-variants` is required for `VariantMode=true` and any product attribute selection. `sizes` is additionally required for the size axis; `colors` for the color axis. Custom axes require only `product-variants`.
- `multiple-units` is required for any active non-base product unit.
- `product-barcode-settings` is required to add or replace barcodes. Existing barcodes remain readable and may be retired while it is globally disabled.
- `barcode-scanning` is a global operational gate for lookup; it is not a product assignment.
- `weight-based-products` does not imply multiple units; a product whose base unit is a weight unit may use it alone. Task 2.2 does not add scale-barcode parsing.
- `inventory` and `sales` assignments are applicability metadata only. They do not create stock or make the existing mock POS persistent.
- Change `FeatureRegistry` so `product-variants` has no hard global dependencies. Do **not** make it depend on both `sizes` and `colors`. Product-level validation enforces the appropriate axis capability.
- If an administrator disables a global capability after products use it, assignments and data are retained. Existing behavior/invariants remain effective for those products. The UI shows the assignment as “مستخدم — الميزة العامة معطلة”, allows only safe retirement/deactivation/removal, and blocks additions or expansion. Global disablement must not cascade-delete or auto-disable product assignments.
- Removing an assignment is allowed while its global feature is disabled only after dependent configuration has been removed safely. Violations return `capability_in_use`.

### 2.9 Authentication, permission, CSRF, audit, and concurrency

- Every endpoint requires the existing HttpOnly session cookie.
- Product reads require any existing product route permission accepted by `CatalogController`. Basic product create/update retains current cashier permissions.
- Any request that changes capability assignments, axes, variants, SKUs, barcodes, or product units is administrator-only. Global attribute/value writes are administrator-only. This is the existing blueprint rule that product capability administration is administrator-only.
- Scanner lookup permits an administrator or a cashier with any effective permission among `pos`, `sales-management`, `products-list`, or `product-edit`.
- Every mutation requires `X-CSRF-Token`, including retire/deactivate operations.
- The server checks effective feature state; frontend hiding is never authorization.
- Aggregate updates require the current `ProductDetails.Version`. The transaction updates with `WHERE Version=@Version`; zero rows return HTTP 409 `version_conflict`. One successful aggregate update increments the product version exactly once regardless of child-row count.
- Attribute/value updates require their own current version and return the same conflict code.
- Product create remains operation-key idempotent. Its canonical request hash must include the complete normalized Task 2.2 configuration sorted by stable IDs/keys. Same key/same canonical request returns the original complete product; same key/different configuration returns `operation_key_conflict`.
- Every Task 2.2 mutation writes `CatalogAuditLog` in the same transaction. Audit JSON contains stable IDs, action, previous/new non-secret values, and reason; it never contains session tokens or credentials. Audit rows are append-only through database triggers.

## 3. Migration 010 schema contract

Create `src/AlKhayal.Data/Migrations/010_product_variants_and_identifiers.sql`. Do not edit migrations 001–009. The SQL below is the required schema; formatting may change but names, types, constraints, indexes, seeds, and backfill semantics may not.

```sql
ALTER TABLE ProductDetails
ADD COLUMN VariantMode INTEGER NOT NULL DEFAULT 0 CHECK(VariantMode IN (0, 1));

ALTER TABLE ProductVariants
ADD COLUMN CombinationKey TEXT NULL CHECK(CombinationKey IS NULL OR length(CombinationKey) BETWEEN 3 AND 1000);
ALTER TABLE ProductVariants
ADD COLUMN SellPriceOverrideScaled INTEGER NULL CHECK(SellPriceOverrideScaled IS NULL OR SellPriceOverrideScaled >= 0);
ALTER TABLE ProductVariants
ADD COLUMN PurchasePriceOverrideScaled INTEGER NULL CHECK(PurchasePriceOverrideScaled IS NULL OR PurchasePriceOverrideScaled >= 0);
ALTER TABLE ProductVariants
ADD COLUMN ArchivedUtc TEXT NULL;
ALTER TABLE ProductVariants
ADD COLUMN Version INTEGER NOT NULL DEFAULT 1 CHECK(Version >= 1);

CREATE UNIQUE INDEX UX_ProductVariants_IdProduct ON ProductVariants(Id, ProductId);
CREATE UNIQUE INDEX UX_ProductVariants_Combination
ON ProductVariants(ProductId, CombinationKey)
WHERE CombinationKey IS NOT NULL AND ArchivedUtc IS NULL;
CREATE INDEX IX_ProductVariants_ProductState
ON ProductVariants(ProductId, ArchivedUtc, IsActive, IsDefault);

CREATE TABLE ProductAttributes (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    AttributeKey  TEXT NOT NULL CHECK(length(AttributeKey) BETWEEN 2 AND 32),
    NormalizedKey TEXT NOT NULL CHECK(length(NormalizedKey) BETWEEN 2 AND 32),
    Name          TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 80),
    Kind          TEXT NOT NULL CHECK(Kind IN ('size', 'color', 'custom')),
    IsSystem      INTEGER NOT NULL DEFAULT 0 CHECK(IsSystem IN (0, 1)),
    IsActive      INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0, 1)),
    SortOrder     INTEGER NOT NULL DEFAULT 0,
    Version       INTEGER NOT NULL DEFAULT 1 CHECK(Version >= 1),
    CreatedUtc    TEXT NOT NULL,
    UpdatedUtc    TEXT NOT NULL
);
CREATE UNIQUE INDEX UX_ProductAttributes_Key ON ProductAttributes(NormalizedKey);
CREATE INDEX IX_ProductAttributes_KindSort ON ProductAttributes(Kind, IsActive, SortOrder, Name);

INSERT INTO ProductAttributes
    (Id, AttributeKey, NormalizedKey, Name, Kind, IsSystem, IsActive, SortOrder, Version, CreatedUtc, UpdatedUtc)
VALUES
    (1, 'SIZE', 'SIZE', 'المقاس', 'size', 1, 1, 10, 1, strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    (2, 'COLOR', 'COLOR', 'اللون', 'color', 1, 1, 20, 1, strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now'));

CREATE TABLE ProductAttributeValues (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    AttributeId     INTEGER NOT NULL REFERENCES ProductAttributes(Id) ON DELETE RESTRICT,
    Value           TEXT NOT NULL CHECK(length(trim(Value)) BETWEEN 1 AND 80),
    NormalizedValue TEXT NOT NULL CHECK(length(NormalizedValue) BETWEEN 1 AND 80),
    ColorHex        TEXT NULL CHECK(ColorHex IS NULL OR (length(ColorHex)=7 AND substr(ColorHex,1,1)='#')),
    SortOrder       INTEGER NOT NULL DEFAULT 0,
    IsActive        INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0, 1)),
    Version         INTEGER NOT NULL DEFAULT 1 CHECK(Version >= 1),
    CreatedUtc      TEXT NOT NULL,
    UpdatedUtc      TEXT NOT NULL
);
CREATE UNIQUE INDEX UX_ProductAttributeValues_Name ON ProductAttributeValues(AttributeId, NormalizedValue);
CREATE UNIQUE INDEX UX_ProductAttributeValues_IdAttribute ON ProductAttributeValues(Id, AttributeId);
CREATE INDEX IX_ProductAttributeValues_AttributeSort ON ProductAttributeValues(AttributeId, IsActive, SortOrder, Value);

CREATE TABLE ProductAttributeAssignments (
    ProductId  INTEGER NOT NULL REFERENCES ProductDetails(ProductId) ON DELETE RESTRICT,
    AttributeId INTEGER NOT NULL REFERENCES ProductAttributes(Id) ON DELETE RESTRICT,
    SortOrder  INTEGER NOT NULL DEFAULT 0,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    PRIMARY KEY(ProductId, AttributeId)
);

CREATE TABLE ProductAttributeValueAssignments (
    ProductId        INTEGER NOT NULL,
    AttributeId      INTEGER NOT NULL,
    AttributeValueId INTEGER NOT NULL,
    SortOrder        INTEGER NOT NULL DEFAULT 0,
    CreatedUtc       TEXT NOT NULL,
    PRIMARY KEY(ProductId, AttributeId, AttributeValueId),
    FOREIGN KEY(ProductId, AttributeId)
        REFERENCES ProductAttributeAssignments(ProductId, AttributeId) ON DELETE RESTRICT,
    FOREIGN KEY(AttributeValueId, AttributeId)
        REFERENCES ProductAttributeValues(Id, AttributeId) ON DELETE RESTRICT
);
CREATE INDEX IX_ProductAttributeValueAssignments_Value ON ProductAttributeValueAssignments(AttributeValueId, ProductId);

CREATE TABLE ProductVariantValues (
    VariantId        INTEGER NOT NULL,
    ProductId        INTEGER NOT NULL,
    AttributeId      INTEGER NOT NULL,
    AttributeValueId INTEGER NOT NULL,
    PRIMARY KEY(VariantId, AttributeId),
    FOREIGN KEY(VariantId, ProductId)
        REFERENCES ProductVariants(Id, ProductId) ON DELETE RESTRICT,
    FOREIGN KEY(ProductId, AttributeId, AttributeValueId)
        REFERENCES ProductAttributeValueAssignments(ProductId, AttributeId, AttributeValueId) ON DELETE RESTRICT
);
CREATE INDEX IX_ProductVariantValues_Value ON ProductVariantValues(AttributeValueId, VariantId);

CREATE TABLE ProductCapabilityAssignments (
    ProductId       INTEGER NOT NULL REFERENCES ProductDetails(ProductId) ON DELETE RESTRICT,
    CapabilityKey   TEXT NOT NULL CHECK(length(CapabilityKey) BETWEEN 2 AND 64),
    IsEnabled       INTEGER NOT NULL CHECK(IsEnabled IN (0, 1)),
    AssignedUtc     TEXT NOT NULL,
    UpdatedUtc      TEXT NOT NULL,
    ChangedByUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    PRIMARY KEY(ProductId, CapabilityKey)
);
CREATE INDEX IX_ProductCapabilityAssignments_Key ON ProductCapabilityAssignments(CapabilityKey, IsEnabled, ProductId);

CREATE TABLE ProductUnits (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId         INTEGER NOT NULL REFERENCES ProductDetails(ProductId) ON DELETE RESTRICT,
    UnitId            INTEGER NOT NULL REFERENCES Units(Id) ON DELETE RESTRICT,
    ToBaseNumerator   INTEGER NOT NULL CHECK(ToBaseNumerator BETWEEN 1 AND 1000000000),
    ToBaseDenominator INTEGER NOT NULL CHECK(ToBaseDenominator BETWEEN 1 AND 1000000000),
    IsBase            INTEGER NOT NULL DEFAULT 0 CHECK(IsBase IN (0, 1)),
    IsSaleUnit        INTEGER NOT NULL DEFAULT 1 CHECK(IsSaleUnit IN (0, 1)),
    IsPurchaseUnit    INTEGER NOT NULL DEFAULT 1 CHECK(IsPurchaseUnit IN (0, 1)),
    IsActive          INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0, 1)),
    FirstUsedUtc      TEXT NULL,
    Version           INTEGER NOT NULL DEFAULT 1 CHECK(Version >= 1),
    CreatedUtc        TEXT NOT NULL,
    UpdatedUtc        TEXT NOT NULL,
    CHECK(IsSaleUnit=1 OR IsPurchaseUnit=1),
    CHECK(IsBase=0 OR (ToBaseNumerator=1 AND ToBaseDenominator=1))
);
CREATE UNIQUE INDEX UX_ProductUnits_IdProduct ON ProductUnits(Id, ProductId);
CREATE UNIQUE INDEX UX_ProductUnits_Base ON ProductUnits(ProductId) WHERE IsBase=1;
CREATE UNIQUE INDEX UX_ProductUnits_ActiveUnit ON ProductUnits(ProductId, UnitId) WHERE IsActive=1;
CREATE INDEX IX_ProductUnits_ProductState ON ProductUnits(ProductId, IsActive, UnitId);

INSERT INTO ProductUnits
    (ProductId, UnitId, ToBaseNumerator, ToBaseDenominator, IsBase, IsSaleUnit, IsPurchaseUnit, IsActive, FirstUsedUtc, Version, CreatedUtc, UpdatedUtc)
SELECT ProductId, BaseUnitId, 1, 1, 1, 1, 1, 1, NULL, 1, CreatedUtc, UpdatedUtc
FROM ProductDetails;

CREATE TABLE ProductSkus (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId       INTEGER NOT NULL,
    VariantId       INTEGER NOT NULL,
    Sku             TEXT NOT NULL CHECK(length(Sku) BETWEEN 1 AND 64),
    NormalizedSku   TEXT NOT NULL CHECK(length(NormalizedSku) BETWEEN 1 AND 64),
    RetiredUtc      TEXT NULL,
    RetiredByUserId INTEGER NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    CreatedUtc      TEXT NOT NULL,
    CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    FOREIGN KEY(VariantId, ProductId)
        REFERENCES ProductVariants(Id, ProductId) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX UX_ProductSkus_Normalized ON ProductSkus(NormalizedSku);
CREATE UNIQUE INDEX UX_ProductSkus_ActiveVariant ON ProductSkus(VariantId) WHERE RetiredUtc IS NULL;
CREATE INDEX IX_ProductSkus_Product ON ProductSkus(ProductId, VariantId, RetiredUtc);

CREATE TABLE ProductBarcodes (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId          INTEGER NOT NULL,
    VariantId          INTEGER NOT NULL,
    ProductUnitId      INTEGER NOT NULL,
    Barcode            TEXT NOT NULL CHECK(length(Barcode) BETWEEN 1 AND 64),
    NormalizedBarcode  TEXT NOT NULL CHECK(length(NormalizedBarcode) BETWEEN 1 AND 64),
    BarcodeType        TEXT NOT NULL CHECK(BarcodeType IN ('gtin', 'internal', 'supplier')),
    IsPrimary          INTEGER NOT NULL DEFAULT 0 CHECK(IsPrimary IN (0, 1)),
    RetiredUtc         TEXT NULL,
    RetiredByUserId    INTEGER NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    CreatedUtc         TEXT NOT NULL,
    CreatedByUserId    INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    FOREIGN KEY(VariantId, ProductId)
        REFERENCES ProductVariants(Id, ProductId) ON DELETE RESTRICT,
    FOREIGN KEY(ProductUnitId, ProductId)
        REFERENCES ProductUnits(Id, ProductId) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX UX_ProductBarcodes_Normalized ON ProductBarcodes(NormalizedBarcode);
CREATE UNIQUE INDEX UX_ProductBarcodes_PrimaryUnit
ON ProductBarcodes(VariantId, ProductUnitId)
WHERE IsPrimary=1 AND RetiredUtc IS NULL;
CREATE INDEX IX_ProductBarcodes_Variant ON ProductBarcodes(ProductId, VariantId, RetiredUtc);

CREATE TABLE CatalogAuditLog (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId       INTEGER NULL REFERENCES ProductDetails(ProductId) ON DELETE RESTRICT,
    EntityType      TEXT NOT NULL CHECK(EntityType IN ('product_configuration','attribute','attribute_value','variant','sku','barcode','product_unit','product_capability')),
    EntityId        INTEGER NULL,
    Action          TEXT NOT NULL CHECK(length(Action) BETWEEN 2 AND 64),
    ChangeJson      TEXT NOT NULL CHECK(length(ChangeJson) BETWEEN 2 AND 20000),
    ChangedByUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    ChangedUtc      TEXT NOT NULL
);
CREATE INDEX IX_CatalogAuditLog_ProductTime ON CatalogAuditLog(ProductId, ChangedUtc, Id);
CREATE INDEX IX_CatalogAuditLog_Entity ON CatalogAuditLog(EntityType, EntityId, ChangedUtc);

CREATE TRIGGER TR_CatalogAuditLog_NoUpdate
BEFORE UPDATE ON CatalogAuditLog
BEGIN
    SELECT RAISE(ABORT, 'catalog_audit_immutable');
END;

CREATE TRIGGER TR_CatalogAuditLog_NoDelete
BEFORE DELETE ON CatalogAuditLog
BEGIN
    SELECT RAISE(ABORT, 'catalog_audit_immutable');
END;
```

### 3.1 Preflight, backfill, and failure behavior

Before migration 010 is applied, `Migrator` must run `CatalogLegacyBackfill.Run` in its own transaction if migration 008 is already present. This closes the current late-legacy-row gap before `ProductUnits` is backfilled.

Add a C# preflight invoked immediately before executing migration 010. It must abort with an actionable `InvalidOperationException` if any query returns rows:

1. `ProductDetails` without a matching `Products` row or valid `Units` row.
2. Any product with other than exactly one `IsDefault=1` variant.
3. Any `ProductVariants.ProductId` without `ProductDetails`.
4. Any duplicate future normalized SKU/barcode is irrelevant because those tables do not yet exist; do not inspect mock frontend data.

Migration execution and its `SchemaVersion` insert remain in the same transaction. On SQL, seed, backfill, or foreign-key failure, rollback everything and leave the migration 010 `SchemaVersion` row absent. Do not catch and continue.

After migration 010, extend `CatalogLegacyBackfill.Run` so every newly reconciled legacy product also receives exactly one base `ProductUnits` row when that table exists. Use `INSERT ... SELECT ... WHERE NOT EXISTS`, never delete/recreate a product. The existing product ID and default variant ID remain unchanged.

Migration tests must assert `PRAGMA foreign_key_check` is empty and `PRAGMA integrity_check` is `ok`. There is no down migration; recovery is the existing verified-backup/previous-binary procedure. An older binary writing to a migration-010 database remains unsupported.

## 4. Application contracts and validation

### 4.1 Response models

Keep `CatalogProduct` as the paged-list summary and add `primarySku`, `variantCount`, and `barcodeCount`. `GET /api/catalog/products/{id}` returns a new `CatalogProductDetail` containing all existing basic fields plus:

```text
variantMode: bool
capabilities: [{ key:string, isAssigned:bool, isGloballyEnabled:bool, state:string }]
attributes: [{ attributeId:int64, key:string, name:string, kind:string, sortOrder:int32,
               values:[{ valueId:int64, value:string, colorHex:string|null, sortOrder:int32 }] }]
units: [{ id:int64, unitId:int64, code:string, name:string, symbol:string,
          numerator:int64, denominator:int64, isBase:bool, isSaleUnit:bool,
          isPurchaseUnit:bool, isActive:bool, isLocked:bool, version:int64 }]
variants: [{ id:int64, name:string, isDefault:bool, isActive:bool, isArchived:bool,
             attributeValueIds:int64[], sku:string|null,
             salePriceOverride:string|null, purchasePriceOverride:string|null,
             effectiveSalePrice:string, effectivePurchasePrice:string|null,
             version:int64,
             barcodes:[{ id:int64, code:string, type:string, productUnitId:int64,
                         isPrimary:bool, isRetired:bool }] }]
version: int64
```

Do not serialize scaled money as JSON numbers. Do not return normalized identifiers or audit JSON in ordinary product responses.

### 4.2 Aggregate create/update request

Extend the existing `ProductRequest` with nullable `configuration`. Omission on update leaves all Task 2.2 configuration unchanged. Presence means a complete authoritative configuration and requires administrator access.

```text
configuration: {
  variantMode: bool,
  capabilityKeys: string[],                 // complete enabled product assignment set
  attributes: [{ attributeId:int64, valueIds:int64[], sortOrder:int32 }],
  units: [{ id:int64|null, unitId:int64, numerator:int64, denominator:int64,
            isBase:bool, isSaleUnit:bool, isPurchaseUnit:bool,
            isActive:bool, version:int64|null }],
  variants: [{ id:int64|null, name:string, attributeValueIds:int64[],
               isDefault:bool, isActive:bool, archive:bool,
               salePriceOverride:string|null, purchasePriceOverride:string|null,
               sku:string|null,
               barcodes:[{ id:int64|null, code:string, type:string,
                           productUnitId:int64|null, isPrimary:bool, retire:bool }] }]
}
```

For create, `productUnitId:null` means the newly created base product-unit row; `isDefault:true` identifies the variant command that reuses the newly created compatibility default variant. For update, all existing child rows returned by the detail endpoint must be represented. Omission of an existing unit, variant, or barcode returns `configuration_incomplete`; the API never interprets omission as deletion. Retirement, deactivation, and archival must be explicit flags.

The client never supplies `CombinationKey`, normalized values, effective prices, lock state, timestamps, product IDs inside children, or audit fields.

### 4.3 Rule helper behavior

Create pure Application helpers (no SQL/HTTP) for:

- Form-KC identifier normalization and validation.
- Attribute/value normalization.
- checked GCD/rational validation.
- canonical combination-key generation.
- Cartesian-combination validation and 200-combination limit.
- canonical aggregate request hashing with stable ordering.
- price inheritance/effective price formatting.

`CatalogService` loads the current feature snapshot through a new narrow `ICatalogFeatureReader` port. Implement it as `CatalogFeatureReader`, an Application-layer adapter that receives the existing `FeatureService` and projects only `key/isEnabled` values; do not reference Data or HTTP types. `AlKhayalServer` constructs `new CatalogFeatureReader(features)` and passes it to `CatalogService`. The service must distinguish newly added configuration from existing assigned data so globally disabled capabilities can be safely maintained/retired but not expanded.

Store exceptions use stable codes. Controllers map `not_found`/`barcode_not_found` to 404, `barcode_retired` to 410, authorization codes to 401/403, validation and unsupported capability to 400, and all uniqueness/concurrency/lifecycle conflicts to 409.

## 5. Exact API contract

All responses use `{ success, data, error }`, camelCase JSON, authenticated cookies, and Arabic safe messages with the stable English codes below.

### 5.1 Existing product endpoints (extended)

| Method and route | Request / response | Authorization and feature rules | Concurrency / notes |
|---|---|---|---|
| `GET /api/catalog/products` | Existing query/response plus summary counts/SKU. `search` also matches active SKU and active barcode. | Authenticated; existing `products-list` or `product-edit`; `product-catalog`. | Paged, max 100. Never exposes retired identifiers. |
| `GET /api/catalog/products/{id}` | Returns `CatalogProductDetail`. | Authenticated; existing product read permission; `product-catalog`. | Administrator receives retired rows needed for authoritative editing; cashier response omits retired identifiers and audit metadata. |
| `POST /api/catalog/products` | Existing fields plus optional complete `configuration`; returns detail. | Existing create permission and CSRF. If `configuration` is present, administrator only. Required global/product capabilities are checked. | `operationKey` required; entire normalized aggregate is hashed and persisted atomically. HTTP 201. |
| `PUT /api/catalog/products/{id}` | Existing fields/version plus optional complete `configuration`; returns detail. | Existing update permission and CSRF. Configuration mutation is administrator only. | Product `version` required; one aggregate version increment. |
| `PUT /api/catalog/products/{id}/state` | Existing `{isActive,version}`. | Administrator + CSRF + `products-list`. | Deactivation also makes all variants nonsellable but does not rewrite child `IsActive`. Activation requires at least one valid active variant and valid base unit. |
| `POST /api/catalog/products/{id}/archive` | Existing `{version}`. | Administrator + CSRF + `products-list`. | Archives product; identifiers remain reserved and lookup reports archived status. |

### 5.2 Global attribute metadata endpoints

| Method and route | DTO | Authorization | Validation / result |
|---|---|---|---|
| `GET /api/catalog/attributes?kind={size|color|custom}&includeInactive={bool}` | Response array includes attribute fields, version, and ordered values. `kind` is optional. | Authenticated with any product add/edit/list permission; `product-catalog`. | Unknown kind -> `validation`. |
| `POST /api/catalog/attributes` | `{ key:string, name:string, kind:"custom", sortOrder:int, isActive:bool }` | Administrator + CSRF. | Only custom creation; HTTP 201. Duplicate -> `duplicate_attribute`. |
| `PUT /api/catalog/attributes/{id}` | `{ name:string, sortOrder:int, isActive:bool, version:int64 }` | Administrator + CSRF. | System attributes cannot be deactivated; key/kind immutable. Used custom attribute cannot be deactivated while assigned. |
| `POST /api/catalog/attributes/{attributeId}/values` | `{ value:string, colorHex:string|null, sortOrder:int, isActive:bool }` | Administrator + CSRF. | HTTP 201. Attribute-specific validation; duplicate -> `duplicate_attribute_value`. |
| `PUT /api/catalog/attribute-values/{id}` | `{ value:string, colorHex:string|null, sortOrder:int, isActive:bool, version:int64 }` | Administrator + CSRF. | A selected/used value may be renamed/recolored but cannot be deactivated until removed from all unarchived variants; conflict `attribute_value_in_use`. |

The API intentionally has no DELETE endpoint for attributes, values, variants, SKUs, barcodes, or product units.

### 5.3 Barcode lookup endpoint

`GET /api/catalog/barcodes/lookup?code={scannerText}`

- Authenticated administrator, or cashier with `pos`, `sales-management`, `products-list`, or `product-edit`.
- Requires effective global `barcode-scanning`. If disabled, HTTP 409 `feature_disabled`.
- No CSRF because it is read-only.
- Empty/invalid input returns 400 `validation`.
- Active result DTO:

```text
{
  barcodeId:int64, code:string, type:string,
  productId:int64, productName:string,
  variantId:int64, variantName:string, isDefaultVariant:bool,
  productUnitId:int64, unitId:int64, unitName:string, unitSymbol:string,
  numerator:int64, denominator:int64,
  effectiveSalePrice:string, effectivePurchasePrice:string|null,
  isSellable:bool, notSellableReason:string|null
}
```

The endpoint does not accept SKU; SKU search remains a catalog search concern. It performs one indexed exact lookup and joins product/variant/unit data in one query.

### 5.4 Stable error codes

Retain existing codes and add:

`duplicate_attribute`, `duplicate_attribute_value`, `attribute_in_use`, `attribute_value_in_use`, `capability_disabled`, `capability_not_supported`, `capability_in_use`, `feature_disabled`, `duplicate_combination`, `variant_set_incomplete`, `variant_mode_locked`, `default_variant_required`, `variant_archived`, `duplicate_sku`, `sku_retired`, `duplicate_barcode`, `barcode_retired`, `barcode_not_found`, `invalid_conversion`, `conversion_locked`, `base_unit_locked`, `configuration_incomplete`, `configuration_limit`, and `identifier_invalid`.

Use HTTP 409 for all duplicate, in-use, locked, archived, limit, and version/operation conflicts except malformed limits/identifiers supplied for a new row, which are 400 validation. Database unique violations must be translated to the specific stable code; do not return raw SQLite messages.

## 6. Persistence behavior

`SqliteCatalogStore` must expose one aggregate create and one aggregate update transaction rather than controller-orchestrated child writes.

Aggregate write order is fixed:

1. Start one transaction and load current product/version plus all configuration rows.
2. For create, resolve the operation key before any insert. For update, claim the product version with the existing optimistic predicate only after all validation reads and immediately before child writes.
3. Validate global references, product ownership, complete-row representation, immutable retired/archived rows, and locked conversions.
4. Write basic product/default prices.
5. Reconcile product capability assignments without deleting rows (`IsEnabled` changes only).
6. Reconcile product units; create/update base row first.
7. Reconcile product attribute/value selections.
8. Convert the hidden default or write variant rows and `ProductVariantValues`; compute names and combination keys server-side. Suggested name is `Product name — value / value`; a supplied trimmed name up to 200 characters may override it.
9. Retire/insert SKUs, then retire/insert barcodes and repair primary selection deterministically.
10. Verify exactly one default, at least one valid active unarchived variant, one base unit, all active combination shapes, and no unsupported configuration.
11. Append audit rows, increment `ProductDetails.Version` exactly once, and commit.

Any failure rolls back the entire transaction. Cancellation is honored before commit. There is no generic child-row delete method. Dapper/ADO parameters are required; no user value is concatenated into SQL.

Future attachment rules are fixed now: Task 2.3 batches/serials and Task 2.4 ledger/balances reference `ProductVariants.Id`; movement lines also reference `ProductUnits.Id` and copy numerator/denominator. They never reference product name, combination text, SKU, or barcode as identity. This is why all IDs and retired rows are preserved.

## 7. Frontend integration contract

### 7.1 Preserve the approved screen design

Do not add a framework, new visual language, new viewport owner, new `100vh`, CSS zoom, or a second settings navigation model. Reuse `card`, `card-head`, `card-body`, `form-grid`, `field`, `table-wrap`, `data-table`, `badge`, `row-actions`, `button`, shared modal/confirm/toast helpers, theme tokens, and existing breakpoints. All new text is Arabic and RTL; codes/SKUs/barcodes/ratios use `.ltr`.

No new top-level route is required. Update the descriptions for `products-list`, `product-add`, `product-edit`, and `product-quick-add` in `screens.json`; keep their route IDs, renderer keys, navigation behavior, and feature key. Global attribute/value creation is an administrator modal launched from the product form, following the current inline category modal pattern.

### 7.2 Product add/edit layout

Keep the current “البيانات الأساسية” card unchanged in order and meaning. Replace `futureDetails()` with the following progressive cards:

1. **الإمكانات المطبقة على المنتج** — administrator only. Toggle rows for the Task 2.2 allowlist, showing global state. Disabled global/unassigned rows are disabled. Disabled global/assigned rows remain checked, locked, and labelled as retained.
2. **الوحدات والتحويلات** — visible only when `multiple-units` is assigned, plus a read-only base row always shown. Table columns: unit, exact equation (`1 [unit] = n/d [base]`), sale, purchase, state, lock state, actions. Add/edit uses a compact modal and validates reduced positive integers. Locked rows offer only deactivation.
3. **المتغيرات والسمات** — visible when `product-variants` is assigned. Axis selectors show Size only when `sizes` is assigned/globally usable, Color only when `colors` is assigned/globally usable, and active custom attributes. Value chips have keyboard-operable checkboxes. “إضافة قيمة” and “إضافة سمة مخصصة” open compact administrator modals without unmounting the draft. “توليد التركيبات” previews the deterministic Cartesian result and preserves rows whose canonical combination already exists.
4. **جدول المتغيرات والمعرفات** — columns: default selector, variant/name, attribute values, SKU, barcode count, effective sale price, effective reference purchase cost, state, actions. Variant price fields clearly say “اتركه فارغًا لوراثة سعر المنتج”. Barcode management opens a compact modal listing code, type, mapped product unit, primary/retired state, and explicit retire action.
5. **المخزون المستقبلي** — a compact empty-state card stating that warehouse quantities, reorder levels, movements, batches/expiry, serials/IMEI, and average cost are not implemented until later tasks. It must display no numeric placeholders or mock records.

For a simple product, show one compact “SKU والباركود” section mapped to the hidden default variant when the relevant assignments permit it. Do not expose the words “متغير افتراضي مخفي” to ordinary users.

`product-quick-add` remains intentionally basic: no advanced cards and no Task 2.2 configuration payload. `product-add` and `product-edit` show progressive configuration only to administrators. Cashiers retain the existing basic form behavior.

### 7.3 Interaction and save behavior

- Load attributes, effective feature state, and product detail in parallel where possible. Use the existing loading/error/retry patterns and ignore stale async completions through `context.isActive()`.
- Keep one in-memory draft. Opening/closing nested attribute, unit, or barcode modals must not discard it.
- Disable the primary save button while saving. POST/PUT one aggregate request; never issue a basic save followed by child saves.
- On `version_conflict`, keep the draft mounted, show an Arabic conflict toast, and offer reload; do not silently overwrite.
- Before irreversible SKU/barcode retirement, variant archive, or one-way simple-to-variant conversion, use `UI.confirm` with explicit Arabic consequences.
- Combination count is shown before generation. Disable generation over 200 and show the server-compatible validation message.
- Scanner behavior is ready for keyboard-wedge devices: any consuming screen calls the exact lookup endpoint only after Enter, trims once, prevents duplicate in-flight requests for the same code, and restores focus. Task 2.2 need not replace the mock POS; frontend automated coverage uses a small lookup helper exported from the catalog module.
- Update the catalog list to show primary SKU and variant count without adding quantities. Keep the table horizontally scrollable.

## 8. Implementation file map

| Exact path | Action | Required changes | Dependencies | Test coverage |
|---|---|---|---|---|
| `src/AlKhayal.Data/Migrations/010_product_variants_and_identifiers.sql` | Create | Transcribe Section 3 SQL exactly. | Existing 001–009. | `CatalogMigrationTests.cs`, `MigrationTests.cs` |
| `src/AlKhayal.Data/Database/Migrator.cs` | Modify | Reconcile before 010; invoke preflight; retain atomic script/version transaction. | `CatalogLegacyBackfill`, new preflight. | `CatalogMigrationTests.cs` |
| `src/AlKhayal.Data/Database/CatalogLegacyBackfill.cs` | Modify | Insert base `ProductUnits` row for late products when table exists. | Migration table detection. | `CatalogMigrationTests.cs` |
| `src/AlKhayal.Data/Database/CatalogTask22Preflight.cs` | Create | Implement the four fail-fast integrity queries and actionable exceptions. | SQLite only. | `CatalogMigrationTests.cs` |
| `src/AlKhayal.Application/Features/FeatureRegistry.cs` | Modify | Remove the `sizes`,`colors` hard dependencies from `product-variants`; no other registry changes. | Existing feature service. | `FeatureServiceTests.cs`, new catalog rule tests |
| `src/AlKhayal.Application/Catalog/CatalogContracts.cs` | Modify | Add summary fields and reference new detail/configuration contracts. Preserve existing public contracts. | None beyond Application. | `CatalogServiceTests.cs` |
| `src/AlKhayal.Application/Catalog/CatalogVariantContracts.cs` | Create | Add exact DTOs/commands/results from Sections 4–5. | Existing catalog contracts. | `CatalogVariantServiceTests.cs` |
| `src/AlKhayal.Application/Catalog/CatalogVariantRules.cs` | Create | Normalization, GCD, combination key, limits, inheritance, canonical hashing helpers. | BCL only; .NET Standard-compatible APIs. | `CatalogVariantServiceTests.cs` |
| `src/AlKhayal.Application/Catalog/CatalogPorts.cs` | Modify | Add aggregate detail/read/write, attribute metadata, lookup, and audit-bearing store records; add narrow `ICatalogFeatureReader`. | Application contracts only. | Fake ports in unit tests |
| `src/AlKhayal.Application/Catalog/CatalogFeatureReader.cs` | Create | Implement `ICatalogFeatureReader` by projecting the existing `FeatureService.GetEffectiveAsync` snapshot to catalog key/enabled records. | `Application.Features`; no Data/HTTP dependency. | `CatalogVariantServiceTests.cs` |
| `src/AlKhayal.Application/Catalog/CatalogService.cs` | Modify | Validate/normalize full aggregate, global/product capabilities, lifecycle, concurrency inputs, response mapping, and scanner lookup. | `ICatalogStore`, `ICatalogFeatureReader`, rules. | Both catalog unit test files |
| `src/AlKhayal.Data/Catalog/SqliteCatalogStore.cs` | Modify | Mark class `partial`; extend list search/summary and make existing create/update delegate to aggregate paths. | Dapper/SQLite. | Persistence/API tests |
| `src/AlKhayal.Data/Catalog/SqliteCatalogConfigurationStore.cs` | Create | Partial-class aggregate queries/transactions, attributes, variants, identifiers, units, lookup, audit, unique-error translation. | Same `_connectionString`; no second store/service. | `CatalogVariantPersistenceTests.cs` |
| `src/AlKhayal.Api/Controllers/CatalogController.cs` | Modify | Extend product DTO translation; add attribute/value and barcode lookup routes; enforce exact role/screen/feature/CSRF rules and HTTP mappings. | Existing auth/users/features/catalog services. | `CatalogVariantApiTests.cs` |
| `src/AlKhayal.Server.Host/AlKhayalServer.cs` | Modify | Construct the catalog feature-reader adapter and pass it to `CatalogService`; no topology/binding change. | Existing `FeatureService`. | API integration startup |
| `frontend/js/modules/catalog.js` | Modify | Replace `futureDetails`, use detail DTO, manage one draft, generate combinations, modal unit/attribute/barcode flows, aggregate save, scanner helper, honest placeholder. | Existing API/UI/utils/features. | `frontend/tests/run-tests.js` |
| `frontend/css/modules.css` | Modify | Add intrinsic grids, chips, combination/unit tables, bounded modal body, RTL/LTR details using tokens and existing breakpoints. | Existing design tokens. | Registry/static tests plus manual matrix |
| `frontend/data/screens.json` | Modify | Update only the four product route descriptions; no new route. | Existing renderer names. | Registry test |
| `frontend/tests/run-tests.js` | Modify | Assert routes/renderers, aggregate API strings, no mock/future quantity claims, feature dependency text, generated-combination behavior, scanner Enter/debounce helper, Chromium-109 syntax assumptions. | Node built-ins. | Self |
| `tests/AlKhayal.UnitTests/CatalogServiceTests.cs` | Modify | Update fake store for expanded port and preserve Task 2.1 tests. | Application. | Unit run |
| `tests/AlKhayal.UnitTests/CatalogVariantServiceTests.cs` | Create | Pure rules, capabilities, transitions, canonical hash, limits, normalization, rational math, inheritance. | Application/Data project references already present. | Unit run |
| `tests/AlKhayal.UnitTests/FeatureServiceTests.cs` | Modify | Prove product variants no longer globally require both sizes/colors and dependent-disable behavior remains correct. | Features. | Unit run |
| `tests/AlKhayal.IntegrationTests/CatalogMigrationTests.cs` | Modify | Fresh/repeat/009-upgrade/late-row/preflight-failure/schema/index/backfill/FK/integrity cases. | Data. | Integration run |
| `tests/AlKhayal.IntegrationTests/MigrationTests.cs` | Modify | Expected migration count becomes 10; assert key tables/columns. | Data. | Integration run |
| `tests/AlKhayal.IntegrationTests/CatalogPersistenceTests.cs` | Modify | Preserve current catalog flow and assert simple default/base-unit compatibility. | Data/Application. | Integration run |
| `tests/AlKhayal.IntegrationTests/CatalogVariantPersistenceTests.cs` | Create | Transactionality, restart persistence, uniqueness/retirement, transitions, conversion locks, audit immutability, version conflicts. | Data/Application. | Integration run |
| `tests/AlKhayal.IntegrationTests/CatalogApiTests.cs` | Modify | Preserve existing auth/basic catalog cases with expanded response. | Hosted API. | Integration run |
| `tests/AlKhayal.IntegrationTests/CatalogVariantApiTests.cs` | Create | Exact routes/status/codes, roles/screens/CSRF/features, scanner results, idempotent create. | Hosted API. | Integration run |
| `docs/architecture.md` | Modify in Batch D | Mark Task 2.2 boundary and future variant identity attachment. | This spec. | Documentation review |
| `docs/database.md` | Modify in Batch D | Document migration 010/tables/retirement/rational locks. | This spec. | Documentation review |
| `docs/api-contracts.md` | Modify in Batch D | Add exact Task 2.2 endpoints/DTO summaries/codes. | This spec. | Documentation review |
| `docs/design.md` | Modify in Batch D | Document progressive product form and honest inventory placeholder. | This spec. | Documentation review |
| `docs/feature-management.md` | Modify in Batch D | Document global-versus-product state and disablement behavior. | This spec. | Documentation review |
| `docs/testing.md` | Modify in Batch D | Record focused commands/matrices without claiming unrun checks. | This spec. | Documentation review |
| `docs/product-catalog.md` | Modify in Batch D | Replace Task 2.1-only limitations with delivered Task 2.2 behavior. | This spec. | Documentation review |

No Core project change is required: the current catalog vertical slice deliberately owns its use-case contracts in Application. No new NuGet package is permitted.

## 9. Mechanical execution batches

### Batch A — SQLite migration and persistence

**Preconditions**

- Working tree preserves the existing uncommitted Task 2.1 files.
- Migrations 001–009 and current catalog tests are understood; none is edited.
- No application host is writing to the test database.

**Instructions**

1. Add migration 010 exactly as Section 3.
2. Add preflight/reconciliation ordering and late-product base-unit backfill.
3. Add store records and the partial `SqliteCatalogConfigurationStore` implementation.
4. Implement parameterized aggregate transactions, identifier history, exact lookups, and audit inserts.
5. Add migration and persistence tests before changing API/UI.

**Expected output**

- Fresh and 009 databases contain the full Task 2.2 schema, one base unit per existing product, unchanged product/default-variant IDs, and no invented attributes/values beyond the two system attributes.
- Repository operations can round-trip a simple product and a configured variant product after process restart.

**Acceptance checks**

- Migration count 10; repeat migration is a no-op.
- `foreign_key_check` empty; `integrity_check=ok`.
- Failed preflight or forced child-write failure leaves no migration/partial aggregate/audit residue.
- Global SKU/barcode uniqueness includes retired rows.
- Audit update/delete triggers abort.

**Failure conditions**

- Any edit to 001–009, product/default-variant ID change, direct delete, floating conversion, partial commit, or raw SQLite error exposed.

**Boundary**

- No API/frontend work and no stock/tracking schema.

### Batch B — Core/Application services and API

**Preconditions**

- Batch A tests pass.
- Store aggregate methods and schema names are stable.

**Instructions**

1. Add contracts and pure rules exactly as Sections 2, 4, and 5.
2. Inject the narrow feature reader; update only the specified registry dependency.
3. Extend product create/update/detail/list while preserving existing clients that omit configuration.
4. Add global attribute/value routes and exact barcode lookup.
5. Enforce role, effective screen access, feature state, CSRF, stable codes, and status mapping.
6. Add unit/API tests including restart and idempotency.

**Expected output**

- Existing Task 2.1 requests still work.
- Administrators can atomically create/update full configurations; cashiers cannot mutate them.
- Scanner lookup is indexed, exact, identity-only, and permission/feature aware.

**Acceptance checks**

- Same operation key/same full body returns same product; changed nested body conflicts.
- Stale product/attribute versions conflict.
- Global disable retains existing assignments/data while blocking expansion.
- Every mutation rejects missing/invalid CSRF and unauthenticated requests.
- Stable codes/statuses match Section 5.

**Failure conditions**

- Controller business logic, Data dependency in Application, child HTTP orchestration, bearer/localStorage credentials, or unpermissioned lookup.

**Boundary**

- No POS persistence, stock, batch, serial, warehouse, or printing changes.

### Batch C — Frontend integration

**Preconditions**

- Batch B API tests pass and DTOs are frozen.
- Existing shell/design contracts remain intact.

**Instructions**

1. Extend the existing catalog renderer; do not add a parallel product renderer.
2. Implement the progressive cards/modals and simple/default presentation from Section 7.
3. Keep one draft and send one aggregate save.
4. Add list SKU/variant count and the scanner-ready helper.
5. Replace the misleading nine-column future table with the honest variant table plus inventory placeholder.
6. Add only token-based intrinsic CSS and update registry/static frontend tests.

**Expected output**

- Arabic RTL add/edit flows support simple products and administrator variant configuration without losing drafts.
- Light/dark/system themes, keyboard focus, long Arabic text, narrow widths, and bounded wide tables remain usable.

**Acceptance checks**

- No mock notice on real catalog data and no fake quantity/average cost.
- Quick add remains basic.
- Variant generation is deterministic, capped, keyboard reachable, and preserves known IDs.
- Disabled global features show retained existing configuration without allowing expansion.
- `node --check` succeeds for changed JS and the frontend registry test passes.

**Failure conditions**

- New framework/CDN, duplicated route registry, second viewport, CSS zoom/scale, mock inventory, separate child-save calls, or unsupported Chromium 109 API.

**Boundary**

- Existing mock POS may call/test lookup only; it is not converted to persistent sales.

### Batch D — Automated tests, validation, and documentation

**Preconditions**

- Batches A–C are complete.

**Instructions**

1. Complete every test case below and update the seven listed documentation files.
2. Run focused tests first, then the repository's normal solution tests/build once implementation is complete.
3. Run frontend syntax/registry tests and `git diff --check`.
4. Manually verify browser and WebView2 light/dark/system, RTL, keyboard, 1080/760/560 breakpoints, scanner Enter behavior, error/retry, and version conflict. Record any environment limitation honestly.

**Expected output**

- Documentation distinguishes delivered identifiers/configuration from future stock/tracking.
- All automated checks pass or a specific blocker is reported without claiming success.

**Acceptance checks**

- Unit: normalization (including Form KC/case/whitespace/control rejection), GCD/reduction/bounds, combination ordering/duplicates/limits, one-way transition, inheritance/null cost, capability matrices, canonical idempotency hash.
- Migration: fresh, repeated, 009 upgrade, late legacy row, corrupt-default preflight rollback, seeds, columns/checks/indexes/FKs, ID preservation, base rows, integrity.
- Persistence: full aggregate rollback injection, restart, simple/default reuse, variant archive/default switch, retired non-reuse, barcode-to-unit ownership, conversion lock, feature disable maintenance, optimistic concurrency, immutable audit.
- API: anonymous/CSRF/cashier/admin matrices; exact DTOs/routes/status/codes; operation retry/conflict; scanner active/inactive/archived/retired/missing; no quantity fields.
- Frontend: renderer registration, route descriptions, aggregate save, no fake inventory columns, deterministic combination helper, scanner Enter/in-flight suppression, no syntax beyond Chromium 109.

**Failure conditions**

- Any skipped invariant without a documented blocker, unexecuted test claimed as passed, documentation claiming stock/tracking delivery, or Windows 7 compatibility claim without legacy-runtime testing.

**Boundary**

- Stabilize Task 2.2 only; do not begin migrations 011+ or Phase 2 Tasks 2.3–2.5.

## 10. Compatibility assumptions and blockers

- Runtime remains .NET Framework 4.8 / current .NET Standard-compatible Application style, Web API 2/OWIN, System.Data.SQLite/Dapper, vanilla JS, and Chromium 109-compatible browser syntax.
- Deployment remains standalone local host on `127.0.0.1`; no LAN/cloud behavior is introduced.
- Money scale 4 and future quantity scale 6 remain fixed. SQLite is local disk only.
- The exact rational design is ready for Task 2.4 snapshots but Task 2.2 has no quantity consumer and therefore cannot mark real usage except identifier-based conversion locking.
- Windows 7/WebView2 legacy runtime and real scanner hardware require later manual qualification; no compatibility pass is implied by automated tests.
- There are **no unresolved architecture blockers**. Tracking assignments/configuration are intentionally deferred to Task 2.3, and stock/warehouse attachment is intentionally deferred to Task 2.4 through the stable `ProductVariants.Id`/`ProductUnits.Id` identities defined here.
