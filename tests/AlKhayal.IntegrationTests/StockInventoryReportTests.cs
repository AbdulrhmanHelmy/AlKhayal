using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Reports;
using AlKhayal.Data.Reports;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class InventoryPersistenceTests
    {
        [Fact]
        public async Task Stock_reports_purchase_receipt_and_return_show_original_sources_and_remaining_cost()
        {
            using(var f=new Fixture())
            {
                var ct=CancellationToken.None;var suppliers=new AlKhayal.Data.Suppliers.SqliteSupplierRepository(f.Cs);
                var supplier=suppliers.Add(new AlKhayal.Core.Models.Supplier{Code="REPORT-S",Name="مورد",Phone="010",Status="نشط"});
                var service=new AlKhayal.Application.Purchasing.PurchaseService(new AlKhayal.Data.Purchasing.SqlitePurchaseRepository(f.Cs),suppliers);
                var draft=await service.CreateDraftAsync(new AlKhayal.Core.Models.PurchaseDraftCommand{SupplierId=supplier,WarehouseId=1,InvoiceNumber="REPORT-P",InvoiceDate="2026-01-01",PaymentMode="credit",Lines=new System.Collections.Generic.List<AlKhayal.Core.Models.PurchaseInvoiceLine>{new AlKhayal.Core.Models.PurchaseInvoiceLine{VariantId=f.VariantId,ProductUnitId=f.UnitId,Quantity="2",PurchaseUnitPrice="10.13",SaleUnitPrice="15"}}},1,ct);
                Assert.True((await service.PostAsync(draft.Id,"report-purchase",1,ct)).Succeeded);
                var returned=await service.PostReturnAsync(draft.Id,new AlKhayal.Core.Models.PurchaseReturnCommand{WarehouseId=1,ReturnMode="cash_refund",Lines=new System.Collections.Generic.List<AlKhayal.Core.Models.PurchaseReturnLine>{new AlKhayal.Core.Models.PurchaseReturnLine{PurchaseInvoiceLineId=draft.Lines[0].Id,Quantity="1"}}},"report-purchase-return",1,ct);Assert.True(returned.Succeeded,returned.ErrorMessage);
                var reports=new StockReportService(new SqliteStockReportStore(f.Cs),TimeZoneInfo.Utc);
                var movement=await reports.QueryAsync("inventory-movements",new StockReportQuery{From="2000-01-01",To="2099-12-31"},ct);
                Assert.Equal(2,movement.Count);Assert.All(movement.Items,x=>{Assert.Equal("purchase-management",x.Route);Assert.Equal(draft.Id,x.DocumentId);});
                Assert.Equal("2",movement.Items.Single(x=>x.Type=="purchase").QuantityIn);Assert.Equal("-10.1300",movement.Items.Single(x=>x.Type=="purchase-return").Cost);
                var value=await reports.QueryAsync("inventory-valuation",new StockReportQuery(),ct);Assert.Equal("10.1300",value.Summary.Cost);Assert.Equal("15.0000",value.Summary.SellingValue);
            }
        }
        [Fact]
        public async Task Stock_reports_transfers_counts_and_adjustments_preserve_exact_FIFO_and_directions()
        {
            using(var f=new Fixture())
            {
                var ct=CancellationToken.None;var reports=new StockReportService(new SqliteStockReportStore(f.Cs),TimeZoneInfo.Utc);
                var second=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Code="REPORT2",Name="ثاني"},true,ct);Assert.True(second.Succeeded);
                await PostOk(f,f.Command("manual_receipt","report-a","10",cost:"100"));await PostOk(f,f.Command("manual_receipt","report-b","10",cost:"120"));
                var transfer=f.Command("transfer","report-transfer","12",1,second.Value.Id,null);Assert.True((await f.Store.PostAsync(transfer,InventoryService.CanonicalHashPost(transfer),ct)).Succeeded);
                var history=await reports.QueryAsync("inventory-movements",new StockReportQuery{From="2000-01-01",To="2099-12-31",Type="transfer"},ct);Assert.Equal(2,history.Count);Assert.Equal("0.0000",history.Summary.Cost);Assert.Equal("12",history.Items.Single(x=>x.WarehouseId==1).QuantityOut);Assert.Equal("12",history.Items.Single(x=>x.WarehouseId==second.Value.Id).QuantityIn);
                var valuation=await reports.QueryAsync("inventory-valuation",new StockReportQuery{WarehouseId=second.Value.Id},ct);Assert.Equal("1240.0000",valuation.Summary.Cost);
                await PostOk(f,f.Command("manual_issue","report-issue","11",second.Value.Id,cost:null));
                valuation=await reports.QueryAsync("inventory-valuation",new StockReportQuery{WarehouseId=second.Value.Id},ct);Assert.Equal("120.0000",valuation.Summary.Cost);
                await PostOk(f,f.Command("adjustment","report-adjust","-1",1,cost:null));
                var count=await f.Store.CreateCountAsync(1,1,null,ct);Assert.True(count.Succeeded);var line=count.Value.Lines.Single();
                Assert.True((await f.InventoryService.PostCountAsync(count.Value.Id,new InventoryCountPostCommand{OperationKey="report-count",UserId=1,Lines=new[]{new InventoryCountLine{VariantId=f.VariantId,BalanceVersion=line.BalanceVersion,CountedQuantity="6"}}},ct)).Succeeded);
                foreach(var type in new[]{"count","adjustment"})
                {
                    var result=await reports.QueryAsync("inventory-movements",new StockReportQuery{From="2000-01-01",To="2099-12-31",Type=type},ct);Assert.Single(result.Items);Assert.Equal("1",result.Items[0].QuantityOut);Assert.Equal("-120.0000",result.Items[0].Cost);
                }
                valuation=await reports.QueryAsync("inventory-valuation",new StockReportQuery(),ct);Assert.Equal("840.0000",valuation.Summary.Cost);Assert.True(await f.Store.VerifyProjectionAsync(ct));
            }
        }
    }
}
