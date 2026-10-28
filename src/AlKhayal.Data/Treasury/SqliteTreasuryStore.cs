using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Treasury;
using Dapper;

namespace AlKhayal.Data.Treasury
{
    public sealed class SqliteTreasuryStore : ITreasuryStore
    {
        private readonly string _connectionString;
        private const string AccountsSql = @"SELECT a.Id,a.Code,a.Name,a.Type,a.Notes,a.Version,
            COALESCE((SELECT SUM(CASE WHEN m.Type='deposit' THEN m.AmountScaled ELSE -m.AmountScaled END)
            FROM TreasuryMovements m WHERE m.AccountId=a.Id),0) AS BalanceScaled
            FROM TreasuryAccounts a WHERE a.IsDeleted=0";
        public SqliteTreasuryStore(string connectionString) { _connectionString = connectionString; }
        public async Task<IReadOnlyList<TreasuryAccount>> ListAsync(CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                return (await c.QueryAsync<TreasuryAccount>(new CommandDefinition(AccountsSql + " ORDER BY a.Id", cancellationToken: ct)).ConfigureAwait(false)).ToList();
            }
        }
        public async Task<TreasuryAccount> SaveAsync(long? id, TreasuryAccountCommand command, string normalizedCode, long userId, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction())
                {
                    var existing = id.HasValue ? await Account(c, tx, id.Value, ct).ConfigureAwait(false) : null;
                    if (id.HasValue)
                    {
                        TreasuryRules.EnsureEditable(existing, command.Version);
                        TreasuryRules.EnsureTypeChange(existing, command.Type, await HasMovements(c, tx, id.Value, ct).ConfigureAwait(false));
                    }
                    var duplicate = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM TreasuryAccounts WHERE NormalizedCode=@normalizedCode AND (@id IS NULL OR Id<>@id)", new { normalizedCode, id }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (duplicate != 0) throw new TreasuryException("duplicate_code", "كود الخزنة أو البنك مستخدم بالفعل.");
                    var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    var args = new { id, command.Code, normalizedCode, command.Name, command.Type, command.Notes, now };
                    long accountId;
                    if (!id.HasValue)
                        accountId = await c.ExecuteScalarAsync<long>(new CommandDefinition(@"INSERT INTO TreasuryAccounts(Code,NormalizedCode,Name,Type,Notes,CreatedUtc,UpdatedUtc)
                            VALUES(@Code,@normalizedCode,@Name,@Type,@Notes,@now,@now); SELECT last_insert_rowid();", args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    else
                    {
                        accountId = id.Value;
                        await c.ExecuteAsync(new CommandDefinition("UPDATE TreasuryAccounts SET Code=@Code,NormalizedCode=@normalizedCode,Name=@Name,Type=@Type,Notes=@Notes,Version=Version+1,UpdatedUtc=@now WHERE Id=@id", args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    }
                    await Audit(c, tx, accountId, null, id.HasValue ? "edit" : "create", command.Code + " | " + command.Name + " | " + command.Type + " | " + command.Notes, userId, now, ct).ConfigureAwait(false);
                    var result = await Account(c, tx, accountId, ct).ConfigureAwait(false);
                    tx.Commit();
                    return result;
                }
            }
        }
        public async Task DeleteAsync(long id, long version, long userId, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction())
                {
                    var account = await Account(c, tx, id, ct).ConfigureAwait(false);
                    TreasuryRules.EnsureEditable(account, version);
                    TreasuryRules.EnsureDeletable(account, await HasMovements(c, tx, id, ct).ConfigureAwait(false));
                    var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    await c.ExecuteAsync(new CommandDefinition("UPDATE TreasuryAccounts SET IsDeleted=1,Version=Version+1,UpdatedUtc=@now WHERE Id=@id", new { id, now }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    await Audit(c, tx, id, null, "delete", account.Code + " | " + account.Name, userId, now, ct).ConfigureAwait(false);
                    tx.Commit();
                }
            }
        }
        public async Task<TreasuryMovement> PostAsync(TreasuryMovementCommand command, string type, long amount, string hash, long userId, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                // System.Data.SQLite's default transaction acquires the writer lock before reading balances.
                using (var tx = c.BeginTransaction())
                {
                    var result = await PostWithinTransactionAsync(c, tx, command, type, amount, hash, userId, ct).ConfigureAwait(false);
                    tx.Commit();
                    return result;
                }
            }
        }
        internal static async Task<TreasuryMovement> PostWithinTransactionAsync(SQLiteConnection c, SQLiteTransaction tx,
            TreasuryMovementCommand command, string type, long amount, string hash, long userId, CancellationToken ct, bool allowSalesRefundOverdraft = false)
        {
            var previous = await c.QuerySingleOrDefaultAsync<StoredMovement>(new CommandDefinition("SELECT * FROM TreasuryMovements WHERE OperationKey=@OperationKey", new { command.OperationKey }, tx, cancellationToken: ct)).ConfigureAwait(false);
            if (previous != null)
            {
                if (previous.RequestHash != hash) throw new TreasuryException("operation_conflict", "رقم العملية مستخدم ببيانات أخرى. ابدأ عملية جديدة.");
                return new TreasuryMovement { Id = previous.Id, AccountId = previous.AccountId, Type = previous.Type,
                    AmountScaled = previous.AmountScaled, BalanceAfterScaled = previous.BalanceAfterScaled,
                    Notes = previous.Notes, CreatedUtc = previous.CreatedUtc, Replayed = true };
            }
            var account = await Account(c, tx, command.AccountId, ct).ConfigureAwait(false);
            if (account == null) throw new TreasuryException("not_found", "الخزنة أو البنك غير موجود.");
            if (allowSalesRefundOverdraft && (type != "withdraw" || !command.OperationKey.StartsWith("sale-return:refund:", StringComparison.Ordinal) ||
                await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM ReturnTreasuryAccounts WHERE Purpose='sales' AND AccountId=@AccountId", new { command.AccountId }, tx, cancellationToken: ct)).ConfigureAwait(false) != 1))
                throw new TreasuryException("validation", "السحب بالسالب متاح لمرتجع البيع فقط.");
            var balance = TreasuryRules.NextBalance(account.BalanceScaled, amount, type, allowSalesRefundOverdraft);
            var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var movementId = await c.ExecuteScalarAsync<long>(new CommandDefinition(@"INSERT INTO TreasuryMovements(AccountId,Type,AmountScaled,BalanceAfterScaled,OperationKey,RequestHash,Notes,UserId,CreatedUtc)
                VALUES(@AccountId,@type,@amount,@balance,@OperationKey,@hash,@Notes,@userId,@now); SELECT last_insert_rowid();",
                new { command.AccountId, type, amount, balance, command.OperationKey, hash, command.Notes, userId, now }, tx, cancellationToken: ct)).ConfigureAwait(false);
            await c.ExecuteAsync(new CommandDefinition("UPDATE TreasuryAccounts SET Version=Version+1,UpdatedUtc=@now WHERE Id=@AccountId", new { command.AccountId, now }, tx, cancellationToken: ct)).ConfigureAwait(false);
            await Audit(c, tx, command.AccountId, movementId, type, command.Notes, userId, now, ct).ConfigureAwait(false);
            return new TreasuryMovement { Id = movementId, AccountId = command.AccountId, Type = type, AmountScaled = amount, BalanceAfterScaled = balance, Notes = command.Notes, CreatedUtc = now };
        }
        private static Task<TreasuryAccount> Account(SQLiteConnection c, SQLiteTransaction tx, long id, CancellationToken ct) =>
            c.QuerySingleOrDefaultAsync<TreasuryAccount>(new CommandDefinition(AccountsSql + " AND a.Id=@id", new { id }, tx, cancellationToken: ct));
        private static async Task<bool> HasMovements(SQLiteConnection c, SQLiteTransaction tx, long id, CancellationToken ct) =>
            await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM TreasuryMovements WHERE AccountId=@id)", new { id }, tx, cancellationToken: ct)).ConfigureAwait(false) != 0;
        private static Task Audit(SQLiteConnection c, SQLiteTransaction tx, long accountId, long? movementId, string action, string detail, long userId, string now, CancellationToken ct) =>
            c.ExecuteAsync(new CommandDefinition("INSERT INTO TreasuryAudit(AccountId,MovementId,Action,Detail,UserId,CreatedUtc) VALUES(@accountId,@movementId,@action,@detail,@userId,@now)", new { accountId, movementId, action, detail, userId, now }, tx, cancellationToken: ct));
        private sealed class StoredMovement : TreasuryMovement { public string RequestHash { get; set; } }
    }
}
