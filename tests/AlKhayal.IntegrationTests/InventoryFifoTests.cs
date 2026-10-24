using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Inventory;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class InventoryPersistenceTests
    {
        private static readonly CancellationToken NoCancellation=CancellationToken.None;
        private static async Task PostOk(Fixture f,InventoryPostCommand command)
        {
            var result=await f.InventoryService.PostAsync(command,NoCancellation);
            Assert.True(result.Succeeded,result.ErrorCode+": "+result.ErrorMessage);
        }

        [Fact]
        public async Task Generated_warehouse_codes_skip_existing_manual_codes()
        {
            using(var f=new Fixture())
            {
                Assert.True((await f.Store.SaveWarehouseAsync(new WarehouseCommand{Code="WH-0003",Name="كود سابق"},true,NoCancellation)).Succeeded);
                var generated=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Name="مخزن جديد"},true,NoCancellation);
                Assert.True(generated.Succeeded,generated.ErrorMessage);Assert.Equal("WH-0004",generated.Value.Code);
            }
        }

        [Fact]
        public async Task Fifo_consumes_oldest_layers_partially_across_receipts_and_survives_restart_and_retry()
        {
            using(var f=new Fixture())
            {
                await PostOk(f,f.Command("manual_receipt","fifo-100","10",cost:"100"));
                await PostOk(f,f.Command("manual_receipt","fifo-120","10",cost:"120"));
                await PostOk(f,f.Command("manual_issue","fifo-first","3",cost:null));
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(7000000,c.ExecuteScalar<long>("SELECT RemainingQuantityScaled FROM InventoryCostLayers ORDER BY Id LIMIT 1"));}
                var issue=f.Command("manual_issue","fifo-cross","9",cost:null);
                await PostOk(f,issue);
                var replay=await f.InventoryService.PostAsync(issue,NoCancellation);Assert.True(replay.Value.Replayed);
                var restarted=new SqliteInventoryStore(f.Cs);
                var balance=(await restarted.BalancesAsync(1,null,NoCancellation)).Single();
                Assert.Equal(8000000,balance.QuantityBaseScaled);Assert.Equal(9600000,balance.ValueScaled);Assert.True(balance.CostBasisComplete);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(new long[]{0,8000000},c.Query<long>("SELECT RemainingQuantityScaled FROM InventoryCostLayers ORDER BY Id").ToArray());
                    Assert.Equal(new long[]{0,9600000},c.Query<long>("SELECT RemainingValueScaled FROM InventoryCostLayers ORDER BY Id").ToArray());
                    Assert.Equal(new long[]{3000000,7000000,2000000},c.Query<long>("SELECT QuantityScaled FROM InventoryCostAllocations ORDER BY Id").ToArray());
                    Assert.Equal(new long[]{3000000,7000000,2400000},c.Query<long>("SELECT ValueScaled FROM InventoryCostAllocations ORDER BY Id").ToArray());
                    Assert.Equal(4,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements"));
                    Assert.Throws<SQLiteException>(()=>c.Execute("UPDATE InventoryCostAllocations SET ValueScaled=0"));
                    Assert.Throws<SQLiteException>(()=>c.Execute("UPDATE InventoryCostLayers SET ValueScaled=0"));
                }
                Assert.True(await restarted.VerifyProjectionAsync(NoCancellation));
            }
        }

        [Fact]
        public async Task Fifo_transfer_preserves_origin_order_cost_and_allocations_and_rolls_back_both_sides()
        {
            using(var f=new Fixture())
            {
                var second=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="مخزن ثان"},true,NoCancellation);Assert.True(second.Succeeded);
                await PostOk(f,f.Command("manual_receipt","t-old","10",cost:"100"));
                await PostOk(f,f.Command("manual_receipt","t-new","10",cost:"120"));
                await PostOk(f,f.Command("manual_receipt","t-destination","2",second.Value.Id,cost:"150"));
                var transfer=f.Command("transfer","t-transfer","12",1,second.Value.Id,null);
                var posted=await f.Store.PostAsync(transfer,"v2:transfer",NoCancellation);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var replay=await f.Store.PostAsync(transfer,"v2:transfer",NoCancellation);Assert.True(replay.Value.Replayed);
                await PostOk(f,f.Command("manual_issue","t-out","11",second.Value.Id,cost:null));
                var rows=await f.Store.BalancesAsync(null,null,NoCancellation);
                Assert.Equal(9600000,rows.Single(x=>x.WarehouseId==1).ValueScaled);
                Assert.Equal(4200000,rows.Single(x=>x.WarehouseId==second.Value.Id).ValueScaled);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers WHERE Basis='transfer'"));
                    Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements WHERE DocumentId=@Id",new{Id=posted.Value.DocumentId}));
                    c.Execute("CREATE TRIGGER test_reject_transfer BEFORE INSERT ON StockMovements WHEN NEW.WarehouseId="+second.Value.Id+" AND NEW.QuantityBaseScaled>0 BEGIN SELECT RAISE(ABORT,'test_failure'); END;");
                }
                var rejected=f.Command("transfer","t-rollback","1",1,second.Value.Id,null);
                Assert.False((await f.Store.PostAsync(rejected,"v2:rollback",NoCancellation)).Succeeded);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(8000000,c.ExecuteScalar<long>("SELECT SUM(RemainingQuantityScaled) FROM InventoryCostLayers WHERE WarehouseId=1"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments WHERE OperationKey='t-rollback'"));}
                var history=await f.Store.MovementsAsync(null,null,"transfer",null,null,null,1,100,NoCancellation);
                Assert.Equal(2,history.Items.Count);Assert.All(history.Items,x=>Assert.False(string.IsNullOrWhiteSpace(x.RelatedWarehouseName)));
                Assert.True(await f.Store.VerifyProjectionAsync(NoCancellation));
            }
        }

        [Fact]
        public async Task Fifo_adjustments_and_count_shortages_consume_layers_and_positive_counts_require_cost()
        {
            using(var f=new Fixture())
            {
                await PostOk(f,f.Command("manual_receipt","a-old","10",cost:"100"));
                await PostOk(f,f.Command("manual_receipt","a-new","10",cost:"120"));
                await PostOk(f,f.Command("adjustment","a-minus","-5",cost:null));
                var draft=await f.Store.CreateCountAsync(1,1,null,NoCancellation);Assert.True(draft.Succeeded);
                var line=draft.Value.Lines.Single();
                var count=new InventoryCountPostCommand{OperationKey="a-count",UserId=1,Lines=new[]{new InventoryCountLine{VariantId=f.VariantId,BalanceVersion=line.BalanceVersion,CountedQuantity="8"}}};
                var posted=await f.InventoryService.PostCountAsync(draft.Value.Id,count,NoCancellation);Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.True((await f.InventoryService.PostCountAsync(draft.Value.Id,count,NoCancellation)).Value.Replayed);
                Assert.Equal(9600000,(await f.Store.BalancesAsync(1,null,NoCancellation)).Single().ValueScaled);
                Assert.Equal("cost_required",(await f.InventoryService.PostAsync(f.Command("adjustment","a-unknown","1",cost:null),NoCancellation)).ErrorCode);
                await PostOk(f,f.Command("adjustment","a-plus","2",cost:"140"));
                Assert.Equal(12400000,(await f.Store.BalancesAsync(1,null,NoCancellation)).Single().ValueScaled);
                Assert.True(await f.Store.VerifyProjectionAsync(NoCancellation));
            }
        }

        [Fact]
        public async Task Receipt_converts_selected_unit_prices_and_changes_only_selected_variant_atomically()
        {
            using(var f=new Fixture())
            {
                long pack,other;
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    c.Execute("INSERT INTO Units(Code,NormalizedCode,Name,Symbol,IsActive,CreatedUtc,UpdatedUtc) VALUES('pack-test','PACK-TEST','كرتونة','كرتونة',1,'2026-01-01','2026-01-01')");
                    var unit=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                    c.Execute("INSERT INTO ProductUnits(ProductId,UnitId,ToBaseNumerator,ToBaseDenominator,IsBase,IsSaleUnit,IsPurchaseUnit,IsActive,CreatedUtc,UpdatedUtc) VALUES(@ProductId,@Unit,12,1,0,1,1,1,'2026-01-01','2026-01-01')",new{f.ProductId,Unit=unit});pack=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                    c.Execute("INSERT INTO ProductVariants(ProductId,Name,NormalizedName,IsDefault,IsActive,CreatedUtc,UpdatedUtc) VALUES(@ProductId,'أزرق','أزرق',0,1,'2026-01-01','2026-01-01')",new{f.ProductId});other=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                }
                var receipt=f.Command("manual_receipt","pack-in","2",cost:"120");receipt.Lines[0].ProductUnitId=pack;receipt.Lines[0].SalePrice="180";
                await PostOk(f,receipt);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(120000,c.ExecuteScalar<long>("SELECT COALESCE(v.SellPriceOverrideScaled,d.SellPriceScaled) FROM ProductVariants v JOIN ProductDetails d ON d.ProductId=v.ProductId WHERE v.Id=@Id",new{Id=other}));}
                var second=f.Command("manual_receipt","variant-in","1",cost:"20");second.Lines[0].VariantId=other;second.Lines[0].SalePrice="25";await PostOk(f,second);
                var balance=(await f.Store.BalancesAsync(1,null,NoCancellation)).Single(x=>x.VariantId==f.VariantId);
                Assert.Equal(24000000,balance.QuantityBaseScaled);Assert.Equal(2400000,balance.ValueScaled);Assert.Equal(150000,balance.SalePriceScaled);
                await PostOk(f,f.Command("manual_issue","pack-out","13",cost:null));
                Assert.Equal(1100000,(await f.Store.BalancesAsync(1,null,NoCancellation)).Single(x=>x.VariantId==f.VariantId).ValueScaled);
                var bad=f.Command("manual_receipt","pack-bad","2",cost:"240");bad.Lines=new[]{bad.Lines[0],new InventoryLineInput{VariantId=999,ProductUnitId=pack,Quantity="1",UnitCost="1",SalePrice="1"}};
                Assert.False((await f.InventoryService.PostAsync(bad,NoCancellation)).Succeeded);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(150000,c.ExecuteScalar<long>("SELECT SellPriceOverrideScaled FROM ProductVariants WHERE Id=@Id",new{Id=f.VariantId}));Assert.Equal(250000,c.ExecuteScalar<long>("SELECT SellPriceOverrideScaled FROM ProductVariants WHERE Id=@Id",new{Id=other}));}
                Assert.True(await f.Store.VerifyProjectionAsync(NoCancellation));
            }
        }

        [Theory]
        [InlineData("-1","10","12","invalid_quantity")]
        [InlineData("0","10","12","invalid_quantity")]
        [InlineData("1","-1","12","invalid_cost")]
        [InlineData("1","0.00001","12","invalid_cost")]
        [InlineData("1","10",null,"sale_price_required")]
        [InlineData("1","10","-1","sale_price_required")]
        [InlineData("1",null,"12","cost_required")]
        public async Task Invalid_receipt_never_posts_any_stock_or_cost(string quantity,string cost,string sale,string code)
        {
            using(var f=new Fixture())
            {
                var cmd=f.Command("manual_receipt","invalid","1");cmd.Lines[0].Quantity=quantity;cmd.Lines[0].UnitCost=cost;cmd.Lines[0].SalePrice=sale;
                Assert.Equal(code,(await f.InventoryService.PostAsync(cmd,NoCancellation)).ErrorCode);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers"));}
            }
        }

        [Fact]
        public async Task Tracked_fifo_transfer_and_issue_preserve_cost_and_rejected_identifiers_roll_back_consumption()
        {
            using(var f=new Fixture())
            {
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();foreach(var cap in new[]{"batch-tracking","expiry-dates","serial-numbers","imei-tracking"})
                        c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@ProductId,@Cap,1,'2026-01-01','2026-01-01',1)",new{f.ProductId,Cap=cap});
                }
                var second=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="ثان"},true,NoCancellation);
                var first=f.Command("manual_receipt","tracked-fifo-1","1",cost:"100");var newer=f.Command("manual_receipt","tracked-fifo-2","1",cost:"120");
                var a=new[]{new InventoryUnitInput{Serial="FIFO-SN1",Imeis=new[]{"490154203237518"}}};
                var b=new[]{new InventoryUnitInput{Serial="FIFO-SN2",Imeis=new[]{"356938035643809"}}};
                first.Lines[0].PhysicalUnits=a;newer.Lines[0].PhysicalUnits=b;
                foreach(var input in new[]{first.Lines[0],newer.Lines[0]}){input.LotNumber="FIFO-LOT";input.ExpirationDate=DateTime.Today.AddDays(10).ToString("yyyy-MM-dd");}
                await PostOk(f,first);await PostOk(f,newer);
                var transfer=f.Command("transfer","tracked-fifo-transfer","1",1,second.Value.Id,null);transfer.Lines[0].LotNumber="FIFO-LOT";transfer.Lines[0].PhysicalUnits=a;
                var moved=await f.Store.PostAsync(transfer,"v2:tracked-fifo",NoCancellation);Assert.True(moved.Succeeded,moved.ErrorMessage);
                var issue=f.Command("manual_issue","tracked-fifo-fail","1",cost:null);issue.Lines[0].LotNumber="FIFO-LOT";issue.Lines[0].PhysicalUnits=a;
                Assert.Equal("identifier_not_in_stock",(await f.InventoryService.PostAsync(issue,NoCancellation)).ErrorCode);
                var balance=(await f.Store.BalancesAsync(1,null,NoCancellation)).Single();Assert.Equal(1200000,balance.ValueScaled);Assert.True(balance.CostBasisComplete);
                issue.OperationKey="tracked-fifo-issue";issue.Lines[0].PhysicalUnits=b;await PostOk(f,issue);
                var destination=(await f.Store.BalancesAsync(second.Value.Id,null,NoCancellation)).Single();Assert.Equal(1000000,destination.ValueScaled);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostAllocations"));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryPhysicalUnits"));Assert.Equal(second.Value.Id,c.ExecuteScalar<long>("SELECT WarehouseId FROM InventoryPhysicalUnits WHERE Id=1"));}
                Assert.True(await f.Store.VerifyProjectionAsync(NoCancellation));
            }
        }

        [Fact]
        public async Task Fifo_migration_preserves_legacy_ledger_and_blocks_unknown_cost_until_documented_opening_basis()
        {
            using(var f=new Fixture(false))
            {
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();c.Execute("PRAGMA foreign_keys=ON");
                    c.Execute("INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,CreatedByUserId,CreatedUtc,OperationKey,RequestHash) VALUES('opening','draft',1,'قديم',1,'2025-01-01','legacy-key','legacy-hash')");
                    c.Execute("INSERT INTO InventoryDocumentLines(DocumentId,LineNumber,VariantId,EnteredQuantityScaled,ProductUnitId,ToBaseNumerator,ToBaseDenominator,BaseQuantityScaled,UnitCostScaled) VALUES(1,1,@VariantId,10000000,@UnitId,1,1,10000000,1000000)",new{f.VariantId,f.UnitId});
                    c.Execute("INSERT INTO StockMovements(DocumentId,DocumentLineId,WarehouseId,VariantId,QuantityBaseScaled,ValueScaled,UnitCostScaled,CreatedUtc) VALUES(1,1,1,@VariantId,10000000,10000000,1000000,'2025-01-01')",new{f.VariantId});
                    c.Execute("INSERT INTO InventoryBalances(WarehouseId,VariantId,QuantityBaseScaled,ValueScaled,UpdatedUtc) VALUES(1,@VariantId,10000000,10000000,'2025-01-01')",new{f.VariantId});
                    c.Execute("UPDATE InventoryDocuments SET Status='posted',PostedUtc='2025-01-01' WHERE Id=1");
                }
                Migrator.Run(f.Cs);Migrator.Run(f.Cs);
                var old=(await f.Store.BalancesAsync(1,null,NoCancellation)).Single();
                Assert.Equal(10000000,old.QuantityBaseScaled);Assert.Equal(10000000,old.ValueScaled);Assert.False(old.CostBasisComplete);Assert.NotNull(old.UnresolvedLayerId);
                Assert.Equal("unresolved_cost_basis",(await f.InventoryService.PostAsync(f.Command("manual_issue","legacy-out","1",cost:null),NoCancellation)).ErrorCode);
                var recon=new InventoryCostReconciliationCommand{OperationKey="reconcile",LayerId=old.UnresolvedLayerId.Value,ExpectedVersion=old.Version,UserId=1,UnitCost="99",Evidence="تكلفة افتتاحية أقرها المالك استنادًا إلى سجل الشراء"};
                Assert.Equal("legacy_value_mismatch",(await f.InventoryService.ReconcileCostAsync(recon,NoCancellation)).ErrorCode);
                recon.UnitCost="100";
                var saved=await f.InventoryService.ReconcileCostAsync(recon,NoCancellation);Assert.True(saved.Succeeded,saved.ErrorMessage);
                Assert.Equal(saved.Value,(await f.InventoryService.ReconcileCostAsync(recon,NoCancellation)).Value);
                recon.Evidence="تغيير المصدر";Assert.Equal("operation_key_conflict",(await f.InventoryService.ReconcileCostAsync(recon,NoCancellation)).ErrorCode);
                await PostOk(f,f.Command("manual_receipt","legacy-new","10",cost:"120"));
                await PostOk(f,f.Command("manual_issue","legacy-consume","12",cost:null));
                var after=(await f.Store.BalancesAsync(1,null,NoCancellation)).Single();Assert.True(after.CostBasisComplete);Assert.Equal(9600000,after.ValueScaled);
                using(var c=new SQLiteConnection(f.Cs))
                {
                    c.Open();Assert.Equal(10000000,c.ExecuteScalar<long>("SELECT ValueScaled FROM StockMovements WHERE Id=1"));
                    Assert.Equal("legacy-hash",c.ExecuteScalar<string>("SELECT RequestHash FROM InventoryDocuments WHERE Id=1"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostReconciliations"));
                    Assert.Equal(18,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await f.Store.VerifyProjectionAsync(NoCancellation));
            }
        }
    }
}
