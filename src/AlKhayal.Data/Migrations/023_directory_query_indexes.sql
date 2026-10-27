CREATE INDEX IF NOT EXISTS IX_InventoryBalances_WarehouseVariantBatch ON InventoryBalances(WarehouseId, VariantId, BatchId);
CREATE INDEX IF NOT EXISTS IX_ProductSkus_ProductActive ON ProductSkus(ProductId, RetiredUtc, Sku);
CREATE INDEX IF NOT EXISTS IX_ProductBarcodes_ProductActive ON ProductBarcodes(ProductId, RetiredUtc, Barcode);
