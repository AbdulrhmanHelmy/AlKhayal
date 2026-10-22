CREATE TABLE ProductCategories (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    ParentCategoryId INTEGER NULL REFERENCES ProductCategories(Id) ON DELETE RESTRICT,
    Name             TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 120),
    NormalizedName   TEXT NOT NULL CHECK(length(NormalizedName) BETWEEN 1 AND 120),
    IsActive         INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0, 1)),
    SortOrder        INTEGER NOT NULL DEFAULT 0,
    CreatedUtc       TEXT NOT NULL,
    UpdatedUtc       TEXT NOT NULL
);

CREATE UNIQUE INDEX UX_ProductCategories_RootName
ON ProductCategories(NormalizedName) WHERE ParentCategoryId IS NULL;
CREATE UNIQUE INDEX UX_ProductCategories_ChildName
ON ProductCategories(ParentCategoryId, NormalizedName) WHERE ParentCategoryId IS NOT NULL;
CREATE INDEX IX_ProductCategories_ParentSort ON ProductCategories(ParentCategoryId, SortOrder, Name);

CREATE TABLE Units (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    Code           TEXT NOT NULL CHECK(length(trim(Code)) BETWEEN 1 AND 32),
    NormalizedCode TEXT NOT NULL CHECK(length(NormalizedCode) BETWEEN 1 AND 32),
    Name           TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 80),
    Symbol         TEXT NOT NULL CHECK(length(trim(Symbol)) BETWEEN 1 AND 20),
    IsActive       INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0, 1)),
    IsSystem       INTEGER NOT NULL DEFAULT 0 CHECK(IsSystem IN (0, 1)),
    CreatedUtc     TEXT NOT NULL,
    UpdatedUtc     TEXT NOT NULL
);

CREATE UNIQUE INDEX UX_Units_Code ON Units(NormalizedCode);

INSERT INTO Units (Id, Code, NormalizedCode, Name, Symbol, IsActive, IsSystem, CreatedUtc, UpdatedUtc)
VALUES (1, 'PCS', 'PCS', 'قطعة', 'قطعة', 1, 1, strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now'));

CREATE TABLE ProductDetails (
    ProductId       INTEGER PRIMARY KEY REFERENCES Products(Id) ON DELETE RESTRICT,
    Description     TEXT NULL CHECK(Description IS NULL OR length(Description) <= 1000),
    CategoryId      INTEGER NULL REFERENCES ProductCategories(Id) ON DELETE RESTRICT,
    BaseUnitId      INTEGER NOT NULL REFERENCES Units(Id) ON DELETE RESTRICT,
    ProductType     TEXT NOT NULL DEFAULT 'stock' CHECK(ProductType IN ('stock', 'service', 'non_stock')),
    SellPriceScaled INTEGER NOT NULL CHECK(SellPriceScaled >= 0),
    MoneyScale      INTEGER NOT NULL DEFAULT 4 CHECK(MoneyScale = 4),
    IsActive        INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0, 1)),
    ArchivedUtc     TEXT NULL,
    MigrationState TEXT NOT NULL DEFAULT 'ready' CHECK(MigrationState IN ('ready', 'review_required')),
    Version         INTEGER NOT NULL DEFAULT 1 CHECK(Version >= 1),
    CreatedUtc      TEXT NOT NULL,
    UpdatedUtc      TEXT NOT NULL
);

CREATE INDEX IX_ProductDetails_CategoryActive ON ProductDetails(CategoryId, IsActive, ArchivedUtc);
CREATE INDEX IX_ProductDetails_BaseUnit ON ProductDetails(BaseUnitId);

CREATE TABLE ProductVariants (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId      INTEGER NOT NULL REFERENCES Products(Id) ON DELETE RESTRICT,
    Name           TEXT NOT NULL,
    NormalizedName TEXT NOT NULL,
    IsDefault      INTEGER NOT NULL DEFAULT 1 CHECK(IsDefault IN (0, 1)),
    IsActive       INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0, 1)),
    CreatedUtc     TEXT NOT NULL,
    UpdatedUtc     TEXT NOT NULL
);

CREATE UNIQUE INDEX UX_ProductVariants_Default
ON ProductVariants(ProductId) WHERE IsDefault = 1;
CREATE INDEX IX_ProductVariants_Product ON ProductVariants(ProductId);

CREATE TABLE ProductMigrationIssues (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId         INTEGER NOT NULL REFERENCES Products(Id) ON DELETE RESTRICT,
    IssueCode         TEXT NOT NULL,
    Details           TEXT NOT NULL,
    OriginalPriceType TEXT NULL,
    OriginalPriceText TEXT NULL,
    CreatedUtc        TEXT NOT NULL,
    ResolvedUtc       TEXT NULL
);

CREATE INDEX IX_ProductMigrationIssues_Unresolved ON ProductMigrationIssues(ResolvedUtc, ProductId);

CREATE TABLE CatalogOperations (
    OperationKey TEXT PRIMARY KEY,
    RequestHash  TEXT NOT NULL,
    OperationType TEXT NOT NULL CHECK(OperationType IN ('create_product')),
    ProductId    INTEGER NOT NULL REFERENCES Products(Id) ON DELETE RESTRICT,
    CreatedUtc   TEXT NOT NULL
);
