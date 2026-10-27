using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Suppliers;
using AlKhayal.Core.Models;
using AlKhayal.Data.Database;
using AlKhayal.Data.Suppliers;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SupplierPersistenceTests
    {
        [Fact]
        public async Task Supplier_pages_search_all_rows_and_keep_balances()
        {
            var path=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"alkhayal-supplier-pages-"+Guid.NewGuid().ToString("N")+".db"));
            var app=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AlKhayal"));
            Assert.False(path.StartsWith(app+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));Assert.False(File.Exists(path));
            try
            {
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var repo=new SqliteSupplierRepository(cs);
                for(var i=0;i<31;i++)repo.Add(new Supplier{Code="PAGE-"+i.ToString("00"),Name="مورد "+i.ToString("00"),Phone="010"+i,Payable=12,Status="نشط"});
                var first=await repo.ListAsync("",1,10,CancellationToken.None);var second=await repo.ListAsync("",2,10,CancellationToken.None);
                Assert.Equal(31,first.TotalCount);Assert.Equal(10,first.Items.Count);Assert.Equal(10,second.Items.Count);Assert.Empty(first.Items.Select(x=>x.Id).Intersect(second.Items.Select(x=>x.Id)));
                var found=await repo.ListAsync("PAGE-30",1,10,CancellationToken.None);Assert.Single(found.Items);Assert.Equal(12m,found.Items[0].Payable);
                Assert.Single((await repo.ListAsync("01030",1,10,CancellationToken.None)).Items);
                Assert.Empty((await repo.ListAsync("%",1,10,CancellationToken.None)).Items);
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        private static void WithDatabase(Action<string> body)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var connectionString = Db.ConnectionStringFor(path);
                Migrator.Run(connectionString);
                body(connectionString);
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static long NewSupplier(string connectionString, decimal opening = 0)
        {
            return new SqliteSupplierRepository(connectionString).Add(new Supplier { Code = "SUP-" + Guid.NewGuid().ToString("N").Substring(0, 6), Name = "مورد اختبار", Contact = "مسؤول", Phone = "01000000000", Address = "القاهرة", Payable = opening, Status = "نشط" });
        }

        [Fact]
        public void Supplier_fields_survive_repository_recreation_and_update_never_changes_payable()
        {
            WithDatabase(cs =>
            {
                var repo = new SqliteSupplierRepository(cs);
                var id = NewSupplier(cs, 500);
                var reopened = new SqliteSupplierRepository(cs);
                var supplier = reopened.GetById(id);
                Assert.Equal("القاهرة", supplier.Address); Assert.Equal(500m, supplier.Payable); Assert.Equal(0m, supplier.Paid);
                supplier.Name = "اسم جديد"; supplier.Payable = 99999;
                Assert.True(reopened.Update(supplier));
                var after = new SqliteSupplierRepository(cs).GetById(id);
                Assert.Equal("اسم جديد", after.Name); Assert.Equal(500m, after.Payable);
                Assert.Throws<SQLiteException>(() => repo.Delete(id)); Assert.NotNull(repo.GetById(id));
            });
        }

        [Fact]
        public void Invoice_increases_payable_and_payment_on_invoice_reduces_supplier_and_invoice_atomically()
        {
            WithDatabase(cs =>
            {
                var suppliers = new SqliteSupplierRepository(cs); var accounts = new SqliteSupplierAccountRepository(cs);
                var id = NewSupplier(cs, 100);
                var invoice = accounts.AddInvoice(id, "INV-1", "2026-10-01", "2026-10-31", 400m, "توريد");
                Assert.Equal(500m, suppliers.GetById(id).Payable);
                Assert.Equal("غير مسدد", invoice.Status); Assert.Equal(400m, invoice.Remaining);

                var payment = accounts.AddPayment(id, invoice.Id, 150m, "2026-10-05", "نقدي", "الخزنة الرئيسية", null);
                Assert.StartsWith("SP-", payment.Reference); Assert.Equal("INV-1", payment.InvoiceNumber);
                var supplier = suppliers.GetById(id);
                Assert.Equal(350m, supplier.Payable); Assert.Equal(150m, supplier.Paid);
                var listed = accounts.GetInvoices(id).Single();
                Assert.Equal(150m, listed.PaidAmount); Assert.Equal(250m, listed.Remaining); Assert.Equal("مسدد جزئيًا", listed.Status);

                accounts.AddPayment(id, invoice.Id, 250m, "2026-10-06", "تحويل", "الخزنة الرئيسية", null);
                var settled = accounts.GetInvoices(id).Single();
                Assert.Equal(0m, settled.Remaining); Assert.Equal("مسدد", settled.Status);
                Assert.Equal(100m, suppliers.GetById(id).Payable);
            });
        }

        [Fact]
        public void Rejected_payments_leave_balances_and_payment_rows_unchanged()
        {
            WithDatabase(cs =>
            {
                var suppliers = new SqliteSupplierRepository(cs); var accounts = new SqliteSupplierAccountRepository(cs);
                var id = NewSupplier(cs); var other = NewSupplier(cs);
                var invoice = accounts.AddInvoice(id, "INV-1", "2026-10-01", null, 200m, null);
                var otherInvoice = accounts.AddInvoice(other, "INV-1", "2026-10-01", null, 999m, null);

                Assert.Throws<ArgumentException>(() => accounts.AddPayment(id, invoice.Id, 200.01m, "2026-10-02", "نقدي", "الخزنة الرئيسية", null));
                Assert.Throws<ArgumentException>(() => accounts.AddPayment(id, null, 200.01m, "2026-10-02", "نقدي", "الخزنة الرئيسية", null));
                Assert.Throws<ArgumentException>(() => accounts.AddPayment(id, invoice.Id, 0m, "2026-10-02", "نقدي", "الخزنة الرئيسية", null));
                Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => accounts.AddPayment(id, otherInvoice.Id, 10m, "2026-10-02", "نقدي", "الخزنة الرئيسية", null));
                Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => accounts.AddPayment(12345, null, 10m, "2026-10-02", "نقدي", "الخزنة الرئيسية", null));

                Assert.Equal(200m, suppliers.GetById(id).Payable);
                Assert.Equal(999m, suppliers.GetById(other).Payable);
                Assert.Empty(accounts.GetPayments(id)); Assert.Empty(accounts.GetPayments(other));
                Assert.Equal(0m, accounts.GetInvoices(id).Single().PaidAmount);
            });
        }

        [Fact]
        public void Payment_without_invoice_reduces_only_the_supplier_balance()
        {
            WithDatabase(cs =>
            {
                var suppliers = new SqliteSupplierRepository(cs); var accounts = new SqliteSupplierAccountRepository(cs);
                var id = NewSupplier(cs, 300);
                accounts.AddPayment(id, null, 100m, "2026-10-02", "نقدي", "الخزنة الرئيسية", "على الحساب");
                Assert.Equal(200m, suppliers.GetById(id).Payable);
                Assert.Null(accounts.GetPayments(id).Single().InvoiceId);
            });
        }

        [Fact]
        public void Duplicate_invoice_numbers_are_rejected_per_supplier_without_changing_balance()
        {
            WithDatabase(cs =>
            {
                var suppliers = new SqliteSupplierRepository(cs); var accounts = new SqliteSupplierAccountRepository(cs);
                var id = NewSupplier(cs); var other = NewSupplier(cs);
                accounts.AddInvoice(id, "inv-7", "2026-10-01", null, 50m, null);
                Assert.Throws<ArgumentException>(() => accounts.AddInvoice(id, "INV-7", "2026-10-02", null, 70m, null));
                Assert.Equal(50m, suppliers.GetById(id).Payable);
                accounts.AddInvoice(other, "INV-7", "2026-10-02", null, 70m, null);
                Assert.Equal(70m, suppliers.GetById(other).Payable);
                var auto = accounts.AddInvoice(id, null, "2026-10-03", null, 5m, null);
                Assert.StartsWith("SI-", auto.InvoiceNumber);
            });
        }

        [Fact]
        public void Statement_orders_movements_and_balance_matches_current_payable()
        {
            WithDatabase(cs =>
            {
                var suppliers = new SqliteSupplierRepository(cs); var accounts = new SqliteSupplierAccountRepository(cs);
                var id = NewSupplier(cs, 100);
                var invoice = accounts.AddInvoice(id, "A", "2026-10-01", null, 400m, null);
                accounts.AddPayment(id, invoice.Id, 150m, "2026-10-05", "نقدي", "الخزنة الرئيسية", null);
                accounts.AddInvoice(id, "B", "2026-10-10", null, 60m, null);

                var all = accounts.GetStatement(id, null, null);
                Assert.Equal(100m, all.OpeningBalance); Assert.Equal(460m, all.TotalInvoices); Assert.Equal(150m, all.TotalPaid);
                Assert.Equal(410m, all.ClosingBalance); Assert.Equal(suppliers.GetById(id).Payable, all.ClosingBalance);
                Assert.Equal(new[] { "OPEN", "A", all.Movements[2].Reference, "B" }, all.Movements.Select(m => m.Reference).ToArray());

                var period = accounts.GetStatement(id, "2026-10-06", "2026-10-31");
                Assert.Equal(350m, period.OpeningBalance); Assert.Equal(60m, period.TotalInvoices); Assert.Equal(0m, period.TotalPaid); Assert.Equal(410m, period.ClosingBalance);
                Assert.Null(accounts.GetStatement(9999, null, null));
            });
        }

        [Fact]
        public void Supplier_with_invoices_or_payments_cannot_be_deleted()
        {
            WithDatabase(cs =>
            {
                var suppliers = new SqliteSupplierRepository(cs); var accounts = new SqliteSupplierAccountRepository(cs);
                var id = NewSupplier(cs);
                accounts.AddInvoice(id, "A", "2026-10-01", null, 10m, null);
                Assert.Throws<SQLiteException>(() => suppliers.Delete(id));
                Assert.NotNull(suppliers.GetById(id));
            });
        }

        [Fact]
        public void Unified_supplier_ledger_includes_purchase_history_and_payment_replays_idempotently()
        {
            WithDatabase(cs =>
            {
                var suppliers = new SqliteSupplierRepository(cs); var accounts = new SqliteSupplierAccountRepository(cs);
                var id = NewSupplier(cs);
                var invoice = accounts.AddInvoice(id, "LEDGER-1", "2026-10-01", null, 100m, "legacy bill");
                var ledger = new SupplierLedgerService(new SqliteSupplierLedgerRepository(cs));
                var payment = ledger.AddPaymentAsync(id, invoice.Id, null, "20.13", "2026-10-05", "تحويل", "حساب مسجل", "دفعة", "ledger-pay-1", CancellationToken.None).GetAwaiter().GetResult();
                Assert.False(payment.Replayed);
                var replay = ledger.AddPaymentAsync(id, invoice.Id, null, "20.13", "2026-10-05", "تحويل", "حساب مسجل", "دفعة", "ledger-pay-1", CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(replay.Replayed);
                Assert.Throws<InvalidOperationException>(() => ledger.AddPaymentAsync(id, invoice.Id, null, "21", "2026-10-05", "تحويل", "حساب مسجل", "دفعة", "ledger-pay-1", CancellationToken.None).GetAwaiter().GetResult());
                Assert.Equal(79.87m, suppliers.GetById(id).Payable);
                Assert.Equal(20.13m, accounts.GetInvoices(id).Single().PaidAmount);
                Assert.Equal(20.13m, accounts.GetPayments(id).Single().Amount);
                var statement = ledger.StatementAsync(id, null, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(100m, statement.TotalInvoices); Assert.Equal(20.13m, statement.TotalPaid); Assert.Equal(79.87m, statement.ClosingBalance);
                Assert.Equal(2, ledger.HistoryAsync(id, CancellationToken.None).GetAwaiter().GetResult().Count);
            });
        }
    }
}
