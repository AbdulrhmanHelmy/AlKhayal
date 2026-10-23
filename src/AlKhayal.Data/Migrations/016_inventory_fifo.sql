-- Existing balances retain their immutable ledger value. Their FIFO basis is unknown,
-- even when the old weighted-average total happens to be known.
ALTER TABLE InventoryDocumentLines ADD COLUMN EnteredUnitCostScaled INTEGER NULL CHECK(EnteredUnitCostScaled IS NULL OR EnteredUnitCostScaled>=0);
ALTER TABLE InventoryDocumentLines ADD COLUMN EnteredSalePriceScaled INTEGER NULL CHECK(EnteredSalePriceScaled IS NULL OR EnteredSalePriceScaled>=0);
CREATE TABLE InventoryCostLayers (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 WarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id),
 VariantId INTEGER NOT NULL REFERENCES ProductVariants(Id),
 BatchId INTEGER NULL REFERENCES ProductBatches(Id),
 SourceMovementId INTEGER NULL REFERENCES StockMovements(Id),
 ParentLayerId INTEGER NULL REFERENCES InventoryCostLayers(Id),
 OriginMovementId INTEGER NULL REFERENCES StockMovements(Id),
 AcquiredUtc TEXT NULL,
 Basis TEXT NOT NULL CHECK(Basis IN ('receipt','transfer','unresolved','reconciled_opening')),
 QuantityScaled INTEGER NOT NULL CHECK(QuantityScaled>0),
 ValueScaled INTEGER NOT NULL CHECK(ValueScaled>=0),
 UnitCostScaled INTEGER NULL CHECK(UnitCostScaled IS NULL OR UnitCostScaled>=0),
 RemainingQuantityScaled INTEGER NOT NULL CHECK(RemainingQuantityScaled BETWEEN 0 AND QuantityScaled),
 RemainingValueScaled INTEGER NOT NULL CHECK(RemainingValueScaled BETWEEN 0 AND ValueScaled),
 CHECK(RemainingQuantityScaled<>0 OR RemainingValueScaled=0)
);
CREATE INDEX IX_InventoryCostLayers_Available ON InventoryCostLayers(WarehouseId,VariantId,BatchId,OriginMovementId,Id) WHERE RemainingQuantityScaled>0;
CREATE TABLE InventoryCostAllocations (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 MovementId INTEGER NOT NULL REFERENCES StockMovements(Id),
 LayerId INTEGER NOT NULL REFERENCES InventoryCostLayers(Id),
 QuantityScaled INTEGER NOT NULL CHECK(QuantityScaled>0),
 ValueScaled INTEGER NOT NULL CHECK(ValueScaled>=0),
 UNIQUE(MovementId,LayerId)
);
CREATE TRIGGER TR_InventoryCostAllocations_NoUpdate BEFORE UPDATE ON InventoryCostAllocations BEGIN SELECT RAISE(ABORT,'cost_allocation_immutable'); END;
CREATE TRIGGER TR_InventoryCostAllocations_NoDelete BEFORE DELETE ON InventoryCostAllocations BEGIN SELECT RAISE(ABORT,'cost_allocation_immutable'); END;
CREATE TRIGGER TR_InventoryCostLayers_NoDelete BEFORE DELETE ON InventoryCostLayers BEGIN SELECT RAISE(ABORT,'cost_layer_immutable'); END;
CREATE TRIGGER TR_InventoryCostLayers_Metadata BEFORE UPDATE ON InventoryCostLayers
WHEN NEW.WarehouseId<>OLD.WarehouseId OR NEW.VariantId<>OLD.VariantId OR NEW.BatchId IS NOT OLD.BatchId
 OR NEW.SourceMovementId IS NOT OLD.SourceMovementId OR NEW.ParentLayerId IS NOT OLD.ParentLayerId
 OR NEW.OriginMovementId IS NOT OLD.OriginMovementId OR NEW.AcquiredUtc IS NOT OLD.AcquiredUtc
 OR NEW.Basis<>OLD.Basis OR NEW.QuantityScaled<>OLD.QuantityScaled OR NEW.ValueScaled<>OLD.ValueScaled
 OR NEW.UnitCostScaled IS NOT OLD.UnitCostScaled OR NEW.RemainingQuantityScaled>OLD.RemainingQuantityScaled
 OR NEW.RemainingValueScaled>OLD.RemainingValueScaled
BEGIN SELECT RAISE(ABORT,'cost_layer_immutable'); END;
INSERT INTO InventoryCostLayers(WarehouseId,VariantId,BatchId,Basis,AcquiredUtc,QuantityScaled,ValueScaled,RemainingQuantityScaled,RemainingValueScaled)
 SELECT WarehouseId,VariantId,BatchId,'unresolved',strftime('%Y-%m-%dT%H:%M:%f','now')||'0000Z',QuantityBaseScaled,ValueScaled,QuantityBaseScaled,ValueScaled
 FROM InventoryBalances WHERE QuantityBaseScaled>0;
CREATE TABLE InventoryCostReconciliations (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 LegacyLayerId INTEGER NOT NULL UNIQUE REFERENCES InventoryCostLayers(Id),
 ReplacementLayerId INTEGER NOT NULL UNIQUE REFERENCES InventoryCostLayers(Id),
 Evidence TEXT NOT NULL CHECK(length(trim(Evidence))>0),
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id),
 CreatedUtc TEXT NOT NULL,
 OperationKey TEXT NOT NULL UNIQUE,
 RequestHash TEXT NOT NULL
);
CREATE TRIGGER TR_InventoryCostReconciliations_NoUpdate BEFORE UPDATE ON InventoryCostReconciliations BEGIN SELECT RAISE(ABORT,'cost_reconciliation_immutable'); END;
CREATE TRIGGER TR_InventoryCostReconciliations_NoDelete BEFORE DELETE ON InventoryCostReconciliations BEGIN SELECT RAISE(ABORT,'cost_reconciliation_immutable'); END;
