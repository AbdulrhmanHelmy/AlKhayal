using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Expenses;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Reports;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Reports;
using AlKhayal.Data.Sales;
using AlKhayal.Data.Expenses;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class NetProfitReportTests
    {
        private static readonly CancellationToken Ct=CancellationToken.None;
        [Fact]
        public async Task Collections_installment_principal_and_maintenance_advances_do_not_add_sales_revenue()
        {
            using(var f=await Fixture.Create())
            {
                await f.Receipt("1","2");var sale=await f.Sale("1");
                var day=DateTime.UtcNow.ToString("yyyy-MM-dd");var before=await f.Report(day,day);
                var paid=await f.Sales.AddPaymentAsync(sale.Id,new SalePaymentCommand{Amount="5",Method="cash",OccurredOn=day,OperationKey="profit-collection"},f.Db.UserId,Ct);Assert.True(paid.Succeeded,paid.ErrorMessage);
                var customer=new AlKhayal.Application.Customers.CustomerService(new AlKhayal.Data.Customers.SqliteCustomerRepository(f.Cs)).Create(new AlKhayal.Core.Models.Customer{Name="خدمات وتقسيط",Phone="010",Balance=0,Status="نشط"});
                var maintenance=new AlKhayal.Application.Maintenance.MaintenanceService(new AlKhayal.Data.Maintenance.SqliteMaintenanceRepository(f.Cs));
                maintenance.Create(new AlKhayal.Application.Maintenance.MaintenanceTicketCommand{CustomerId=customer.Id,Phone="010",Device="هاتف",Model="موديل",Issue="عطل",EstimatedCost=500m,AdvancePaid=100m,ReceivedOn=day,ExpectedOn=day,OperationKey="profit-maintenance-advance"});
                long product;using(var c=f.Db.Open())product=c.ExecuteScalar<long>("SELECT Id FROM Products LIMIT 1");
                var installments=new AlKhayal.Application.Customers.InstallmentService(new AlKhayal.Data.Customers.SqliteCustomerInstallmentRepository(f.Cs));
                var contract=installments.Create(customer.Id,product,10m,2m,0m,2,1,day,"profit-contract");
                installments.Pay(customer.Id,contract.Id,1,day,"profit-principal");
                var after=await f.Report(day,day);Assert.Equal(before.Summary.Revenue,after.Summary.Revenue);Assert.Equal(before.Summary.NetProfit,after.Summary.NetProfit);Assert.Equal(1,after.Summary.EventCount);
            }
        }

        [Fact]
        public async Task Thirty_thousand_saved_FIFO_sales_are_aggregated_in_SQL_and_expenses_are_server_paged()
        {
            using(var f=await Fixture.Create())
            {
                await f.Receipt("30000","2");
                using(var c=f.Db.Open())using(var tx=c.BeginTransaction())
                {
                    // Bulk fixture of persisted posting facts. Every sale has its own issue,
                    // saved allocation and original financial components; no mocked store.
                    const string ids="WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<30000) ";
                    c.Execute(ids+@"INSERT INTO InventoryDocuments(Id,DocumentType,Status,SourceWarehouseId,Reason,CreatedByUserId,CreatedUtc,PostedUtc)
                        SELECT 100000+x,'manual_issue','posted',1,'bulk',@User,@Now,@Now FROM n",new{User=f.Db.UserId,Now="2026-03-01T12:00:00.0000000Z"},tx);
                    c.Execute(ids+@"INSERT INTO InventoryDocumentLines(Id,DocumentId,LineNumber,VariantId,EnteredQuantityScaled,ProductUnitId,ToBaseNumerator,ToBaseDenominator,BaseQuantityScaled)
                        SELECT 100000+x,100000+x,1,(SELECT Id FROM ProductVariants LIMIT 1),1000000,(SELECT Id FROM ProductUnits LIMIT 1),1,1,-1000000 FROM n",transaction:tx);
                    c.Execute(ids+@"INSERT INTO StockMovements(Id,DocumentId,DocumentLineId,WarehouseId,VariantId,QuantityBaseScaled,ValueScaled,CreatedUtc)
                        SELECT 100000+x,100000+x,100000+x,1,(SELECT Id FROM ProductVariants LIMIT 1),-1000000,-20000,'2026-03-01T12:00:00.0000000Z' FROM n",transaction:tx);
                    c.Execute(ids+@"INSERT INTO InventoryCostAllocations(MovementId,LayerId,QuantityScaled,ValueScaled)
                        SELECT 100000+x,(SELECT Id FROM InventoryCostLayers LIMIT 1),1000000,20000 FROM n",transaction:tx);
                    c.Execute(ids+@"INSERT INTO SalesInvoices(Id,InvoiceNumber,InvoiceDate,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,InventoryDocumentId,CreatedUtc)
                        SELECT 100000+x,'BULK-'||x,'2020-01-01',1,@User,'draft','credit',100000,0,0,100000,100000+x,'2026-03-01T12:00:00.0000000Z' FROM n",new{User=f.Db.UserId},tx);
                    c.Execute(ids+@"INSERT INTO SalesInvoiceLines(SalesInvoiceId,LineNumber,VariantId,ProductUnitId,QuantityScaled,UnitPriceScaled,DiscountScaled,InvoiceDiscountScaled,TaxScaled,LineTotalScaled,InventoryDocumentLineId)
                        SELECT 100000+x,1,(SELECT Id FROM ProductVariants LIMIT 1),(SELECT Id FROM ProductUnits LIMIT 1),1000000,100000,0,0,0,100000,100000+x FROM n",transaction:tx);
                    c.Execute("UPDATE SalesInvoices SET Status='posted',PostedUtc='2026-03-01T12:00:00.0000000Z';UPDATE InventoryCostLayers SET RemainingQuantityScaled=0,RemainingValueScaled=0;UPDATE InventoryBalances SET QuantityBaseScaled=0,ValueScaled=0",transaction:tx);
                    tx.Commit();
                }
                var clock=System.Diagnostics.Stopwatch.StartNew();var report=await f.Report("2026-03-01","2026-03-31");clock.Stop();
                Assert.True(report.Summary.Complete);Assert.Equal("300000.0000",report.Summary.Revenue);Assert.Equal("60000.0000",report.Summary.Cogs);Assert.Equal("240000.0000",report.Summary.NetProfit);Reconciles(report);
                Assert.True(clock.Elapsed<TimeSpan.FromSeconds(30),"SQL aggregation exceeded 30 seconds: "+clock.Elapsed);
                for(int n=0;n<3;n++)await f.Expense("1");
                var day=DateTime.UtcNow.ToString("yyyy-MM-dd");var first=await f.Report(day,day,pageSize:1);var second=await f.Report(day,day,page:2,pageSize:1);
                Assert.Equal(3,first.ExpenseCount);Assert.Single(first.ExpenseItems);Assert.NotEqual(first.ExpenseItems[0].Id,second.ExpenseItems[0].Id);Assert.Equal(first.Summary.UnclassifiedAmount,second.Summary.UnclassifiedAmount);
                // Also exercise the maximum bounded date range/SQLite parameter count.
                var empty=await f.Report("2000-01-01","2010-01-07");Assert.Equal(3660,empty.Items.Count);Assert.Equal("0.0000",empty.Summary.NetProfit);
            }
        }
        [Fact]
        public async Task Two_FIFO_batches_and_classified_expenses_reconcile_restart_and_read_only()
        {
            using(var f=await Fixture.Create())
            {
                await f.Receipt("10","100"); await f.Receipt("10","120"); await f.Price("200");
                var sale=await f.Sale("12"); f.Time("SalesInvoices","PostedUtc","Id",sale.Id,"2026-01-31T12:00:00.0000000Z");
                await f.Price("900");
                var voucher=await f.Expense("160"); f.Time("ExpenseVouchers","CreatedUtc","Id",voucher.Id,"2026-02-01T12:00:00.0000000Z");
                var unclassified=await f.Report("2026-01-31","2026-02-01");
                Assert.False(unclassified.Summary.Complete); Assert.Null(unclassified.Summary.NetProfit);
                Assert.Equal("160.0000",unclassified.Summary.UnclassifiedAmount);
                await f.Classify(voucher.Id,"included");
                var before=File.ReadAllBytes(f.Db.DatabasePath);
                var daily=await f.Report("2026-01-31","2026-02-01");
                Assert.True(daily.Summary.Complete,Newtonsoft.Json.JsonConvert.SerializeObject(daily.Summary)); Assert.Equal("2400.0000",daily.Summary.Revenue);
                Assert.Equal("1240.0000",daily.Summary.Cogs); Assert.Equal("1160.0000",daily.Summary.GrossProfit);
                Assert.Equal("160.0000",daily.Summary.Expenses); Assert.Equal("1000.0000",daily.Summary.NetProfit);
                Assert.Equal("41.67",daily.Summary.NetMargin);
                Assert.Equal("1160.0000",daily.Items[0].NetProfit); Assert.Equal("-160.0000",daily.Items[1].NetProfit);
                Reconciles(daily);
                var monthly=await f.Report("2026-01-31","2026-02-01","monthly"); Reconciles(monthly);
                Assert.Equal(daily.Summary.NetProfit,monthly.Summary.NetProfit); Assert.Equal(2,monthly.Items.Count);
                Migrator.Run(f.Cs); var restarted=await f.Report("2026-01-31","2026-02-01");
                Assert.Equal("1000.0000",restarted.Summary.NetProfit);
                Assert.Equal(before,File.ReadAllBytes(f.Db.DatabasePath));
                Assert.Equal("0.0000",(await f.Report("2026-01-30","2026-01-30")).Summary.NetProfit);
                Assert.Equal("-160.0000",(await f.Report("2026-02-01","2026-02-01")).Summary.NetProfit);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Original_tax_discounts_repeated_partial_returns_and_final_residual_use_saved_facts(bool inclusive)
        {
            using(var f=await Fixture.Create())
            {
                await f.Receipt("3","2"); await f.Price("10");
                using(var c=f.Db.Open()) c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=15,PricesIncludeTax=@I",new{I=inclusive?1:0});
                var sale=await f.Sale("3","0.01","0.02","cash"); var line=sale.Lines.Single();
                f.Time("SalesInvoices","PostedUtc","Id",sale.Id,"2026-01-31T12:00:00.0000000Z");
                using(var c=f.Db.Open()) c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=70,PricesIncludeTax=@I",new{I=inclusive?0:1});
                await f.Price("99");
                var gross=new List<long>(); var returnIds=new List<long>();
                for(int n=0;n<3;n++)
                {
                    var returned=await f.Sales.ReturnAsync(sale.Id,new SaleReturnCommand{OperationKey="return-"+n,Reason="إرجاع جزئي",Lines=new List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},f.Db.UserId,Ct);
                    Assert.True(returned.Succeeded,"Partial "+n+": "+returned.ErrorMessage);gross.Add(returned.TotalScaled);returnIds.Add(returned.ReturnId);
                }
                for(int n=0;n<returnIds.Count;n++)f.Time("SalesReturns","CreatedUtc","Id",returnIds[n],"2026-02-0"+(n+1)+"T12:00:00.0000000Z");
                var report=await f.Report("2026-01-31","2026-02-03"); Assert.True(report.Summary.Complete,Newtonsoft.Json.JsonConvert.SerializeObject(report.Summary));
                Assert.Equal("0.0000",report.Summary.Revenue);Assert.Equal("0.0000",report.Summary.Cogs);Assert.Equal("0.0000",report.Summary.NetProfit);
                var firstTax=(long)decimal.Floor((decimal)line.TaxScaled/3);
                Assert.Equal(Money(-(gross[0]-firstTax)),report.Items[1].Revenue);
                // Allocation is cumulative, so the middle and final events carry any scale-4 residual.
                var secondTax=(long)decimal.Floor((decimal)line.TaxScaled*2/3)-firstTax;
                Assert.Equal(Money(-(gross[1]-secondTax)),report.Items[2].Revenue);
                Assert.Equal(Money(-(gross[2]-(line.TaxScaled-firstTax-secondTax))),report.Items[3].Revenue);
                Assert.Equal("-2.0000",report.Items[1].Cogs);
                var laterOnly=await f.Report("2026-02-02","2026-02-03");
                Assert.Equal(Money(Parse(report.Items[2].Revenue)+Parse(report.Items[3].Revenue),false),laterOnly.Summary.Revenue);
                Reconciles(report); Reconciles(await f.Report("2026-01-31","2026-02-03","monthly"));
            }
        }

        [Fact]
        public async Task Mixed_original_taxable_and_exempt_line_snapshots_remain_distinct()
        {
            using(var f=await Fixture.Create())
            {
                await f.Receipt("2","2"); long variant,unit,secondVariant,secondUnit;
                using(var c=f.Db.Open()){variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants LIMIT 1");unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits LIMIT 1");}
                var second=(await new CatalogService(new SqliteCatalogStore(f.Cs)).CreateProductAsync(new CreateProductCommand{OperationKey="profit-exempt",Name="منتج معفى تاريخيًا",BaseUnitId=1,ProductType="stock",SalePrice="10",IsActive=true},Ct)).Value;
                using(var c=f.Db.Open()){secondVariant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=second.Id});secondUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=second.Id});}
                var stock=new InventoryPostCommand{OperationKey="profit-exempt-stock",Type="manual_receipt",WarehouseId=1,UserId=f.Db.UserId,Lines=new[]{new InventoryLineInput{VariantId=secondVariant,ProductUnitId=secondUnit,Quantity="1",UnitCost="2",SalePrice="10"}}};Assert.True((await new SqliteInventoryStore(f.Cs).PostAsync(stock,InventoryService.CanonicalHashPost(stock),Ct)).Succeeded);
                var draft=await f.Sales.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2020-01-01",PaymentMode="cash",Lines=new List<SaleLine>{new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="10"},new SaleLine{VariantId=secondVariant,ProductUnitId=secondUnit,Quantity="1",UnitPrice="10"}}},f.Db.UserId,Ct);
                // Historical mixed-treatment snapshot while still draft. Reporting must use
                // these saved components, rather than impose today's uniform rate on both lines.
                using(var c=f.Db.Open())c.Execute("UPDATE SalesInvoices SET TaxRate=15,TaxIncluded=0,TaxScaled=15000,TotalScaled=215000 WHERE Id=@Id;UPDATE SalesInvoiceLines SET TaxScaled=15000,LineTotalScaled=115000 WHERE SalesInvoiceId=@Id AND LineNumber=1",new{Id=draft.Id});
                var posted=await f.Sales.PostAsync(draft.Id,"mixed-post",f.Db.UserId,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var line=posted.Invoice.Lines.Single(x=>x.TaxScaled==0);
                var returned=await f.Sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="mixed-exempt-return",Reason="إرجاع معفى",Lines=new List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},f.Db.UserId,Ct);Assert.True(returned.Succeeded,returned.ErrorMessage);
                var day=DateTime.UtcNow.ToString("yyyy-MM-dd");var report=await f.Report(day,day);Assert.True(report.Summary.Complete);
                Assert.Equal("10.0000",report.Summary.Revenue);Assert.Equal("2.0000",report.Summary.Cogs);Assert.Equal("8.0000",report.Summary.NetProfit);
            }
        }

        [Fact]
        public async Task Returns_crossing_two_FIFO_layers_reverse_saved_cost_in_their_own_periods()
        {
            using (var f = await Fixture.Create())
            {
                await f.Receipt("10", "100");
                await f.Receipt("10", "120");
                await f.Price("200");
                var sale = await f.Sale("12", paymentMode: "cash");
                f.Time("SalesInvoices", "PostedUtc", "Id", sale.Id, "2026-01-31T12:00:00.0000000Z");
                var line = Assert.Single(sale.Lines);
                var first = await f.Sales.ReturnAsync(sale.Id, new SaleReturnCommand {
                    OperationKey = "profit-cross-layer", Reason = "إرجاع من دفعتين",
                    Lines = new List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = line.Id, Quantity = "11" } }
                }, f.Db.UserId, Ct);
                Assert.True(first.Succeeded, first.ErrorMessage);
                f.Time("SalesReturns", "CreatedUtc", "Id", first.ReturnId, "2026-02-01T12:00:00.0000000Z");
                await f.Price("900");
                var final = await f.Sales.ReturnAsync(sale.Id, new SaleReturnCommand {
                    OperationKey = "profit-cross-layer-final", Reason = "إرجاع المتبقي",
                    Lines = new List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = line.Id, Quantity = "1" } }
                }, f.Db.UserId, Ct);
                Assert.True(final.Succeeded, final.ErrorMessage);
                f.Time("SalesReturns", "CreatedUtc", "Id", final.ReturnId, "2026-02-02T12:00:00.0000000Z");
                var report = await f.Report("2026-01-31", "2026-02-02");
                Assert.True(report.Summary.Complete);
                Assert.Equal("-1120.0000", report.Items[1].Cogs);
                Assert.Equal("-120.0000", report.Items[2].Cogs);
                Assert.Equal("0.0000", report.Summary.NetProfit);
                Reconciles(report);
                Reconciles(await f.Report("2026-01-31", "2026-02-02", "monthly"));
                var firstPeriod = await f.Report("2026-01-31", "2026-02-01");
                Assert.Equal("200.0000", firstPeriod.Summary.Revenue);
                Assert.Equal("120.0000", firstPeriod.Summary.Cogs);
                Assert.Equal("80.0000", firstPeriod.Summary.NetProfit);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Missing_saved_tax_or_restoration_evidence_never_produces_complete_return_profit(bool missingTax)
        {
            using (var f = await Fixture.Create())
            {
                await f.Receipt("2", "2");
                using (var c = f.Db.Open()) c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=15,PricesIncludeTax=0");
                var sale = await f.Sale("2", paymentMode: "cash");
                f.Time("SalesInvoices", "PostedUtc", "Id", sale.Id, "2026-01-31T12:00:00.0000000Z");
                var returned = await f.Sales.ReturnAsync(sale.Id, new SaleReturnCommand {
                    OperationKey = "profit-missing-evidence", Reason = "اختبار بيانات قديمة",
                    Lines = new List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = sale.Lines.Single().Id, Quantity = "1" } }
                }, f.Db.UserId, Ct);
                Assert.True(returned.Succeeded, returned.ErrorMessage);
                f.Time("SalesReturns", "CreatedUtc", "Id", returned.ReturnId, "2026-02-01T12:00:00.0000000Z");
                // Disposable legacy fixture only: remove evidence and immediately restore its immutable trigger.
                using (var c = f.Db.Open()) using (var tx = c.BeginTransaction())
                {
                    var name = missingTax ? "TR_SalesInvoices_PostedImmutable" : "TR_SalesReturnCostAllocations_NoDelete";
                    var trigger = c.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE name=@Name", new { Name = name }, tx);
                    c.Execute("DROP TRIGGER \"" + name + "\"", transaction: tx);
                    if (missingTax) c.Execute("UPDATE SalesInvoices SET TaxScaled=0 WHERE Id=@Id", new { Id = sale.Id }, tx);
                    else c.Execute("DELETE FROM SalesReturnCostAllocations WHERE SalesReturnLineId IN (SELECT Id FROM SalesReturnLines WHERE SalesReturnId=@Id)", new { Id = returned.ReturnId }, tx);
                    c.Execute(trigger, transaction: tx);
                    tx.Commit();
                }
                var before = File.ReadAllBytes(f.Db.DatabasePath);
                var report = await f.Report("2026-02-01", "2026-02-01");
                Assert.False(report.Summary.Complete);
                Assert.Null(report.Summary.NetProfit);
                Assert.Null(report.Summary.GrossProfit);
                Assert.Equal(missingTax ? 1 : 0, report.Summary.MissingRevenueCount);
                Assert.Equal(missingTax ? 0 : 1, report.Summary.MissingCostCount);
                if (missingTax) { Assert.Null(report.Summary.Revenue); Assert.Equal("-2.0000", report.Summary.Cogs); }
                else { Assert.Equal("-10.0000", report.Summary.Revenue); Assert.Null(report.Summary.Cogs); }
                Assert.Equal(before, File.ReadAllBytes(f.Db.DatabasePath));
            }
        }

        [Fact]
        public async Task Later_cancellation_reverses_original_revenue_and_cost_once()
        {
            using(var f=await Fixture.Create())
            {
                await f.Receipt("2","4"); await f.Price("10"); var sale=await f.Sale("2");
                f.Time("SalesInvoices","PostedUtc","Id",sale.Id,"2026-01-31T12:00:00.0000000Z");
                var canceled=await f.Sales.CancelAsync(sale.Id,"cancel-profit","إلغاء فاتورة آجلة",f.Db.UserId,Ct);Assert.True(canceled.Succeeded,canceled.ErrorMessage);
                f.Time("SalesCancellations","CreatedUtc","SalesInvoiceId",sale.Id,"2026-02-01T12:00:00.0000000Z");
                var report=await f.Report("2026-01-31","2026-02-01");Assert.True(report.Summary.Complete);
                Assert.Equal("12.0000",report.Items[0].NetProfit);Assert.Equal("-12.0000",report.Items[1].NetProfit);Assert.Equal("0.0000",report.Summary.NetProfit);Reconciles(report);
                Assert.Equal("-20.0000",(await f.Report("2026-02-01","2026-02-01")).Summary.Revenue);
            }
        }

        [Fact]
        public async Task Classifications_are_explicit_audited_idempotent_and_do_not_change_voucher_or_treasury()
        {
            using(var f=await Fixture.Create())
            {
                var included=await f.Expense("10");var excluded=await f.Expense("20");var unknown=await f.Expense("30");
                await f.Classify(included.Id,"included");await f.Classify(excluded.Id,"excluded");
                var today=DateTime.UtcNow.ToString("yyyy-MM-dd");var report=await f.Report(today,today);
                Assert.Equal("10.0000",report.Summary.Expenses);Assert.Equal("20.0000",report.Summary.ExcludedExpenses);
                Assert.Equal("30.0000",report.Summary.UnclassifiedAmount);Assert.Equal(1,report.Summary.UnclassifiedCount);Assert.Null(report.Summary.NetProfit);
                var cmd=new ExpenseClassificationCommand{Classification="excluded",Reason="مراجعة المصروف",ExpectedRevision=0,OperationKey="classify-final"};
                await f.Service.ClassifyAsync(unknown.Id,cmd,f.Db.UserId,Ct);await f.Service.ClassifyAsync(unknown.Id,cmd,f.Db.UserId,Ct);
                report=await f.Report(today,today);Assert.Equal("-10.0000",report.Summary.NetProfit);Assert.True(report.Summary.Complete);
                cmd.OperationKey="stale";await Assert.ThrowsAsync<ExpenseException>(()=>f.Service.ClassifyAsync(unknown.Id,cmd,f.Db.UserId,Ct));
                cmd.ExpectedRevision=1;cmd.Classification="unclassified";await f.Service.ClassifyAsync(unknown.Id,cmd,f.Db.UserId,Ct);
                Assert.False((await f.Report(today,today)).Summary.Complete);
                using(var c=f.Db.Open())
                {
                    Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ExpenseVouchers"));
                    Assert.Equal(600000,c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM ExpenseVouchers"));
                    Assert.Equal(4,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ExpenseProfitClassifications"));
                    Assert.Throws<SQLiteException>(()=>c.Execute("DELETE FROM ExpenseProfitClassifications"));
                }
            }
        }

        [Fact]
        public async Task Missing_legacy_line_or_FIFO_evidence_hides_profit_instead_of_using_zero()
        {
            using(var f=await Fixture.Create())
            {
                await f.Receipt("1","2");var sale=await f.Sale("1");
                using(var c=f.Db.Open())
                {
                    // Isolated legacy fixture: retain saved sale but remove its link to costing evidence.
                    var trigger=c.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE name='TR_SalesInvoiceLines_PostedUpdate'");
                    c.Execute("DROP TRIGGER TR_SalesInvoiceLines_PostedUpdate;UPDATE SalesInvoiceLines SET InventoryDocumentLineId=NULL WHERE SalesInvoiceId=@Id",new{Id=sale.Id});c.Execute(trigger);
                }
                var today=DateTime.UtcNow.ToString("yyyy-MM-dd");var report=await f.Report(today,today);
                Assert.Null(report.Summary.Cogs);Assert.Null(report.Summary.NetProfit);Assert.Equal(1,report.Summary.MissingCostCount);
                using(var c=f.Db.Open()) c.Execute(@"INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,CreatedUtc,PostedUtc)
                    VALUES('LEGACY-NO-LINES','2020-01-01',1,@User,'posted','credit',10000,0,0,10000,@Now,@Now)",new{User=f.Db.UserId,Now=DateTime.UtcNow.ToString("o")});
                report=await f.Report(today,today);Assert.Null(report.Summary.Revenue);Assert.Equal(1,report.Summary.MissingRevenueCount);Assert.Equal(2,report.Summary.MissingCostCount);
                using(var c=f.Db.Open())c.Execute(@"INSERT INTO SalesReturns(ReturnNumber,SalesInvoiceId,InventoryDocumentId,OperationKey,RequestHash,TotalScaled,CashRefundScaled,CreditReductionScaled,Reason,CreatedByUserId,CreatedUtc)
                    SELECT 'LEGACY-RETURN',Id,InventoryDocumentId,'legacy-return','legacy',10000,10000,0,'بيانات قديمة ناقصة',@User,@Now FROM SalesInvoices WHERE Id=@Id",new{Id=sale.Id,User=f.Db.UserId,Now=DateTime.UtcNow.ToString("o")});
                report=await f.Report(today,today);Assert.Equal(2,report.Summary.MissingRevenueCount);Assert.Equal(3,report.Summary.MissingCostCount);Assert.Null(report.Summary.NetProfit);
            }
        }

        [Theory]
        [InlineData("2026-03-08",23)]
        [InlineData("2026-11-01",25)]
        public async Task Local_DST_boundaries_and_empty_periods_reconcile(string day,int hours)
        {
            using(var f=await Fixture.Create())
            {
                await f.Receipt("1","2");var sale=await f.Sale("1");
                var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                var q=new ReportQuery{From=day,To=day};ReportPeriod.Validate(q,zone);
                Assert.Equal(hours,(DateTime.Parse(q.EndUtc).ToUniversalTime()-DateTime.Parse(q.StartUtc).ToUniversalTime()).TotalHours);
                f.Time("SalesInvoices","PostedUtc","Id",sale.Id,q.StartUtc);
                var service=new NetProfitService(new SqliteNetProfitStore(f.Cs),zone);
                var report=await service.QueryAsync(new NetProfitQuery{From=day,To=day},Ct);
                using(var c=f.Db.Open())Assert.True(report.Summary.Complete,Newtonsoft.Json.JsonConvert.SerializeObject(report.Summary)+" | "+Newtonsoft.Json.JsonConvert.SerializeObject(c.Query("SELECT l.*,dl.BaseQuantityScaled,dl.EnteredQuantityScaled,m.QuantityBaseScaled,m.ValueScaled,a.QuantityScaled AllocQty,a.ValueScaled AllocValue,cl.UnitCostScaled,cl.Basis FROM SalesInvoiceLines l JOIN InventoryDocumentLines dl ON dl.Id=l.InventoryDocumentLineId JOIN StockMovements m ON m.DocumentLineId=dl.Id JOIN InventoryCostAllocations a ON a.MovementId=m.Id JOIN InventoryCostLayers cl ON cl.Id=a.LayerId")));
                Assert.Equal("8.0000",report.Summary.NetProfit);Reconciles(report);
                f.Time("SalesInvoices","PostedUtc","Id",sale.Id,q.EndUtc);
                report=await service.QueryAsync(new NetProfitQuery{From=day,To=day},Ct);Assert.Equal("0.0000",report.Summary.NetProfit);Assert.Equal(0,report.Summary.EventCount);
            }
        }

        internal static void Reconciles(NetProfitResult report)
        {
            Assert.Equal(Parse(report.Summary.Revenue),report.Items.Sum(x=>Parse(x.Revenue)));
            Assert.Equal(Parse(report.Summary.Cogs),report.Items.Sum(x=>Parse(x.Cogs)));
            Assert.Equal(Parse(report.Summary.Expenses),report.Items.Sum(x=>Parse(x.Expenses)));
            Assert.Equal(Parse(report.Summary.NetProfit),report.Items.Sum(x=>Parse(x.NetProfit)));
        }
        private static decimal Parse(string amount) => decimal.Parse(amount,CultureInfo.InvariantCulture);
        private static string Money(decimal value,bool scaled=true) => (scaled?value/10000m:value).ToString("0.0000",CultureInfo.InvariantCulture);
        internal sealed class Fixture : IDisposable
        {
            internal readonly TreasuryPersistenceTests.Fixture Db=new TreasuryPersistenceTests.Fixture();
            internal string Cs=>Db.ConnectionString;
            internal SalesService Sales=>new SalesService(new SqliteSalesStore(Cs));
            internal NetProfitService Service=>new NetProfitService(new SqliteNetProfitStore(Cs),TimeZoneInfo.Utc);
            private long _product,_variant,_unit,_customer;private string _price="10";private int _key;private long _account,_category;
            internal static async Task<Fixture> Create()
            {
                var f=new Fixture();
                try
                {
                    var p=await new CatalogService(new SqliteCatalogStore(f.Cs)).CreateProductAsync(new CreateProductCommand{OperationKey="profit-product",Name="اختبار الأرباح",BaseUnitId=1,ProductType="stock",SalePrice="10",PurchasePrice="2",IsActive=true},Ct);
                    Assert.True(p.Succeeded,p.ErrorMessage);f._product=p.Value.Id;
                    using(var c=f.Db.Open())
                    {
                        f._variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=f._product});f._unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=f._product});
                        c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('PROFIT-C','عميل','','',0,0,'أساسي','نشط','2026-01-01','2026-01-01')");f._customer=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                    }
                    return f;
                }catch{f.Dispose();throw;}
            }
            internal async Task Receipt(string quantity,string cost)
            {
                var cmd=new InventoryPostCommand{OperationKey="profit-stock-"+(++_key),Type="manual_receipt",WarehouseId=1,UserId=Db.UserId,Lines=new[]{new InventoryLineInput{VariantId=_variant,ProductUnitId=_unit,Quantity=quantity,UnitCost=cost,SalePrice=_price}}};
                var r=await new SqliteInventoryStore(Cs).PostAsync(cmd,InventoryService.CanonicalHashPost(cmd),Ct);Assert.True(r.Succeeded,r.ErrorMessage);
            }
            internal async Task Price(string price)
            {
                var catalog=new CatalogService(new SqliteCatalogStore(Cs));var p=(await catalog.GetProductDetailAsync(_product,Ct)).Value;
                var r=await catalog.UpdateProductAsync(new UpdateProductCommand{ProductId=_product,Version=p.Version,Name=p.Name,BaseUnitId=p.BaseUnitId,ProductType=p.ProductType,IsActive=true,SalePrice=price,PurchasePrice="999",UserId=Db.UserId},Ct);Assert.True(r.Succeeded,r.ErrorMessage);_price=price;
            }
            internal async Task<SaleInvoice> Sale(string quantity,string invoiceDiscount="0",string lineDiscount="0",string paymentMode="credit")
            {
                var draft=await Sales.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,CustomerId=_customer,InvoiceDate="2020-01-01",PaymentMode=paymentMode,Discount=invoiceDiscount,Lines=new List<SaleLine>{new SaleLine{VariantId=_variant,ProductUnitId=_unit,Quantity=quantity,UnitPrice=_price,Discount=lineDiscount}}},Db.UserId,Ct);
                var result=await Sales.PostAsync(draft.Id,"profit-sale-"+(++_key),Db.UserId,Ct);Assert.True(result.Succeeded,result.ErrorMessage);return result.Invoice;
            }
            internal Task<NetProfitResult> Report(string from,string to,string breakdown="daily",int page=1,int pageSize=20)=>Service.QueryAsync(new NetProfitQuery{From=from,To=to,Breakdown=breakdown,Page=page,PageSize=pageSize},Ct);
            internal async Task<ExpenseVoucher> Expense(string amount)
            {
                var expenses=new ExpenseService(new SqliteExpenseStore(Cs));
                if(_account==0){_account=(await Db.Create()).Id;await Db.Post(_account,"10000");_category=(await expenses.AddCategoryAsync(new ExpenseCategoryCommand{Name="تصنيف يحدده المشغل"},Db.UserId,Ct)).Id;}
                return await expenses.PostAsync(new ExpenseCommand{CategoryId=_category,TreasuryAccountId=_account,Amount=amount,ExpenseDate="2020-01-01",Payee="جهة",Notes="اختبار",OperationKey="profit-expense-"+(++_key)},Db.UserId,Ct);
            }
            internal Task Classify(long id,string value)=>Service.ClassifyAsync(id,new ExpenseClassificationCommand{Classification=value,Reason="مراجعة المدير",ExpectedRevision=0,OperationKey="profit-classify-"+id},Db.UserId,Ct);
            // Test-only re-dating of immutable events: restore every original trigger immediately.
            internal void Time(string table,string column,string key,long id,string utc)
            {
                using(var c=Db.Open())using(var tx=c.BeginTransaction())
                {
                    var triggers=c.Query<Trigger>("SELECT name,sql FROM sqlite_master WHERE type='trigger' AND tbl_name=@Table AND sql LIKE '%UPDATE%'",new{Table=table},tx).ToArray();
                    foreach(var t in triggers)c.Execute("DROP TRIGGER \""+t.Name+"\"",transaction:tx);
                    c.Execute("UPDATE "+table+" SET "+column+"=@Utc WHERE "+key+"=@Id",new{Utc=utc,Id=id},tx);
                    foreach(var t in triggers)c.Execute(t.Sql,transaction:tx);tx.Commit();
                }
            }
            private sealed class Trigger{public string Name{get;set;}public string Sql{get;set;}}
            public void Dispose()=>Db.Dispose();
        }
    }
}
