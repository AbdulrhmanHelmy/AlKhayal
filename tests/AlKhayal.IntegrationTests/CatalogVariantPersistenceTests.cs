using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class CatalogVariantPersistenceTests
    {
        [Fact]
        public async Task Audit_log_triggers_abort_on_update_and_delete()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "audit-user");

                long auditId;
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    auditId = connection.ExecuteScalar<long>(
                        "INSERT INTO CatalogAuditLog (ProductId, EntityType, EntityId, Action, ChangeJson, ChangedByUserId, ChangedUtc) " +
                        "VALUES (NULL, 'attribute', 1, 'create', '{\"test\":\"value\"}', @UserId, @Now); SELECT last_insert_rowid();",
                        new { UserId = userId, Now = DateTime.UtcNow.ToString("o") });

                    var updateEx = Assert.Throws<SQLiteException>(() =>
                        connection.Execute("UPDATE CatalogAuditLog SET Action='hacked' WHERE Id=@Id", new { Id = auditId }));
                    Assert.Contains("catalog_audit_immutable", updateEx.Message);

                    var deleteEx = Assert.Throws<SQLiteException>(() =>
                        connection.Execute("DELETE FROM CatalogAuditLog WHERE Id=@Id", new { Id = auditId }));
                    Assert.Contains("catalog_audit_immutable", deleteEx.Message);
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Configured_variant_product_persists_and_survives_process_restart()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "var-admin");
                var store = new SqliteCatalogStore(cs);

                var boxUnit = await store.SaveUnitAsync(new SaveUnitCommand { Code = "BOX", Name = "علبة", Symbol = "ع", IsActive = true }, "BOX", CancellationToken.None);

                // Add values to system attributes
                var sizeM = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 1, Value = "M", SortOrder = 1, IsActive = true, UserId = userId }, CancellationToken.None);
                var sizeL = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 1, Value = "L", SortOrder = 2, IsActive = true, UserId = userId }, CancellationToken.None);
                var colorRed = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 2, Value = "Red", ColorHex = "#FF0000", SortOrder = 1, IsActive = true, UserId = userId }, CancellationToken.None);

                var createRecord = new CreateProductRecord
                {
                    OperationKey = "create-shirt",
                    RequestHash = "hash-shirt",
                    Name = "قميص",
                    Description = "قميص قطني",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 1500000,
                    PurchasePriceScaled = 1000000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = true,
                        CapabilityKeys = new List<string> { "product-variants", "sizes", "colors", "multiple-units", "product-barcode-settings" },
                        Attributes = new List<ProductAttributeAssignmentConfigRecord>
                        {
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 1, ValueIds = new List<long> { sizeM.Id, sizeL.Id }, SortOrder = 1 },
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 2, ValueIds = new List<long> { colorRed.Id }, SortOrder = 2 }
                        },
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true },
                            new ProductUnitConfigRecord { UnitId = boxUnit.Id, ToBaseNumerator = 10, ToBaseDenominator = 1, IsBase = false, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                Name = "قميص — M / أحمر",
                                IsDefault = true,
                                IsActive = true,
                                AttributeValueIds = new List<long> { sizeM.Id, colorRed.Id },
                                Sku = "SHIRT-M-RED",
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "111222333444", BarcodeType = "gtin", IsPrimary = true }
                                }
                            },
                            new ProductVariantConfigRecord
                            {
                                Name = "قميص — L / أحمر",
                                IsDefault = false,
                                IsActive = true,
                                AttributeValueIds = new List<long> { sizeL.Id, colorRed.Id },
                                SellPriceOverrideScaled = 1600000,
                                Sku = "SHIRT-L-RED",
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "555666777888", BarcodeType = "gtin", IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var created = await store.CreateProductAsync(createRecord, CancellationToken.None);
                Assert.NotNull(created);
                Assert.Equal("قميص", created.Name);

                // Re-instantiate store to simulate process restart
                var restartedStore = new SqliteCatalogStore(cs);
                var detail = await restartedStore.GetProductDetailAsync(created.Id, CancellationToken.None);
                Assert.NotNull(detail);
                Assert.True(detail.VariantMode);
                Assert.Equal(2, detail.Units.Count);
                Assert.Equal(2, detail.Attributes.Count);
                Assert.Equal(2, detail.Variants.Count);

                var defaultVar = Assert.Single(detail.Variants, v => v.IsDefault);
                Assert.Equal("SHIRT-M-RED", defaultVar.Sku.Sku);
                Assert.Single(defaultVar.Barcodes);
                Assert.Equal("111222333444", defaultVar.Barcodes[0].Barcode);
                Assert.True(defaultVar.Barcodes[0].IsPrimary);

                var nonDefaultVar = Assert.Single(detail.Variants, v => !v.IsDefault);
                Assert.Equal("SHIRT-L-RED", nonDefaultVar.Sku.Sku);
                Assert.Equal(1600000L, nonDefaultVar.SellPriceOverrideScaled);

                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    var auditCount = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CatalogAuditLog WHERE ProductId=@ProductId", new { ProductId = created.Id });
                    Assert.True(auditCount >= 1);
                    Assert.Empty(connection.Query<string>("PRAGMA foreign_key_check"));
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Global_sku_and_barcode_uniqueness_includes_retired_rows()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "retire-admin");
                var store = new SqliteCatalogStore(cs);

                var create = new CreateProductRecord
                {
                    OperationKey = "create-p1",
                    RequestHash = "hash-p1",
                    Name = "منتج أول",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                IsDefault = true,
                                IsActive = true,
                                Sku = "UNIQUE-SKU-1",
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "BARCODE-1", BarcodeType = "internal", IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var p1 = await store.CreateProductAsync(create, CancellationToken.None);
                var detail1 = await store.GetProductDetailAsync(p1.Id, CancellationToken.None);
                var v1 = detail1.Variants[0];
                var b1 = v1.Barcodes[0];

                // Update product 1: retire the barcode and change the SKU
                var update = new UpdateProductRecord
                {
                    ProductId = p1.Id,
                    Name = "منتج أول معدل",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    Version = p1.Version,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { Id = detail1.Units[0].Id, UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                Id = v1.Id,
                                IsDefault = true,
                                IsActive = true,
                                Sku = "UNIQUE-SKU-NEW",
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Id = b1.Id, Barcode = b1.Barcode, BarcodeType = b1.BarcodeType, Retire = true }
                                }
                            }
                        }
                    }
                };

                await store.UpdateProductAsync(update, CancellationToken.None);

                // Now try creating product 2 with the retired SKU-1
                var dupSkuCreate = new CreateProductRecord
                {
                    OperationKey = "create-p2-sku",
                    RequestHash = "hash-p2-sku",
                    Name = "منتج ثان",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord { IsDefault = true, IsActive = true, Sku = "UNIQUE-SKU-1" }
                        }
                    }
                };
                var skuEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.CreateProductAsync(dupSkuCreate, CancellationToken.None));
                Assert.Equal("duplicate_sku", skuEx.Code);

                // Now try creating product 3 with the retired BARCODE-1
                var dupBcCreate = new CreateProductRecord
                {
                    OperationKey = "create-p3-bc",
                    RequestHash = "hash-p3-bc",
                    Name = "منتج ثالث",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                IsDefault = true,
                                IsActive = true,
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "BARCODE-1", BarcodeType = "internal" }
                                }
                            }
                        }
                    }
                };
                var bcEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.CreateProductAsync(dupBcCreate, CancellationToken.None));
                Assert.Equal("duplicate_barcode", bcEx.Code);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Barcode_lookup_returns_exact_identity_and_retired_status()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "lookup-admin");
                var store = new SqliteCatalogStore(cs);

                var create = new CreateProductRecord
                {
                    OperationKey = "create-scan-prod",
                    RequestHash = "hash-scan",
                    Name = "عصير",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 250000,
                    PurchasePriceScaled = 150000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                IsDefault = true,
                                IsActive = true,
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "SCAN-ACTIVE-01", BarcodeType = "gtin", IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var p = await store.CreateProductAsync(create, CancellationToken.None);

                // Active barcode lookup
                var found = await store.LookupBarcodeAsync("scan-active-01", CancellationToken.None);
                Assert.NotNull(found);
                Assert.Equal("SCAN-ACTIVE-01", found.Code);
                Assert.Equal("عصير", found.ProductName);
                Assert.False(found.IsBarcodeRetired);
                Assert.True(found.IsProductActive);
                Assert.Equal(250000L, found.EffectiveSalePriceScaled);

                // Unknown barcode lookup
                var notFound = await store.LookupBarcodeAsync("UNKNOWN-CODE", CancellationToken.None);
                Assert.Null(notFound);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Conversion_locks_when_barcode_references_unit()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "lock-admin");
                var store = new SqliteCatalogStore(cs);

                var packUnit = await store.SaveUnitAsync(new SaveUnitCommand { Code = "PACK", Name = "طرد", Symbol = "ط", IsActive = true }, "PACK", CancellationToken.None);

                var create = new CreateProductRecord
                {
                    OperationKey = "create-locked-unit",
                    RequestHash = "hash-locked-unit",
                    Name = "بسكويت",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 50000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        CapabilityKeys = new List<string> { "multiple-units", "product-barcode-settings" },
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true },
                            new ProductUnitConfigRecord { UnitId = packUnit.Id, ToBaseNumerator = 12, ToBaseDenominator = 1, IsBase = false, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                IsDefault = true,
                                IsActive = true,
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "PACK-BC-123", BarcodeType = "internal", ProductUnitId = packUnit.Id, IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var p = await store.CreateProductAsync(create, CancellationToken.None);
                var detail = await store.GetProductDetailAsync(p.Id, CancellationToken.None);
                var packUnitRow = Assert.Single(detail.Units, u => !u.IsBase);
                Assert.True(packUnitRow.IsLocked);

                // Trying to edit numerator/ratio of the locked unit fails with conversion_locked
                var update = new UpdateProductRecord
                {
                    ProductId = p.Id,
                    Name = "بسكويت",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 50000,
                    IsActive = true,
                    Version = p.Version,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { Id = detail.Units[0].Id, UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true },
                            new ProductUnitConfigRecord { Id = packUnitRow.Id, UnitId = packUnit.Id, ToBaseNumerator = 24, ToBaseDenominator = 1, IsBase = false, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                Id = detail.Variants[0].Id,
                                IsDefault = true,
                                IsActive = true,
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Id = detail.Variants[0].Barcodes[0].Id, Barcode = "PACK-BC-123", BarcodeType = "internal", IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var lockEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.UpdateProductAsync(update, CancellationToken.None));
                Assert.Equal("conversion_locked", lockEx.Code);

                // Trying to change base unit when barcode exists fails with base_unit_locked
                update.BaseUnitId = packUnit.Id;
                update.Configuration.Units[1].ToBaseNumerator = 12; // revert ratio
                var baseEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.UpdateProductAsync(update, CancellationToken.None));
                Assert.Equal("base_unit_locked", baseEx.Code);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Transition_from_simple_to_variant_converts_default_row_and_locks_variant_mode()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "trans-admin");
                var store = new SqliteCatalogStore(cs);

                var sizeS = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 1, Value = "S", SortOrder = 1, IsActive = true, UserId = userId }, CancellationToken.None);
                var sizeM = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 1, Value = "M", SortOrder = 2, IsActive = true, UserId = userId }, CancellationToken.None);

                // Create simple product
                var create = new CreateProductRecord
                {
                    OperationKey = "create-simple-trans",
                    RequestHash = "hash-trans",
                    Name = "حذاء",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 3000000,
                    IsActive = true,
                    UserId = userId
                };

                var p = await store.CreateProductAsync(create, CancellationToken.None);
                var detailBefore = await store.GetProductDetailAsync(p.Id, CancellationToken.None);
                Assert.False(detailBefore.VariantMode);
                var defaultVarBeforeId = detailBefore.Variants[0].Id;

                // Update to variant mode
                var update = new UpdateProductRecord
                {
                    ProductId = p.Id,
                    Name = "حذاء",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 3000000,
                    IsActive = true,
                    Version = p.Version,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = true,
                        CapabilityKeys = new List<string> { "product-variants", "sizes" },
                        Attributes = new List<ProductAttributeAssignmentConfigRecord>
                        {
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 1, ValueIds = new List<long> { sizeS.Id, sizeM.Id }, SortOrder = 1 }
                        },
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { Id = detailBefore.Units[0].Id, UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                Id = defaultVarBeforeId,
                                Name = "حذاء — S",
                                IsDefault = true,
                                IsActive = true,
                                AttributeValueIds = new List<long> { sizeS.Id }
                            },
                            new ProductVariantConfigRecord
                            {
                                Name = "حذاء — M",
                                IsDefault = false,
                                IsActive = true,
                                AttributeValueIds = new List<long> { sizeM.Id }
                            }
                        }
                    }
                };

                var updated = await store.UpdateProductAsync(update, CancellationToken.None);
                var detailAfter = await store.GetProductDetailAsync(updated.Id, CancellationToken.None);
                Assert.True(detailAfter.VariantMode);
                Assert.Equal(2, detailAfter.Variants.Count);

                var defaultVarAfter = Assert.Single(detailAfter.Variants, v => v.IsDefault);
                Assert.Equal(defaultVarBeforeId, defaultVarAfter.Id); // ID preserved!

                // Attempting to revert back to simple mode fails with variant_mode_locked
                var revert = new UpdateProductRecord
                {
                    ProductId = p.Id,
                    Name = "حذاء",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 3000000,
                    IsActive = true,
                    Version = updated.Version,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false
                    }
                };

                var revEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.UpdateProductAsync(revert, CancellationToken.None));
                Assert.Equal("variant_mode_locked", revEx.Code);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Transactionality_rolls_back_entire_aggregate_on_child_write_failure()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "tx-admin");
                var store = new SqliteCatalogStore(cs);

                // Invalid ratio in unit (numerator 0)
                var invalidCreate = new CreateProductRecord
                {
                    OperationKey = "create-rollback-test",
                    RequestHash = "hash-rb",
                    Name = "منتج متراجع",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true },
                            new ProductUnitConfigRecord { UnitId = 1, ToBaseNumerator = 0, ToBaseDenominator = 1, IsBase = false, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        }
                    }
                };

                await Assert.ThrowsAsync<CatalogStoreException>(() => store.CreateProductAsync(invalidCreate, CancellationToken.None));

                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    // Verify no product was created
                    var prodCount = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Products WHERE Name='منتج متراجع'");
                    Assert.Equal(0, prodCount);
                    // Verify no audit log was created
                    var auditCount = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CatalogAuditLog");
                    Assert.Equal(0, auditCount);
                    // Verify no operation key was recorded
                    var opCount = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CatalogOperations WHERE OperationKey='create-rollback-test'");
                    Assert.Equal(0, opCount);
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Aggregate_create_accepts_free_text_sizes_and_colors_and_generates_combinations()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "free-text-admin");
                var store = new SqliteCatalogStore(cs);
                var product = await store.CreateAggregateProductAsync(new CreateProductRecord
                {
                    OperationKey = "free-text-variants", RequestHash = "free-text-variants-hash", Name = "تيشيرت",
                    BaseUnitId = 1, ProductType = "stock", SellPriceScaled = 2500000, PurchasePriceScaled = 1500000,
                    IsActive = true, UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = true,
                        CapabilityKeys = new List<string> { "sizes", "colors", "product-variants" },
                        Attributes = new List<ProductAttributeAssignmentConfigRecord>
                        {
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 1, Values = new List<string> { "M", "L" }, SortOrder = 0 },
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 2, Values = new List<string> { "أسود", "أبيض" }, SortOrder = 1 }
                        }
                    }
                }, CancellationToken.None);

                var detail = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                Assert.True(detail.VariantMode);
                Assert.Equal(2, detail.Attributes.Count);
                Assert.Equal(4, detail.Variants.Count);
                Assert.Equal(4, detail.Variants.Select(v => v.CombinationKey).Distinct().Count());
                Assert.Contains(detail.Attributes.SelectMany(a => a.Values), value => value.Value == "M");
                Assert.Contains(detail.Attributes.SelectMany(a => a.Values), value => value.Value == "أسود");
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Simple_aggregate_create_materializes_one_exact_base_unit_and_edits()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "simple-form-admin");
                var store = new SqliteCatalogStore(cs);
                var product = await store.CreateAggregateProductAsync(new CreateProductRecord
                {
                    OperationKey = "simple-form", RequestHash = "simple-form-hash", Name = "تي شيرت",
                    BaseUnitId = 1, ProductType = "stock", SellPriceScaled = 5050000,
                    PurchasePriceScaled = 2000000, IsActive = true, UserId = userId,
                    Configuration = new ProductConfigurationRecord { CapabilityKeys = new List<string>(), Units = new List<ProductUnitConfigRecord>() }
                }, CancellationToken.None);
                var detail = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                var baseUnit = Assert.Single(detail.Units);
                Assert.True(baseUnit.IsBase);
                Assert.Equal(1, baseUnit.UnitId);
                Assert.Equal(1, baseUnit.ToBaseNumerator);
                Assert.Equal(1, baseUnit.ToBaseDenominator);

                await store.UpdateAggregateProductAsync(new UpdateProductRecord
                {
                    ProductId = product.Id, Name = "تي شيرت معدل", BaseUnitId = 1, ProductType = "stock",
                    SellPriceScaled = 5050000, PurchasePriceScaled = 2000000, IsActive = true,
                    Version = product.Version, UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        CapabilityKeys = new List<string>(),
                        Units = new List<ProductUnitConfigRecord> { new ProductUnitConfigRecord { Id = baseUnit.Id, UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true } },
                        Variants = detail.Variants.Select(v => new ProductVariantConfigRecord { Id = v.Id, Name = v.Name, IsDefault = v.IsDefault, IsActive = v.IsActive, AttributeValueIds = v.AttributeValueIds.ToList(), Barcodes = new List<ProductBarcodeConfigRecord>() }).ToList()
                    }
                }, CancellationToken.None);
                var edited = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                Assert.Equal(baseUnit.Id, Assert.Single(edited.Units).Id);
                Assert.Equal("تي شيرت معدل", edited.Product.Name);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Free_text_initial_save_keeps_identifiers_and_rolls_back_invalid_units()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "initial-form-admin");
                var store = new SqliteCatalogStore(cs);
                var create = new CreateProductRecord
                {
                    OperationKey = "initial-form", RequestHash = "initial-form-hash", Name = "قميص",
                    BaseUnitId = 1, ProductType = "stock", SellPriceScaled = 5050000, IsActive = true, UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = true, CapabilityKeys = new List<string> { "sizes", "colors", "product-variants" },
                        Units = new List<ProductUnitConfigRecord> { new ProductUnitConfigRecord { UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true } },
                        Attributes = new List<ProductAttributeAssignmentConfigRecord>
                        {
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 1, Values = new List<string> { "M", "L" } },
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 2, Values = new List<string> { "أسود", "red" } }
                        },
                        Variants = new List<ProductVariantConfigRecord> { new ProductVariantConfigRecord { IsDefault = true, IsActive = true, Sku = "FORM-SKU", Barcodes = new List<ProductBarcodeConfigRecord> { new ProductBarcodeConfigRecord { Barcode = "FORM-BC", BarcodeType = "internal", IsPrimary = true } } } }
                    }
                };
                var product = await store.CreateAggregateProductAsync(create, CancellationToken.None);
                var detail = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                Assert.Equal(4, detail.Variants.Count);
                Assert.Equal("FORM-SKU", detail.Variants.Single(v => v.IsDefault).Sku.Sku);
                Assert.Contains(detail.Variants.Single(v => v.IsDefault).Barcodes, b => b.Barcode == "FORM-BC");

                await store.UpdateAggregateProductAsync(new UpdateProductRecord
                {
                    ProductId = product.Id, Name = "قميص معدل", BaseUnitId = 1, ProductType = "stock",
                    SellPriceScaled = 5050000, IsActive = true, Version = product.Version, UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = true, CapabilityKeys = new List<string> { "sizes", "colors", "product-variants" },
                        Units = detail.Units.Select(u => new ProductUnitConfigRecord { Id = u.Id, UnitId = u.UnitId, ToBaseNumerator = u.ToBaseNumerator, ToBaseDenominator = u.ToBaseDenominator, IsBase = u.IsBase, IsSaleUnit = u.IsSaleUnit, IsPurchaseUnit = u.IsPurchaseUnit, IsActive = u.IsActive }).ToList(),
                        Attributes = detail.Attributes.Select(a => new ProductAttributeAssignmentConfigRecord { AttributeId = a.AttributeId, Values = a.Values.Select(v => v.Value).ToList() }).ToList(),
                        Variants = detail.Variants.Select(v => new ProductVariantConfigRecord
                        {
                            Id = v.Id, Name = v.Name, AttributeValueIds = v.AttributeValueIds.ToList(),
                            IsDefault = v.IsDefault, IsActive = v.IsActive, Sku = v.Sku?.Sku,
                            Barcodes = v.Barcodes.Select(b => new ProductBarcodeConfigRecord { Id = b.Id, Barcode = b.Barcode, BarcodeType = b.BarcodeType, ProductUnitId = b.ProductUnitId, IsPrimary = b.IsPrimary }).ToList()
                        }).ToList()
                    }
                }, CancellationToken.None);
                var edited = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                Assert.Equal(detail.Variants.Select(v => v.Id).OrderBy(id => id), edited.Variants.Select(v => v.Id).OrderBy(id => id));
                Assert.Equal("FORM-SKU", edited.Variants.Single(v => v.IsDefault).Sku.Sku);
                Assert.Contains(edited.Variants.Single(v => v.IsDefault).Barcodes, b => b.Barcode == "FORM-BC" && b.RetiredUtc == null);

                create.OperationKey = "invalid-base-ratio"; create.RequestHash = "invalid-base-ratio-hash";
                create.Configuration.Units[0].ToBaseNumerator = 2;
                await Assert.ThrowsAsync<CatalogStoreException>(() => store.CreateAggregateProductAsync(create, CancellationToken.None));
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Products WHERE Id=@Id", new { product.Id }));
                    Assert.Equal(0, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CatalogOperations WHERE OperationKey='invalid-base-ratio'"));
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Paginated_catalog_summarizes_saved_values_and_edit_reconciles_variant_identity()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "summary-admin");
                var store = new SqliteCatalogStore(cs);
                var service = new CatalogService(store);
                var simple = await store.CreateAggregateProductAsync(new CreateProductRecord
                {
                    OperationKey = "summary-simple", RequestHash = "summary-simple-hash", Name = "منتج عادي",
                    BaseUnitId = 1, ProductType = "stock", SellPriceScaled = 10000, IsActive = true, UserId = userId
                }, CancellationToken.None);
                var product = await store.CreateAggregateProductAsync(new CreateProductRecord
                {
                    OperationKey = "summary-shirt", RequestHash = "summary-shirt-hash", Name = "تي شيرت",
                    BaseUnitId = 1, ProductType = "stock", SellPriceScaled = 5050000, IsActive = true, UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = true, CapabilityKeys = new List<string> { "sizes", "colors", "product-variants", "batch-tracking", "expiry-dates" },
                        Attributes = new List<ProductAttributeAssignmentConfigRecord>
                        {
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 1, Values = new List<string> { "M", "L" } },
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 2, Values = new List<string> { "أسود", "أبيض" } }
                        },
                        Variants = new List<ProductVariantConfigRecord> { new ProductVariantConfigRecord { IsDefault = true, IsActive = true, Sku = "SUMMARY-SKU", Barcodes = new List<ProductBarcodeConfigRecord> { new ProductBarcodeConfigRecord { Barcode = "SUMMARY-BC", BarcodeType = "internal", IsPrimary = true } } } }
                    }
                }, CancellationToken.None);

                var page = await service.ListProductsAsync(new ProductQuery { Page = 1, PageSize = 10 }, CancellationToken.None);
                Assert.True(page.Succeeded);
                Assert.Equal(2, page.Value.TotalCount);
                Assert.Equal(2, page.Value.Items.Count);
                var plain = Assert.Single(page.Value.Items, p => p.Id == simple.Id);
                Assert.Empty(plain.SizeValues);
                Assert.Empty(plain.ColorValues);
                Assert.Equal(1, plain.VariantCount);
                var shirt = Assert.Single(page.Value.Items, p => p.Id == product.Id);
                Assert.Equal(new[] { "M", "L" }, shirt.SizeValues);
                Assert.Equal(new[] { "أسود", "أبيض" }, shirt.ColorValues);
                Assert.Equal(4, shirt.VariantCount);
                Assert.True(shirt.HasExpiryTracking);
                var detail = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                Assert.Equal(4, detail.Variants.Count);
                var originalIds = detail.Variants.Select(v => v.Id).OrderBy(id => id).ToArray();

                await store.UpdateAggregateProductAsync(UpdateFromDetail(detail, userId, new[] { "M", "L", "XL" }, new[] { "أسود", "أبيض" }), CancellationToken.None);
                detail = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                Assert.Equal(6, detail.Variants.Count(v => v.ArchivedUtc == null));
                Assert.All(originalIds, id => Assert.Contains(detail.Variants, v => v.Id == id && v.ArchivedUtc == null));
                Assert.Equal("SUMMARY-SKU", detail.Variants.Single(v => v.IsDefault).Sku.Sku);

                await store.UpdateAggregateProductAsync(UpdateFromDetail(detail, userId, new[] { "M", "L" }, new[] { "أسود", "أبيض" }), CancellationToken.None);
                detail = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                Assert.Equal(4, detail.Variants.Count(v => v.ArchivedUtc == null));
                Assert.Equal(2, detail.Variants.Count(v => v.ArchivedUtc != null));
                Assert.All(originalIds, id => Assert.Contains(detail.Variants, v => v.Id == id && v.ArchivedUtc == null));

                var error = await Assert.ThrowsAsync<CatalogStoreException>(() => store.UpdateAggregateProductAsync(
                    UpdateFromDetail(detail, userId, new[] { "L" }, new[] { "أسود", "أبيض" }), CancellationToken.None));
                Assert.Equal("variant_in_use", error.Code);
                var after = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                Assert.Equal(4, after.Variants.Count(v => v.ArchivedUtc == null));
                Assert.Equal("SUMMARY-SKU", after.Variants.Single(v => v.IsDefault).Sku.Sku);
            }
            finally { Cleanup(path); }
        }

        private static UpdateProductRecord UpdateFromDetail(CatalogProductDetailRecord detail, long userId, IEnumerable<string> sizes, IEnumerable<string> colors)
        {
            return new UpdateProductRecord
            {
                ProductId = detail.Product.Id, Name = detail.Product.Name, BaseUnitId = detail.Product.BaseUnitId,
                ProductType = detail.Product.ProductType, SellPriceScaled = detail.Product.SellPriceScaled,
                IsActive = detail.Product.IsActive, Version = detail.Product.Version, UserId = userId,
                Configuration = new ProductConfigurationRecord
                {
                    VariantMode = true, CapabilityKeys = new List<string> { "sizes", "colors", "product-variants", "batch-tracking", "expiry-dates" },
                    Units = detail.Units.Select(u => new ProductUnitConfigRecord { Id = u.Id, UnitId = u.UnitId, ToBaseNumerator = u.ToBaseNumerator, ToBaseDenominator = u.ToBaseDenominator, IsBase = u.IsBase, IsSaleUnit = u.IsSaleUnit, IsPurchaseUnit = u.IsPurchaseUnit, IsActive = u.IsActive }).ToList(),
                    Attributes = new List<ProductAttributeAssignmentConfigRecord>
                    {
                        new ProductAttributeAssignmentConfigRecord { AttributeId = 1, Values = sizes.ToList() },
                        new ProductAttributeAssignmentConfigRecord { AttributeId = 2, Values = colors.ToList() }
                    },
                    Variants = detail.Variants.Select(v => new ProductVariantConfigRecord
                    {
                        Id = v.Id, Name = v.Name, IsDefault = v.IsDefault, IsActive = v.IsActive,
                        Archive = v.ArchivedUtc != null, AttributeValueIds = v.AttributeValueIds.ToList(), Sku = v.Sku?.Sku,
                        Barcodes = v.Barcodes.Select(b => new ProductBarcodeConfigRecord { Id = b.Id, Barcode = b.Barcode, BarcodeType = b.BarcodeType, ProductUnitId = b.ProductUnitId, IsPrimary = b.IsPrimary, Retire = b.RetiredUtc != null }).ToList()
                    }).ToList()
                }
            };
        }

        private static async Task<long> CreateUserAsync(string cs, string username)
        {
            var users = new SqliteUserStore(cs);
            return await users.CreateAsync(username, "مستخدم تجريبي", new PasswordHasher().Hash("secret"), "administrator", CancellationToken.None);
        }

        private static string Temp() { return Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db"); }
        private static void Cleanup(string path) { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
