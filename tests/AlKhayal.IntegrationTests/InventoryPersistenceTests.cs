using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Features;
using AlKhayal.Data.BusinessActivities;
using AlKhayal.Data.Features;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class InventoryPersistenceTests
    {
        private sealed class Fixture : IDisposable
        {
            public readonly string Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".db");
            public readonly string Cs;
            public readonly SqliteInventoryStore Store;
            public readonly InventoryService InventoryService;
            public long ProductId,VariantId,UnitId;
            public Fixture(bool includeFifo=true)
            {
                Cs=Db.ConnectionStringFor(Path);
                if(includeFifo)Migrator.Run(Cs);
                else
                {
                    using(var c=new SQLiteConnection(Cs))
                    {
                        c.Open();c.Execute("PRAGMA foreign_keys=ON; CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL)");
                        var assembly=typeof(Migrator).Assembly;
                        foreach(var resource in assembly.GetManifestResourceNames().Where(x=>x.EndsWith(".sql")&&!x.EndsWith("016_inventory_fifo.sql")).OrderBy(x=>x,StringComparer.Ordinal))
                        {
                            using(var stream=assembly.GetManifestResourceStream(resource))using(var reader=new StreamReader(stream))c.Execute(reader.ReadToEnd());
                            c.Execute("INSERT INTO SchemaVersion(Name,AppliedAt) VALUES(@Name,'2026-01-01')",new{Name=resource});
                        }
                    }
                }
                Store=new SqliteInventoryStore(Cs);
                InventoryService=new InventoryService(Store,new FeatureService(new SqliteFeatureStore(Cs),new SqliteBusinessActivityStore(Cs)));
                using(var c=new SQLiteConnection(Cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('inventory-admin','test-only','2026-01-01T00:00:00Z')");}
                var catalog=new CatalogService(new SqliteCatalogStore(Cs));
                var created=catalog.CreateProductAsync(new CreateProductCommand{OperationKey=Guid.NewGuid().ToString(),Name="شاي",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(created.Succeeded,created.ErrorMessage);ProductId=created.Value.Id;
                using(var c=new SQLiteConnection(Cs)){c.Open();VariantId=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=ProductId});UnitId=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=ProductId});}
            }
            public InventoryPostCommand Command(string type,string key,string qty,long warehouse=1,long? destination=null,string cost="2")
            {return new InventoryPostCommand{Type=type,OperationKey=key,WarehouseId=warehouse,DestinationWarehouseId=destination,UserId=1,Reason="تصحيح موثق",Lines=new[]{new InventoryLineInput{VariantId=VariantId,ProductUnitId=UnitId,Quantity=qty,UnitCost=cost,SalePrice="12"}}};}
            public void Dispose(){SQLiteConnection.ClearAllPools();if(File.Exists(Path))File.Delete(Path);}
        }

        [Fact]
        public async Task Balance_page_searches_full_warehouse_by_name_sku_and_barcode()
        {
            using(var f=new Fixture())
            {
                Assert.True((await f.InventoryService.PostAsync(f.Command("manual_receipt","page-receipt","3"),CancellationToken.None)).Succeeded);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();c.Execute("INSERT INTO ProductSkus(ProductId,VariantId,Sku,NormalizedSku,CreatedUtc,CreatedByUserId) VALUES(@P,@V,'STAB-SKU','STAB-SKU','2026-01-01',1); INSERT INTO ProductBarcodes(ProductId,VariantId,ProductUnitId,Barcode,NormalizedBarcode,BarcodeType,CreatedUtc,CreatedByUserId) VALUES(@P,@V,@U,'STAB-BC','STAB-BC','internal','2026-01-01',1)",new{P=f.ProductId,V=f.VariantId,U=f.UnitId});}
                var page=await f.Store.BalancePageAsync(1,"شاي",1,1,CancellationToken.None);
                Assert.Equal(1,page.TotalCount);Assert.Equal(3000000,page.Items.Single().QuantityBaseScaled);
                Assert.Single((await f.Store.BalancePageAsync(1,"STAB-SKU",1,25,CancellationToken.None)).Items);
                Assert.Single((await f.Store.BalancePageAsync(1,"STAB-BC",1,25,CancellationToken.None)).Items);
                Assert.Empty((await f.Store.BalancePageAsync(1,"missing",1,25,CancellationToken.None)).Items);
                Assert.Empty((await f.Store.BalancePageAsync(999,"شاي",1,25,CancellationToken.None)).Items);
            }
        }

        [Fact]
        public async Task Manual_stock_and_transfer_are_atomic_idempotent_and_persist()
        {
            using(var f=new Fixture())
            {
                var svc=f.InventoryService;
                var features=new FeatureService(new SqliteFeatureStore(f.Cs),new SqliteBusinessActivityStore(f.Cs));
                var enabled=await features.SetAsync(new SetFeatureCommand{Key="multiple-warehouses",Kind=FeatureKinds.Module,IsEnabled=true,ChangedByUserId=1},CancellationToken.None);Assert.True(enabled.Succeeded,enabled.ErrorMessage);
                var second=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="مخزن ثان",IsActive=true},true,CancellationToken.None);Assert.True(second.Succeeded,second.ErrorMessage);
                var receive=f.Command("manual_receipt","receive-1","10");var first=await svc.PostAsync(receive,CancellationToken.None);Assert.True(first.Succeeded,first.ErrorMessage);
                var replay=await svc.PostAsync(receive,CancellationToken.None);Assert.True(replay.Succeeded);Assert.True(replay.Value.Replayed);Assert.Equal(first.Value.DocumentId,replay.Value.DocumentId);
                Assert.Equal("operation_key_conflict",(await svc.PostAsync(f.Command("manual_receipt","receive-1","8"),CancellationToken.None)).ErrorCode);
                var issue=await svc.PostAsync(f.Command("manual_issue","issue-1","3"),CancellationToken.None);Assert.True(issue.Succeeded,issue.ErrorMessage);
                var transfer=await svc.PostAsync(f.Command("transfer","transfer-1","2",1,second.Value.Id,null),CancellationToken.None);Assert.True(transfer.Succeeded,transfer.ErrorMessage);
                var invalid=await svc.PostAsync(f.Command("manual_issue","too-much","6"),CancellationToken.None);Assert.Equal("insufficient_stock",invalid.ErrorCode);
                var balances=await new SqliteInventoryStore(f.Cs).BalancesAsync(null,null,CancellationToken.None);
                Assert.Equal(5000000L,balances.Single(x=>x.WarehouseId==1).QuantityBaseScaled);
                Assert.Equal(2000000L,balances.Single(x=>x.WarehouseId==second.Value.Id).QuantityBaseScaled);
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(4,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements"));Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments"));}
            }
        }

        [Fact]
        public async Task Receipt_requires_actual_purchase_and_sale_prices_and_never_mutates_the_request()
        {
            using(var f=new Fixture())
            {
                var cmd=f.Command("manual_receipt","actual-cost","4",cost:null);
                Assert.Equal("cost_required",(await f.InventoryService.PostAsync(cmd,CancellationToken.None)).ErrorCode);
                cmd.Lines[0].UnitCost="100";cmd.Lines[0].SalePrice=null;
                Assert.Equal("sale_price_required",(await f.InventoryService.PostAsync(cmd,CancellationToken.None)).ErrorCode);
                cmd.Lines[0].SalePrice="150";
                var posted=await f.InventoryService.PostAsync(cmd,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.Equal("100",cmd.Lines[0].UnitCost);
                var replay=await f.InventoryService.PostAsync(cmd,CancellationToken.None);Assert.True(replay.Value.Replayed);
                Assert.Equal(posted.Value.DocumentId,replay.Value.DocumentId);
                cmd.Lines[0].SalePrice="151";
                Assert.Equal("operation_key_conflict",(await f.InventoryService.PostAsync(cmd,CancellationToken.None)).ErrorCode);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers"));Assert.Equal(4000000,c.ExecuteScalar<long>("SELECT ValueScaled FROM StockMovements"));Assert.Equal(1500000,c.ExecuteScalar<long>("SELECT SellPriceOverrideScaled FROM ProductVariants WHERE Id=@Id",new{Id=f.VariantId}));Assert.Equal(90000,c.ExecuteScalar<long>("SELECT PurchasePriceScaled FROM ProductDetails WHERE ProductId=@Id",new{Id=f.ProductId}));Assert.Equal(1500000,c.ExecuteScalar<long>("SELECT SellPriceScaled FROM ProductDetails WHERE ProductId=@Id",new{Id=f.ProductId}));}
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task Failed_multiline_post_rolls_back_every_effect()
        {
            using(var f=new Fixture())
            {
                var command=f.Command("manual_receipt","bad-lines","2");command.Lines=new[]{command.Lines[0],new InventoryLineInput{VariantId=9999,ProductUnitId=f.UnitId,Quantity="1",UnitCost="2",SalePrice="12"}};
                var result=await f.Store.PostAsync(command,"hash",CancellationToken.None);Assert.Equal("invalid_variant",result.ErrorCode);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments"));}
            }
        }

        [Fact]
        public async Task Fresh_migration_has_foreign_keys_and_immutable_movements()
        {
            using(var f=new Fixture())
            {
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(31,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));Assert.Equal(0,c.ExecuteScalar<long>("PRAGMA foreign_key_check"));}
                var receipt=await f.Store.PostAsync(f.Command("opening","open","1"),"hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Throws<SQLiteException>(()=>c.Execute("DELETE FROM StockMovements"));
                    Assert.Throws<SQLiteException>(()=>c.Execute("UPDATE InventoryBalances SET QuantityBaseScaled=0"));
                    Assert.Throws<SQLiteException>(()=>c.Execute("UPDATE InventoryDocuments SET Reason='تعديل' WHERE Status='posted'"));
                    Assert.Throws<SQLiteException>(()=>c.Execute("DELETE FROM InventoryDocumentLines"));
                }
            }
        }

        [Fact]
        public async Task Batch_expiry_and_serial_imei_follow_the_same_ledger()
        {
            using(var f=new Fixture())
            {
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    foreach(var capability in new[]{"batch-tracking","expiry-dates","serial-numbers","imei-tracking"})
                        c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@ProductId,@Capability,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',1)",new{f.ProductId,Capability=capability});
                }
                var second=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="ثان",IsActive=true},true,CancellationToken.None);
                var receipt=f.Command("manual_receipt","tracked-in","1");
                receipt.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="1",UnitCost="50",SalePrice="12",LotNumber="B-1",ExpirationDate=DateTime.Today.AddDays(10).ToString("yyyy-MM-dd"),PhysicalUnits=new[]{new InventoryUnitInput{Serial="SN-1",Imeis=new[]{"490154203237518","356938035643809"}}}}};
                var posted=await f.Store.PostAsync(receipt,"in-hash",CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var duplicate=f.Command("manual_receipt","tracked-duplicate","1");duplicate.Lines=receipt.Lines;
                Assert.Equal("inventory_conflict",(await f.Store.PostAsync(duplicate,"dup-hash",CancellationToken.None)).ErrorCode);
                var transfer=f.Command("transfer","tracked-transfer","1",1,second.Value.Id,null);
                transfer.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="1",LotNumber="B-1",PhysicalUnits=receipt.Lines[0].PhysicalUnits}};
                var moved=await f.Store.PostAsync(transfer,"transfer-hash",CancellationToken.None);Assert.True(moved.Succeeded,moved.ErrorMessage);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();Assert.Equal(second.Value.Id,c.ExecuteScalar<long>("SELECT WarehouseId FROM InventoryPhysicalUnits"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryPhysicalUnits"));
                    Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryIdentifiers"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductBatches"));
                }
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task Batch_without_expiry_can_be_issued()
        {
            using(var f=new Fixture())
            {
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@ProductId,'batch-tracking',1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',1)",new{f.ProductId});
                }
                var receipt=f.Command("manual_receipt","lot-only-receipt","2");receipt.Lines[0].LotNumber="LOT-1";
                Assert.True((await f.Store.PostAsync(receipt,"lot-in",CancellationToken.None)).Succeeded);
                var issue=f.Command("manual_issue","lot-only-issue","1",cost:null);issue.Lines[0].LotNumber="LOT-1";
                var result=await f.Store.PostAsync(issue,"lot-out",CancellationToken.None);
                Assert.True(result.Succeeded,result.ErrorMessage);
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task Count_rejects_stale_capture_and_reversal_is_a_new_document()
        {
            using(var f=new Fixture())
            {
                var receipt=await f.Store.PostAsync(f.Command("opening","opening-1","5"),"open-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var draft=await f.Store.CreateCountAsync(1,1,null,CancellationToken.None);Assert.True(draft.Succeeded);Assert.Single(draft.Value.Lines);
                var adjustment=await f.Store.PostAsync(f.Command("manual_issue","issue-1","1"),"issue-hash",CancellationToken.None);Assert.True(adjustment.Succeeded);
                var count=new InventoryCountPostCommand{OperationKey="count-1",Reason="جرد فعلي",UserId=1,Lines=new[]{new InventoryCountLine{VariantId=f.VariantId,BalanceVersion=draft.Value.Lines[0].BalanceVersion,CountedQuantity="3"}}};
                Assert.Equal("stale_count",(await f.Store.PostCountAsync(draft.Value.Id,count,"count-hash",CancellationToken.None)).ErrorCode);
                var reverse=await f.Store.ReverseAsync(receipt.Value.DocumentId,new InventoryPostCommand{OperationKey="reverse-1",Reason="تصحيح",UserId=1},"reverse-hash",CancellationToken.None);
                Assert.Equal("reversal_conflict",reverse.ErrorCode);
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
            using(var f=new Fixture())
            {
                var receipt=await f.Store.PostAsync(f.Command("opening","opening-2","5"),"open-hash",CancellationToken.None);
                var reverse=await f.Store.ReverseAsync(receipt.Value.DocumentId,new InventoryPostCommand{OperationKey="reverse-2",Reason="تصحيح",UserId=1},"reverse-hash",CancellationToken.None);
                Assert.True(reverse.Succeeded,reverse.ErrorMessage);Assert.Equal("already_reversed",(await f.Store.ReverseAsync(receipt.Value.DocumentId,new InventoryPostCommand{OperationKey="reverse-3",Reason="تصحيح",UserId=1},"another-hash",CancellationToken.None)).ErrorCode);
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task Physical_count_posts_difference_and_zero_difference_without_extra_movement()
        {
            using(var f=new Fixture())
            {
                Assert.True((await f.Store.PostAsync(f.Command("opening","count-open","5"),"open-hash",CancellationToken.None)).Succeeded);
                var draft=await f.Store.CreateCountAsync(1,1,null,CancellationToken.None);var line=draft.Value.Lines.Single();
                var command=new InventoryCountPostCommand{OperationKey="count-post-1",Reason="جرد فعلي",UserId=1,Lines=new[]{new InventoryCountLine{VariantId=line.VariantId,BatchId=line.BatchId,BalanceVersion=line.BalanceVersion,CountedQuantity="3"}}};
                var posted=await f.Store.PostCountAsync(draft.Value.Id,command,"count-hash",CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.Equal(3000000L,(await f.Store.BalancesAsync(1,null,CancellationToken.None)).Single().QuantityBaseScaled);
                var fetched=await f.Store.GetCountAsync(draft.Value.Id,CancellationToken.None);Assert.Equal("posted",fetched.Value.Status);Assert.Equal(3000000L,fetched.Value.Lines.Single().CountedQuantityScaled);
                var zero=await f.Store.CreateCountAsync(1,1,null,CancellationToken.None);var zeroLine=zero.Value.Lines.Single();
                var before=(await f.Store.MovementsAsync(null,null,null,null,null,null,1,100,CancellationToken.None)).TotalCount;
                var zeroCommand=new InventoryCountPostCommand{OperationKey="count-post-zero",Reason="جرد مطابق",UserId=1,Lines=new[]{new InventoryCountLine{VariantId=zeroLine.VariantId,BatchId=zeroLine.BatchId,BalanceVersion=zeroLine.BalanceVersion,CountedQuantity="3"}}};
                var zeroPost=await f.Store.PostCountAsync(zero.Value.Id,zeroCommand,"zero-hash",CancellationToken.None);Assert.True(zeroPost.Succeeded,zeroPost.ErrorMessage);
                Assert.Equal(before,(await f.Store.MovementsAsync(null,null,null,null,null,null,1,100,CancellationToken.None)).TotalCount);
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public void Upgrade_from_existing_010_schema_preserves_product_and_user_ids()
        {
            var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();c.Execute("CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL)");
                    var assembly=typeof(Migrator).Assembly;
                    foreach(var resource in assembly.GetManifestResourceNames().Where(x=>x.EndsWith(".sql")&&!x.EndsWith("011_inventory_ledger.sql")&&!x.EndsWith("016_inventory_fifo.sql")&&!x.EndsWith("023_directory_query_indexes.sql")).OrderBy(x=>x,StringComparer.Ordinal))
                    {
                        using(var reader=new StreamReader(assembly.GetManifestResourceStream(resource)))c.Execute(reader.ReadToEnd());
                        c.Execute("INSERT INTO SchemaVersion(Name,AppliedAt) VALUES(@Name,'2026-01-01T00:00:00Z')",new{Name=resource});
                    }
                    c.Execute("INSERT INTO Users(Id,Username,PasswordHash,CreatedUtc) VALUES(33,'existing','hash','2026-01-01T00:00:00Z')");
                    c.Execute("INSERT INTO Products(Id,Name,Price) VALUES(77,'منتج قديم',12)");
                    c.Execute("INSERT INTO ProductDetails(ProductId,BaseUnitId,ProductType,SellPriceScaled,CreatedUtc,UpdatedUtc) VALUES(77,1,'stock',120000,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')");
                    c.Execute("INSERT INTO ProductVariants(Id,ProductId,Name,NormalizedName,IsDefault,IsActive,CreatedUtc,UpdatedUtc) VALUES(88,77,'الافتراضي','الافتراضي',1,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')");
                    c.Execute("INSERT INTO ProductUnits(ProductId,UnitId,ToBaseNumerator,ToBaseDenominator,IsBase,IsSaleUnit,IsPurchaseUnit,IsActive,CreatedUtc,UpdatedUtc) VALUES(77,1,1,1,1,1,1,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')");
                }
                Migrator.Run(cs);Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(31,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));Assert.Equal(77,c.ExecuteScalar<long>("SELECT ProductId FROM ProductDetails WHERE ProductId=77"));Assert.Equal(88,c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=77"));Assert.Equal(33,c.ExecuteScalar<long>("SELECT Id FROM Users WHERE Id=33"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM Warehouses WHERE IsDefault=1"));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Concurrent_issues_never_make_stock_negative()
        {
            using(var f=new Fixture())
            {
                Assert.True((await f.Store.PostAsync(f.Command("opening","concurrent-open","1"),"open-hash",CancellationToken.None)).Succeeded);
                var first=Task.Run(()=>f.Store.PostAsync(f.Command("manual_issue","concurrent-a","1"),"a-hash",CancellationToken.None));
                var second=Task.Run(()=>f.Store.PostAsync(f.Command("manual_issue","concurrent-b","1"),"b-hash",CancellationToken.None));
                var results=await Task.WhenAll(first,second);
                Assert.Single(results,x=>x.Succeeded);
                Assert.Equal(0,(await f.Store.BalancesAsync(1,null,CancellationToken.None)).Single().QuantityBaseScaled);
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task Administrative_projection_rebuild_creates_verified_backup_and_audit()
        {
            using(var f=new Fixture())
            {
                Assert.True((await f.Store.PostAsync(f.Command("opening","rebuild-open","2"),"open-hash",CancellationToken.None)).Succeeded);
                var result=await f.Store.RebuildProjectionAsync(1,CancellationToken.None);
                Assert.True(result.Succeeded,result.ErrorMessage);
                Assert.True(File.Exists(result.Value.BackupPath));
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryAudit WHERE EventType='projection_rebuild'"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments WHERE DocumentType='projection_rebuild' AND Status='posted'"));}
                SQLiteConnection.ClearAllPools();File.Delete(result.Value.BackupPath);
            }
        }

        [Fact]
        public async Task Selected_zero_stock_variant_can_be_counted_and_draft_can_be_canceled()
        {
            using(var f=new Fixture())
            {
                var draft=await f.Store.CreateCountAsync(1,1,new[]{f.VariantId},CancellationToken.None);
                Assert.True(draft.Succeeded,draft.ErrorMessage);Assert.Equal(0,draft.Value.Lines.Single().RecordedQuantityScaled);
                Assert.Contains(await f.Store.CountsAsync(1,CancellationToken.None),x=>x.Id==draft.Value.Id&&x.Status=="draft");
                var canceled=await f.Store.CancelCountAsync(draft.Value.Id,1,CancellationToken.None);Assert.True(canceled.Succeeded);Assert.Equal("canceled",canceled.Value.Status);
                var second=await f.Store.CreateCountAsync(1,1,new[]{f.VariantId},CancellationToken.None);
                var command=new InventoryCountPostCommand{OperationKey="discover-count",Reason="بضاعة مكتشفة",UserId=1,Lines=new[]{new InventoryCountLine{VariantId=f.VariantId,BalanceVersion=0,CountedQuantity="2",UnitCost="3"}}};
                var posted=await f.Store.PostCountAsync(second.Value.Id,command,"count-hash",CancellationToken.None);
                Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.Equal(2000000L,(await f.Store.BalancesAsync(1,null,CancellationToken.None)).Single().QuantityBaseScaled);
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task Additional_warehouse_and_transfer_follow_effective_module_flag()
        {
            using(var f=new Fixture())
            {
                var features=new FeatureService(new SqliteFeatureStore(f.Cs),new SqliteBusinessActivityStore(f.Cs));
                var inventory=new InventoryService(f.Store,features);
                Assert.Equal("feature_disabled",(await inventory.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="ثان",IsActive=true},CancellationToken.None)).ErrorCode);
                var enabled=await features.SetAsync(new SetFeatureCommand{Key="multiple-warehouses",Kind=FeatureKinds.Module,IsEnabled=true,ChangedByUserId=1},CancellationToken.None);Assert.True(enabled.Succeeded,enabled.ErrorMessage);
                var second=await inventory.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="ثان",IsActive=true},CancellationToken.None);Assert.True(second.Succeeded,second.ErrorMessage);
                Assert.True((await inventory.PostAsync(f.Command("opening","feature-open","2"),CancellationToken.None)).Succeeded);
                var transfer=await inventory.PostAsync(f.Command("transfer","feature-transfer","1",1,second.Value.Id,null),CancellationToken.None);Assert.True(transfer.Succeeded,transfer.ErrorMessage);
                var disabled=await features.SetAsync(new SetFeatureCommand{Key="multiple-warehouses",Kind=FeatureKinds.Module,IsEnabled=false,ChangedByUserId=1},CancellationToken.None);Assert.True(disabled.Succeeded,disabled.ErrorMessage);
                Assert.Equal("feature_disabled",(await inventory.PostAsync(f.Command("transfer","feature-transfer-2","1",1,second.Value.Id,null),CancellationToken.None)).ErrorCode);
            }
        }

        [Fact]
        public async Task Warehouse_edit_persists_normalized_code_rejects_duplicate_and_survives_reopen()
        {
            using(var f=new Fixture())
            {
                var second=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="مخزن ثان",IsActive=true},true,CancellationToken.None);
                Assert.True(second.Succeeded,second.ErrorMessage);
                var edited=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Id=1,Code=" main-central ",Name="المخزن الرئيسي",Description="وصف محدث",Location="المقر الرئيسي",IsActive=true,IsDefault=true},true,CancellationToken.None);
                Assert.True(edited.Succeeded,edited.ErrorMessage);
                Assert.Equal("main-central",edited.Value.Code);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal("MAIN-CENTRAL",c.ExecuteScalar<string>("SELECT NormalizedCode FROM Warehouses WHERE Id=1"));
                    Assert.Equal("وصف محدث",c.ExecuteScalar<string>("SELECT Description FROM Warehouses WHERE Id=1"));
                    Assert.Equal("المقر الرئيسي",c.ExecuteScalar<string>("SELECT Location FROM Warehouses WHERE Id=1"));
                }
                var duplicateCreate=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Code="main-CENTRAL",Name="مخزن مكرر",IsActive=true},true,CancellationToken.None);
                Assert.Equal("duplicate_warehouse",duplicateCreate.ErrorCode);
                var duplicateEdit=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Id=1,Code="w2",Name="المخزن الرئيسي",Description="وصف محدث",Location="المقر الرئيسي",IsActive=true,IsDefault=true},true,CancellationToken.None);
                Assert.Equal("duplicate_warehouse",duplicateEdit.ErrorCode);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM Warehouses"));
                    Assert.Equal("MAIN-CENTRAL",c.ExecuteScalar<string>("SELECT NormalizedCode FROM Warehouses WHERE Id=1"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM Warehouses WHERE IsDefault=1"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT IsDefault FROM Warehouses WHERE Id=1"));
                }
                var deactivated=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Id=second.Value.Id,Code="W2",Name="مخزن ثان",Description=null,Location=null,IsActive=false,IsDefault=false},true,CancellationToken.None);
                Assert.True(deactivated.Succeeded,deactivated.ErrorMessage);
                var reactivated=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Id=second.Value.Id,Code="W2",Name="مخزن ثان",Description=null,Location=null,IsActive=true,IsDefault=false},true,CancellationToken.None);
                Assert.True(reactivated.Succeeded,reactivated.ErrorMessage);
                var reopened=new SqliteInventoryStore(f.Cs);
                var rows=await reopened.WarehousesAsync(CancellationToken.None);
                var main=rows.Single(x=>x.Id==1);
                Assert.Equal("main-central",main.Code);
                Assert.Equal("وصف محدث",main.Description);
                Assert.Equal("المقر الرئيسي",main.Location);
                Assert.True(main.IsActive);
                Assert.True(main.IsDefault);
                var secondRow=rows.Single(x=>x.Id==second.Value.Id);
                Assert.Equal("W2",secondRow.Code);
                Assert.True(secondRow.IsActive);
                Assert.False(secondRow.IsDefault);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal("MAIN-CENTRAL",c.ExecuteScalar<string>("SELECT NormalizedCode FROM Warehouses WHERE Id=1"));
                    Assert.Equal("W2",c.ExecuteScalar<string>("SELECT NormalizedCode FROM Warehouses WHERE Id=@Id",new{Id=second.Value.Id}));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM Warehouses WHERE IsActive=0"));
                }
            }
        }

        [Fact]
        public async Task Exact_unit_conversion_cost_snapshot_and_overflow_are_enforced()
        {
            using(var f=new Fixture())
            {
                long boxUnit,thirdUnit;
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    c.Execute("INSERT INTO Units(Code,NormalizedCode,Name,Symbol,CreatedUtc,UpdatedUtc) VALUES('BOX','BOX','صندوق','ص','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')");
                    var unitId=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                    c.Execute("INSERT INTO ProductUnits(ProductId,UnitId,ToBaseNumerator,ToBaseDenominator,IsBase,CreatedUtc,UpdatedUtc) VALUES(@Id,@UnitId,3,2,0,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')",new{Id=f.ProductId,UnitId=unitId});boxUnit=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                    c.Execute("INSERT INTO Units(Code,NormalizedCode,Name,Symbol,CreatedUtc,UpdatedUtc) VALUES('THIRD','THIRD','ثلث','ث','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')");
                    unitId=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                    c.Execute("INSERT INTO ProductUnits(ProductId,UnitId,ToBaseNumerator,ToBaseDenominator,IsBase,CreatedUtc,UpdatedUtc) VALUES(@Id,@UnitId,1,3,0,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')",new{Id=f.ProductId,UnitId=unitId});thirdUnit=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                }
                var exact=f.Command("manual_receipt","exact-1","2.5");exact.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=boxUnit,Quantity="2.5",UnitCost="4",SalePrice="12"}};
                Assert.True((await f.Store.PostAsync(exact,"exact-hash",CancellationToken.None)).Succeeded);
                Assert.Equal(3750000L,(await f.Store.BalancesAsync(1,null,CancellationToken.None)).Single().QuantityBaseScaled);
                var precision=f.Command("manual_receipt","precision-1","1");precision.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=thirdUnit,Quantity="1",UnitCost="4",SalePrice="12"}};
                Assert.Equal("invalid_quantity_precision",(await f.Store.PostAsync(precision,"precision-hash",CancellationToken.None)).ErrorCode);
                var overflow=f.Command("manual_receipt","overflow-1","2");overflow.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="2",UnitCost="922337203685477.58",SalePrice="12"}};
                Assert.Equal("numeric_overflow",(await f.Store.PostAsync(overflow,"overflow-hash",CancellationToken.None)).ErrorCode);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();Assert.Equal(26667L,c.ExecuteScalar<long>("SELECT UnitCostScaled FROM StockMovements LIMIT 1"));Assert.Equal(40000L,c.ExecuteScalar<long>("SELECT EnteredUnitCostScaled FROM InventoryDocumentLines LIMIT 1"));
                    c.Execute("UPDATE ProductDetails SET PurchasePriceScaled=999999 WHERE ProductId=@Id",new{Id=f.ProductId});
                    Assert.Equal(26667L,c.ExecuteScalar<long>("SELECT UnitCostScaled FROM StockMovements LIMIT 1"));Assert.Equal(40000L,c.ExecuteScalar<long>("SELECT EnteredUnitCostScaled FROM InventoryDocumentLines LIMIT 1"));
                }
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task Canonical_v2_hash_distinguishes_delimiter_and_newline_payloads()
        {
            using(var f=new Fixture())
            {
                var svc=f.InventoryService;
                var key="delimiter-newline-key";
                var line=new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="1",UnitCost="2",SalePrice="12"};
                var first=new InventoryPostCommand{Type="manual_receipt",OperationKey=key,WarehouseId=1,UserId=1,Reason="a|b\nc",Notes="d",Lines=new[]{line}};
                var changed=new InventoryPostCommand{Type="manual_receipt",OperationKey=key,WarehouseId=1,UserId=1,Reason="a",Notes="b\nc|d",Lines=new[]{line}};
                Assert.NotEqual(first.Reason,changed.Reason);
                Assert.NotEqual(first.Notes,changed.Notes);
                Assert.Equal(LegacyPostHash(first),LegacyPostHash(changed));
                var canonical=typeof(InventoryService).GetMethod("CanonicalHashPost",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
                Assert.NotNull(canonical);
                var firstHash=(string)canonical.Invoke(null,new object[]{first});
                var changedHash=(string)canonical.Invoke(null,new object[]{changed});
                Assert.StartsWith("v2:",firstHash);
                Assert.NotEqual(firstHash,changedHash);

                var a=await svc.PostAsync(first,CancellationToken.None);
                Assert.True(a.Succeeded,a.ErrorMessage);
                var before=(await f.Store.BalancesAsync(1,f.ProductId,CancellationToken.None)).Single();
                var b=await svc.PostAsync(changed,CancellationToken.None);
                Assert.Equal("operation_key_conflict",b.ErrorCode);
                var after=(await f.Store.BalancesAsync(1,f.ProductId,CancellationToken.None)).Single();
                Assert.Equal(before.QuantityBaseScaled,after.QuantityBaseScaled);
                Assert.Equal(before.ValueScaled,after.ValueScaled);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements"));}
            }
        }

        private static string LegacyPostHash(InventoryPostCommand command)
        {
            var s=new StringBuilder();s.Append(command.Type).Append('|').Append(command.WarehouseId).Append('|').Append(command.DestinationWarehouseId).Append('|').Append(command.Reason).Append('|').Append(command.Notes).Append('|').Append(command.UserId);
            foreach(var line in command.Lines)
            {
                s.Append('\n').Append(line.VariantId).Append('|').Append(line.ProductUnitId).Append('|').Append(line.Quantity).Append('|').Append(line.UnitCost).Append('|').Append(line.LotNumber).Append('|').Append(line.ExpirationDate).Append('|').Append(line.ExpectedVersion).Append('|').Append(line.ExpectedQuantity);
                foreach(var unit in line.PhysicalUnits??new InventoryUnitInput[0]){s.Append('|').Append(unit.Serial);foreach(var imei in unit.Imeis??new string[0])s.Append(':').Append(imei);}
            }
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s.ToString()))).Replace("-","");
        }

        [Fact]
        public async Task Identical_post_replays_and_changed_payload_conflicts()
        {
            using(var f=new Fixture())
            {
                var svc=f.InventoryService;
                var cmd=f.Command("manual_receipt","replay-key","3");
                var first=await svc.PostAsync(cmd,CancellationToken.None);Assert.True(first.Succeeded,first.ErrorMessage);
                var replay=await svc.PostAsync(cmd,CancellationToken.None);Assert.True(replay.Succeeded);Assert.True(replay.Value.Replayed);Assert.Equal(first.Value.DocumentId,replay.Value.DocumentId);
                cmd.Lines[0].Quantity="4";
                var conflict=await svc.PostAsync(cmd,CancellationToken.None);Assert.Equal("operation_key_conflict",conflict.ErrorCode);
            }
        }

        [Fact]
        public async Task Legacy_unversioned_hash_conflicts_and_does_not_repost()
        {
            using(var f=new Fixture())
            {
                var svc=f.InventoryService;
                var legacyKey="legacy-key";
                var legacyHash=new string('a',64);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();c.Execute("INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,CreatedByUserId,CreatedUtc,PostedUtc,OperationKey,RequestHash) VALUES('manual_receipt','posted',1,'استلام',1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',@Key,@Hash)",new{Key=legacyKey,Hash=legacyHash});}
                var cmd=f.Command("manual_receipt",legacyKey,"5");
                var result=await svc.PostAsync(cmd,CancellationToken.None);
                Assert.Equal("operation_key_conflict",result.ErrorCode);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments WHERE OperationKey=@Key",new{Key=legacyKey}));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements"));}
            }
        }

        [Fact]
        public async Task Count_identical_replay_and_changed_payload_conflict()
        {
            using(var f=new Fixture())
            {
                var svc=f.InventoryService;
                Assert.True((await svc.PostAsync(f.Command("opening","count-open","5"),CancellationToken.None)).Succeeded);
                var draft=await svc.CreateCountAsync(1,1,null,CancellationToken.None);Assert.True(draft.Succeeded);
                var line=draft.Value.Lines.Single();
                var countCmd=new InventoryCountPostCommand{OperationKey="count-key",Reason="جرد",UserId=1,Lines=new[]{new InventoryCountLine{VariantId=line.VariantId,BatchId=line.BatchId,BalanceVersion=line.BalanceVersion,CountedQuantity="3"}}};
                var first=await svc.PostCountAsync(draft.Value.Id,countCmd,CancellationToken.None);Assert.True(first.Succeeded,first.ErrorMessage);
                var replay=await svc.PostCountAsync(draft.Value.Id,countCmd,CancellationToken.None);Assert.True(replay.Succeeded);Assert.True(replay.Value.Replayed);Assert.Equal(first.Value.DocumentId,replay.Value.DocumentId);
                countCmd.Lines[0].CountedQuantity="4";
                var conflict=await svc.PostCountAsync(draft.Value.Id,countCmd,CancellationToken.None);Assert.Equal("operation_key_conflict",conflict.ErrorCode);
            }
        }

        [Fact]
        public async Task Count_hash_includes_cost_and_physical_unit_identities()
        {
            using(var f=new Fixture())
            {
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    foreach(var capability in new[]{"serial-numbers","imei-tracking"})
                        c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@ProductId,@Capability,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',1)",new{f.ProductId,Capability=capability});
                }
                var svc=f.InventoryService;
                var draft=await svc.CreateCountAsync(1,1,new[]{f.VariantId},CancellationToken.None);
                Assert.True(draft.Succeeded,draft.ErrorMessage);
                var line=draft.Value.Lines.Single();
                var countCmd=new InventoryCountPostCommand
                {
                    OperationKey="count-physical-key",
                    Reason="بضاعة مكتشفة",
                    UserId=1,
                    Lines=new[]
                    {
                        new InventoryCountLine
                        {
                            VariantId=line.VariantId,
                            BatchId=line.BatchId,
                            BalanceVersion=line.BalanceVersion,
                            CountedQuantity="1",
                            UnitCost="5",
                            PhysicalUnits=new[]{new InventoryUnitInput{Serial="SN-A",Imeis=new[]{"490154203237518","356938035643809"}}}
                        }
                    }
                };
                var first=await svc.PostCountAsync(draft.Value.Id,countCmd,CancellationToken.None);Assert.True(first.Succeeded,first.ErrorMessage);
                countCmd.Lines[0].UnitCost="6";
                var changedCost=await svc.PostCountAsync(draft.Value.Id,countCmd,CancellationToken.None);Assert.Equal("operation_key_conflict",changedCost.ErrorCode);
                countCmd.Lines[0].UnitCost="5";
                countCmd.Lines[0].PhysicalUnits[0].Imeis=new[]{"490154203237518"};
                var changedIdentity=await svc.PostCountAsync(draft.Value.Id,countCmd,CancellationToken.None);Assert.Equal("operation_key_conflict",changedIdentity.ErrorCode);
                Assert.Equal(1000000L,(await f.Store.BalancesAsync(1,null,CancellationToken.None)).Single().QuantityBaseScaled);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryPhysicalUnits"));
                    Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryIdentifiers"));
                }
            }
        }

        [Fact]
        public async Task Reversal_identical_replay_and_changed_payload_conflict()
        {
            using(var f=new Fixture())
            {
                var svc=f.InventoryService;
                var receipt=await svc.PostAsync(f.Command("opening","rev-open","5"),CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var reverseCmd=new InventoryPostCommand{OperationKey="reverse-key",Reason="تصحيح",UserId=1};
                var first=await svc.ReverseAsync(receipt.Value.DocumentId,reverseCmd,CancellationToken.None);Assert.True(first.Succeeded,first.ErrorMessage);
                var replay=await svc.ReverseAsync(receipt.Value.DocumentId,reverseCmd,CancellationToken.None);Assert.True(replay.Succeeded);Assert.True(replay.Value.Replayed);Assert.Equal(first.Value.DocumentId,replay.Value.DocumentId);
                reverseCmd.Reason="تصحيح آخر";
                var conflict=await svc.ReverseAsync(receipt.Value.DocumentId,reverseCmd,CancellationToken.None);Assert.Equal("operation_key_conflict",conflict.ErrorCode);
            }
        }

        [Fact]
        public async Task Server_local_date_blocks_yesterday_expiry_for_issue_and_transfer_allows_today()
        {
            using(var f=new Fixture())
            {
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    foreach(var capability in new[]{"batch-tracking","expiry-dates"})
                        c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@ProductId,@Capability,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',1)",new{f.ProductId,Capability=capability});
                }
                var second=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="ثان",IsActive=true},true,CancellationToken.None);Assert.True(second.Succeeded,second.ErrorMessage);
                var yesterday=DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
                var today=DateTime.Today.ToString("yyyy-MM-dd");
                var receiptExpired=f.Command("manual_receipt","expired-in","1");receiptExpired.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="1",UnitCost="10",SalePrice="12",LotNumber="LOT-Y",ExpirationDate=yesterday}};
                Assert.True((await f.Store.PostAsync(receiptExpired,"expired-hash",CancellationToken.None)).Succeeded);
                var issueExpired=f.Command("manual_issue","expired-issue","1",cost:null);issueExpired.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="1",LotNumber="LOT-Y"}};
                Assert.Equal("expired_batch",(await f.Store.PostAsync(issueExpired,"expired-issue-hash",CancellationToken.None)).ErrorCode);
                var transferExpired=f.Command("transfer","expired-transfer","1",1,second.Value.Id,null);transferExpired.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="1",LotNumber="LOT-Y"}};
                Assert.Equal("expired_batch",(await f.Store.PostAsync(transferExpired,"expired-transfer-hash",CancellationToken.None)).ErrorCode);
                var receiptToday=f.Command("manual_receipt","today-in","2");receiptToday.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="2",UnitCost="10",SalePrice="12",LotNumber="LOT-T",ExpirationDate=today}};
                Assert.True((await f.Store.PostAsync(receiptToday,"today-hash",CancellationToken.None)).Succeeded);
                var issueToday=f.Command("manual_issue","today-issue","1",cost:null);issueToday.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="1",LotNumber="LOT-T"}};
                var issueResult=await f.Store.PostAsync(issueToday,"today-issue-hash",CancellationToken.None);Assert.True(issueResult.Succeeded,issueResult.ErrorMessage);
                var transferToday=f.Command("transfer","today-transfer","1",1,second.Value.Id,null);transferToday.Lines=new[]{new InventoryLineInput{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="1",LotNumber="LOT-T"}};
                var transferResult=await f.Store.PostAsync(transferToday,"today-transfer-hash",CancellationToken.None);Assert.True(transferResult.Succeeded,transferResult.ErrorMessage);
            }
        }

        [Fact]
        public async Task Receiving_a_size_color_variant_does_not_change_the_default_variant()
        {
            using(var f=new Fixture())
            {
                long other;
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();c.Execute("INSERT INTO ProductVariants(ProductId,Name,NormalizedName,IsDefault,IsActive,CreatedUtc,UpdatedUtc) VALUES(@Id,'M / أسود','M / أسود',0,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')",new{Id=f.ProductId});other=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                }
                var command=f.Command("manual_receipt","variant-1","5");command.Lines=new[]{new InventoryLineInput{VariantId=other,ProductUnitId=f.UnitId,Quantity="5",UnitCost="8",SalePrice="12"}};
                Assert.True((await f.Store.PostAsync(command,"variant-hash",CancellationToken.None)).Succeeded);
                var balances=await f.Store.BalancesAsync(1,f.ProductId,CancellationToken.None);
                Assert.Single(balances);Assert.Equal(other,balances[0].VariantId);Assert.Equal(5000000L,balances[0].QuantityBaseScaled);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(f.VariantId,c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id AND IsDefault=1",new{Id=f.ProductId}));}
            }
        }
    }
}
