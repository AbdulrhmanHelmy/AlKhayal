using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Treasury;
using AlKhayal.Application.Expenses;
using AlKhayal.Data.Treasury;
using AlKhayal.Data.Expenses;
using AlKhayal.Data.Database;
using AlKhayal.Data.Authentication;
using AlKhayal.Application.Users;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class RefundOverdraftMigrationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Upgrade_preserves_ledger_audit_and_source_links_and_rolls_back_on_late_failure(bool failFirst)
        {
            var root=Path.Combine(Path.GetTempPath(),"refund-upgrade-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var cs=Db.ConnectionStringFor(Path.Combine(root,"test.db"));
            try
            {
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();c.Execute("CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL)");
                    var assembly=typeof(Migrator).Assembly;
                    foreach(var script in assembly.GetManifestResourceNames().Where(n => !n.EndsWith("071_phase7_1_workforce.sql", StringComparison.Ordinal) && !n.EndsWith("072_phase7_2_printing.sql", StringComparison.Ordinal) && !n.EndsWith("073_phase7_3_labels.sql", StringComparison.Ordinal) && !n.EndsWith("032_customer_installments.sql", StringComparison.Ordinal) && !n.EndsWith("033_maintenance_tickets.sql", StringComparison.Ordinal) && !n.EndsWith("074_payment_instruments.sql", StringComparison.Ordinal) && !n.EndsWith("075_expense_profit_classification.sql", StringComparison.Ordinal)).Where(x=>x.EndsWith(".sql")&&!x.EndsWith("031_sales_refund_overdraft.sql")).OrderBy(x=>x,StringComparer.Ordinal))
                    {
                        using(var stream=assembly.GetManifestResourceStream(script))using(var reader=new StreamReader(stream))c.Execute(reader.ReadToEnd());
                        c.Execute("INSERT INTO SchemaVersion VALUES(@script,'2026-10-29T00:00:00Z')",new{script});
                    }
                }
                var user=await new SqliteUserStore(cs).CreateAsync("refund-upgrade","اختبار",new PasswordHasher().Hash(Guid.NewGuid().ToString()),UserRoles.Administrator,CancellationToken.None);
                var treasury=new TreasuryService(new SqliteTreasuryStore(cs));var account=await treasury.SaveAsync(null,new TreasuryAccountCommand{Code="UPGRADE",Name="خزنة الترقية",Type="cash"},user,CancellationToken.None);
                await treasury.PostAsync(new TreasuryMovementCommand{AccountId=account.Id,Amount="12.34",OperationKey="upgrade-deposit"},"deposit",user,CancellationToken.None);
                var expenses=new ExpenseService(new SqliteExpenseStore(cs));var category=await expenses.AddCategoryAsync(new ExpenseCategoryCommand{Name="مصروف الترقية"},user,CancellationToken.None);
                var voucher=await expenses.PostAsync(new ExpenseCommand{CategoryId=category.Id,TreasuryAccountId=account.Id,Amount="1.00",ExpenseDate="2026-10-29",OperationKey="upgrade-expense"},user,CancellationToken.None);
                string before;
                using(var c=new SQLiteConnection(cs)){c.Open();before=Snapshot(c);if(failFirst)c.Execute("CREATE TRIGGER FailMigration BEFORE INSERT ON SchemaVersion WHEN NEW.Name LIKE '%031_sales_refund_overdraft.sql' BEGIN SELECT RAISE(ABORT,'injected'); END;");}
                if(failFirst)
                {
                    Assert.Throws<SQLiteException>(()=>Migrator.Run(cs));
                    using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(before,Snapshot(c));Assert.Equal(30,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));Assert.Contains("BalanceAfterScaled >= 0",c.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE name='TreasuryMovements'"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE name='TreasuryMovements_031'"));c.Execute("DROP TRIGGER FailMigration");}
                }
                Migrator.Run(cs);Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();Assert.Equal(before,Snapshot(c));Assert.Equal(MigrationTestExpectations.Count,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));Assert.Equal(voucher.TreasuryMovementId,c.ExecuteScalar<long>("SELECT TreasuryMovementId FROM ExpenseVouchers"));
                    Assert.Throws<SQLiteException>(()=>c.Execute("UPDATE TreasuryMovements SET Notes='tampered'"));
                    Assert.Throws<SQLiteException>(()=>c.Execute("INSERT INTO TreasuryMovements(AccountId,Type,AmountScaled,BalanceAfterScaled,OperationKey,RequestHash,Notes,UserId,CreatedUtc) VALUES(@account,'withdraw',100,-100,'invalid-negative','hash','',@user,'2026-10-29')",new{account=account.Id,user}));
                }
                Assert.Equal("11.34",Assert.Single(await treasury.ListAsync(CancellationToken.None)).Balance);
                Assert.Equal("insufficient_balance",(await Assert.ThrowsAsync<TreasuryException>(()=>treasury.PostAsync(new TreasuryMovementCommand{AccountId=account.Id,Amount="12",OperationKey="ordinary-overdraw"},"withdraw",user,CancellationToken.None))).Code);
            }
            finally{SQLiteConnection.ClearAllPools();Directory.Delete(root,true);}
        }
        private static string Snapshot(SQLiteConnection c) => string.Join("|",c.Query<string>("SELECT Id||':'||AccountId||':'||Type||':'||AmountScaled||':'||BalanceAfterScaled||':'||OperationKey||':'||RequestHash||':'||Notes||':'||UserId||':'||CreatedUtc FROM TreasuryMovements ORDER BY Id")) + ";" + string.Join("|",c.Query<string>("SELECT Id||':'||AccountId||':'||COALESCE(MovementId,0)||':'||Action||':'||Detail||':'||UserId||':'||CreatedUtc FROM TreasuryAudit ORDER BY Id"));
    }
}
