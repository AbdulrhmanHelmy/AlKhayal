using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using AlKhayal.Data.Database;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class CustomerLedgerMigrationPreflightTests
    {
        [Fact]
        public void SQLite_numeric_real_values_at_scale_four_upgrade_without_false_failure()
        {
            var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".db");
            try
            {
                var cs=CreatePre015(path,"100.40","0.10"); Migrator.Run(cs); Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs)) { c.Open(); Assert.Equal(1004000,c.ExecuteScalar<long>("SELECT SUM(CASE WHEN TransactionType='opening' THEN AmountScaled ELSE -AmountScaled END) FROM CustomerTransactions")); Assert.Equal("real",c.ExecuteScalar<string>("SELECT typeof(Balance) FROM Customers")); Assert.Equal(MigrationTestExpectations.Count,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion")); }
            }
            finally { SQLiteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
        }

        [Fact]
        public void Genuine_sub_scale_legacy_value_stops_with_diagnostic_and_preserves_original()
        {
            var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".db");
            try
            {
                var cs=CreatePre015(path,"1.00001",null); var error=Assert.Throws<InvalidOperationException>(()=>Migrator.Run(cs)); Assert.Contains("Customers رقم",error.Message); Assert.Contains("1.00001",error.Message);
                using(var c=new SQLiteConnection(cs)) { c.Open(); Assert.Equal("1.00001",c.ExecuteScalar<string>("SELECT CAST(Balance AS TEXT) FROM Customers")); Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CustomerTransactions'")); }
            }
            finally { SQLiteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
        }

        private static string CreatePre015(string path,string balance,string payment)
        {
            var cs=Db.ConnectionStringFor(path);
            using(var c=new SQLiteConnection(cs))
            {
                c.Open(); c.Execute("CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL)"); var assembly=typeof(Migrator).Assembly;
                foreach(var script in assembly.GetManifestResourceNames().Where(n => !n.EndsWith("071_phase7_1_workforce.sql", StringComparison.Ordinal) && !n.EndsWith("072_phase7_2_printing.sql", StringComparison.Ordinal) && !n.EndsWith("073_phase7_3_labels.sql", StringComparison.Ordinal) && !n.EndsWith("074_payment_instruments.sql", StringComparison.Ordinal) && !n.EndsWith("075_expense_profit_classification.sql", StringComparison.Ordinal)).Where(x=>x.EndsWith(".sql",StringComparison.Ordinal)&&!x.EndsWith("015_customer_transactions.sql",StringComparison.Ordinal)&&!x.EndsWith("016_inventory_fifo.sql",StringComparison.Ordinal)&&!x.EndsWith("017_integrated_purchasing.sql",StringComparison.Ordinal)&&!x.EndsWith("018_purchasing_provenance_and_credit.sql",StringComparison.Ordinal)&&!x.EndsWith("019_sales_foundation.sql",StringComparison.Ordinal)&&!x.EndsWith("020_sales_lifecycle.sql",StringComparison.Ordinal)&&!x.EndsWith("022_pos_browse_indexes.sql",StringComparison.Ordinal)&&!x.EndsWith("025_purchase_treasury.sql",StringComparison.Ordinal)&&!x.EndsWith("026_supplier_payment_treasury.sql",StringComparison.Ordinal)&&!x.EndsWith("027_supplier_payment_allocations.sql",StringComparison.Ordinal)&&!x.EndsWith("028_expenses.sql",StringComparison.Ordinal)&&!x.EndsWith("029_sales_treasury.sql",StringComparison.Ordinal)&&!x.EndsWith("030_returns_management.sql",StringComparison.Ordinal)&&!x.EndsWith("031_sales_refund_overdraft.sql",StringComparison.Ordinal)).OrderBy(x=>x,StringComparer.Ordinal))
                { using(var stream=assembly.GetManifestResourceStream(script))using(var reader=new StreamReader(stream))c.Execute(reader.ReadToEnd()); c.Execute("INSERT INTO SchemaVersion(Name,AppliedAt) VALUES(@Name,'2026-01-01')",new{Name=script}); }
                c.Execute("INSERT INTO Customers(Code,Name,Phone,Balance,CreatedAt,UpdatedAt) VALUES('LEGACY-C','عميل قديم','010',CAST(@Balance AS NUMERIC),'2026-01-01','2026-01-01')",new{Balance=balance});
                if(payment!=null)c.Execute("INSERT INTO CustomerPayments(CustomerId,Reference,Amount,PaymentDate,Method,Account,CreatedAt) VALUES(1,'CP-OLD',CAST(@Amount AS NUMERIC),'2026-01-02','cash','safe','2026-01-02')",new{Amount=payment});
            }
            return cs;
        }
    }
}
