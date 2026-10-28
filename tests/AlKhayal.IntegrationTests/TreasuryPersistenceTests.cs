using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Treasury;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Data.Treasury;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class TreasuryPersistenceTests
    {
        internal sealed class Fixture : IDisposable
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "alkhayal-treasury-" + Guid.NewGuid().ToString("N"));
            public string DatabasePath => Path.Combine(_root, "test.db");
            public string ConnectionString => Db.ConnectionStringFor(DatabasePath);
            public long UserId { get; }
            public TreasuryService Service => new TreasuryService(new SqliteTreasuryStore(ConnectionString));
            public Fixture()
            {
                Directory.CreateDirectory(_root);
                Migrator.Run(ConnectionString);
                UserId = new SqliteUserStore(ConnectionString).CreateAsync("treasury-test", "اختبار", new PasswordHasher().Hash(Guid.NewGuid().ToString()), UserRoles.Administrator, CancellationToken.None).GetAwaiter().GetResult();
            }
            public SQLiteConnection Open() { var c = new SQLiteConnection(ConnectionString); c.Open(); return c; }
            public Task<TreasuryAccount> Create(string code = "C1", string type = "cash") => Service.SaveAsync(null, new TreasuryAccountCommand { Code = code, Name = "الحساب " + code, Type = type }, UserId, CancellationToken.None);
            public Task<TreasuryMovement> Post(long id, string amount, string type = "deposit", string key = null, string notes = "اختبار") => Service.PostAsync(new TreasuryMovementCommand { AccountId = id, Amount = amount, Notes = notes, OperationKey = key ?? Guid.NewGuid().ToString("N") }, type, UserId, CancellationToken.None);
            public void Dispose() { SQLiteConnection.ClearAllPools(); Directory.Delete(_root, true); }
        }

        [Fact]
        public async Task Accounts_crud_restart_uniqueness_and_stale_versions()
        {
            using (var f = new Fixture())
            {
                var cash = await f.Create(); var bank = await f.Create("B1", "bank");
                Assert.Equal("0.00", cash.Balance);
                await Error("duplicate_code", () => f.Create(" c1 "));
                var edited = await f.Service.SaveAsync(bank.Id, new TreasuryAccountCommand { Code = "B2", Name = "بنك الشركة", Type = "bank", Notes = "حساب جاري", Version = bank.Version }, f.UserId, CancellationToken.None);
                Assert.Equal(2, edited.Version);
                await Error("version_conflict", () => f.Service.SaveAsync(bank.Id, new TreasuryAccountCommand { Code = "B3", Name = "قديم", Type = "bank", Version = bank.Version }, f.UserId, CancellationToken.None));
                await Error("version_conflict", () => f.Service.DeleteAsync(bank.Id, bank.Version, f.UserId, CancellationToken.None));
                await f.Service.DeleteAsync(cash.Id, cash.Version, f.UserId, CancellationToken.None);
                Migrator.Run(f.ConnectionString);
                var rows = await f.Service.ListAsync(CancellationToken.None);
                Assert.Single(rows); Assert.Equal("بنك الشركة", rows[0].Name); Assert.Equal("B2", rows[0].Code); Assert.Equal("حساب جاري", rows[0].Notes);
                using (var c = f.Open())
                {
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%024_treasury.sql'"));
                    Assert.Equal(4, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryAudit"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT IsDeleted FROM TreasuryAccounts WHERE Id=@Id", new { cash.Id }));
                    Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check"));
                    Assert.Empty(c.Query("PRAGMA foreign_key_check"));
                }
            }
        }

        [Theory]
        [InlineData("cash")]
        [InlineData("bank")]
        public async Task Exact_balances_replay_and_immutable_history(string type)
        {
            using (var f = new Fixture())
            {
                var account = await f.Create(type: type);
                var first = await f.Post(account.Id, "10.10", key: "same");
                var replay = await f.Post(account.Id, "10.1", key: "same");
                Assert.Equal(first.Id, replay.Id); Assert.True(replay.Replayed);
                await Error("operation_conflict", () => f.Post(account.Id, "10.11", key: "same"));
                await Error("operation_conflict", () => f.Post(account.Id, "10.10", key: "same", notes: "بيان مختلف"));
                var other = await f.Create("OTHER");
                await Error("operation_conflict", () => f.Post(other.Id, "10.10", key: "same"));
                await f.Post(account.Id, "0.01", "withdraw");
                Assert.Equal("10.09", (await f.Service.ListAsync(CancellationToken.None)).First(x => x.Id == account.Id).Balance);
                await Error("insufficient_balance", () => f.Post(account.Id, "10.10", "withdraw"));
                await f.Post(account.Id, "10.09", "withdraw");
                var saved = (await f.Service.ListAsync(CancellationToken.None)).First(x => x.Id == account.Id);
                Assert.Equal("0.00", saved.Balance);
                await Error("account_in_use", () => f.Service.DeleteAsync(account.Id, saved.Version, f.UserId, CancellationToken.None));
                await Error("account_in_use", () => f.Service.SaveAsync(account.Id, new TreasuryAccountCommand { Code = "C1", Name = "تعديل", Type = type == "cash" ? "bank" : "cash", Version = saved.Version }, f.UserId, CancellationToken.None));
                using (var c = f.Open())
                {
                    Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT SUM(CASE WHEN Type='deposit' THEN AmountScaled ELSE -AmountScaled END) FROM TreasuryMovements"));
                    Assert.Throws<SQLiteException>(() => c.Execute("UPDATE TreasuryMovements SET AmountScaled=100"));
                    Assert.Throws<SQLiteException>(() => c.Execute("DELETE FROM TreasuryMovements"));
                    Assert.Throws<SQLiteException>(() => c.Execute("DELETE FROM TreasuryAudit"));
                }
                Migrator.Run(f.ConnectionString);
                Assert.Equal("0.00", (await f.Service.ListAsync(CancellationToken.None)).First(x => x.Id == account.Id).Balance);
            }
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("0.001")]
        [InlineData("10.100")]
        [InlineData("1e2")]
        [InlineData("922337203685477.59")]
        public async Task Invalid_money_never_posts(string amount)
        {
            using (var f = new Fixture())
            {
                var account = await f.Create();
                await Error("validation", () => f.Post(account.Id, amount));
                using (var c = f.Open()) Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
            }
        }

        [Fact]
        public async Task Overflow_and_deleted_accounts_never_post()
        {
            using (var f = new Fixture())
            {
                var account = await f.Create(); var deleted = await f.Create("DELETE");
                await f.Service.DeleteAsync(deleted.Id, deleted.Version, f.UserId, CancellationToken.None);
                await Error("not_found", () => f.Post(deleted.Id, "1"));
                await f.Post(account.Id, "922337203685477.58");
                await Error("balance_overflow", () => f.Post(account.Id, "0.01"));
                Assert.Equal("922337203685477.58", (await f.Service.ListAsync(CancellationToken.None)).Single().Balance);
            }
        }

        [Fact]
        public async Task Late_failure_rolls_back_movement_version_and_audit_then_same_key_can_retry()
        {
            using (var f = new Fixture())
            {
                var account = await f.Create(); await f.Post(account.Id, "10");
                using (var c = f.Open()) c.Execute("CREATE TRIGGER TreasuryTestFailure BEFORE INSERT ON TreasuryAudit WHEN NEW.Action='withdraw' BEGIN SELECT RAISE(ABORT,'injected failure'); END;");
                await Assert.ThrowsAsync<SQLiteException>(() => f.Post(account.Id, "3", "withdraw", "rollback"));
                using (var c = f.Open())
                {
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(2, c.ExecuteScalar<long>("SELECT Version FROM TreasuryAccounts"));
                    Assert.Equal(2, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryAudit"));
                    c.Execute("DROP TRIGGER TreasuryTestFailure");
                }
                Assert.Equal("10.00", (await f.Service.ListAsync(CancellationToken.None)).Single().Balance);
                var retry = await f.Post(account.Id, "3", "withdraw", "rollback");
                Assert.False(retry.Replayed); Assert.Equal("7.00", retry.BalanceAfter);
            }
        }

        [Fact]
        public async Task Concurrent_withdrawals_cannot_overdraw_and_concurrent_retries_post_once()
        {
            using (var f = new Fixture())
            {
                var account = await f.Create();
                var replays = await Task.WhenAll(Task.Run(() => f.Post(account.Id, "10", key: "concurrent")), Task.Run(() => f.Post(account.Id, "10", key: "concurrent")));
                Assert.Equal(replays[0].Id, replays[1].Id); Assert.Single(replays, x => x.Replayed);
                Func<Task<bool>> withdraw = async () => { try { await f.Post(account.Id, "7", "withdraw"); return true; } catch (TreasuryException ex) { Assert.Equal("insufficient_balance", ex.Code); return false; } };
                var results = await Task.WhenAll(Task.Run(withdraw), Task.Run(withdraw));
                Assert.Single(results, x => x);
                Assert.Equal("3.00", (await f.Service.ListAsync(CancellationToken.None)).Single().Balance);
                using (var c = f.Open()) Assert.Equal(2, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
            }
        }
        private static async Task Error(string code, Func<Task> action) { Assert.Equal(code, (await Assert.ThrowsAsync<TreasuryException>(action)).Code); }
    }
}
