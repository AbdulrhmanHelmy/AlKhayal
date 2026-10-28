using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Suppliers;
using AlKhayal.Application.Treasury;
using AlKhayal.Data.Suppliers;
using AlKhayal.Data.Treasury;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class InventoryPersistenceTests
    {
        [Theory]
        [InlineData("cash",false)]
        [InlineData("bank",true)]
        public async Task Supplier_payment_withdraws_once_from_selected_account_and_retains_source_link(string type,bool invoicePayment)
        {
            using(var f=new Fixture())
            {
                var account=await FundPurchaseAccount(f,type:type);var other=await FundPurchaseAccount(f);
                var supplier=PurchaseSupplier(f);var invoice=new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs)).AddInvoice(supplier,DebtInvoice(),"payment-debt",1);
                var service=new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs));
                Func<Task<AlKhayal.Core.Models.SupplierLedgerEntry>> pay=()=>service.AddTreasuryPaymentAsync(supplier,invoicePayment?(long?)invoice.Id:null,null,"10.13","2026-10-28","نقدي",null,"سداد فعلي","linked-payment",account,1,CancellationToken.None);
                var results=await Task.WhenAll(Task.Run(pay),Task.Run(pay));Assert.Equal(results[0].Id,results[1].Id);Assert.Single(results,x=>x.Replayed);
                Assert.NotNull(results[0].TreasuryMovementId);Assert.Equal(results[0].TreasuryMovementId,results[1].TreasuryMovementId);
                Assert.Equal(account,results[0].TreasuryAccountId);Assert.True((await pay()).Replayed);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AddTreasuryPaymentAsync(supplier,invoicePayment?(long?)invoice.Id:null,null,"10.13","2026-10-28","نقدي",null,"سداد فعلي","linked-payment",other,1,CancellationToken.None));
                var balances=await new TreasuryService(new SqliteTreasuryStore(f.Cs)).ListAsync(CancellationToken.None);
                Assert.Equal("89.87",balances.Single(x=>x.Id==account).Balance);Assert.Equal("100.00",balances.Single(x=>x.Id==other).Balance);
                Assert.Equal(89.88m,new SqliteSupplierRepository(f.Cs).GetById(supplier).Payable);
                var history=await new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs)).HistoryAsync(supplier,CancellationToken.None);
                Assert.Equal(results[0].TreasuryMovementId,history.Single(x=>x.EntryType=="payment").TreasuryMovementId);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierPaymentTreasuryLinks"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements WHERE Type='withdraw'"));Assert.Throws<SQLiteException>(()=>c.Execute("DELETE FROM SupplierPaymentTreasuryLinks"));Assert.Empty(c.Query("PRAGMA foreign_key_check"));}
            }
        }
        [Fact]
        public async Task Supplier_payment_requires_account_and_insufficient_funds_roll_back_the_supplier_entry()
        {
            using(var f=new Fixture())
            {
                var account=await FundPurchaseAccount(f,"1");var supplier=PurchaseSupplier(f);
                new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs)).AddInvoice(supplier,DebtInvoice(),"insufficient-debt",1);
                var service=new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs));
                await Assert.ThrowsAsync<ArgumentException>(()=>service.AddTreasuryPaymentAsync(supplier,null,null,"10","2026-10-28","تحويل",null,null,"no-account",null,1,CancellationToken.None));
                var failed=await Assert.ThrowsAsync<TreasuryException>(()=>service.AddTreasuryPaymentAsync(supplier,null,null,"10","2026-10-28","تحويل",null,null,"low-funds",account,1,CancellationToken.None));Assert.Equal("insufficient_balance",failed.Code);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierPaymentTreasuryLinks"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE EntryType='payment'"));Assert.Equal(100.01m,c.ExecuteScalar<decimal>("SELECT Payable FROM Suppliers"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));}
            }
        }
        [Fact]
        public async Task Late_supplier_projection_failure_rolls_back_treasury_link_audit_and_can_retry_same_key()
        {
            using(var f=new Fixture())
            {
                var account=await FundPurchaseAccount(f);var supplier=PurchaseSupplier(f);
                new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs)).AddInvoice(supplier,DebtInvoice(),"rollback-debt",1);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();c.Execute("CREATE TRIGGER fail_supplier_payment BEFORE UPDATE OF Payable ON Suppliers WHEN NEW.Payable<OLD.Payable BEGIN SELECT RAISE(ABORT,'injected failure'); END");}
                var service=new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs));
                Func<Task<AlKhayal.Core.Models.SupplierLedgerEntry>> pay=()=>service.AddTreasuryPaymentAsync(supplier,null,null,"10","2026-10-28","نقدي",null,null,"late-failure",account,1,CancellationToken.None);
                await Assert.ThrowsAsync<SQLiteException>(pay);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierPaymentTreasuryLinks"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements WHERE Type='withdraw'"));Assert.Equal(2,c.ExecuteScalar<long>("SELECT Version FROM TreasuryAccounts"));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryAudit"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE OperationKey='late-failure'"));c.Execute("DROP TRIGGER fail_supplier_payment");}
                Assert.False((await pay()).Replayed);Assert.True((await pay()).Replayed);
            }
        }
        [Fact]
        public async Task Concurrent_supplier_payments_cannot_spend_the_same_treasury_funds()
        {
            using(var f=new Fixture())
            {
                var account=await FundPurchaseAccount(f);var first=PurchaseSupplier(f);var second=PurchaseSupplier(f);
                var accounts=new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs));accounts.AddInvoice(first,DebtInvoice(),"concurrent-debt-1",1);accounts.AddInvoice(second,DebtInvoice(),"concurrent-debt-2",1);
                var service=new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs));
                Func<long,Task<bool>> pay=async id=>{try{await service.AddTreasuryPaymentAsync(id,null,null,"70","2026-10-28","نقدي",null,null,"concurrent-pay-"+id,account,1,CancellationToken.None);return true;}catch(TreasuryException ex){Assert.Equal("insufficient_balance",ex.Code);return false;}};
                var results=await Task.WhenAll(Task.Run(()=>pay(first)),Task.Run(()=>pay(second)));Assert.Single(results,x=>x);
                Assert.Equal("30.00",(await new TreasuryService(new SqliteTreasuryStore(f.Cs)).ListAsync(CancellationToken.None)).Single().Balance);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierPaymentTreasuryLinks"));}
            }
        }
    }
}
