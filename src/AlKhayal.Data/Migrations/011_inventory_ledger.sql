CREATE TABLE Warehouses (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 Code TEXT NOT NULL CHECK(length(trim(Code)) BETWEEN 1 AND 32),
 NormalizedCode TEXT NOT NULL UNIQUE,
 Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 120),
 Description TEXT NULL,
 Location TEXT NULL,
 IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)),
 IsDefault INTEGER NOT NULL DEFAULT 0 CHECK(IsDefault IN (0,1)),
 CreatedUtc TEXT NOT NULL,
 UpdatedUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX UX_Warehouses_Default ON Warehouses(IsDefault) WHERE IsDefault=1;
INSERT INTO Warehouses(Code,NormalizedCode,Name,IsActive,IsDefault,CreatedUtc,UpdatedUtc)
VALUES('MAIN','MAIN','المخزن الرئيسي',1,1,strftime('%Y-%m-%dT%H:%M:%fZ','now'),strftime('%Y-%m-%dT%H:%M:%fZ','now'));

CREATE TABLE ProductBatches (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 VariantId INTEGER NOT NULL REFERENCES ProductVariants(Id) ON DELETE RESTRICT,
 LotNumber TEXT NOT NULL,
 NormalizedLotNumber TEXT NOT NULL,
 ExpirationDate TEXT NULL,
 CreatedUtc TEXT NOT NULL,
 UNIQUE(VariantId,NormalizedLotNumber)
);

CREATE TABLE InventoryDocuments (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 DocumentType TEXT NOT NULL CHECK(DocumentType IN ('opening','manual_receipt','manual_issue','adjustment','count','transfer','reversal','projection_rebuild')),
 Status TEXT NOT NULL CHECK(Status IN ('draft','posted','canceled')),
 SourceWarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id) ON DELETE RESTRICT,
 DestinationWarehouseId INTEGER NULL REFERENCES Warehouses(Id) ON DELETE RESTRICT,
 Reason TEXT NOT NULL,
 Notes TEXT NULL,
 CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 CreatedUtc TEXT NOT NULL,
 PostedUtc TEXT NULL,
 OperationKey TEXT NULL UNIQUE,
 RequestHash TEXT NULL,
 ReversesDocumentId INTEGER NULL REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 CHECK(DestinationWarehouseId IS NULL OR DestinationWarehouseId<>SourceWarehouseId)
);
CREATE UNIQUE INDEX UX_InventoryDocuments_Reversal ON InventoryDocuments(ReversesDocumentId) WHERE ReversesDocumentId IS NOT NULL;
CREATE INDEX IX_InventoryDocuments_WarehouseTime ON InventoryDocuments(SourceWarehouseId,CreatedUtc,Id);
CREATE TRIGGER TR_InventoryDocuments_NoUpdatePosted BEFORE UPDATE ON InventoryDocuments WHEN OLD.Status<>'draft' BEGIN SELECT RAISE(ABORT,'posted_document_immutable'); END;
CREATE TRIGGER TR_InventoryDocuments_NoDeletePosted BEFORE DELETE ON InventoryDocuments WHEN OLD.Status<>'draft' BEGIN SELECT RAISE(ABORT,'posted_document_immutable'); END;

CREATE TABLE InventoryDocumentLines (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 DocumentId INTEGER NOT NULL REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 LineNumber INTEGER NOT NULL,
 VariantId INTEGER NOT NULL REFERENCES ProductVariants(Id) ON DELETE RESTRICT,
 BatchId INTEGER NULL REFERENCES ProductBatches(Id) ON DELETE RESTRICT,
 EnteredQuantityScaled INTEGER NOT NULL,
 ProductUnitId INTEGER NOT NULL REFERENCES ProductUnits(Id) ON DELETE RESTRICT,
 ToBaseNumerator INTEGER NOT NULL CHECK(ToBaseNumerator>0),
 ToBaseDenominator INTEGER NOT NULL CHECK(ToBaseDenominator>0),
 BaseQuantityScaled INTEGER NOT NULL,
 UnitCostScaled INTEGER NULL CHECK(UnitCostScaled IS NULL OR UnitCostScaled>=0),
 ExpectedBalanceScaled INTEGER NULL,
 ExpectedBalanceVersion INTEGER NULL,
 CountedQuantityScaled INTEGER NULL CHECK(CountedQuantityScaled IS NULL OR CountedQuantityScaled>=0),
 UNIQUE(DocumentId,LineNumber)
);
CREATE TRIGGER TR_InventoryDocumentLines_NoUpdatePosted BEFORE UPDATE ON InventoryDocumentLines WHEN EXISTS(SELECT 1 FROM InventoryDocuments WHERE Id=OLD.DocumentId AND Status<>'draft') BEGIN SELECT RAISE(ABORT,'posted_document_immutable'); END;
CREATE TRIGGER TR_InventoryDocumentLines_NoDeletePosted BEFORE DELETE ON InventoryDocumentLines WHEN EXISTS(SELECT 1 FROM InventoryDocuments WHERE Id=OLD.DocumentId AND Status<>'draft') BEGIN SELECT RAISE(ABORT,'posted_document_immutable'); END;

CREATE TABLE StockMovements (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 DocumentId INTEGER NOT NULL REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 DocumentLineId INTEGER NOT NULL REFERENCES InventoryDocumentLines(Id) ON DELETE RESTRICT,
 WarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id) ON DELETE RESTRICT,
 VariantId INTEGER NOT NULL REFERENCES ProductVariants(Id) ON DELETE RESTRICT,
 BatchId INTEGER NULL REFERENCES ProductBatches(Id) ON DELETE RESTRICT,
 QuantityBaseScaled INTEGER NOT NULL CHECK(QuantityBaseScaled<>0),
 ValueScaled INTEGER NOT NULL,
 UnitCostScaled INTEGER NULL,
 CreatedUtc TEXT NOT NULL,
 UNIQUE(DocumentLineId,WarehouseId)
);
CREATE INDEX IX_StockMovements_WarehouseTime ON StockMovements(WarehouseId,CreatedUtc,Id);
CREATE INDEX IX_StockMovements_VariantTime ON StockMovements(VariantId,CreatedUtc,Id);
CREATE TRIGGER TR_StockMovements_NoUpdate BEFORE UPDATE ON StockMovements BEGIN SELECT RAISE(ABORT,'stock_movement_immutable'); END;
CREATE TRIGGER TR_StockMovements_NoDelete BEFORE DELETE ON StockMovements BEGIN SELECT RAISE(ABORT,'stock_movement_immutable'); END;

CREATE TABLE InventoryBalances (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 WarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id) ON DELETE RESTRICT,
 VariantId INTEGER NOT NULL REFERENCES ProductVariants(Id) ON DELETE RESTRICT,
 BatchId INTEGER NULL REFERENCES ProductBatches(Id) ON DELETE RESTRICT,
 QuantityBaseScaled INTEGER NOT NULL DEFAULT 0 CHECK(QuantityBaseScaled>=0),
 ValueScaled INTEGER NOT NULL DEFAULT 0 CHECK(ValueScaled>=0),
 Version INTEGER NOT NULL DEFAULT 1 CHECK(Version>=1),
 UpdatedUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX UX_InventoryBalances_Unbatched ON InventoryBalances(WarehouseId,VariantId) WHERE BatchId IS NULL;
CREATE UNIQUE INDEX UX_InventoryBalances_Batched ON InventoryBalances(WarehouseId,VariantId,BatchId) WHERE BatchId IS NOT NULL;
CREATE TRIGGER TR_InventoryBalances_InsertLedger BEFORE INSERT ON InventoryBalances BEGIN
 SELECT CASE WHEN NEW.QuantityBaseScaled<>COALESCE((SELECT SUM(QuantityBaseScaled) FROM StockMovements WHERE WarehouseId=NEW.WarehouseId AND VariantId=NEW.VariantId AND ((BatchId IS NULL AND NEW.BatchId IS NULL) OR BatchId=NEW.BatchId)),0)
 OR NEW.ValueScaled<>COALESCE((SELECT SUM(ValueScaled) FROM StockMovements WHERE WarehouseId=NEW.WarehouseId AND VariantId=NEW.VariantId AND ((BatchId IS NULL AND NEW.BatchId IS NULL) OR BatchId=NEW.BatchId)),0)
 THEN RAISE(ABORT,'balance_not_ledger') END;
END;
CREATE TRIGGER TR_InventoryBalances_UpdateLedger BEFORE UPDATE ON InventoryBalances BEGIN
 SELECT CASE WHEN NEW.WarehouseId<>OLD.WarehouseId OR NEW.VariantId<>OLD.VariantId OR COALESCE(NEW.BatchId,-1)<>COALESCE(OLD.BatchId,-1)
 OR NEW.QuantityBaseScaled<>COALESCE((SELECT SUM(QuantityBaseScaled) FROM StockMovements WHERE WarehouseId=NEW.WarehouseId AND VariantId=NEW.VariantId AND ((BatchId IS NULL AND NEW.BatchId IS NULL) OR BatchId=NEW.BatchId)),0)
 OR NEW.ValueScaled<>COALESCE((SELECT SUM(ValueScaled) FROM StockMovements WHERE WarehouseId=NEW.WarehouseId AND VariantId=NEW.VariantId AND ((BatchId IS NULL AND NEW.BatchId IS NULL) OR BatchId=NEW.BatchId)),0)
 THEN RAISE(ABORT,'balance_not_ledger') END;
END;
CREATE TRIGGER TR_InventoryBalances_NoDelete BEFORE DELETE ON InventoryBalances BEGIN SELECT RAISE(ABORT,'balance_projection_immutable'); END;
CREATE TRIGGER TR_InventoryBalances_BatchVariant BEFORE INSERT ON InventoryBalances WHEN NEW.BatchId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM ProductBatches WHERE Id=NEW.BatchId AND VariantId=NEW.VariantId) BEGIN SELECT RAISE(ABORT,'batch_variant_mismatch'); END;
CREATE TRIGGER TR_StockMovements_BatchVariant BEFORE INSERT ON StockMovements WHEN NEW.BatchId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM ProductBatches WHERE Id=NEW.BatchId AND VariantId=NEW.VariantId) BEGIN SELECT RAISE(ABORT,'batch_variant_mismatch'); END;
CREATE TRIGGER TR_ProductVariants_StockArchive BEFORE UPDATE OF ArchivedUtc ON ProductVariants
WHEN NEW.ArchivedUtc IS NOT NULL AND OLD.ArchivedUtc IS NULL AND EXISTS(SELECT 1 FROM InventoryBalances WHERE VariantId=OLD.Id AND QuantityBaseScaled>0)
BEGIN SELECT RAISE(ABORT,'variant_has_stock'); END;

CREATE TABLE InventoryPhysicalUnits (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 VariantId INTEGER NOT NULL REFERENCES ProductVariants(Id) ON DELETE RESTRICT,
 BatchId INTEGER NULL REFERENCES ProductBatches(Id) ON DELETE RESTRICT,
 WarehouseId INTEGER NULL REFERENCES Warehouses(Id) ON DELETE RESTRICT,
 CreatedUtc TEXT NOT NULL,
 UpdatedUtc TEXT NOT NULL
);
CREATE TRIGGER TR_InventoryPhysicalUnits_BatchVariant BEFORE INSERT ON InventoryPhysicalUnits WHEN NEW.BatchId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM ProductBatches WHERE Id=NEW.BatchId AND VariantId=NEW.VariantId) BEGIN SELECT RAISE(ABORT,'batch_variant_mismatch'); END;
CREATE INDEX IX_InventoryPhysicalUnits_Location ON InventoryPhysicalUnits(WarehouseId,VariantId,BatchId);
CREATE TABLE InventoryIdentifiers (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 PhysicalUnitId INTEGER NOT NULL REFERENCES InventoryPhysicalUnits(Id) ON DELETE RESTRICT,
 Kind TEXT NOT NULL CHECK(Kind IN ('serial','imei')),
 DisplayValue TEXT NOT NULL,
 NormalizedValue TEXT NOT NULL UNIQUE,
 CreatedUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX UX_InventoryIdentifiers_UnitSerial ON InventoryIdentifiers(PhysicalUnitId,Kind) WHERE Kind='serial';
CREATE TABLE StockMovementUnits (
 MovementId INTEGER NOT NULL REFERENCES StockMovements(Id) ON DELETE RESTRICT,
 PhysicalUnitId INTEGER NOT NULL REFERENCES InventoryPhysicalUnits(Id) ON DELETE RESTRICT,
 PRIMARY KEY(MovementId,PhysicalUnitId)
);
CREATE TABLE InventoryAudit (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 DocumentId INTEGER NOT NULL REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 EventType TEXT NOT NULL,
 EventUtc TEXT NOT NULL,
 Details TEXT NULL
);
CREATE TRIGGER TR_InventoryAudit_NoUpdate BEFORE UPDATE ON InventoryAudit BEGIN SELECT RAISE(ABORT,'inventory_audit_immutable'); END;
CREATE TRIGGER TR_InventoryAudit_NoDelete BEFORE DELETE ON InventoryAudit BEGIN SELECT RAISE(ABORT,'inventory_audit_immutable'); END;
