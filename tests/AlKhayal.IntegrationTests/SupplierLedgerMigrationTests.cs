using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
                var scripts = assembly.GetManifestResourceNames().Where(n => !n.EndsWith("071_phase7_1_workforce.sql", StringComparison.Ordinal) && !n.EndsWith("072_phase7_2_printing.sql", StringComparison.Ordinal) && !n.EndsWith("073_phase7_3_labels.sql", StringComparison.Ordinal) && !n.EndsWith("032_customer_installments.sql", StringComparison.Ordinal) && !n.EndsWith("033_maintenance_tickets.sql", StringComparison.Ordinal) && !n.EndsWith("074_payment_instruments.sql", StringComparison.Ordinal) && !n.EndsWith("075_expense_profit_classification.sql", StringComparison.Ordinal)).Where(x => x.EndsWith(".sql", StringComparison.Ordinal) && !x.EndsWith("017_integrated_purchasing.sql", StringComparison.Ordinal) && !x.EndsWith("018_purchasing_provenance_and_credit.sql", StringComparison.Ordinal) && !x.EndsWith("019_sales_foundation.sql", StringComparison.Ordinal) && !x.EndsWith("020_sales_lifecycle.sql", StringComparison.Ordinal) && !x.EndsWith("022_pos_browse_indexes.sql", StringComparison.Ordinal) && !x.EndsWith("023_directory_query_indexes.sql", StringComparison.Ordinal) && !x.EndsWith("024_treasury.sql", StringComparison.Ordinal) && !x.EndsWith("025_purchase_treasury.sql", StringComparison.Ordinal) && !x.EndsWith("026_supplier_payment_treasury.sql", StringComparison.Ordinal) && !x.EndsWith("027_supplier_payment_allocations.sql", StringComparison.Ordinal) && !x.EndsWith("028_expenses.sql", StringComparison.Ordinal) && !x.EndsWith("029_sales_treasury.sql", StringComparison.Ordinal) && !x.EndsWith("030_returns_management.sql", StringComparison.Ordinal) && !x.EndsWith("031_sales_refund_overdraft.sql", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal).ToList();
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

        private static string CreateRecorded017WithoutLegacyPaymentId(string path)
        {
            var cs = CreateLegacyDatabase(path, 100m);
            using (var c = new SQLiteConnection(cs))
            {
                c.Open();
                var assembly = typeof(Migrator).Assembly;
                var script = assembly.GetManifestResourceNames().Single(x => x.EndsWith("017_integrated_purchasing.sql", StringComparison.Ordinal));
                string sql;
                using (var stream = assembly.GetManifestResourceStream(script))
                using (var reader = new StreamReader(stream)) sql = reader.ReadToEnd();
                // Representative older 017 ledger: historical rows were imported, but
                // the payment ID link had not yet been introduced.
                sql = sql.Replace("\r\n", "\n")
                    .Replace(" LegacyPaymentId INTEGER NULL REFERENCES SupplierPayments(Id) ON DELETE RESTRICT,\n", "")
                    .Replace("CREATE UNIQUE INDEX UX_SupplierLedger_LegacyPayment ON SupplierLedgerEntries(LegacyPaymentId) WHERE LegacyPaymentId IS NOT NULL;\n", "")
                    .Replace("Reference,LegacyPaymentId,Notes,CreatedUtc)", "Reference,Notes,CreatedUtc)")
                    .Replace("PaymentDate,Reference,Id,\n", "PaymentDate,Reference,\n");
                Assert.DoesNotContain("LegacyPaymentId", sql);
                c.Execute(sql);
                c.Execute("INSERT INTO SchemaVersion(Name,AppliedAt) VALUES(@Name,'2026-01-01')", new { Name = script });
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
                    Assert.Equal(MigrationTestExpectations.Count,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
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
                    Assert.Equal(17, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SupplierLedgerEntries'"));
                    Assert.Equal(39m, c.ExecuteScalar<decimal>("SELECT Payable FROM Suppliers WHERE Code='LEGACY'"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierInvoices WHERE InvoiceNumber='OLD-1'"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierPayments WHERE Reference='OLD-PAY-1'"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public async Task Recorded_legacy_ledger_without_payment_id_upgrades_and_restarts_without_data_loss()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = CreateRecorded017WithoutLegacyPaymentId(path);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    using (var tx = c.BeginTransaction())
                    {
                        c.Execute("ALTER TABLE SupplierLedgerEntries ADD COLUMN SupplierInvoiceId INTEGER NULL REFERENCES SupplierInvoices(Id) ON DELETE RESTRICT", transaction: tx);
                        c.Execute("DROP TRIGGER TR_SupplierLedgerEntries_NoUpdate", transaction: tx);
                        var original = Assert.Throws<SQLiteException>(() => c.Execute(@"UPDATE SupplierLedgerEntries SET SupplierInvoiceId=(
                            SELECT InvoiceId FROM SupplierPayments p WHERE p.Id=SupplierLedgerEntries.LegacyPaymentId)
                            WHERE EntryType='legacy_payment' AND LegacyPaymentId IS NOT NULL", transaction: tx));
                        Assert.Contains("no such column", original.Message);
                        Assert.Contains("LegacyPaymentId", original.Message);
                        tx.Rollback();
                    }
                }

                Migrator.Run(cs);
                Migrator.Run(cs);
                var statement = await new AlKhayal.Data.Suppliers.SqliteSupplierLedgerRepository(cs)
                    .StatementAsync(1, null, null, System.Threading.CancellationToken.None);
                Assert.Equal(100m, statement.ClosingBalance);
                Assert.Equal(3, statement.Movements.Count);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    Assert.Equal(MigrationTestExpectations.Count,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries e JOIN SupplierPayments p ON p.Id=e.LegacyPaymentId WHERE e.EntryType='legacy_payment' AND e.SupplierInvoiceId=p.InvoiceId"));
                    Assert.Equal(1000000, c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM SupplierLedgerEntries"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierInvoices"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierPayments"));
                    Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Unmatched_legacy_payment_link_stops_upgrade_without_changing_ledger()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = CreateRecorded017WithoutLegacyPaymentId(path);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    c.Execute(@"INSERT INTO SupplierPayments(SupplierId,InvoiceId,Reference,Amount,PaymentDate,Method,Account,CreatedAt)
                        SELECT SupplierId,InvoiceId,'UNMATCHED-2',Amount,PaymentDate,Method,Account,CreatedAt FROM SupplierPayments LIMIT 1");
                }
                var error = Assert.Throws<InvalidOperationException>(() => Migrator.Run(cs));
                Assert.Contains("missing or ambiguous", error.Message);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    Assert.Equal(18, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries"));
                    Assert.Equal(2, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierPayments"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_table_info('SupplierLedgerEntries') WHERE name='LegacyPaymentId'"));
                    Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
