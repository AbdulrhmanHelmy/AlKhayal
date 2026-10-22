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
