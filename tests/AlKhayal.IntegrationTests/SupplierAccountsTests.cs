using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Suppliers;
using AlKhayal.Core.Models;
using AlKhayal.Data.Purchasing;
using AlKhayal.Data.Suppliers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class InventoryPersistenceTests
    {
        private static SupplierInvoice DebtInvoice(decimal total = 100.01m) => new SupplierInvoice { InvoiceDate="2026-10-28",Total=total,Notes="مديونية موثقة" };
        [Fact]
        public async Task Supplier_debt_invoice_retries_once_and_invoice_families_keep_independent_payments()
        {
            using(var f=new Fixture())
            {
                var supplier=PurchaseSupplier(f);
                var accounts=new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs));
                var results=await Task.WhenAll(Task.Run(()=>accounts.AddInvoice(supplier,DebtInvoice(),"new-debt",1)),Task.Run(()=>accounts.AddInvoice(supplier,DebtInvoice(),"new-debt",1)));
                Assert.Equal(results[0].Id,results[1].Id);Assert.Single(results,x=>x.Replayed);
                Assert.Equal(100.01m,new SqliteSupplierRepository(f.Cs).GetById(supplier).Payable);
                Assert.Throws<InvalidOperationException>(()=>accounts.AddInvoice(supplier,DebtInvoice(100.02m),"new-debt",1));
                var ledger=new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs));
                await ledger.AddPaymentAsync(supplier,results[0].Id,null,"25.25","2026-10-28","نقدي",null,"سداد","debt-pay",CancellationToken.None);
                var purchase=CashPurchase(f,supplier,null,"PUR-SAME-ID");purchase.PaymentMode="credit";
                var service=new PurchaseService(new SqlitePurchaseRepository(f.Cs),new SqliteSupplierRepository(f.Cs));
                var draft=await service.CreateDraftAsync(purchase,1,CancellationToken.None);
                Assert.Equal(results[0].Id,draft.Id);
                Assert.Equal("draft",accounts.GetInvoices(supplier).Single(x=>x.InvoiceType=="purchase").DocumentStatus);
                Assert.True((await service.PostAsync(draft.Id,"supplier-account-purchase",1,CancellationToken.None)).Succeeded);
                await ledger.AddPaymentAsync(supplier,null,draft.Id,"0.13","2026-10-28","تحويل",null,"سداد شراء","purchase-pay",CancellationToken.None);
                var rows=new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs)).GetInvoices(supplier);
                Assert.Equal(74.76m,rows.Single(x=>x.InvoiceType=="legacy").Remaining);
                Assert.Equal(10m,rows.Single(x=>x.InvoiceType=="purchase").Remaining);
                Assert.Equal(84.76m,new SqliteSupplierRepository(f.Cs).GetById(supplier).Payable);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierInvoices"));Assert.Equal(1000100,c.ExecuteScalar<long>("SELECT AmountScaled FROM SupplierLedgerEntries WHERE OperationKey='new-debt'"));Assert.Empty(c.Query("PRAGMA foreign_key_check"));}
            }
        }
        [Fact]
        public void Supplier_invoice_failure_rolls_back_invoice_projection_and_operation_key()
        {
            using(var f=new Fixture())
            {
                var supplier=PurchaseSupplier(f);var accounts=new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs));
                using(var c=new SQLiteConnection(f.Cs)){c.Open();c.Execute("CREATE TRIGGER fail_supplier_invoice BEFORE INSERT ON SupplierLedgerEntries WHEN NEW.EntryType='legacy_invoice' BEGIN SELECT RAISE(ABORT,'injected failure'); END");}
                Assert.Throws<SQLiteException>(()=>accounts.AddInvoice(supplier,DebtInvoice(),"debt-rollback",1));
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierInvoices"));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Payable FROM Suppliers"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE OperationKey='debt-rollback'"));c.Execute("DROP TRIGGER fail_supplier_invoice");}
                Assert.False(accounts.AddInvoice(supplier,DebtInvoice(),"debt-rollback",1).Replayed);
                Assert.True(accounts.AddInvoice(supplier,DebtInvoice(),"debt-rollback",1).Replayed);
            }
        }
        [Fact]
        public async Task Returned_purchase_amount_reduces_invoice_due_and_cannot_be_overpaid_using_another_invoice_balance()
        {
            using(var f=new Fixture())
            {
                var supplier=PurchaseSupplier(f);var accounts=new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs));
                accounts.AddInvoice(supplier,DebtInvoice(),"separate-debt",1);
                var purchase=CashPurchase(f,supplier,null);purchase.PaymentMode="credit";purchase.Lines[0].PurchaseUnitPrice="10";
                var service=Purchases(f);var draft=await service.CreateDraftAsync(purchase,1,CancellationToken.None);
                Assert.True((await service.PostAsync(draft.Id,"return-test-purchase",1,CancellationToken.None)).Succeeded);
                var returned=await service.PostReturnAsync(draft.Id,new PurchaseReturnCommand{WarehouseId=1,ReturnMode="supplier_credit",Lines=new System.Collections.Generic.List<PurchaseReturnLine>{new PurchaseReturnLine{PurchaseInvoiceLineId=draft.Lines[0].Id,Quantity="0.5"}}},"return-half",1,CancellationToken.None);
                Assert.True(returned.Succeeded,returned.ErrorMessage);
                var invoice=accounts.GetInvoices(supplier).Single(x=>x.InvoiceType=="purchase");Assert.Equal(5m,invoice.ReturnedAmount);Assert.Equal(5m,invoice.Remaining);
                var ledger=new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs));
                await Assert.ThrowsAsync<ArgumentException>(()=>ledger.AddPaymentAsync(supplier,null,draft.Id,"6","2026-10-28","نقدي",null,null,"too-much",CancellationToken.None));
                await ledger.AddPaymentAsync(supplier,null,draft.Id,"5","2026-10-28","نقدي",null,null,"pay-remaining",CancellationToken.None);
                Assert.Equal(0m,accounts.GetInvoices(supplier).Single(x=>x.InvoiceType=="purchase").Remaining);
            }
        }
        [Fact]
        public void Debt_invoice_consumes_supplier_credit_before_creating_new_payable()
        {
            using(var f=new Fixture())
            {
                var supplier=PurchaseSupplier(f);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();c.Execute("UPDATE Suppliers SET SupplierCredit=20 WHERE Id=@supplier; INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,Notes,CreatedUtc) VALUES(@supplier,'opening',-200000,'2026-10-28','credit-opening','','2026-10-28T00:00:00Z')",new{supplier});}
                new SupplierAccountService(new SqliteSupplierAccountRepository(f.Cs)).AddInvoice(supplier,DebtInvoice(10),"credit-debt",1);
                using(var c=new SQLiteConnection(f.Cs)){c.Open();Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Payable FROM Suppliers"));Assert.Equal(10m,c.ExecuteScalar<decimal>("SELECT SupplierCredit FROM Suppliers"));Assert.Equal(-100000,c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM SupplierLedgerEntries"));}
            }
        }
    }
}
