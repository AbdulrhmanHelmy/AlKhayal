using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Reports;
using AlKhayal.Data.Reports;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class OperationalReportTests
    {
        private static readonly CancellationToken Ct=CancellationToken.None;
        internal static OperationalReportService Service(string cs)=>new OperationalReportService(new SqliteOperationalReportStore(cs),TimeZoneInfo.Utc);
        internal static OperationalReportQuery Query(string status=null,int page=1,int size=20)=>new OperationalReportQuery{From="2026-03-01",To="2026-03-31",Status=status,Page=page,PageSize=size};
        [Theory]
        [InlineData("customers")][InlineData("suppliers")][InlineData("shifts")][InlineData("attendance")][InlineData("maintenance")][InlineData("installments")]
        public async Task Empty_reports_validate_filters_and_never_write(string report)
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var before=File.ReadAllBytes(f.DatabasePath);var d=await Service(f.ConnectionString).QueryAsync(report,Query(),Ct);
                Assert.Equal(0,d.Count);Assert.Empty(d.Items);Assert.Equal(before,File.ReadAllBytes(f.DatabasePath));
                foreach(var q in new[]{Query("invented"),new OperationalReportQuery{Page=0},new OperationalReportQuery{PersonId=0},new OperationalReportQuery{PageSize=101},new OperationalReportQuery{From="bad",To="bad"},new OperationalReportQuery{Search=new string('a',121)}})
                    await Assert.ThrowsAsync<ArgumentException>(()=>Service(f.ConnectionString).QueryAsync(report,q,Ct));
            }
        }
        internal static void Parties(TreasuryPersistenceTests.Fixture f)
        {
            using(var c=f.Open())c.Execute(@"INSERT INTO Customers(Id,Code,Name,Phone,CreatedAt,UpdatedAt) VALUES(100,'C100','عميل 100','001','2026-01-01','2026-01-01');
                INSERT INTO Customers(Id,Code,Name,Phone,CreatedAt,UpdatedAt) VALUES(101,'C101','عميل 101','002','2026-01-01','2026-01-01');
                INSERT INTO Suppliers(Id,Code,Name,Phone,CreatedAt,UpdatedAt) VALUES(100,'S100','مورد 100','001','2026-01-01','2026-01-01');
                INSERT INTO Suppliers(Id,Code,Name,Phone,CreatedAt,UpdatedAt) VALUES(101,'S101','مورد 101','002','2026-01-01','2026-01-01');
                INSERT INTO CustomerTransactions(CustomerId,TransactionType,AmountScaled,OccurredOn,CreatedUtc) VALUES(100,'opening',1000000,'2026-01-01','2026-01-01T00:00:00.0000000Z'),(100,'increase',200000,'2026-03-01','2026-03-01T00:00:00.0000000Z'),(100,'collection',100000,'2026-02-01','2026-03-31T23:59:59.0000000Z'),(100,'collection',50000,'2026-04-01','2026-04-01T00:00:00.0000000Z');
                INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,CreatedUtc) VALUES(100,'opening',1000000,'2026-01-01','OPEN','2026-01-01T00:00:00.0000000Z'),(100,'payment',-200000,'2026-01-01','PAY','2026-03-01T00:00:00.0000000Z'),(100,'purchase_return_credit',-100000,'2026-03-10','RET','2026-03-10T00:00:00.0000000Z'),(100,'payment',-50000,'2026-04-01','PAY2','2026-04-01T00:00:00.0000000Z');");
        }
        [Theory][InlineData("customers","105.0000","10.0000")][InlineData("suppliers","65.0000","20.0000")]
        public async Task Ledger_snapshots_period_events_search_and_paging_reconcile(string report,string balance,string payment)
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                Parties(f);var before=File.ReadAllBytes(f.DatabasePath);var service=Service(f.ConnectionString);
                var d=await service.QueryAsync(report,Query(),Ct);Assert.Equal(2,d.Count);Assert.Equal(balance,d.Summary["balanceScaled"]);Assert.Equal(payment,d.Summary["periodPaidScaled"]);Assert.Equal("1",d.Summary["debtCount"]);
                d=await service.QueryAsync(report,Query("debt",2,1),Ct);Assert.Equal(1,d.Count);Assert.Empty(d.Items);Assert.Equal(balance,d.Summary["balanceScaled"]);
                var q=Query();q.Search="001";d=await service.QueryAsync(report,q,Ct);Assert.Single(d.Items);Assert.Equal(100L,d.Items[0]["id"]);
                q=Query();q.PersonId=101;Assert.Equal("0.0000",(await service.QueryAsync(report,q,Ct)).Summary["balanceScaled"]);
                q=Query();q.From="2026-04-01";q.To="2026-04-30";d=await service.QueryAsync(report,q,Ct);Assert.Equal(balance,d.Summary["balanceScaled"]);Assert.Equal("5.0000",d.Summary["periodPaidScaled"]);
                Assert.Equal(before,File.ReadAllBytes(f.DatabasePath));
            }
        }
        [Fact]
        public async Task Customer_credit_invoices_are_events_not_collections_or_reconstructed_debt()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("2","2");await f.Sale("1");var q=new OperationalReportQuery();
                var d=await Service(f.Cs).QueryAsync("customers",q,Ct);Assert.Equal("10.0000",d.Summary["creditScaled"]);Assert.Equal("10.0000",d.Summary["balanceScaled"]);Assert.Equal("0.0000",d.Summary["periodPaidScaled"]);
            }
        }
        [Fact]
        public async Task Supplier_credit_purchase_total_uses_posted_evidence_not_settlements()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                Parties(f);using(var c=f.Open())c.Execute(@"INSERT INTO PurchaseInvoices(SupplierId,WarehouseId,InvoiceNumber,InvoiceDate,PaymentMode,Status,TotalScaled,CreatedByUserId,UpdatedByUserId,CreatedUtc,UpdatedUtc,PostedUtc) VALUES(100,1,'P100','2020-01-01','credit','posted',123456,@UserId,@UserId,'2026-03-01','2026-03-01','2026-03-01T00:00:00.0000000Z'),(100,1,'D100','2020-01-01','credit','draft',900000,@UserId,@UserId,'2026-03-01','2026-03-01',NULL)",new{f.UserId});
                var d=await Service(f.ConnectionString).QueryAsync("suppliers",Query(),Ct);Assert.Equal("12.3456",d.Summary["creditScaled"]);Assert.Equal("65.0000",d.Summary["balanceScaled"]);
            }
        }
        [Fact]
        public async Task Shifts_reuse_cash_events_frozen_expected_and_separate_shortage_overage()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                using(var c=f.Open()) {
                    c.Execute(@"INSERT INTO CashierShifts(Id,CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(100,@UserId,'s1','2026-03-01T00:00:00.0000000Z',@UserId,1000000)",new{f.UserId});
                    c.Execute(@"INSERT INTO CashierShiftEvents(ShiftId,Kind,AmountScaled,ActorUserId,CreatedUtc) VALUES(100,'cash_sale',200000,@UserId,'2026-03-01T01:00:00.0000000Z'),(100,'credit_sale',300000,@UserId,'2026-03-01T01:00:00.0000000Z'),(100,'collection',100000,@UserId,'2026-03-01T01:00:00.0000000Z'),(100,'out',50000,@UserId,'2026-03-01T01:00:00.0000000Z');
                        UPDATE CashierShifts SET ClosedUtc='2026-03-02',ClosedBy=@UserId,CountedScaled=1200000,ClosingExpectedScaled=1250000 WHERE Id=100;
                        INSERT INTO CashierShifts(Id,CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled,ClosedUtc,ClosedBy,CountedScaled,ClosingExpectedScaled) VALUES(101,@UserId,'s2','2026-03-02',@UserId,100000,'2026-03-03',@UserId,130000,100000);
                        INSERT INTO CashierShifts(Id,CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(102,@UserId,'s3','2026-03-03',@UserId,50000)",new{f.UserId});
                }
                var before=File.ReadAllBytes(f.DatabasePath);var d=await Service(f.ConnectionString).QueryAsync("shifts",Query(),Ct);
                Assert.Equal(3,d.Count);Assert.Equal("5.0000",d.Summary["shortageScaled"]);Assert.Equal("3.0000",d.Summary["overageScaled"]);Assert.Equal("135.0000",d.Summary["expectedScaled"]);Assert.Equal("20.0000",d.Summary["cashSalesScaled"]);Assert.Equal("30.0000",d.Summary["creditSalesScaled"]);Assert.Equal("2",d.Summary["legacySalesCount"]);
                var row=d.Items.Single(x=>Convert.ToInt64(x["id"])==100);var original=await new AlKhayal.Data.Workforce.SqliteWorkforceStore(f.ConnectionString).ShiftAsync(100,null,Ct);Assert.Equal((original.ExpectedScaled/10000m).ToString("0.0000",System.Globalization.CultureInfo.InvariantCulture),row["expectedScaled"]);
                Assert.Equal(1,(await Service(f.ConnectionString).QueryAsync("shifts",Query("open"),Ct)).Count);Assert.Equal(before,File.ReadAllBytes(f.DatabasePath));
            }
        }
        [Fact]
        public async Task Attendance_has_only_saved_rows_completed_duration_and_no_salary_expense()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                using(var c=f.Open())c.Execute(@"INSERT INTO Employees(Id,Name,IsActive,SalaryScaled,CreatedUtc,UpdatedUtc) VALUES(100,'موظف',1,99999900,'2026-01-01','2026-01-01'),(101,'غير نشط',0,NULL,'2026-01-01','2026-01-01');
                    INSERT INTO EmployeeAttendance(EmployeeId,CheckInUtc,CheckOutUtc) VALUES(100,'2026-03-01T23:00:00.0000000Z','2026-03-02T07:00:00.0000000Z'),(100,'2026-03-03T00:00:00.0000000Z',NULL),(101,'2026-02-28T23:59:59.0000000Z','2026-03-01T08:00:00.0000000Z')");
                var before=File.ReadAllBytes(f.DatabasePath);var d=await Service(f.ConnectionString).QueryAsync("attendance",Query(),Ct);Assert.Equal(2,d.Count);Assert.Equal("28800",d.Summary["workedSeconds"]);Assert.Equal("1",d.Summary["activeEmployees"]);Assert.Equal("1",d.Summary["openCount"]);
                Assert.Null(d.Items.Single(x=>x["status"].ToString()=="open")["workedSeconds"]);Assert.Equal(1,(await Service(f.ConnectionString).QueryAsync("attendance",Query("closed"),Ct)).Count);Assert.Equal(before,File.ReadAllBytes(f.DatabasePath));
            }
        }
        [Fact]
        public async Task Maintenance_intake_advance_delivery_dates_and_current_pending_are_independent()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                Parties(f);using(var c=f.Open())c.Execute(@"INSERT INTO MaintenanceTickets(Id,CustomerId,TicketNumber,OperationKey,RequestHash,Phone,Device,Model,Issue,EstimatedCostScaled,AdvancePaidScaled,ReceivedOn,ExpectedOn,Status,CreatedUtc) VALUES(100,100,'M100','m100','h','001','جهاز','موديل','عطل',1000000,200000,'2026-01-01','2026-03-01','جاهز للتسليم','2026-02-01T00:00:00.0000000Z'),(101,100,'M101','m101','h','001','جهاز','موديل','عطل',500000,0,'2026-03-01','2026-04-01','تم الاستلام','2026-03-01T00:00:00.0000000Z');
                    INSERT INTO MaintenanceTicketReceipts(TicketId,ReceiptType,AmountScaled,OperationKey,ReceivedOn,CreatedUtc) VALUES(100,'advance',200000,'a100','2026-01-01','2026-02-01T00:00:00.0000000Z');
                    UPDATE MaintenanceTickets SET Status='تم التسليم',FinalCostScaled=1000000,SettlementType='installment',DeliveredOn='2026-01-01',DeliveryOperationKey='d100' WHERE Id=100;
                    INSERT INTO MaintenanceTicketStatusHistory(TicketId,Status,OperationKey,ChangedUtc) VALUES(100,'تم التسليم','h100','2026-03-10T00:00:00.0000000Z')");
                var before=File.ReadAllBytes(f.DatabasePath);var d=await Service(f.ConnectionString).QueryAsync("maintenance",Query(),Ct);Assert.Equal("1",d.Summary["intakeCount"]);Assert.Equal("1",d.Summary["deliveredCount"]);Assert.Equal("1",d.Summary["openCount"]);Assert.Equal("0.0000",d.Summary["periodPaidScaled"]);Assert.Equal("100.0000",d.Summary["periodChargeScaled"]);Assert.Equal("80.0000",d.Summary["balanceScaled"]);Assert.Equal(2,d.Count);Assert.Equal(before,File.ReadAllBytes(f.DatabasePath));
            }
        }
        [Fact]
        public async Task Installment_schedule_full_partial_future_due_overdue_and_collection_dates()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                using(var c=f.Db.Open())c.Execute(@"INSERT INTO CustomerInstallmentContracts(Id,CustomerId,ProductId,OperationKey,RequestHash,ContractNumber,ProductName,ProductPriceScaled,DownPaymentScaled,InterestRateBasisPoints,TotalAmountScaled,InstallmentCount,IntervalMonths,FirstDueOn,CreatedUtc) SELECT 100,(SELECT Id FROM Customers LIMIT 1),(SELECT Id FROM Products LIMIT 1),'ins100','h','I100','منتج',1000000,0,0,1000000,3,1,'2020-01-01','2026-03-01';
                    INSERT INTO CustomerInstallmentSchedule(ContractId,InstallmentNumber,DueOn,AmountScaled) VALUES(100,1,'2020-01-01',300000),(100,2,'2026-03-01',300000),(100,3,'2099-01-01',400000);
                    INSERT INTO CustomerInstallmentPayments(ContractId,InstallmentNumber,OperationKey,RequestHash,AmountScaled,PaidOn,CreatedUtc) VALUES(100,1,'pay1','h',300000,'2020-01-01','2026-03-01T00:00:00.0000000Z'),(100,2,'pay2','h',100000,'2026-01-01','2026-04-01T00:00:00.0000000Z')");
                var before=File.ReadAllBytes(f.Db.DatabasePath);var d=await Service(f.Cs).QueryAsync("installments",Query(),Ct);Assert.Equal("60.0000",d.Summary["balanceScaled"]);Assert.Equal("20.0000",d.Summary["overdueScaled"]);Assert.Equal("30.0000",d.Summary["dueScaled"]);Assert.Equal("30.0000",d.Summary["periodPaidScaled"]);Assert.Equal("1",d.Summary["coverageCount"]);Assert.Equal("2026-03-01",d.Items[0]["nextDue"]);Assert.Equal(0,(await Service(f.Cs).QueryAsync("installments",Query("completed"),Ct)).Count);Assert.Equal(before,File.ReadAllBytes(f.Db.DatabasePath));
            }
        }
        [Fact]
        public async Task Overview_incomplete_profit_stays_unknown_and_reconciles_classification()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                var expense=await f.Expense("3");var overview=new BusinessOverviewService(new SalesReportService(new SqliteSalesReportStore(f.Cs)),new TransactionReportService(new SqliteTransactionReportStore(f.Cs)),f.Service,Service(f.Cs));
                var grants=new System.Collections.Generic.HashSet<string>(new[]{"net-profit"});var d=await overview.QueryAsync(new ReportQuery(),grants,Ct);Assert.Single(d);Assert.False(d[0].Complete);Assert.Null(d[0].Value);
                await f.Classify(expense.Id,"included");d=await overview.QueryAsync(new ReportQuery(),grants,Ct);Assert.True(d[0].Complete);Assert.Equal("-3.0000",d[0].Value);
            }
        }
        [Fact]
        public async Task Overview_reuses_exact_sources_and_omits_ungranted_values()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("2","2");await f.Sale("1");var sales=new SalesReportService(new SqliteSalesReportStore(f.Cs));var transactions=new TransactionReportService(new SqliteTransactionReportStore(f.Cs));var operations=Service(f.Cs);var overview=new BusinessOverviewService(sales,transactions,f.Service,operations);
                var q=new ReportQuery();var before=File.ReadAllBytes(f.Db.DatabasePath);
                Assert.Empty(await overview.QueryAsync(q,new System.Collections.Generic.HashSet<string>(),Ct));
                var grants=new System.Collections.Generic.HashSet<string>(new[]{"sales","returns","net-profit","payment-methods","customers","suppliers","maintenance","installments"});var d=await overview.QueryAsync(q,grants,Ct);
                Assert.Equal((await sales.QueryAsync(new SalesReportQuery(),Ct)).Total,d.Single(x=>x.Key=="Sales").Value);Assert.Equal((await f.Service.QueryAsync(new NetProfitQuery(),Ct)).Summary.NetProfit,d.Single(x=>x.Key=="NetProfit").Value);Assert.Equal((await operations.QueryAsync("customers",new OperationalReportQuery(),Ct)).Summary["balanceScaled"],d.Single(x=>x.Key=="customers").Value);Assert.Equal(before,File.ReadAllBytes(f.Db.DatabasePath));
            }
        }
    }
}
