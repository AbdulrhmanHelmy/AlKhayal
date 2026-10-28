using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Expenses;
using AlKhayal.Application.Treasury;
using AlKhayal.Data.Database;
using AlKhayal.Data.Expenses;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class ExpensePersistenceTests
    {
        private static ExpenseService Service(TreasuryPersistenceTests.Fixture f) => new ExpenseService(new SqliteExpenseStore(f.ConnectionString));
        private static ExpenseCommand Command(long type, long account, string amount = "10.10", string key = "expense-test") => new ExpenseCommand {
            CategoryId = type, TreasuryAccountId = account, Amount = amount, ExpenseDate = "2026-10-28", Payee = "المستفيد", Notes = "مصروف موثق", OperationKey = key
        };
        [Fact]
        public async Task Categories_and_exact_vouchers_persist_with_source_snapshot_replay_and_immutable_audit()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var s = Service(f); Assert.Empty(await s.CategoriesAsync(CancellationToken.None));
                var type = await s.AddCategoryAsync(new ExpenseCategoryCommand { Name = " إيجار " }, f.UserId, CancellationToken.None);
                Assert.Equal(type.Id, (await s.AddCategoryAsync(new ExpenseCategoryCommand { Name = "إيجار" }, f.UserId, CancellationToken.None)).Id);
                var bank = await f.Create(type: "bank"); await f.Post(bank.Id, "100.20");
                var cmd = Command(type.Id, bank.Id); var result = await s.PostAsync(cmd, f.UserId, CancellationToken.None);
                Assert.Equal("10.10", result.Amount); Assert.Equal("إيجار", result.CategoryName); Assert.False(result.Replayed);
                Assert.Equal("EXP-000001", result.Number); Assert.True(result.TreasuryMovementId > 0);
                Assert.Equal("90.10", (await f.Service.ListAsync(CancellationToken.None)).Single().Balance);
                cmd.Amount = "10.1"; Assert.True((await Service(f).PostAsync(cmd, f.UserId, CancellationToken.None)).Replayed);
                cmd.Amount = "10.11"; Assert.Equal("operation_conflict", (await Assert.ThrowsAsync<ExpenseException>(() => s.PostAsync(cmd, f.UserId, CancellationToken.None))).Code);
                cmd.Amount = "10.10"; Assert.Equal("operation_conflict", (await Assert.ThrowsAsync<ExpenseException>(() => s.PostAsync(cmd, f.UserId + 1, CancellationToken.None))).Code);
                Migrator.Run(f.ConnectionString);
                var saved = Assert.Single(await Service(f).VouchersAsync(CancellationToken.None));
                Assert.Equal(result.Id, saved.Id); Assert.Equal(cmd.Notes, saved.Notes); Assert.Equal(f.UserId, saved.UserId);
                Assert.Single(await Service(f).CategoriesAsync(CancellationToken.None));
                using (var c = f.Open())
                {
                    Assert.Throws<SQLiteException>(() => c.Execute("UPDATE ExpenseVouchers SET Notes='tamper'"));
                    Assert.Throws<SQLiteException>(() => c.Execute("DELETE FROM ExpenseVouchers"));
                    Assert.Equal(2, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
        }
        [Fact]
        public async Task Invalid_types_amounts_dates_accounts_and_insufficient_funds_make_no_writes()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var s = Service(f); var type = await s.AddCategoryAsync(new ExpenseCategoryCommand { Name = "نقل" }, f.UserId, CancellationToken.None); var account = await f.Create(); await f.Post(account.Id, "10");
                foreach (var amount in new[] { "0", "-1", "0.001", "922337203685477.59", "abc" })
                    Assert.Equal("validation", (await Assert.ThrowsAsync<ExpenseException>(() => s.PostAsync(Command(type.Id, account.Id, amount), f.UserId, CancellationToken.None))).Code);
                var bad = Command(type.Id, account.Id); bad.ExpenseDate = "2026-02-30";
                await Assert.ThrowsAsync<ExpenseException>(() => s.PostAsync(bad, f.UserId, CancellationToken.None));
                await Assert.ThrowsAsync<ExpenseException>(() => s.PostAsync(Command(type.Id + 9, account.Id), f.UserId, CancellationToken.None));
                await Assert.ThrowsAsync<ExpenseException>(() => s.PostAsync(Command(type.Id, account.Id + 9), f.UserId, CancellationToken.None));
                Assert.Equal("insufficient_balance", (await Assert.ThrowsAsync<TreasuryException>(() => s.PostAsync(Command(type.Id, account.Id), f.UserId, CancellationToken.None))).Code);
                Assert.Empty(await s.VouchersAsync(CancellationToken.None));
                Assert.Equal("10.00", (await f.Service.ListAsync(CancellationToken.None)).Single().Balance);
            }
        }
        [Fact]
        public async Task Late_failure_rolls_back_voucher_treasury_balance_version_and_audit_then_retry_succeeds()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var s = Service(f); var type = await s.AddCategoryAsync(new ExpenseCategoryCommand { Name = "كهرباء" }, f.UserId, CancellationToken.None); var account = await f.Create(); await f.Post(account.Id, "20"); var cmd = Command(type.Id, account.Id);
                using (var c = f.Open()) c.Execute("CREATE TRIGGER ExpenseFailure BEFORE INSERT ON ExpenseVouchers BEGIN SELECT RAISE(ABORT,'injected'); END;");
                await Assert.ThrowsAsync<SQLiteException>(() => s.PostAsync(cmd, f.UserId, CancellationToken.None));
                using (var c = f.Open())
                {
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM ExpenseVouchers"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(2, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryAudit"));
                    Assert.Equal(2, c.ExecuteScalar<long>("SELECT Version FROM TreasuryAccounts"));
                    c.Execute("DROP TRIGGER ExpenseFailure");
                }
                Assert.Equal("20.00", (await f.Service.ListAsync(CancellationToken.None)).Single().Balance);
                Assert.False((await s.PostAsync(cmd, f.UserId, CancellationToken.None)).Replayed);
                Assert.Equal("9.90", (await f.Service.ListAsync(CancellationToken.None)).Single().Balance);
            }
        }
        [Fact]
        public async Task Concurrent_retries_post_once_and_competing_vouchers_cannot_overdraw()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var s = Service(f); var type = await s.AddCategoryAsync(new ExpenseCategoryCommand { Name = "مياه" }, f.UserId, CancellationToken.None); var account = await f.Create(); await f.Post(account.Id, "20");
                var results = await Task.WhenAll(Task.Run(() => s.PostAsync(Command(type.Id, account.Id), f.UserId, CancellationToken.None)), Task.Run(() => s.PostAsync(Command(type.Id, account.Id), f.UserId, CancellationToken.None)));
                Assert.Equal(results[0].Id, results[1].Id); Assert.Single(results, x => x.Replayed);
                Func<string, Task<bool>> spend = async key => { try { await s.PostAsync(Command(type.Id, account.Id, "7", key), f.UserId, CancellationToken.None); return true; } catch (TreasuryException ex) { Assert.Equal("insufficient_balance", ex.Code); return false; } };
                Assert.Single(await Task.WhenAll(Task.Run(() => spend("a")), Task.Run(() => spend("b"))), x => x);
                Assert.Equal(2, (await s.VouchersAsync(CancellationToken.None)).Count);
                Assert.Equal("2.90", (await f.Service.ListAsync(CancellationToken.None)).Single().Balance);
            }
        }
    }
}
