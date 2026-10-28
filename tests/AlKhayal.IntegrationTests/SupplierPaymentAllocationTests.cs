using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Suppliers;
using AlKhayal.Data.Suppliers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class InventoryPersistenceTests
    {
        [Fact]
        public async Task On_account_payment_allocates_earliest_due_then_oldest_creation_and_updates_invoice_paid_and_remaining()
        {
            using(var f=new Fixture())
            {
                var account=await FundPurchaseAccount(f,"1000");var supplier=PurchaseSupplier(f);var invoices=new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs));
                var late=DebtInvoice(100);late.DueDate="2026-11-10";var first=invoices.AddInvoice(supplier,late,"alloc-late",1);
                var soon=DebtInvoice(100);soon.DueDate="2026-11-01";var second=invoices.AddInvoice(supplier,soon,"alloc-soon",1);var third=invoices.AddInvoice(supplier,soon,"alloc-oldest",1);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();c.Execute("UPDATE SupplierInvoices SET CreatedAt=CASE Id WHEN @second THEN '2026-10-28T12:02:00Z' WHEN @third THEN '2026-10-28T12:01:00Z' ELSE '2026-10-28T12:00:00Z' END",new{second=second.Id,third=third.Id});}
                var service=new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs));
                var payment=await service.AddTreasuryPaymentAsync(supplier,null,null,"250","2026-10-28","تحويل",null,null,"allocate-priority",account,1,CancellationToken.None);
                var rows=invoices.GetInvoices(supplier);Assert.Equal(100m,rows.Single(x=>x.Id==third.Id).PaidAmount);Assert.Equal(100m,rows.Single(x=>x.Id==second.Id).PaidAmount);Assert.Equal(50m,rows.Single(x=>x.Id==first.Id).PaidAmount);
                Assert.Equal(0m,rows.Single(x=>x.Id==third.Id).Remaining);Assert.Equal(50m,rows.Single(x=>x.Id==first.Id).Remaining);
                Assert.True((await service.AddTreasuryPaymentAsync(supplier,null,null,"250","2026-10-28","تحويل",null,null,"allocate-priority",account,1,CancellationToken.None)).Replayed);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(new[]{third.Id,second.Id,first.Id},c.Query<long>("SELECT SupplierInvoiceId FROM SupplierPaymentAllocations WHERE SupplierLedgerEntryId=@Id ORDER BY Id",new{payment.Id}).ToArray());Assert.Equal(2500000,c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM SupplierPaymentAllocations"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements WHERE Type='withdraw'"));Assert.Throws<SQLiteException>(()=>c.Execute("DELETE FROM SupplierPaymentAllocations"));}
                invoices.AddInvoice(supplier,DebtInvoice(100),"another-debt-after-allocation",1);
                await Assert.ThrowsAsync<ArgumentException>(()=>service.AddTreasuryPaymentAsync(supplier,first.Id,null,"51","2026-10-28","نقدي",null,null,"exceeds-allocated-remaining",account,1,CancellationToken.None));
            }
        }
        [Fact]
        public async Task Account_payment_combines_purchase_date_fallback_and_legacy_due_date_without_double_counting()
        {
            using(var f=new Fixture())
            {
                var account=await FundPurchaseAccount(f,"1000");var supplier=PurchaseSupplier(f);var accounts=new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs));
                var debt=DebtInvoice(100);debt.DueDate="2026-11-01";var legacy=accounts.AddInvoice(supplier,debt,"mixed-debt",1);
                var command=CashPurchase(f,supplier,null);command.PaymentMode="credit";command.InvoiceDate="2026-11-01";command.Lines[0].PurchaseUnitPrice="100";
                var purchases=Purchases(f);var draft=await purchases.CreateDraftAsync(command,1,CancellationToken.None);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();c.Execute("UPDATE SupplierInvoices SET CreatedAt='2026-10-28T12:02:00Z'; UPDATE PurchaseInvoices SET CreatedUtc='2026-10-28T12:01:00Z'");}
                Assert.True((await purchases.PostAsync(draft.Id,"mixed-purchase",1,CancellationToken.None)).Succeeded);
                await new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs)).AddTreasuryPaymentAsync(supplier,null,null,"150","2026-10-28","نقدي",null,null,"mixed-account-payment",account,1,CancellationToken.None);
                var rows=new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs)).GetInvoices(supplier);
                Assert.Equal(100m,rows.Single(x=>x.InvoiceType=="purchase").PaidAmount);Assert.Equal(0m,rows.Single(x=>x.InvoiceType=="purchase").Remaining);
                Assert.Equal(50m,rows.Single(x=>x.InvoiceType=="legacy").PaidAmount);Assert.Equal(50m,rows.Single(x=>x.InvoiceType=="legacy").Remaining);
            }
        }
    }
}
