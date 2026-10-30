using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Reports;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Reports;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class StockReportTests
    {
        private static readonly CancellationToken Ct=CancellationToken.None;
        internal static Task<StockReportResult> Query(NetProfitReportTests.Fixture f,string report,StockReportQuery q=null)
            =>new StockReportService(new SqliteStockReportStore(f.Cs),TimeZoneInfo.Utc).QueryAsync(report,q??new StockReportQuery{From="2000-01-01",To="2099-12-31"},Ct);
        [Fact]
        public async Task Top_products_variants_original_box_conversion_category_scope_and_stable_ties()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("20","2");long product,variant,unit,other,box;
                using(var c=f.Db.Open())
                {
                    product=c.ExecuteScalar<long>("SELECT ProductId FROM ProductVariants LIMIT 1");variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants LIMIT 1");unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits LIMIT 1");
                    c.Execute(@"INSERT INTO ProductVariants(ProductId,Name,NormalizedName,IsDefault,IsActive,CreatedUtc,UpdatedUtc,SellPriceOverrideScaled) VALUES(@P,'متغير ثان','ثان',0,1,@Now,@Now,200000)",new{P=product,Now=DateTime.UtcNow.ToString("o")});other=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                    c.Execute(@"INSERT INTO Units(Code,NormalizedCode,Name,Symbol,CreatedUtc,UpdatedUtc) VALUES('BOX','BOX','علبة','علبة',@Now,@Now);
                        INSERT INTO ProductUnits(ProductId,UnitId,ToBaseNumerator,ToBaseDenominator,IsBase,IsSaleUnit,IsPurchaseUnit,IsActive,CreatedUtc,UpdatedUtc)
                        VALUES(@P,last_insert_rowid(),3,1,0,1,1,1,@Now,@Now)",new{P=product,Now=DateTime.UtcNow.ToString("o")});box=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                    c.Execute(@"INSERT INTO ProductCategories(Name,NormalizedName,CreatedUtc,UpdatedUtc) VALUES('قسم','قسم',@Now,@Now);
                        UPDATE ProductDetails SET CategoryId=last_insert_rowid() WHERE ProductId=@P",new{P=product,Now=DateTime.UtcNow.ToString("o")});
                }
                var inventory=new SqliteInventoryStore(f.Cs);var receipt=new InventoryPostCommand{OperationKey="second-variant",Type="manual_receipt",WarehouseId=1,UserId=f.Db.UserId,Lines=new[]{new InventoryLineInput{VariantId=other,ProductUnitId=unit,Quantity="20",UnitCost="3",SalePrice="20"}}};Assert.True((await inventory.PostAsync(receipt,InventoryService.CanonicalHashPost(receipt),Ct)).Succeeded);
                foreach(var item in new[]{new{V=variant,U=box,Q="2",P="30"},new{V=other,U=unit,Q="6",P="20"}})
                {
                    var draft=await f.Sales.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2020-01-01",PaymentMode="cash",Lines=new List<SaleLine>{new SaleLine{VariantId=item.V,ProductUnitId=item.U,Quantity=item.Q,UnitPrice=item.P}}},f.Db.UserId,Ct);
                    Assert.True((await f.Sales.PostAsync(draft.Id,"variant-sale-"+item.V,f.Db.UserId,Ct)).Succeeded);
                }
                var first=await Query(f,"top-products",new StockReportQuery{From="2000-01-01",To="2099-12-31",PageSize=1});var second=await Query(f,"top-products",new StockReportQuery{From="2000-01-01",To="2099-12-31",Page=2,PageSize=1});
                Assert.Equal(2,first.Count);Assert.Equal(variant,first.Items[0].VariantId);Assert.Equal(other,second.Items[0].VariantId);Assert.Equal("6",first.Items[0].Quantity);Assert.Equal("6",second.Items[0].Quantity);Assert.Equal("60.0000",first.Items[0].RecordedAmount);
                var byAmount=await Query(f,"top-products",new StockReportQuery{From="2000-01-01",To="2099-12-31",Sort="amount"});Assert.Equal(other,byAmount.Items[0].VariantId);
                var productReport=await Query(f,"product-profit");var day=DateTime.UtcNow.ToString("yyyy-MM-dd");Assert.Equal((await f.Report(day,day)).Summary.GrossProfit,productReport.Summary.Profit);
                var scoped=await Query(f,"product-profit",new StockReportQuery{From="2000-01-01",To="2099-12-31",Search="متغير ثان"});Assert.Single(scoped.Items);Assert.Equal("102.0000",scoped.Summary.Profit);
                Assert.Empty((await Query(f,"product-profit",new StockReportQuery{From="2000-01-01",To="2099-12-31",CategoryId=99999})).Items);
            }
        }
        [Fact]
        public async Task Current_inventory_large_catalog_paging_full_totals_and_empty_snapshot()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                Assert.Empty((await Query(f,"inventory-valuation",new StockReportQuery())).Items);
                var catalog=new AlKhayal.Application.Catalog.CatalogService(new AlKhayal.Data.Catalog.SqliteCatalogStore(f.Cs));var stock=new SqliteInventoryStore(f.Cs);
                for(int n=0;n<105;n++)
                {
                    var p=await catalog.CreateProductAsync(new AlKhayal.Application.Catalog.CreateProductCommand{OperationKey="many-"+n,Name="صنف "+n,BaseUnitId=1,ProductType="stock",SalePrice="3",IsActive=true},Ct);Assert.True(p.Succeeded);
                    long v,u;using(var c=f.Db.Open()){v=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=p.Value.Id});u=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id",new{Id=p.Value.Id});}
                    var cmd=new InventoryPostCommand{OperationKey="many-stock-"+n,Type="manual_receipt",WarehouseId=1,UserId=f.Db.UserId,Lines=new[]{new InventoryLineInput{VariantId=v,ProductUnitId=u,Quantity="1.2345",UnitCost="1",SalePrice="3"}}};Assert.True((await stock.PostAsync(cmd,InventoryService.CanonicalHashPost(cmd),Ct)).Succeeded);
                }
                var first=await Query(f,"inventory-valuation",new StockReportQuery{PageSize=100});var next=await Query(f,"inventory-valuation",new StockReportQuery{PageSize=100,Page=2});Assert.Equal(105,first.Count);Assert.Equal(100,first.Items.Count);Assert.Equal(5,next.Items.Count);Assert.Equal("129.6225",first.Summary.Cost);Assert.Equal(first.Summary.Cost,next.Summary.Cost);Assert.Equal("388.5000",next.Summary.SellingValue);Assert.False(first.Items.Select(x=>x.VariantId).Intersect(next.Items.Select(x=>x.VariantId)).Any());
                var availability=await Query(f,"low-stock",new StockReportQuery{Status="available",PageSize=1});Assert.Equal(105,availability.Count);Assert.Single(availability.Items);
            }
        }
        [Fact]
        public async Task Missing_original_unit_snapshot_never_fabricates_zero_top_seller_quantity()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("2","2");var sale=await f.Sale("1");
                f.Time("SalesInvoiceLines","InventoryDocumentLineId","Id",sale.Lines[0].Id,null);
                var top=await Query(f,"top-products");var row=Assert.Single(top.Items);Assert.Null(row.Sold);Assert.Null(row.Quantity);Assert.Equal("10.0000",row.RecordedAmount);Assert.False(top.Summary.Complete);
                var profit=await Query(f,"product-profit");Assert.Null(profit.Summary.Cost);Assert.Null(profit.Summary.Profit);
            }
        }
        [Fact]
        public async Task Product_profit_approved_FIFO_example_price_edits_and_valuation_remain_separate()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("10","100");await f.Receipt("10","120");await f.Price("200");await f.Sale("12");
                var before=File.ReadAllBytes(f.Db.DatabasePath);
                var profit=await Query(f,"product-profit");var row=Assert.Single(profit.Items);
                Assert.Equal("12",row.Sold);Assert.Equal("0",row.Returned);Assert.Equal("2400.0000",row.Revenue);Assert.Equal("1240.0000",row.Cost);Assert.Equal("1160.0000",row.Profit);Assert.Equal("48.33",row.Margin);
                var value=await Query(f,"inventory-valuation",new StockReportQuery());row=Assert.Single(value.Items);
                Assert.Equal("8",row.Quantity);Assert.Equal("960.0000",row.Cost);Assert.Equal("1600.0000",row.SellingValue);Assert.Equal("640.0000",row.Profit);
                var original=await new SqliteInventoryStore(f.Cs).ValuationAsync(1,null,Ct);Assert.Equal(original.CostValue,value.Summary.Cost);Assert.Equal(original.PotentialSellingValue,value.Summary.SellingValue);
                Assert.Equal(before,File.ReadAllBytes(f.Db.DatabasePath));
                await f.Price("900");var after=await Query(f,"product-profit");Assert.Equal(profit.Summary.Profit,after.Summary.Profit);
                Assert.Equal("7200.0000",(await Query(f,"inventory-valuation",new StockReportQuery())).Summary.SellingValue);
                var net=await f.Report("2000-01-01","2000-01-01"); // empty calendar range
                Assert.Equal("0.0000",net.Summary.GrossProfit);
                var day=DateTime.UtcNow.ToString("yyyy-MM-dd");net=await f.Report(day,day);Assert.Equal(net.Summary.GrossProfit,after.Summary.Profit);
            }
        }
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public async Task Product_profit_and_top_sellers_original_tax_discount_repeated_returns_reconcile(bool inclusive)
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("3","2");
                using(var c=f.Db.Open())c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=15,PricesIncludeTax=@I",new{I=inclusive?1:0});
                var sale=await f.Sale("3","0.01","0.02","cash");
                f.Time("SalesInvoices","PostedUtc","Id",sale.Id,"2026-01-31T12:00:00.0000000Z");
                await f.Price("99");using(var c=f.Db.Open())c.Execute("UPDATE ApplicationSettings SET DefaultTaxRate=70");
                for(int n=0;n<3;n++)
                {
                    var result=await f.Sales.ReturnAsync(sale.Id,new SaleReturnCommand{OperationKey="stock-return-"+n,Reason="جزئي",Lines=new List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=sale.Lines.Single().Id,Quantity="1"}}},f.Db.UserId,Ct);
                    Assert.True(result.Succeeded,result.ErrorMessage);f.Time("SalesReturns","CreatedUtc","Id",result.ReturnId,"2026-02-0"+(n+1)+"T12:00:00.0000000Z");
                }
                foreach(var dates in new[]{new[]{"2026-01-31","2026-02-03"},new[]{"2026-02-02","2026-02-03"},new[]{"2026-01-31","2026-01-31"}})
                {
                    var product=await Query(f,"product-profit",new StockReportQuery{From=dates[0],To=dates[1]});var net=await f.Report(dates[0],dates[1]);
                    Assert.True(product.Summary.Complete);Assert.Equal(net.Summary.Revenue,product.Summary.Revenue);Assert.Equal(net.Summary.Cogs,product.Summary.Cost);Assert.Equal(net.Summary.GrossProfit,product.Summary.Profit);
                    var top=await Query(f,"top-products",new StockReportQuery{From=dates[0],To=dates[1]});Assert.Equal(product.Items[0].Quantity,top.Items[0].Quantity);
                }
                var all=Assert.Single((await Query(f,"top-products")).Items);Assert.Equal("3",all.Sold);Assert.Equal("3",all.Returned);Assert.Equal("0",all.Quantity);Assert.Equal("0.0000",all.RecordedAmount);Assert.Null(all.Margin);
            }
        }
        [Fact]
        public async Task Product_profit_return_spanning_layers_and_cancellation_use_event_dates()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("10","100");await f.Receipt("10","120");await f.Price("200");var sale=await f.Sale("12",paymentMode:"cash");
                f.Time("SalesInvoices","PostedUtc","Id",sale.Id,"2026-01-31T12:00:00.0000000Z");
                var returned=await f.Sales.ReturnAsync(sale.Id,new SaleReturnCommand{OperationKey="spanning",Reason="إرجاع",Lines=new List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=sale.Lines[0].Id,Quantity="11"}}},f.Db.UserId,Ct);Assert.True(returned.Succeeded,returned.ErrorMessage);
                f.Time("SalesReturns","CreatedUtc","Id",returned.ReturnId,"2026-02-01T12:00:00.0000000Z");
                var r=await Query(f,"product-profit",new StockReportQuery{From="2026-02-01",To="2026-02-01"});Assert.Equal("-1120.0000",r.Summary.Cost);Assert.Equal("-2200.0000",r.Summary.Revenue);Assert.Equal("-11",r.Items[0].Quantity);Assert.Null(r.Items[0].Margin);
                var second=await f.Sale("1");f.Time("SalesInvoices","PostedUtc","Id",second.Id,"2026-02-02T12:00:00.0000000Z");
                var cancelled=await f.Sales.CancelAsync(second.Id,"stock-cancel","إلغاء",f.Db.UserId,Ct);Assert.True(cancelled.Succeeded,cancelled.ErrorMessage);
                f.Time("SalesCancellations","CreatedUtc","SalesInvoiceId",second.Id,"2026-02-03T12:00:00.0000000Z");
                r=await Query(f,"product-profit",new StockReportQuery{From="2026-02-03",To="2026-02-03"});Assert.Equal("1",r.Items[0].Cancelled);Assert.Equal("-1",r.Items[0].Quantity);
                Assert.Equal((await f.Report("2026-02-03","2026-02-03")).Summary.GrossProfit,r.Summary.Profit);
            }
        }
        [Fact]
        public async Task Product_profit_missing_FIFO_is_unknown_and_negative_profit_is_valid()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("2","20");await f.Sale("1");var r=await Query(f,"product-profit");Assert.Equal("-10.0000",r.Summary.Profit);
                using(var c=f.Db.Open()){c.Execute("DROP TRIGGER TR_InventoryCostAllocations_NoDelete");c.Execute("DELETE FROM InventoryCostAllocations");}
                r=await Query(f,"product-profit");Assert.False(r.Summary.Complete);Assert.Null(r.Summary.Cost);Assert.Null(r.Items[0].Profit);Assert.Equal("10.0000",r.Summary.Revenue);
                // A missing remaining layer must also suppress current valuation.
                using(var c=f.Db.Open())c.Execute("UPDATE InventoryCostLayers SET RemainingValueScaled=0");
                var v=await Query(f,"inventory-valuation",new StockReportQuery());Assert.False(v.Summary.Complete);Assert.Null(v.Summary.Cost);Assert.Null(v.Items[0].Profit);
            }
        }
        [Fact]
        public async Task Low_stock_only_real_assignments_active_items_filtering_paging_and_read_only()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("1","2");await f.Sale("1");
                var store=new SqliteInventoryStore(f.Cs);var wh=await store.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="ثاني"},true,Ct);Assert.True(wh.Succeeded);
                var before=File.ReadAllBytes(f.Db.DatabasePath);
                var low=await Query(f,"low-stock",new StockReportQuery{Status="zero"});Assert.Equal(1,low.Count);Assert.Equal("0",low.Items[0].Quantity);Assert.Equal(1,low.Items[0].WarehouseId);Assert.Contains("لا يوجد حد",low.Scope);
                Assert.Empty((await Query(f,"low-stock",new StockReportQuery{WarehouseId=wh.Value.Id})).Items);
                Assert.Empty((await Query(f,"low-stock",new StockReportQuery{Search="does-not-exist"})).Items);
                var page=await Query(f,"low-stock",new StockReportQuery{Page=2,PageSize=1});Assert.Empty(page.Items);Assert.Equal(1,page.Count);Assert.Equal(before,File.ReadAllBytes(f.Db.DatabasePath));
                using(var c=f.Db.Open())c.Execute("UPDATE ProductDetails SET IsActive=0");Assert.Empty((await Query(f,"low-stock",new StockReportQuery())).Items);
            }
        }
        [Fact]
        public async Task Inventory_movements_saved_cost_direction_references_filters_and_pagination_are_read_only()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("10","100");var sale=await f.Sale("2");await f.Price("999");
                var before=File.ReadAllBytes(f.Db.DatabasePath);
                var all=await Query(f,"inventory-movements");Assert.Equal(2,all.Count);var issue=all.Items.Single(x=>x.Type=="sale");Assert.Equal("2",issue.QuantityOut);Assert.Equal("0",issue.QuantityIn);Assert.Equal("-200.0000",issue.Cost);Assert.Equal(sale.InvoiceNumber,issue.Reference);Assert.Equal(sale.Id,issue.DocumentId);Assert.Equal("sales-management",issue.Route);
                var receipts=await Query(f,"inventory-movements",new StockReportQuery{From="2000-01-01",To="2099-12-31",Type="manual_receipt"});Assert.Single(receipts.Items);Assert.Null(receipts.Items[0].Route);Assert.Equal("10",receipts.Items[0].QuantityIn);
                var filtered=await Query(f,"inventory-movements",new StockReportQuery{From="2000-01-01",To="2099-12-31",Reference=sale.InvoiceNumber});Assert.Single(filtered.Items);
                var p1=await Query(f,"inventory-movements",new StockReportQuery{From="2000-01-01",To="2099-12-31",PageSize=1});var p2=await Query(f,"inventory-movements",new StockReportQuery{From="2000-01-01",To="2099-12-31",PageSize=1,Page=2});Assert.NotEqual(p1.Items[0].Id,p2.Items[0].Id);Assert.Equal(p1.Summary.Cost,p2.Summary.Cost);
                Assert.Equal(before,File.ReadAllBytes(f.Db.DatabasePath));
            }
        }
    }
}
