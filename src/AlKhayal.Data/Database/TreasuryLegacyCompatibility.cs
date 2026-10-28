using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using AlKhayal.Application.Treasury;
using Dapper;

namespace AlKhayal.Data.Database
{
    // Called only inside pending migration 024's transaction. The old tables remain recovery evidence.
    internal static class TreasuryLegacyCompatibility
    {
        public static void Prepare(SQLiteConnection c, SQLiteTransaction tx)
        {
            if (!Exists(c, tx, "TreasuryAccounts")) return;
            var columns = new HashSet<string>(c.Query<string>("SELECT name FROM pragma_table_info('TreasuryAccounts')", transaction: tx), StringComparer.OrdinalIgnoreCase);
            var required = new[] { "Id", "Code", "Name", "AccountType", "BankName", "AccountNumber", "Iban", "Notes", "IsActive", "CreatedByUserId", "CreatedUtc", "UpdatedUtc" };
            if (required.Any(x => !columns.Contains(x)) || columns.Contains("NormalizedCode") ||
                !Exists(c, tx, "TreasuryTransactions") || !Exists(c, tx, "TreasuryAccountAudit") || Exists(c, tx, "LegacyTreasuryAccounts019") ||
                c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%019_treasury.sql'", transaction: tx) != 1)
                throw new InvalidOperationException("Treasury migration 024 stopped: unrecognized existing treasury schema; original data retained.");

            // SQLite updates the legacy FK/trigger references as part of this transactional rename.
            c.Execute("ALTER TABLE TreasuryAccounts RENAME TO LegacyTreasuryAccounts019", transaction: tx);
        }

        public static void Import(SQLiteConnection c, SQLiteTransaction tx)
        {
            if (!Exists(c, tx, "LegacyTreasuryAccounts019")) return;
            c.Execute(@"CREATE TABLE TreasuryLegacyImports (
                MovementId INTEGER PRIMARY KEY REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT,
                LegacyTransactionId INTEGER NOT NULL UNIQUE REFERENCES TreasuryTransactions(Id) ON DELETE RESTRICT);
                CREATE TRIGGER TreasuryLegacyImports_NoUpdate BEFORE UPDATE ON TreasuryLegacyImports BEGIN SELECT RAISE(ABORT,'Legacy treasury links are immutable'); END;
                CREATE TRIGGER TreasuryLegacyImports_NoDelete BEFORE DELETE ON TreasuryLegacyImports BEGIN SELECT RAISE(ABORT,'Legacy treasury links are immutable'); END;", transaction: tx);
            var codes = new HashSet<string>(StringComparer.Ordinal);
            var balances = new Dictionary<long, long>();
            foreach (var account in c.Query<LegacyAccount>("SELECT * FROM LegacyTreasuryAccounts019 ORDER BY Id", transaction: tx))
            {
                var code = (account.Code ?? "").Trim().Normalize(System.Text.NormalizationForm.FormKC).ToUpperInvariant();
                if (code.Length == 0 || !codes.Add(code) || string.IsNullOrWhiteSpace(account.Name) ||
                    (account.AccountType != "cash" && account.AccountType != "bank") || (account.IsActive != 0 && account.IsActive != 1))
                    throw new InvalidOperationException("Treasury migration 024 stopped: invalid or duplicate normalized legacy account " + account.Id + "; original data retained.");
                balances.Add(account.Id, 0);
                c.Execute(@"INSERT INTO TreasuryAccounts(Id,Code,NormalizedCode,Name,Type,Notes,Version,IsDeleted,CreatedUtc,UpdatedUtc)
                    VALUES(@Id,@Code,@normalized,@Name,@AccountType,@Notes,1,@hidden,@CreatedUtc,@UpdatedUtc)",
                    new { account.Id, account.Code, normalized = code, account.Name, account.AccountType, Notes = account.Notes ?? "", hidden = account.IsActive == 0 ? 1 : 0, account.CreatedUtc, account.UpdatedUtc }, tx);
                c.Execute(@"INSERT INTO TreasuryAudit(AccountId,Action,Detail,UserId,CreatedUtc)
                    VALUES(@Id,'legacy_import','Imported from retained LegacyTreasuryAccounts019; original bank details, state and audit retained.',@CreatedByUserId,@CreatedUtc)", account, tx);
            }
            // Materialize first so reads are closed before dependent writes on this connection.
            var entries = c.Query<LegacyEntry>(@"SELECT Id,TreasuryAccountId,EntryType,CAST(AmountScaled AS TEXT) AS AmountText,
                Description,Reference,OccurredOn,OperationKey,CreatedByUserId,CreatedUtc FROM TreasuryTransactions ORDER BY Id", transaction: tx).ToList();
            foreach (var entry in entries)
            {
                long signed;
                if (!long.TryParse(entry.AmountText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out signed) ||
                    signed == 0 || signed == long.MinValue || signed % 100 != 0 || !balances.ContainsKey(entry.TreasuryAccountId) ||
                    !new[] { "opening", "deposit", "withdrawal", "transfer_in", "transfer_out" }.Contains(entry.EntryType) ||
                    ((entry.EntryType == "opening" || entry.EntryType == "deposit" || entry.EntryType == "transfer_in") != (signed > 0)))
                    throw new InvalidOperationException("Treasury migration 024 stopped: invalid or sub-piastre legacy transaction " + entry.Id + "; original data retained without rounding.");
                var type = signed > 0 ? "deposit" : "withdraw";
                long balance;
                try { balance = TreasuryRules.NextBalance(balances[entry.TreasuryAccountId], Math.Abs(signed), type); }
                catch (TreasuryException ex) { throw new InvalidOperationException("Treasury migration 024 stopped at legacy transaction " + entry.Id + "; original data retained.", ex); }
                balances[entry.TreasuryAccountId] = balance;
                // Reserve historical keys but never pretend an old hash can be replayed as a new command.
                var key = entry.OperationKey ?? "legacy019:" + entry.Id.ToString(CultureInfo.InvariantCulture);
                var movementId = c.ExecuteScalar<long>(@"INSERT INTO TreasuryMovements(AccountId,Type,AmountScaled,BalanceAfterScaled,OperationKey,RequestHash,Notes,UserId,CreatedUtc)
                    VALUES(@TreasuryAccountId,@type,@amount,@balance,@key,@hash,@notes,@CreatedByUserId,@CreatedUtc); SELECT last_insert_rowid();",
                    new { entry.TreasuryAccountId, type, amount = Math.Abs(signed), balance, key, hash = "legacy019:" + entry.Id,
                        notes = "[019/" + entry.Id + "/" + entry.EntryType + "] " + entry.OccurredOn + " | " + entry.Reference + " | " + entry.Description, entry.CreatedByUserId, entry.CreatedUtc }, tx);
                c.Execute("INSERT INTO TreasuryLegacyImports(MovementId,LegacyTransactionId) VALUES(@movementId,@Id)", new { movementId, entry.Id }, tx);
                c.Execute(@"INSERT INTO TreasuryAudit(AccountId,MovementId,Action,Detail,UserId,CreatedUtc)
                    VALUES(@TreasuryAccountId,@movementId,'legacy_import',@detail,@CreatedByUserId,@CreatedUtc)",
                    new { entry.TreasuryAccountId, movementId, detail = "TreasuryTransactions:" + entry.Id, entry.CreatedByUserId, entry.CreatedUtc }, tx);
            }
        }
        private static bool Exists(SQLiteConnection c, SQLiteTransaction tx, string name) =>
            c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name", new { name }, tx) != 0;
        private sealed class LegacyAccount
        {
            public long Id { get; set; } public string Code { get; set; } public string Name { get; set; }
            public string AccountType { get; set; } public string Notes { get; set; } public int IsActive { get; set; }
            public long CreatedByUserId { get; set; } public string CreatedUtc { get; set; } public string UpdatedUtc { get; set; }
        }
        private sealed class LegacyEntry
        {
            public long Id { get; set; } public long TreasuryAccountId { get; set; } public string EntryType { get; set; }
            public string AmountText { get; set; } public string Description { get; set; } public string Reference { get; set; }
            public string OccurredOn { get; set; } public string OperationKey { get; set; }
            public long CreatedByUserId { get; set; } public string CreatedUtc { get; set; }
        }
    }
}
