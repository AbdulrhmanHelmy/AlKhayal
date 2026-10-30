using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SQLite;
using AlKhayal.Application.Payments;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Workforce;
using AlKhayal.Data.Database;
using AlKhayal.Data.Payments;
using AlKhayal.Data.Workforce;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class PaymentInstrumentTests
    {
        private static readonly CancellationToken Ct=CancellationToken.None;
        private static PaymentInstrumentService Service(TreasuryPersistenceTests.Fixture f)=>new PaymentInstrumentService(new SqlitePaymentInstrumentStore(f.ConnectionString));
        private static Task<PaymentInstrument> Create(TreasuryPersistenceTests.Fixture f,string type="wallet",string number="001234")=>Service(f).SaveAsync(new PaymentInstrument{Type=type,Name="وسيلة اختبار",Number=number,Provider="Provider"},f.UserId,Ct);
        private static SaleDraftCommand Command(SalesTreasuryTests.Fixture f,string method,long? instrument,string quantity="1")=>new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-28",PaymentMode="cash",ImmediateMethod=method,PaymentInstrumentId=instrument,Discount="1.00",Lines=new System.Collections.Generic.List<SaleLine>{new SaleLine{VariantId=f.Variant,ProductUnitId=f.Unit,Quantity=quantity,UnitPrice="12.34"}}};

        [Theory]
        [InlineData("wallet")]
        [InlineData("card")]
        public async Task Instruments_preserve_strings_uniqueness_versions_audit_and_restart(string type)
        {
            using(var f=new TreasuryPersistenceTests.Fixture()){
                var instrument=await Create(f,type);Assert.Equal("001234",instrument.Number);
                await Assert.ThrowsAsync<ArgumentException>(()=>Service(f).SaveAsync(new PaymentInstrument{Type=type,Name="آخر",Number="00 1234",Provider=" provider "},f.UserId,Ct));
                instrument.Name="اسم جديد";instrument.IsActive=false;instrument=await Service(f).SaveAsync(instrument,f.UserId,Ct);
                Assert.Equal(2,instrument.Version);Assert.Empty(await Service(f).ListAsync(type,true,Ct));
                var replacement=await Create(f,type);Assert.NotEqual(instrument.Id,replacement.Id);
                instrument.IsActive=true;await Assert.ThrowsAsync<ArgumentException>(()=>Service(f).SaveAsync(instrument,f.UserId,Ct));
                instrument.IsActive=false;instrument.Version=1;await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(f).SaveAsync(instrument,f.UserId,Ct));
                Migrator.Run(f.ConnectionString);var saved=(await Service(f).ListAsync(type,false,Ct)).Single(x=>x.Id==instrument.Id);Assert.Equal("001234",saved.Number);Assert.Equal("اسم جديد",saved.Name);Assert.False(saved.IsActive);Assert.NotNull(saved.CreatedUtc);Assert.NotNull(saved.UpdatedUtc);
                using(var c=f.Open()){Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM PaymentInstrumentAudit"));Assert.Throws<SQLiteException>(()=>c.Execute("DELETE FROM PaymentInstrumentAudit"));Assert.Empty(c.Query("PRAGMA foreign_key_check"));}
            }
        }

        [Theory]
        [InlineData("wallet",false)]
        [InlineData("card",false)]
        [InlineData("wallet",true)]
        [InlineData("card",true)]
        public async Task Full_final_payment_post_replay_history_rename_deactivation_and_shift_cash_are_consistent(string type,bool included)
        {
            using(var f=new SalesTreasuryTests.Fixture()){
                await f.Prepare();using(var c=f.Db.Open())c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=10,PricesIncludeTax=@included WHERE Id=1",new{included});
                var workforce=new WorkforceService(new SqliteWorkforceStore(f.Db.ConnectionString));
                var shift=await workforce.ExecuteAsync("shift-open",new WorkforceCommand{Amount="100",OperationKey="payment-shift"},f.Db.UserId,true,Ct);
                var instrument=await Create(f.Db,type);var command=Command(f,type,instrument.Id);
                var draft=await f.Sales.CreateDraftAsync(command,f.Db.UserId,Ct);Assert.Equal(instrument.Id,draft.PaymentInstrumentId);
                var posted=await f.Post(draft.Id,"payment-post");Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(included?113400:124700,posted.Invoice.TotalScaled);Assert.Equal(posted.Invoice.TotalScaled,posted.Invoice.PaidScaled);
                Assert.True((await f.Post(draft.Id,"payment-post")).Succeeded);
                var history=await Service(f.Db).HistoryAsync(new PaymentHistoryQuery{InstrumentId=instrument.Id,PageSize=1},Ct);
                Assert.Equal(1,history.Count);Assert.Equal(1,history.InvoiceCount);Assert.Equal(posted.Invoice.TotalScaled,history.TotalScaled);var row=Assert.Single(history.Items);Assert.Equal(draft.InvoiceNumber,row.InvoiceNumber);Assert.Equal(draft.InvoiceDate,row.InvoiceDate);Assert.Equal(type,row.Method);Assert.Equal("paid",row.PaymentStatus);Assert.Equal(row.TotalScaled,row.AmountScaled);Assert.Equal(instrument.Id,row.InstrumentId);
                var overview=await Service(f.Db).OverviewAsync(Ct);Assert.Equal(posted.Invoice.TotalScaled,type=="wallet"?overview.WalletScaled:overview.CardScaled);Assert.Equal(0,overview.CashScaled);
                Assert.Empty((await Service(f.Db).HistoryAsync(new PaymentHistoryQuery{InstrumentId=instrument.Id,Page=2,PageSize=1},Ct)).Items);
                Assert.Equal(0,(await Service(f.Db).HistoryAsync(new PaymentHistoryQuery{InvoiceNumber="absent"},Ct)).Count);
                Assert.Equal(0,(await Service(f.Db).HistoryAsync(new PaymentHistoryQuery{From="2026-10-29"},Ct)).Count);
                Assert.Equal(0,(await Service(f.Db).HistoryAsync(new PaymentHistoryQuery{CashierName="absent"},Ct)).Count);
                Assert.Equal(1,(await Service(f.Db).HistoryAsync(new PaymentHistoryQuery{CashierId=f.Db.UserId,Status="posted",To="2026-10-28"},Ct)).Count);
                instrument.Name="الاسم المحدث";instrument.IsActive=false;await Service(f.Db).SaveAsync(instrument,f.Db.UserId,Ct);Migrator.Run(f.Db.ConnectionString);
                Assert.True((await f.Post(draft.Id,"payment-post")).Succeeded);Assert.Equal("الاسم المحدث",(await f.Sales.GetAsync(draft.Id,Ct)).PaymentInstrumentName);
                Assert.Equal("الاسم المحدث",Assert.Single((await Service(f.Db).HistoryAsync(new PaymentHistoryQuery{InstrumentId=instrument.Id},Ct)).Items).InstrumentName);
                Assert.Equal(1000000,(await workforce.ShiftAsync(shift,null,Ct)).ExpectedScaled);
                var ret=await f.Sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="electronic-return",Reason="إرجاع",Lines=new System.Collections.Generic.List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="1"}}},f.Db.UserId,Ct);Assert.Equal("electronic_reversal_unsupported",ret.ErrorCode);
                Assert.False((await f.Sales.CancelAsync(draft.Id,"electronic-cancel","إلغاء",f.Db.UserId,Ct)).Succeeded);
                using(var c=f.Db.Open()){
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));Assert.Equal(type=="wallet"?"transfer":"card",c.ExecuteScalar<string>("SELECT PaymentMethod FROM SalesPayments"));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesTreasuryReceipts"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CashierShiftEvents"));
                    Assert.Equal(19000000,c.ExecuteScalar<long>("SELECT SUM(QuantityBaseScaled) FROM InventoryBalances"));Assert.Equal(90000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM InventoryCostAllocations"));
                    Assert.Throws<SQLiteException>(()=>c.Execute("DELETE FROM SalePaymentSelections WHERE SalesInvoiceId=@Id",new{Id=draft.Id}));Assert.Empty(c.Query("PRAGMA foreign_key_check"));
                }
            }
        }

        [Fact]
        public async Task Missing_wrong_inactive_mixed_and_credit_instruments_are_rejected_and_post_rechecks_activity()
        {
            using(var f=new SalesTreasuryTests.Fixture()){
                await f.Prepare();var instrument=await Create(f.Db);
                foreach(var cmd in new[]{Command(f,"wallet",null),Command(f,"card",instrument.Id),Command(f,"cash",instrument.Id),Command(f,"mixed",instrument.Id),Command(f,"wallet",9999)})await Assert.ThrowsAsync<ArgumentException>(()=>f.Sales.CreateDraftAsync(cmd,f.Db.UserId,Ct));
                var credit=Command(f,"wallet",instrument.Id);credit.PaymentMode="credit";credit.CustomerId=1;await Assert.ThrowsAsync<ArgumentException>(()=>f.Sales.CreateDraftAsync(credit,f.Db.UserId,Ct));
                var partial=Command(f,"wallet",instrument.Id);partial.PaymentAmount="1.00";await Assert.ThrowsAsync<ArgumentException>(()=>f.Sales.CreateDraftAsync(partial,f.Db.UserId,Ct));
                var split=Command(f,"card",instrument.Id);split.HasPaymentAllocations=true;await Assert.ThrowsAsync<ArgumentException>(()=>f.Sales.CreateDraftAsync(split,f.Db.UserId,Ct));
                var draft=await f.Sales.CreateDraftAsync(Command(f,"wallet",instrument.Id),f.Db.UserId,Ct);
                instrument.IsActive=false;await Service(f.Db).SaveAsync(instrument,f.Db.UserId,Ct);
                await Assert.ThrowsAsync<ArgumentException>(()=>f.Sales.CreateDraftAsync(Command(f,"wallet",instrument.Id),f.Db.UserId,Ct));
                Assert.Equal("instrument_unavailable",(await f.Post(draft.Id)).ErrorCode);
                using(var c=f.Db.Open()){Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));Assert.Equal(20000000,c.ExecuteScalar<long>("SELECT SUM(QuantityBaseScaled) FROM InventoryBalances"));}
            }
        }

        [Fact]
        public async Task Late_failure_rolls_back_payment_inventory_and_paid_projection_then_retry_posts_once()
        {
            using(var f=new SalesTreasuryTests.Fixture()){
                await f.Prepare();var instrument=await Create(f.Db);var draft=await f.Sales.CreateDraftAsync(Command(f,"wallet",instrument.Id),f.Db.UserId,Ct);
                using(var c=f.Db.Open())c.Execute("CREATE TRIGGER FailPaymentPosting BEFORE INSERT ON SalesInvoiceAudit WHEN NEW.EventType='posted' BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.False((await f.Post(draft.Id)).Succeeded);
                using(var c=f.Db.Open()){
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices"));Assert.Equal("draft",c.ExecuteScalar<string>("SELECT Status FROM SalesInvoices"));Assert.Equal(20000000,c.ExecuteScalar<long>("SELECT SUM(QuantityBaseScaled) FROM InventoryBalances"));c.Execute("DROP TRIGGER FailPaymentPosting");
                }
                Assert.Equal(0,(await Service(f.Db).HistoryAsync(new PaymentHistoryQuery(),Ct)).Count);
                Assert.True((await f.Post(draft.Id)).Succeeded);Assert.True((await f.Post(draft.Id)).Succeeded);Assert.Equal(1,(await Service(f.Db).HistoryAsync(new PaymentHistoryQuery(),Ct)).Count);
            }
        }

        [Fact]
        public async Task Additive_upgrade_preserves_existing_posted_invoice_and_legacy_payment_without_backfill()
        {
            using(var f=new SalesTreasuryTests.Fixture()){
                await f.Prepare();var draft=await f.Draft();Assert.True((await f.Post(draft.Id)).Succeeded);
                // Reconstruct the exact pre-074 schema in this disposable database only.
                using(var c=f.Db.Open())c.Execute("DROP TABLE PaymentInstrumentAudit; DROP TABLE SalePaymentSelections; DROP TABLE PaymentInstruments; DELETE FROM SchemaVersion WHERE Name LIKE '%074_payment_instruments.sql';");
                Migrator.Run(f.Db.ConnectionString);Migrator.Run(f.Db.ConnectionString);
                var invoice=await f.Sales.GetAsync(draft.Id,Ct);Assert.Equal("posted",invoice.Status);Assert.Equal(123400,invoice.PaidScaled);Assert.Null(invoice.PaymentInstrumentId);
                var row=Assert.Single((await Service(f.Db).HistoryAsync(new PaymentHistoryQuery(),Ct)).Items);Assert.Equal(draft.InvoiceNumber,row.InvoiceNumber);Assert.Equal(123400,row.AmountScaled);Assert.Null(row.InstrumentId);
                using(var c=f.Db.Open()){Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalePaymentSelections"));Assert.Empty(c.Query("PRAGMA foreign_key_check"));}
            }
        }

        [Fact]
        public async Task Legacy_unassigned_cash_payments_remain_visible_without_invented_instruments()
        {
            using(var f=new SalesTreasuryTests.Fixture()){
                await f.Prepare();var draft=await f.Draft();using(var c=f.Db.Open())c.Execute("DELETE FROM SalePaymentSelections WHERE SalesInvoiceId=@Id",new{draft.Id});
                Assert.True((await f.Post(draft.Id)).Succeeded);var row=Assert.Single((await Service(f.Db).HistoryAsync(new PaymentHistoryQuery(),Ct)).Items);Assert.Null(row.InstrumentId);Assert.Null(row.InstrumentName);Assert.Equal("cash",row.Method);
            }
        }
    }
}
