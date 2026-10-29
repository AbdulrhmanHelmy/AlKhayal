-- Reserved Phase 7 range; preserve all product and financial identities.
CREATE TABLE LabelConfiguration (
 Id INTEGER PRIMARY KEY CHECK(Id=1), Version INTEGER NOT NULL, Json TEXT NOT NULL,
 ActorId INTEGER NOT NULL REFERENCES Users(Id), UpdatedUtc TEXT NOT NULL
);
CREATE TABLE LabelConfigurationAudit (Id INTEGER PRIMARY KEY AUTOINCREMENT,Version INTEGER NOT NULL,ActorId INTEGER NOT NULL REFERENCES Users(Id),CreatedUtc TEXT NOT NULL);
ALTER TABLE PrintJobs ADD COLUMN RequestHash TEXT;
ALTER TABLE PrintJobs ADD COLUMN BatchSummaryJson TEXT;
CREATE TABLE BarcodeOperations (
 OperationKey TEXT PRIMARY KEY, RequestHash TEXT NOT NULL, ActorId INTEGER NOT NULL REFERENCES Users(Id),
 BarcodeId INTEGER NOT NULL REFERENCES ProductBarcodes(Id), ProductVersion INTEGER NOT NULL, Format TEXT NOT NULL, CreatedUtc TEXT NOT NULL
);
-- Symbology is metadata, separate from existing gtin/internal/supplier provenance.
CREATE TABLE BarcodeSymbologies (BarcodeId INTEGER PRIMARY KEY REFERENCES ProductBarcodes(Id),Format TEXT NOT NULL CHECK(Format IN ('code128','ean13')));
