using System;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Sales;
using Dapper;
using Xunit;
using Xunit.Abstractions;

namespace AlKhayal.IntegrationTests
{
    public sealed class SalesReturnConflictTests
    {
        private readonly ITestOutputHelper _output;
        public SalesReturnConflictTests(ITestOutputHelper output) { _output = output; }

        [Theory]
        [InlineData("credit", "1,1,1")]
        [InlineData("credit", "2,1")]
        [InlineData("credit", "1,2")]
        [InlineData("credit", "3")]
        [InlineData("cash", "1,1,1")]
        [InlineData("cash", "2,1")]
        [InlineData("cash", "1,2")]
        [InlineData("cash", "3")]
        public async Task Discounted_returns_reconcile(string mode, string sequence)
            => await VerifySequence(mode, sequence, "0.01", "0.02", 0, false, "0");

        [Theory]
        [InlineData("0", "0", 0, false, "0")]
        [InlineData("0", "0.01", 0, false, "0")]
        [InlineData("0.01", "0", 0, false, "0")]
        [InlineData("0.01", "0.02", 15, true, "0")]
        [InlineData("0.01", "0.02", 15, false, "0")]
        [InlineData("0.01", "0.02", 0, false, "5")]
        [InlineData("0.01", "0.02", 15, true, "5")]
        [InlineData("0.01", "0.02", 15, false, "5")]
        public async Task Credit_tax_discount_and_collection_matrix(string invoiceDiscount, string lineDiscount, int tax, bool inclusive, string collection)
            => await VerifySequence("credit", "1,1,1", invoiceDiscount, lineDiscount, tax, inclusive, collection);

        private async Task VerifySequence(string mode, string sequence, string invoiceDiscount, string lineDiscount, int tax, bool inclusive, string collection)
        {
            using (var f = await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("1", "2");
                await f.Receipt("2", "3");
                using (var c = f.Db.Open()) c.Execute("UPDATE ApplicationSettings SET TaxEnabled=@Enabled,DefaultTaxRate=@Tax,PricesIncludeTax=@Inclusive", new { Enabled=tax>0?1:0, Tax=tax, Inclusive=inclusive?1:0 });
                var sale = await f.Sale("3", invoiceDiscount, lineDiscount, mode);
                if (tax==0 && invoiceDiscount=="0.01" && lineDiscount=="0.02") Assert.Equal(299700, sale.TotalScaled);
                var original = Original(f);
                if (collection != "0")
                {
                    var payment=await f.Sales.AddPaymentAsync(sale.Id,new SalePaymentCommand { Amount=collection, Method="cash", OperationKey="prior-collection", OccurredOn="2026-10-30" },f.Db.UserId,CancellationToken.None);
                    Assert.True(payment.Succeeded,payment.ErrorCode);
                }
                // The return must retain the saved price and tax snapshots.
                await f.Price("900");
                using (var c=f.Db.Open()) c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=70,PricesIncludeTax=@I",new { I=inclusive?0:1 });
                long cumulative=0;
                var step = 0;
                foreach (var quantity in sequence.Split(','))
                {
                    var cmd = new SaleReturnCommand { OperationKey = "hotfix-" + (++step), Reason = "اختبار", Lines = new() { new SaleReturnLineCommand { InvoiceLineId = sale.Lines.Single().Id, Quantity = quantity } } };
                    using (var c = f.Db.Open())
                        _output.WriteLine("Before step {0}: {1}", step, Newtonsoft.Json.JsonConvert.SerializeObject(c.Query("SELECT printf('%.17g',Balance) Balance, CAST(ROUND(Balance*10000) AS INTEGER) Scaled, Balance-CAST(ROUND(Balance*10000) AS INTEGER)/10000.0 Difference FROM Customers")));
                    var returned = await f.Sales.ReturnAsync(sale.Id, cmd, f.Db.UserId, CancellationToken.None);
                    _output.WriteLine("Step {0}: {1}", step, Newtonsoft.Json.JsonConvert.SerializeObject(returned));
                    Assert.True(returned.Succeeded, "step " + step + ": " + returned.ErrorCode);
                    cumulative+=returned.TotalScaled; Assert.True(cumulative<=sale.TotalScaled);
                    using(var c=f.Db.Open())
                    {
                        Assert.Equal(cumulative,c.ExecuteScalar<long>("SELECT SUM(TotalScaled) FROM SalesReturns"));
                        Assert.Equal(c.ExecuteScalar<long>("SELECT COALESCE(SUM(CASE WHEN TransactionType='increase' THEN AmountScaled ELSE -AmountScaled END),0) FROM CustomerTransactions"),c.ExecuteScalar<long>("SELECT CAST(ROUND(Balance*10000) AS INTEGER) FROM Customers"));
                        Assert.True(c.ExecuteScalar<decimal>("SELECT Balance FROM Customers")>=0);
                    }
                    var beforeReplay=Snapshot(f);
                    var replay = await f.Sales.ReturnAsync(sale.Id, cmd, f.Db.UserId, CancellationToken.None);
                    Assert.True(replay.Succeeded); Assert.True(replay.Replayed); Assert.Equal(returned.ReturnId, replay.ReturnId);
                    Assert.Equal(beforeReplay,Snapshot(f));
                }
                Assert.Equal(sale.TotalScaled,cumulative); Assert.Equal(original,Original(f));
                using(var c=f.Db.Open())
                {
                    Assert.Equal(3000000,c.ExecuteScalar<long>("SELECT SUM(QuantityScaled) FROM SalesReturnLines"));
                    Assert.Equal(80000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations"));
                    Assert.Equal(3000000,c.ExecuteScalar<long>("SELECT SUM(QuantityBaseScaled) FROM InventoryBalances"));
                    Assert.Equal(80000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM InventoryBalances"));
                    Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers"));
                    var expectedCash=mode=="cash"?sale.TotalScaled:(long)(decimal.Parse(collection,CultureInfo.InvariantCulture)*10000m);
                    Assert.Equal(expectedCash,c.ExecuteScalar<long>("SELECT COALESCE(SUM(CashRefundScaled),0) FROM SalesReturns"));
                    Assert.Equal(expectedCash,c.ExecuteScalar<long>("SELECT COALESCE(SUM(m.AmountScaled),0) FROM TreasuryMovements m JOIN ReturnTreasuryLinks l ON l.MovementId=m.Id"));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ReturnTreasuryLinks GROUP BY SalesReturnId HAVING COUNT(*)>1"));
                    Assert.Equal(mode=="cash"?1:0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesTreasuryReceipts"));
                }
                var beforeOver=Snapshot(f);
                var over=await f.Sales.ReturnAsync(sale.Id,new SaleReturnCommand { OperationKey="over",Reason="زيادة",Lines=new() { new SaleReturnLineCommand { InvoiceLineId=sale.Lines.Single().Id,Quantity="0.000001" } } },f.Db.UserId,CancellationToken.None);
                Assert.Equal("return_quantity_exceeded",over.ErrorCode);Assert.Equal(beforeOver,Snapshot(f));
                var day=DateTime.UtcNow.ToString("yyyy-MM-dd");
                var report=await f.Report(day,day);Assert.True(report.Summary.Complete);Assert.Equal("0.0000",report.Summary.NetProfit);Assert.Equal("0.0000",report.Summary.Cogs);Assert.Equal("0.0000",report.Summary.Revenue);Assert.Equal(step+1,report.Summary.EventCount);
                var product=await StockReportTests.Query(f,"product-profit");Assert.Equal("0.0000",product.Summary.Profit);
            }
        }

        private static string Original(NetProfitReportTests.Fixture f)
        {
            using(var c=f.Db.Open()) return Newtonsoft.Json.JsonConvert.SerializeObject(new { Invoice=c.Query("SELECT Id,Status,TotalScaled,TaxScaled,DiscountScaled,Version,PostedUtc FROM SalesInvoices"), Lines=c.Query("SELECT * FROM SalesInvoiceLines") });
        }

        [Fact]
        public async Task Existing_partial_history_with_legacy_REAL_residue_can_finish_without_rewriting_history()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("3","2");var sale=await f.Sale("3","0.01","0.02");
                for(var i=0;i<2;i++) Assert.True((await f.Sales.ReturnAsync(sale.Id,Return(sale,"old-"+i,"1"),f.Db.UserId,CancellationToken.None)).Succeeded);
                string history;
                using(var c=f.Db.Open())
                {
                    // Reproduce the projection left by the old writer; ledger/history remain intact.
                    c.Execute("UPDATE Customers SET Balance=9.989999999999997");
                    history=Newtonsoft.Json.JsonConvert.SerializeObject(c.Query("SELECT * FROM SalesReturns"));
                }
                var result=await f.Sales.ReturnAsync(sale.Id,Return(sale,"finish-old","1"),f.Db.UserId,CancellationToken.None);
                Assert.True(result.Succeeded,result.ErrorCode);Assert.Equal(99900,result.CreditReductionScaled);Assert.Equal(0,result.CashRefundScaled);
                using(var c=f.Db.Open())
                {
                    Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers"));
                    Assert.Equal(history,Newtonsoft.Json.JsonConvert.SerializeObject(c.Query("SELECT * FROM SalesReturns WHERE Id<@Id",new { Id=result.ReturnId })));
                }
            }
        }

        [Fact]
        public async Task Competing_stale_quantity_and_changed_key_requests_keep_real_conflicts()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("3","2");var sale=await f.Sale("3","0.01","0.02");
                var commands=new[] { Return(sale,"compete-a","2"),Return(sale,"compete-b","2") };
                var start=new TaskCompletionSource<bool>();
                var tasks=commands.Select(cmd=>Task.Run(async()=> { await start.Task;return await f.Sales.ReturnAsync(sale.Id,cmd,f.Db.UserId,CancellationToken.None); })).ToArray();
                start.SetResult(true);var results=await Task.WhenAll(tasks);
                for(var i=0;i<results.Length;i++) if(results[i].ErrorCode=="database_busy") results[i]=await f.Sales.ReturnAsync(sale.Id,commands[i],f.Db.UserId,CancellationToken.None);
                Assert.Single(results,x=>x.Succeeded);Assert.Single(results,x=>x.ErrorCode=="return_quantity_exceeded");
                var winner=Array.FindIndex(results,x=>x.Succeeded);var before=Snapshot(f);
                var changed=Return(sale,commands[winner].OperationKey,"1");
                Assert.Equal("operation_key_conflict",(await f.Sales.ReturnAsync(sale.Id,changed,f.Db.UserId,CancellationToken.None)).ErrorCode);Assert.Equal(before,Snapshot(f));
                Assert.True((await f.Sales.ReturnAsync(sale.Id,Return(sale,"remaining","1"),f.Db.UserId,CancellationToken.None)).Succeeded);
                using(var c=f.Db.Open()) { Assert.Equal(299700,c.ExecuteScalar<long>("SELECT SUM(TotalScaled) FROM SalesReturns"));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers")); }
            }
        }

        [Fact]
        public async Task Late_treasury_constraint_rolls_back_stock_ledger_projection_and_events_then_same_key_can_retry()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("3","2");var sale=await f.Sale("3","0.01","0.02");
                Assert.True((await f.Sales.AddPaymentAsync(sale.Id,new SalePaymentCommand { Amount="5",Method="cash",OperationKey="rollback-paid",OccurredOn="2026-10-30" },f.Db.UserId,CancellationToken.None)).Succeeded);
                using(var c=f.Db.Open()) c.Execute("CREATE TRIGGER Test_FailRefund BEFORE INSERT ON TreasuryMovements BEGIN SELECT RAISE(ABORT,'test late constraint'); END");
                var before=Snapshot(f);var cmd=Return(sale,"rollback-return","3");
                var failed=await f.Sales.ReturnAsync(sale.Id,cmd,f.Db.UserId,CancellationToken.None);
                Assert.Equal("sales_conflict",failed.ErrorCode);Assert.Equal(before,Snapshot(f));
                var day=DateTime.UtcNow.ToString("yyyy-MM-dd");Assert.Equal(1,(await f.Report(day,day)).Summary.EventCount);
                using(var c=f.Db.Open()) c.Execute("DROP TRIGGER Test_FailRefund");
                var result=await f.Sales.ReturnAsync(sale.Id,cmd,f.Db.UserId,CancellationToken.None);
                Assert.True(result.Succeeded,result.ErrorCode);Assert.Equal(50000,result.CashRefundScaled);Assert.Equal(249700,result.CreditReductionScaled);
                Assert.Equal(2,(await f.Report(day,day)).Summary.EventCount);
            }
        }

        [Fact]
        public async Task Multiline_credit_return_preserves_other_invoice_debt_and_original_components()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("6","2");var other=await f.Sale("3");
                var template=other.Lines.Single();
                var product=await new AlKhayal.Application.Catalog.CatalogService(new AlKhayal.Data.Catalog.SqliteCatalogStore(f.Cs)).CreateProductAsync(new AlKhayal.Application.Catalog.CreateProductCommand { OperationKey="multi-product",Name="صنف ثان",BaseUnitId=1,ProductType="stock",SalePrice="10",PurchasePrice="2",IsActive=true },CancellationToken.None);
                Assert.True(product.Succeeded,product.ErrorMessage);long variant,unit;
                using(var c=f.Db.Open()) { variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new { Id=product.Value.Id });unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id",new { Id=product.Value.Id }); }
                var receipt=new AlKhayal.Application.Inventory.InventoryPostCommand { OperationKey="multi-receipt",Type="manual_receipt",WarehouseId=1,UserId=f.Db.UserId,Lines=new[] { new AlKhayal.Application.Inventory.InventoryLineInput { VariantId=variant,ProductUnitId=unit,Quantity="2",UnitCost="2",SalePrice="10" } } };
                Assert.True((await new AlKhayal.Data.Inventory.SqliteInventoryStore(f.Cs).PostAsync(receipt,AlKhayal.Application.Inventory.InventoryService.CanonicalHashPost(receipt),CancellationToken.None)).Succeeded);
                var draft=await f.Sales.CreateDraftAsync(new SaleDraftCommand { WarehouseId=1,CustomerId=other.CustomerId,PaymentMode="credit",InvoiceDate="2020-01-01",Discount="0.01",Lines=new() {
                    new SaleLine { VariantId=template.VariantId,ProductUnitId=template.ProductUnitId,Quantity="1",UnitPrice="10",Discount="0.01" },
                    new SaleLine { VariantId=variant,ProductUnitId=unit,Quantity="2",UnitPrice="10",Discount="0.02" }
                } },f.Db.UserId,CancellationToken.None);
                var post=await f.Sales.PostAsync(draft.Id,"multiline-post",f.Db.UserId,CancellationToken.None);Assert.True(post.Succeeded,post.ErrorCode);
                foreach(var line in post.Invoice.Lines)
                {
                    var result=await f.Sales.ReturnAsync(draft.Id,new SaleReturnCommand { OperationKey="multi-return-"+line.Id,Reason="إرجاع",Lines=new() { new SaleReturnLineCommand { InvoiceLineId=line.Id,Quantity=line.Quantity } } },f.Db.UserId,CancellationToken.None);
                    Assert.True(result.Succeeded,result.ErrorCode);Assert.Equal(line.LineTotalScaled,result.CreditReductionScaled);Assert.Equal(0,result.CashRefundScaled);
                }
                using(var c=f.Db.Open()) { Assert.Equal(300000,c.ExecuteScalar<long>("SELECT CAST(ROUND(Balance*10000) AS INTEGER) FROM Customers"));Assert.Equal(post.Invoice.TotalScaled,c.ExecuteScalar<long>("SELECT SUM(TotalScaled) FROM SalesReturns"));Assert.Equal(60000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations")); }
            }
        }

        private static SaleReturnCommand Return(SaleInvoice sale,string key,string quantity) => new SaleReturnCommand { OperationKey=key,Reason="اختبار",Lines=new() { new SaleReturnLineCommand { InvoiceLineId=sale.Lines.Single().Id,Quantity=quantity } } };
        private static string Snapshot(NetProfitReportTests.Fixture f)
        {
            using(var c=f.Db.Open()) return Newtonsoft.Json.JsonConvert.SerializeObject(new { Returns=c.Query("SELECT * FROM SalesReturns"), Lines=c.Query("SELECT * FROM SalesReturnLines"), Costs=c.Query("SELECT * FROM SalesReturnCostAllocations"), Payments=c.Query("SELECT * FROM SalesPayments"), Ledger=c.Query("SELECT * FROM CustomerTransactions"), Customers=c.Query("SELECT * FROM Customers"), Layers=c.Query("SELECT * FROM InventoryCostLayers"), Balances=c.Query("SELECT * FROM InventoryBalances"), Movements=c.Query("SELECT * FROM StockMovements"), Documents=c.Query("SELECT * FROM InventoryDocuments"), DocumentLines=c.Query("SELECT * FROM InventoryDocumentLines"), Audit=c.Query("SELECT * FROM InventoryAudit"), Treasury=c.Query("SELECT * FROM TreasuryMovements"), Accounts=c.Query("SELECT * FROM TreasuryAccounts"), Links=c.Query("SELECT * FROM ReturnTreasuryLinks"), Invoice=c.Query("SELECT * FROM SalesInvoices") });
        }
    }
}
