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
    public sealed class TreasuryLegacyMigrationTests
    {
        private const string LegacySql = @"
CREATE TABLE TreasuryAccounts (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL UNIQUE, Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
 AccountType TEXT NOT NULL CHECK(AccountType IN ('cash','bank')), BankName TEXT, AccountNumber TEXT, Iban TEXT,
 Notes TEXT NOT NULL DEFAULT '', IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)),
 CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id), CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL);
CREATE TABLE TreasuryTransactions (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, TreasuryAccountId INTEGER NOT NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT,
 EntryType TEXT NOT NULL, AmountScaled INTEGER NOT NULL, OccurredOn TEXT NOT NULL, Reference TEXT NOT NULL,
 Description TEXT NOT NULL DEFAULT '', TransferGroup TEXT, CounterAccountId INTEGER REFERENCES TreasuryAccounts(Id),
 OperationKey TEXT UNIQUE, RequestHash TEXT, CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id), CreatedUtc TEXT NOT NULL);
CREATE TABLE TreasuryAccountAudit (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, TreasuryAccountId INTEGER NOT NULL REFERENCES TreasuryAccounts(Id),
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id), EventType TEXT NOT NULL, EventUtc TEXT NOT NULL, Details TEXT NOT NULL DEFAULT '');
CREATE TRIGGER TR_TreasuryTransactions_NoUpdate BEFORE UPDATE ON TreasuryTransactions BEGIN SELECT RAISE(ABORT,'immutable'); END;
CREATE TRIGGER TR_TreasuryTransactions_NoDelete BEFORE DELETE ON TreasuryTransactions BEGIN SELECT RAISE(ABORT,'immutable'); END;
CREATE TRIGGER TR_TreasuryAccountAudit_NoUpdate BEFORE UPDATE ON TreasuryAccountAudit BEGIN SELECT RAISE(ABORT,'immutable'); END;
CREATE TRIGGER TR_TreasuryAccountAudit_NoDelete BEFORE DELETE ON TreasuryAccountAudit BEGIN SELECT RAISE(ABORT,'immutable'); END;
CREATE TRIGGER TR_TreasuryAccounts_CodeImmutable BEFORE UPDATE OF Code ON TreasuryAccounts WHEN NEW.Code<>OLD.Code BEGIN SELECT RAISE(ABORT,'immutable'); END;
CREATE TRIGGER TR_TreasuryAccounts_NoDelete BEFORE DELETE ON TreasuryAccounts BEGIN SELECT RAISE(ABORT,'immutable'); END;
INSERT INTO SchemaVersion(Name,AppliedAt) VALUES('AlKhayal.Data.Migrations.019_treasury.sql','2026-01-01');";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Legacy_019_empty_or_populated_schema_upgrades_and_restarts_without_data_loss(bool populated)
        {
            var path = Path.Combine(Path.GetTempPath(), "treasury-legacy-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var cs = CreateLegacy(path);
                if (populated) using (var c = new SQLiteConnection(cs))
                {
                    c.Open(); Seed(c);
                    c.Execute(@"INSERT INTO TreasuryTransactions(Id,TreasuryAccountId,EntryType,AmountScaled,OccurredOn,Reference,Description,OperationKey,CreatedByUserId,CreatedUtc)
                        VALUES(41,7,'opening',100000,'2026-01-01','OPEN','رصيد قديم',NULL,1,'2026-01-01T00:00:00Z'),
                        (42,7,'transfer_out',-20000,'2026-01-02','TRANSFER','نقل',NULL,1,'2026-01-02T00:00:00Z'),
                        (43,8,'transfer_in',20000,'2026-01-02','TRANSFER','نقل',NULL,1,'2026-01-02T00:00:00Z'),
                        (44,7,'withdrawal',-10000,'2026-01-03','W1','سحب','old-key',1,'2026-01-03T00:00:00Z');");
                }
                Migrator.Run(cs); Migrator.Run(cs);
                var service = new TreasuryService(new SqliteTreasuryStore(cs));
                var rows = await service.ListAsync(CancellationToken.None);
                if (populated)
                {
                    Assert.Equal("7.00", rows.Single(x => x.Id == 7).Balance);
                    Assert.Equal("2.00", rows.Single(x => x.Id == 8).Balance);
                    Assert.DoesNotContain(rows, x => x.Id == 9);
                    var movement = await service.PostAsync(new TreasuryMovementCommand { AccountId = 7, Amount = "0.10", OperationKey = "new-key" }, "deposit", 1, CancellationToken.None);
                    Assert.Equal("7.10", movement.BalanceAfter);
                    var changed = await service.SaveAsync(7, new TreasuryAccountCommand { Code = "NEW-CODE", Name = "تعديل", Type = "cash", Version = 2 }, 1, CancellationToken.None);
                    Assert.Equal("NEW-CODE", changed.Code);
                    var created = await service.SaveAsync(null, new TreasuryAccountCommand { Code = "NEW", Name = "حساب جديد", Type = "bank" }, 1, CancellationToken.None);
                    Assert.True(created.Id > 9);
                    using (var c = new SQLiteConnection(cs))
                    {
                        c.Open();
                        Assert.Equal(4, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryTransactions"));
                        Assert.Equal(4, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryLegacyImports"));
                        Assert.Equal("original-bank", c.ExecuteScalar<string>("SELECT BankName FROM LegacyTreasuryAccounts019 WHERE Id=8"));
                        Assert.Equal("retained-audit", c.ExecuteScalar<string>("SELECT Details FROM TreasuryAccountAudit"));
                        Assert.Equal(0, c.ExecuteScalar<long>("SELECT IsActive FROM LegacyTreasuryAccounts019 WHERE Id=9"));
                        Assert.Throws<SQLiteException>(() => c.Execute("UPDATE TreasuryTransactions SET AmountScaled=100"));
                    }
                }
                else Assert.Empty(rows);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%024_treasury.sql'"));
                    Assert.Empty(c.Query("PRAGMA foreign_key_check")); Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }

        [Theory]
        [InlineData("sub_piastre")]
        [InlineData("negative_balance")]
        [InlineData("duplicate_code")]
        public void Invalid_legacy_data_aborts_the_entire_upgrade_and_retains_original_schema(string scenario)
        {
            var path = Path.Combine(Path.GetTempPath(), "treasury-legacy-invalid-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var cs = CreateLegacy(path);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open(); Seed(c);
                    if (scenario == "duplicate_code") c.Execute("INSERT INTO TreasuryAccounts(Code,Name,AccountType,CreatedByUserId,CreatedUtc,UpdatedUtc) VALUES(' c7 ','متعارض','cash',1,'2026-01-01','2026-01-01')");
                    else c.Execute("INSERT INTO TreasuryTransactions(TreasuryAccountId,EntryType,AmountScaled,OccurredOn,Reference,CreatedByUserId,CreatedUtc) VALUES(7,@type,@amount,'2026-01-01','invalid',1,'2026-01-01')", new { type = scenario == "sub_piastre" ? "deposit" : "withdrawal", amount = scenario == "sub_piastre" ? 101 : -100 });
                }
                Assert.Throws<InvalidOperationException>(() => Migrator.Run(cs));
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open(); Assert.Equal("C7", c.ExecuteScalar<string>("SELECT Code FROM TreasuryAccounts WHERE Id=7"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('LegacyTreasuryAccounts019','TreasuryMovements','TreasuryLegacyImports')"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%024_treasury.sql'"));
                    Assert.Empty(c.Query("PRAGMA foreign_key_check"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }
        private static void Seed(SQLiteConnection c)
        {
            c.Execute(@"INSERT INTO TreasuryAccounts(Id,Code,Name,AccountType,BankName,AccountNumber,Iban,IsActive,CreatedByUserId,CreatedUtc,UpdatedUtc)
                VALUES(7,'C7','خزنة قديمة','cash',NULL,NULL,NULL,1,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z'),
                (8,'B8','بنك قديم','bank','original-bank','retained-number','retained-iban',1,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z'),
                (9,'C9','حساب موقوف','cash',NULL,NULL,NULL,0,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
                INSERT INTO TreasuryAccountAudit(TreasuryAccountId,ActorUserId,EventType,EventUtc,Details) VALUES(7,1,'created','2026-01-01','retained-audit');");
        }
        private static string CreateLegacy(string path)
        {
            var cs = Db.ConnectionStringFor(path);
            using (var c = new SQLiteConnection(cs))
            {
                c.Open(); c.Execute("CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL)");
                var assembly = typeof(Migrator).Assembly;
                foreach (var script in assembly.GetManifestResourceNames().Where(n => !n.EndsWith("071_phase7_1_workforce.sql", StringComparison.Ordinal) && !n.EndsWith("072_phase7_2_printing.sql", StringComparison.Ordinal) && !n.EndsWith("073_phase7_3_labels.sql", StringComparison.Ordinal) && !n.EndsWith("074_payment_instruments.sql", StringComparison.Ordinal) && !n.EndsWith("075_expense_profit_classification.sql", StringComparison.Ordinal)).Where(x => x.EndsWith(".sql") && !x.EndsWith("024_treasury.sql") && !x.EndsWith("025_purchase_treasury.sql") && !x.EndsWith("026_supplier_payment_treasury.sql") && !x.EndsWith("027_supplier_payment_allocations.sql") && !x.EndsWith("028_expenses.sql") && !x.EndsWith("029_sales_treasury.sql") && !x.EndsWith("030_returns_management.sql") && !x.EndsWith("031_sales_refund_overdraft.sql")).OrderBy(x => x, StringComparer.Ordinal))
                {
                    using (var stream = assembly.GetManifestResourceStream(script)) using (var reader = new StreamReader(stream)) c.Execute(reader.ReadToEnd());
                    c.Execute("INSERT INTO SchemaVersion(Name,AppliedAt) VALUES(@script,'2026-01-01')", new { script });
                }
                c.Execute(LegacySql);
            }
            new SqliteUserStore(cs).CreateAsync("legacy", "اختبار", new PasswordHasher().Hash(Guid.NewGuid().ToString()), UserRoles.Administrator, CancellationToken.None).GetAwaiter().GetResult();
            return cs;
        }
    }
}
