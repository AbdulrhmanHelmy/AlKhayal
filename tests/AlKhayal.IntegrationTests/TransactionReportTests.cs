using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Reports;
using AlKhayal.Application.Payments;
using AlKhayal.Data.Reports;
using AlKhayal.Data.Payments;
using AlKhayal.Data.Database;
using Dapper;
using Xunit;
using Xunit.Abstractions;

namespace AlKhayal.IntegrationTests
{
    public sealed class TransactionReportTests
    {
        private static readonly CancellationToken Ct=CancellationToken.None;
        private readonly ITestOutputHelper _output;
        public TransactionReportTests(ITestOutputHelper output) { _output=output; }
        private static TransactionReportService Service(TreasuryPersistenceTests.Fixture f) => new TransactionReportService(new SqliteTransactionReportStore(f.ConnectionString),TimeZoneInfo.Utc);
        private static TransactionReportQuery Query(string day="2026-03-01") => new TransactionReportQuery{From=day,To=day};
        private static SalesReportService Sales(TreasuryPersistenceTests.Fixture f) => new SalesReportService(new SqliteSalesReportStore(f.ConnectionString),TimeZoneInfo.Utc);
        // Explicit read fixtures seed persisted header facts. Domain lifecycle coverage below also
        // posts purchases and repeated partial returns through the actual application services.
        internal static long Sale(TreasuryPersistenceTests.Fixture f,string reference,string time,string method="cash",long? instrument=null,long total=123400,string status="posted",string paymentTime=null,bool legacy=false)
        {
            using(var c=f.Open()) {
                var id=c.ExecuteScalar<long>(@"INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,CreatedUtc)
                    VALUES(@reference,'2026-01-01',1,@UserId,'draft',@mode,@total,0,0,@total,@time); SELECT last_insert_rowid();",new{reference,f.UserId,mode=method=="credit"?"credit":"cash",total,time});
                if(!legacy)c.Execute("INSERT INTO SalePaymentSelections(SalesInvoiceId,Method,InstrumentId) VALUES(@id,@method,@instrument)",new{id,method,instrument});
                if(status=="posted") {
                    c.Execute("UPDATE SalesInvoices SET Status='posted',PostedUtc=@time WHERE Id=@id",new{id,time});
                    if(method!="credit") c.Execute(@"INSERT INTO SalesPayments(SalesInvoiceId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,CreatedUtc)
                        VALUES(@id,@UserId,'cash_sale',@pm,@total,@key,'fixture','2026-01-01',@paymentTime)",new{id,f.UserId,pm=method=="wallet"||method=="unclassified"?"transfer":method,total,key=Guid.NewGuid().ToString(),paymentTime=paymentTime??time});
                }
                return id;
            }
        }
        private static long Document(TreasuryPersistenceTests.Fixture f,string time)
        { using(var c=f.Open())return c.ExecuteScalar<long>("INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,CreatedByUserId,CreatedUtc) VALUES('manual_receipt','posted',1,'fixture',@UserId,@time); SELECT last_insert_rowid();",new{f.UserId,time}); }
        private static long Supplier(TreasuryPersistenceTests.Fixture f,string name)
        { return new AlKhayal.Data.Suppliers.SqliteSupplierRepository(f.ConnectionString).Add(new AlKhayal.Core.Models.Supplier{Code=Guid.NewGuid().ToString(),Name=name,Phone="",Status="نشط"}); }
        private static long Purchase(TreasuryPersistenceTests.Fixture f,long supplier,string reference,string time,long total=100000,string status="posted")
        { using(var c=f.Open())return c.ExecuteScalar<long>(@"INSERT INTO PurchaseInvoices(SupplierId,WarehouseId,InvoiceNumber,InvoiceDate,PaymentMode,Status,TotalScaled,CreatedByUserId,UpdatedByUserId,CreatedUtc,UpdatedUtc,PostedUtc)
            VALUES(@supplier,1,@reference,'2026-01-01','credit',@status,@total,@UserId,@UserId,@time,@time,@time); SELECT last_insert_rowid();",new{supplier,reference,time,total,status,f.UserId}); }
        private static void SalesReturn(TreasuryPersistenceTests.Fixture f,long invoice,string reference,string time,long total)
        { var doc=Document(f,time);using(var c=f.Open())c.Execute(@"INSERT INTO SalesReturns(ReturnNumber,SalesInvoiceId,InventoryDocumentId,OperationKey,RequestHash,TotalScaled,CashRefundScaled,CreditReductionScaled,Reason,CreatedByUserId,CreatedUtc)
            VALUES(@reference,@invoice,@doc,@reference,'fixture',@total,@total,0,'fixture',@UserId,@time)",new{reference,invoice,doc,total,f.UserId,time}); }
        private static void PurchaseReturn(TreasuryPersistenceTests.Fixture f,long invoice,long supplier,string time,long total,string status="posted")
        { var doc=Document(f,time);using(var c=f.Open())c.Execute(@"INSERT INTO PurchaseReturns(PurchaseInvoiceId,SupplierId,WarehouseId,ReturnMode,Status,TotalScaled,InventoryDocumentId,CreatedByUserId,CreatedUtc,PostedUtc)
            VALUES(@invoice,@supplier,1,'supplier_credit',@status,@total,@doc,@UserId,@time,@time)",new{invoice,supplier,status,total,doc,f.UserId,time}); }

        [Fact]
        public async Task Purchases_use_saved_totals_posting_time_supplier_filters_and_return_event_time()
        {
            using(var f=new TreasuryPersistenceTests.Fixture()) {
                var supplier=Supplier(f,"الأول");var second=Supplier(f,"الثاني");var time="2026-03-01T00:00:00.0000000Z";
                var old=Purchase(f,supplier,"OLD","2026-02-28T23:59:59.0000000Z");
                var one=Purchase(f,supplier,"P-1",time,123456);var two=Purchase(f,second,"P-2",time,50000);
                Purchase(f,supplier,"DRAFT",time,999999,"draft");Purchase(f,supplier,"NEXT","2026-03-02T00:00:00.0000000Z");
                PurchaseReturn(f,old,supplier,time,10000);PurchaseReturn(f,one,supplier,"2026-03-02T00:00:00.0000000Z",5000);PurchaseReturn(f,one,supplier,time,9999,"draft");
                var q=Query();q.PageSize=1;var result=await Service(f).PurchasesAsync(q,Ct);
                Assert.Equal(2,result.Count);Assert.Equal("17.3456",result.Total);Assert.Equal("1.0000",result.Returns);Assert.Equal("16.3456",result.NetEvents);Assert.Equal(two,Assert.Single(result.Items).InvoiceId);
                q.Page=2;Assert.Equal(one,Assert.Single((await Service(f).PurchasesAsync(q,Ct)).Items).Id);
                q.Page=1;q.SupplierId=supplier;result=await Service(f).PurchasesAsync(q,Ct);Assert.Equal("12.3456",result.Total);Assert.Equal("1.0000",result.Returns);
                q.Search="P-1";result=await Service(f).PurchasesAsync(q,Ct);Assert.Equal("0.0000",result.Returns);
                q.Party="الثاني";Assert.Empty((await Service(f).PurchasesAsync(q,Ct)).Items);
                Migrator.Run(f.ConnectionString);Assert.Equal("17.3456",(await Service(f).PurchasesAsync(Query(),Ct)).Total);
            }
        }

        [Fact]
        public async Task Returns_keep_multiple_partial_headers_distinct_and_original_links_without_reconstructing_tax()
        {
            using(var f=new TreasuryPersistenceTests.Fixture()) {
                var time="2026-03-01T00:00:00.0000000Z";var sale=Sale(f,"SALE","2026-02-01T00:00:00.0000000Z");
                SalesReturn(f,sale,"R-1",time,33333);SalesReturn(f,sale,"R-2",time,66667);SalesReturn(f,sale,"R-NEXT","2026-03-02T00:00:00.0000000Z",1);
                var supplier=Supplier(f,"مورد");var purchase=Purchase(f,supplier,"PURCHASE","2026-02-01T00:00:00.0000000Z");PurchaseReturn(f,purchase,supplier,time,43210);PurchaseReturn(f,purchase,supplier,time,80000,"draft");
                var q=Query();q.PageSize=1;var r=await Service(f).ReturnsAsync(q,Ct);
                Assert.Equal(3,r.Count);Assert.Equal(2,r.SalesReturnCount);Assert.Equal(1,r.PurchaseReturnCount);Assert.Equal("10.0000",r.SalesReturns);Assert.Equal("4.3210",r.PurchaseReturns);
                q.Type="sales";r=await Service(f).ReturnsAsync(q,Ct);Assert.Equal(2,r.Count);Assert.Equal(sale,Assert.Single(r.Items).InvoiceId);Assert.Equal("SALE",r.Items[0].InvoiceNumber);
                q.Page=2;Assert.Equal("R-1",Assert.Single((await Service(f).ReturnsAsync(q,Ct)).Items).Reference);
                q.Page=1;q.Search="R-2";Assert.Equal("6.6667",(await Service(f).ReturnsAsync(q,Ct)).SalesReturns);
                q.Search=null;q.Type="purchase";q.Party="مورد";Assert.Equal(purchase,Assert.Single((await Service(f).ReturnsAsync(q,Ct)).Items).InvoiceId);
            }
        }

        [Fact]
        public async Task Sales_posting_and_later_returns_and_cancellation_do_not_rewrite_earlier_period()
        {
            using(var f=new TreasuryPersistenceTests.Fixture()) {
                var old="2026-02-28T23:59:59.0000000Z";var next="2026-03-01T00:00:00.0000000Z";
                var sale=Sale(f,"RETURNED",old);var canceled=Sale(f,"CANCELED",old,"credit");
                SalesReturn(f,sale,"LATER",next,10000);var doc=Document(f,next);
                using(var c=f.Open())c.Execute("INSERT INTO SalesCancellations(SalesInvoiceId,InventoryDocumentId,OperationKey,RequestHash,Reason,ActorUserId,CreatedUtc) VALUES(@canceled,@doc,'cancel-fixture','fixture','fixture',@UserId,@next)",new{canceled,doc,f.UserId,next});
                var earlier=await Sales(f).QueryAsync(new SalesReportQuery{From="2026-02-28",To="2026-02-28"},Ct);
                Assert.Equal("24.6800",earlier.Total);Assert.Equal("24.6800",earlier.NetEvents);Assert.Equal("0.0000",earlier.Cancellations);
                Assert.Equal("canceled",earlier.Items.Single(x=>x.Id==canceled).Status);
                var later=await Sales(f).QueryAsync(new SalesReportQuery{From="2026-03-01",To="2026-03-01"},Ct);
                Assert.Equal(0,later.Count);Assert.Equal("1.0000",later.Returns);Assert.Equal("12.3400",later.Cancellations);Assert.Equal("-13.3400",later.NetEvents);
            }
        }

        [Fact]
        public async Task Payment_methods_use_actual_immediate_events_and_keep_credit_collections_unassigned_and_inactive_identity_honest()
        {
            using(var f=new TreasuryPersistenceTests.Fixture()) {
                var time="2026-03-01T00:00:00.0000000Z";var store=new PaymentInstrumentService(new SqlitePaymentInstrumentStore(f.ConnectionString));
                var wallet=await store.SaveAsync(new PaymentInstrument{Type="wallet",Name="قبل التعديل",Number="00123"},f.UserId,Ct);
                var card=await store.SaveAsync(new PaymentInstrument{Type="card",Name="فيزا",Number="0007"},f.UserId,Ct);
                await store.SaveAsync(new PaymentInstrument{Type="wallet",Name="بلا عمليات",Number="005"},f.UserId,Ct);
                Sale(f,"CASH",time,total:10000);Sale(f,"WALLET",time,"wallet",wallet.Id,20000);Sale(f,"CARD",time,"card",card.Id,30000);
                Sale(f,"UNKNOWN",time,"unclassified",total:40000,legacy:true);var credit=Sale(f,"CREDIT",time,"credit",total:50000);
                Sale(f,"DRAFT",time,status:"draft");Sale(f,"PAY-NEXT",time,total:60000,paymentTime:"2026-03-02T00:00:00.0000000Z");
                using(var c=f.Open())c.Execute("INSERT INTO SalesPayments(SalesInvoiceId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,CreatedUtc) VALUES(@credit,@UserId,'customer_collection','cash',50000,'collection-fixture','fixture','2026-03-01',@time)",new{credit,f.UserId,time});
                wallet.Name="الاسم الجديد";wallet.IsActive=false;await store.SaveAsync(wallet,f.UserId,Ct);
                var r=await Service(f).PaymentsAsync(Query(),Ct);Assert.Equal("1.0000",r.Cash);Assert.Equal("2.0000",r.Wallet);Assert.Equal("3.0000",r.Card);Assert.Equal("4.0000",r.Unclassified);Assert.Equal("5.0000",r.Credit);
                Assert.Equal(3,r.Count);var row=r.Instruments.Single(x=>x.Id==wallet.Id);Assert.False(row.IsActive);Assert.Equal("00123",row.Number);Assert.Equal("الاسم الجديد",row.Name);Assert.Equal(1,row.InvoiceCount);Assert.Equal("2.0000",row.Total);
                var q=Query();q.Type="wallet";q.Search="00123";q.PageSize=1;r=await Service(f).PaymentsAsync(q,Ct);Assert.Equal(1,r.Count);Assert.Equal("2.0000",r.Wallet);Assert.Equal("0.0000",r.Cash);Assert.Equal("0.0000",r.Credit);
                q.Page=2;Assert.Empty((await Service(f).PaymentsAsync(q,Ct)).Instruments);
                var sr=await Sales(f).QueryAsync(new SalesReportQuery{From="2026-03-01",To="2026-03-01"},Ct);Assert.Equal("7.0000",sr.Cash);Assert.Equal("2.0000",sr.Wallet);Assert.Equal("3.0000",sr.Card);Assert.Equal("5.0000",sr.Credit);Assert.Equal("4.0000",sr.Unclassified);
                Migrator.Run(f.ConnectionString);Assert.Equal("2.0000",(await Service(f).PaymentsAsync(Query(),Ct)).Wallet);
            }
        }

        [Fact]
        public async Task All_reports_are_empty_on_new_database_and_do_not_write_any_database_pages()
        {
            using(var f=new TreasuryPersistenceTests.Fixture()) {
                var before=System.IO.File.ReadAllBytes(f.DatabasePath);
                Assert.Equal(0,(await Service(f).PurchasesAsync(Query(),Ct)).Count);Assert.Equal(0,(await Service(f).ReturnsAsync(Query(),Ct)).Count);Assert.Equal(0,(await Service(f).PaymentsAsync(Query(),Ct)).Count);
                Assert.Equal(0,(await Sales(f).QueryAsync(new SalesReportQuery{From="2026-03-01",To="2026-03-01"},Ct)).Count);
                Assert.Equal(before,System.IO.File.ReadAllBytes(f.DatabasePath));
                await Assert.ThrowsAsync<ArgumentException>(()=>Service(f).ReturnsAsync(new TransactionReportQuery{From="2026-03-01",To="2026-03-01",Type="bogus"},Ct));
                await Assert.ThrowsAsync<ArgumentException>(()=>Service(f).PaymentsAsync(new TransactionReportQuery{From="2026-03-01",To="2026-03-01",Type="cash"},Ct));
                await Assert.ThrowsAsync<ArgumentException>(()=>Service(f).PurchasesAsync(new TransactionReportQuery{From="2026-03-01",To="2026-03-01",SupplierId=0},Ct));
            }
        }

        [Theory]
        [InlineData("cash","cash")]
        [InlineData("card","card")]
        [InlineData("unclassified","unclassified")]
        public async Task Legacy_immediate_methods_need_explicit_payment_evidence(string method,string expected)
        {
            using(var f=new TreasuryPersistenceTests.Fixture()) {
                Sale(f,"LEGACY","2026-03-01T12:00:00.0000000Z",method,legacy:true);
                var r=await Sales(f).QueryAsync(new SalesReportQuery{From="2026-03-01",To="2026-03-01",Method=expected,CashierId=f.UserId},Ct);
                Assert.Equal(1,r.Count);Assert.Equal(expected,Assert.Single(r.Items).Method);
                Assert.Equal(0,(await Sales(f).QueryAsync(new SalesReportQuery{From="2026-03-01",To="2026-03-01",CashierId=f.UserId+100},Ct)).Count);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Real_sales_tax_discount_price_edits_and_repeated_partial_returns_reconcile_to_saved_facts(bool included)
        {
            using(var f=new SalesTreasuryTests.Fixture()) {
                await f.Prepare();using(var c=f.Db.Open())c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=15,PricesIncludeTax=@included",new{included});
                var draft=await f.Draft(discount:"1",quantity:"3");var post=await f.Post(draft.Id,"report-tax");Assert.True(post.Succeeded,post.ErrorMessage);
                await f.FundRefund("100");var line=post.Invoice.Lines.Single();
                foreach(var key in new[]{"partial-one","partial-two"}) {
                    var ret=await f.Sales.ReturnAsync(draft.Id,new AlKhayal.Application.Sales.SaleReturnCommand{OperationKey=key,Reason="مرتجع جزئي",Lines=new System.Collections.Generic.List<AlKhayal.Application.Sales.SaleReturnLineCommand>{new AlKhayal.Application.Sales.SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},f.Db.UserId,Ct);Assert.True(ret.Succeeded,ret.ErrorMessage);
                }
                using(var c=f.Db.Open()) {c.Execute("UPDATE ProductDetails SET SellPriceScaled=9990000; UPDATE ApplicationSettings SET DefaultTaxRate=5");}
                var before=System.IO.File.ReadAllBytes(f.Db.DatabasePath);var day=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var sales=await Sales(f.Db).QueryAsync(new SalesReportQuery{From=day,To=day},Ct);var returns=await Service(f.Db).ReturnsAsync(Query(day),Ct);var payments=await Service(f.Db).PaymentsAsync(Query(day),Ct);
                Assert.Equal((post.Invoice.TotalScaled/10000m).ToString("0.0000",CultureInfo.InvariantCulture),sales.Total);Assert.Equal(sales.Total,payments.Cash);Assert.Equal(2,returns.SalesReturnCount);
                using(var c=f.Db.Open())Assert.Equal((c.ExecuteScalar<long>("SELECT SUM(TotalScaled) FROM SalesReturns")/10000m).ToString("0.0000",CultureInfo.InvariantCulture),sales.Returns);
                Assert.Equal(sales.Returns,returns.SalesReturns);Assert.All(returns.Items,x=>Assert.Equal(draft.Id,x.InvoiceId));
                Assert.Equal(before,System.IO.File.ReadAllBytes(f.Db.DatabasePath));
            }
        }

        [Fact]
        public async Task Real_purchase_post_and_repeated_returns_reconcile_after_reopening_services()
        {
            using(var f=new SalesTreasuryTests.Fixture()) {
                await f.Prepare();var supplier=Supplier(f.Db,"مورد فعلي");var suppliers=new AlKhayal.Data.Suppliers.SqliteSupplierRepository(f.Db.ConnectionString);
                var purchases=new AlKhayal.Application.Purchasing.PurchaseService(new AlKhayal.Data.Purchasing.SqlitePurchaseRepository(f.Db.ConnectionString),suppliers);
                var command=new AlKhayal.Core.Models.PurchaseDraftCommand{SupplierId=supplier,WarehouseId=1,InvoiceNumber="REAL-REPORT",InvoiceDate="2026-01-01",PaymentMode="credit",Lines=new System.Collections.Generic.List<AlKhayal.Core.Models.PurchaseInvoiceLine>{new AlKhayal.Core.Models.PurchaseInvoiceLine{VariantId=f.Variant,ProductUnitId=f.Unit,Quantity="3",PurchaseUnitPrice="9.99",SaleUnitPrice="12.34"}}};
                var draft=await purchases.CreateDraftAsync(command,f.Db.UserId,Ct);var posted=await purchases.PostAsync(draft.Id,"report-purchase",f.Db.UserId,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var line=posted.Invoice.Lines.Single();
                foreach(var key in new[]{"purchase-partial-one","purchase-partial-two"}) {
                    var ret=await purchases.PostReturnAsync(draft.Id,new AlKhayal.Core.Models.PurchaseReturnCommand{WarehouseId=1,ReturnMode="supplier_credit",Lines=new System.Collections.Generic.List<AlKhayal.Core.Models.PurchaseReturnLine>{new AlKhayal.Core.Models.PurchaseReturnLine{PurchaseInvoiceLineId=line.Id,Quantity="1"}}},key,f.Db.UserId,Ct);Assert.True(ret.Succeeded,ret.ErrorMessage);
                }
                var before=System.IO.File.ReadAllBytes(f.Db.DatabasePath);var day=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);var report=await Service(f.Db).PurchasesAsync(Query(day),Ct);var returns=await Service(f.Db).ReturnsAsync(Query(day),Ct);
                Assert.Equal("29.9700",report.Total);Assert.Equal("19.9800",report.Returns);Assert.Equal("9.9900",report.NetEvents);Assert.Equal(2,returns.PurchaseReturnCount);Assert.Equal(report.Returns,returns.PurchaseReturns);Assert.All(returns.Items,x=>Assert.Equal(draft.Id,x.InvoiceId));
                Assert.Equal(before,System.IO.File.ReadAllBytes(f.Db.DatabasePath));Migrator.Run(f.Db.ConnectionString);Assert.Equal(report.Total,(await Service(f.Db).PurchasesAsync(Query(day),Ct)).Total);
            }
        }

        [Fact]
        public async Task Thirty_thousand_sales_and_ten_thousand_purchases_remain_server_paged()
        {
            using(var f=new TreasuryPersistenceTests.Fixture()) {
                var supplier=Supplier(f,"حجم كبير");
                using(var c=f.Open())using(var tx=c.BeginTransaction()) {
                    c.Execute(@"WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<30000)
                        INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,CreatedUtc,PostedUtc)
                        SELECT 'L-'||x,'2026-03-01',1,@UserId,'posted','cash',10000,0,0,10000,'2026-03-01T12:00:00.0000000Z','2026-03-01T12:00:00.0000000Z' FROM n",new{f.UserId},tx);
                    c.Execute(@"WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<10000)
                        INSERT INTO PurchaseInvoices(SupplierId,WarehouseId,InvoiceNumber,InvoiceDate,PaymentMode,Status,TotalScaled,CreatedByUserId,UpdatedByUserId,CreatedUtc,UpdatedUtc,PostedUtc)
                        SELECT @supplier,1,'P-'||x,'2026-03-01','credit','posted',10000,@UserId,@UserId,'2026-03-01T12:00:00.0000000Z','2026-03-01T12:00:00.0000000Z','2026-03-01T12:00:00.0000000Z' FROM n",new{supplier,f.UserId},tx);
                    c.Execute(@"INSERT INTO SalesPayments(SalesInvoiceId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,CreatedUtc)
                        SELECT Id,@UserId,'cash_sale','cash',TotalScaled,'large-payment-'||Id,'fixture','2026-03-01',PostedUtc FROM SalesInvoices",new{f.UserId},tx);
                    c.Execute(@"WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<10000)
                        INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,CreatedByUserId,CreatedUtc)
                        SELECT 'manual_receipt','posted',1,'fixture',@UserId,'2026-03-01T12:00:00.0000000Z' FROM n",new{f.UserId},tx);
                    c.Execute(@"INSERT INTO SalesReturns(ReturnNumber,SalesInvoiceId,InventoryDocumentId,OperationKey,RequestHash,TotalScaled,CashRefundScaled,CreditReductionScaled,Reason,CreatedByUserId,CreatedUtc)
                        SELECT 'L-RET-'||Id,Id,Id,'large-return-'||Id,'fixture',1000,1000,0,'fixture',@UserId,'2026-03-01T12:00:00.0000000Z' FROM InventoryDocuments",new{f.UserId},tx);
                    tx.Commit();
                }
                var watch=Stopwatch.StartNew();var sales=await Sales(f).QueryAsync(new SalesReportQuery{From="2026-03-01",To="2026-03-01",Page=1500,PageSize=20},Ct);var purchases=await Service(f).PurchasesAsync(new TransactionReportQuery{From="2026-03-01",To="2026-03-01",Page=500,PageSize=20},Ct);
                var returns=await Service(f).ReturnsAsync(new TransactionReportQuery{From="2026-03-01",To="2026-03-01",Page=500,PageSize=20},Ct);var payments=await Service(f).PaymentsAsync(Query(),Ct);watch.Stop();
                _output.WriteLine("30k sales/payments + 10k purchases/returns, all four reports: {0} ms",watch.ElapsedMilliseconds);
                Assert.Equal(30000,sales.Count);Assert.Equal("30000.0000",sales.Total);Assert.Equal(20,sales.Items.Count);Assert.Equal(1,sales.Items.Last().Id);
                Assert.Equal(10000,purchases.Count);Assert.Equal("10000.0000",purchases.Total);Assert.Equal(20,purchases.Items.Count);Assert.Equal(1,purchases.Items.Last().Id);
                Assert.Equal(10000,returns.Count);Assert.Equal(20,returns.Items.Count);Assert.Equal("1000.0000",returns.SalesReturns);Assert.Equal("1000.0000",sales.Returns);Assert.Equal("30000.0000",payments.Cash);
                Assert.True(watch.Elapsed<TimeSpan.FromSeconds(30));
            }
        }
    }
}
