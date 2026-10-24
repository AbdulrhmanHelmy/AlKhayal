using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using AlKhayal.Data.Database;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SupplierLedgerMigrationTests
    {
        private static string CreateLegacyDatabase(string path, decimal payable)
        {
            var cs = Db.ConnectionStringFor(path);
            using (var c = new SQLiteConnection(cs))
            {
                c.Open(); c.Execute("PRAGMA foreign_keys=ON; CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL)");
                var assembly = typeof(Migrator).Assembly;
                var scripts = assembly.GetManifestResourceNames().Where(x => x.EndsWith(".sql", StringComparison.Ordinal) && !x.EndsWith("017_integrated_purchasing.sql", StringComparison.Ordinal) && !x.EndsWith("018_purchasing_provenance_and_credit.sql", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal).ToList();
                foreach (var script in scripts)
                {
                    using (var stream = assembly.GetManifestResourceStream(script)) using (var reader = new StreamReader(stream)) c.Execute(reader.ReadToEnd());
                    c.Execute("INSERT INTO SchemaVersion(Name,AppliedAt) VALUES(@Name,'2026-01-01')", new { Name = script });
                }
                c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('legacy-supplier-test','test-only','2026-01-01T00:00:00Z')");
                c.Execute("INSERT INTO Suppliers(Code,Name,Phone,Payable,Status,CreatedAt,UpdatedAt) VALUES('LEGACY','مورد تاريخي','01000000000',@Payable,'نشط','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')", new { Payable = payable });
                var supplierId = c.ExecuteScalar<long>("SELECT Id FROM Suppliers WHERE Code='LEGACY'");
                c.Execute("INSERT INTO SupplierInvoices(SupplierId,InvoiceNumber,InvoiceDate,Total,PaidAmount,CreatedAt) VALUES(@SupplierId,'OLD-1','2026-05-01',50,10,'2026-05-01T00:00:00Z')", new { SupplierId = supplierId });
                var invoiceId = c.ExecuteScalar<long>("SELECT Id FROM SupplierInvoices WHERE SupplierId=@SupplierId", new { SupplierId = supplierId });
                c.Execute("INSERT INTO SupplierPayments(SupplierId,InvoiceId,Reference,Amount,PaymentDate,Method,Account,CreatedAt) VALUES(@SupplierId,@InvoiceId,'OLD-PAY-1',10,'2026-06-01','تحويل','مرجع قديم','2026-06-01T00:00:00Z')", new { SupplierId = supplierId, InvoiceId = invoiceId });
            }
            return cs;
        }

        [Fact]
        public void Reconciled_legacy_supplier_history_is_preserved_and_opening_is_not_double_counted()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = CreateLegacyDatabase(path, 100m);
                Migrator.Run(cs);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    Assert.Equal(18, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Equal(new long[] { -100000, 500000, 600000 }, c.Query<long>("SELECT AmountScaled FROM SupplierLedgerEntries ORDER BY AmountScaled").ToArray());
                    Assert.Equal(2, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierInvoices") + c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierPayments"));
                    Assert.Equal(1000000, c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM SupplierLedgerEntries"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Reconciliation_discrepancy_stops_before_supplier_ledger_migration_writes()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = CreateLegacyDatabase(path, 39m);
                var error = Assert.Throws<InvalidOperationException>(() => Migrator.Run(cs));
                Assert.Contains("migration stopped", error.Message);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    Assert.Equal(16, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SupplierLedgerEntries'"));
                    Assert.Equal(39m, c.ExecuteScalar<decimal>("SELECT Payable FROM Suppliers WHERE Code='LEGACY'"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierInvoices WHERE InvoiceNumber='OLD-1'"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierPayments WHERE Reference='OLD-PAY-1'"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
