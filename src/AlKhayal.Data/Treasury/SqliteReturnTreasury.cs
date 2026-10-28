using System;
using System.Linq;
using System.Globalization;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Treasury;
using Dapper;

namespace AlKhayal.Data.Treasury
{
    internal static class SqliteReturnTreasury
    {
        internal static async Task PostAsync(SQLiteConnection c, SQLiteTransaction tx, bool sale, long returnId, long amount, string key, string hash, long userId, CancellationToken ct)
        {
            if (amount <= 0) return;
            var purpose = sale ? "sales" : "purchase"; var name = sale ? "مرتجعات البيع" : "مرتجعات الشراء"; var code = sale ? "SALES-RETURNS" : "PURCHASE-RETURNS";
            var accountId = await c.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT AccountId FROM ReturnTreasuryAccounts WHERE Purpose=@purpose", new { purpose }, tx, cancellationToken: ct)).ConfigureAwait(false);
            if (!accountId.HasValue)
            {
                var matches = (await c.QueryAsync<TreasuryAccount>(new CommandDefinition("SELECT Id,Name,Type FROM TreasuryAccounts WHERE IsDeleted=0 AND trim(Name)=@name", new { name }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                if (matches.Count > 1 || matches.Any(x => x.Type != "cash")) throw new TreasuryException("return_account_conflict", "راجع الحسابات المكررة أو نوع خزنة " + name + ".");
                var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                if (matches.Count == 1) accountId = matches[0].Id;
                else
                {
                    if (await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM TreasuryAccounts WHERE NormalizedCode=@code", new { code }, tx, cancellationToken: ct)).ConfigureAwait(false) != 0) throw new TreasuryException("return_account_conflict", "كود خزنة " + name + " مستخدم بالفعل.");
                    accountId = await c.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO TreasuryAccounts(Code,NormalizedCode,Name,Type,Notes,CreatedUtc,UpdatedUtc) VALUES(@code,@code,@name,'cash','خزنة المرتجعات',@now,@now); SELECT last_insert_rowid();", new { code, name, now }, tx, cancellationToken: ct)).ConfigureAwait(false);
                }
                await c.ExecuteAsync(new CommandDefinition("INSERT INTO ReturnTreasuryAccounts(Purpose,AccountId,CreatedUtc) VALUES(@purpose,@accountId,@now); INSERT INTO TreasuryAudit(AccountId,Action,Detail,UserId,CreatedUtc) VALUES(@accountId,'return_account_link',@name,@userId,@now)", new { purpose, accountId, now, name, userId }, tx, cancellationToken: ct)).ConfigureAwait(false);
            }
            var valid = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM TreasuryAccounts WHERE Id=@accountId AND IsDeleted=0 AND Type='cash'", new { accountId }, tx, cancellationToken: ct)).ConfigureAwait(false);
            if (valid != 1) throw new TreasuryException("return_account_unavailable", "خزنة " + name + " غير متاحة.");
            await Movement(c, tx, sale, returnId, accountId.Value, sale ? "withdraw" : "deposit", sale ? "refund" : "purchase_refund", amount, key, hash, userId, ct).ConfigureAwait(false);
        }
        private static async Task Movement(SQLiteConnection c, SQLiteTransaction tx, bool sale, long returnId, long accountId, string type, string role, long amount, string key, string hash, long userId, CancellationToken ct)
        {
            var movement = await SqliteTreasuryStore.PostWithinTransactionAsync(c, tx, new TreasuryMovementCommand { AccountId = accountId, OperationKey = (sale ? "sale-return:" : "purchase-return:") + role + ":" + key, Notes = (sale ? "مرتجع بيع " : "مرتجع شراء ") + returnId + " — " + role }, type, amount, hash, userId, ct, allowSalesRefundOverdraft: sale && role == "refund").ConfigureAwait(false);
            if (movement.Replayed) throw new TreasuryException("operation_conflict", "العملية مرتبطة بحركة مالية أخرى.");
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO ReturnTreasuryLinks(SalesReturnId,PurchaseReturnId,Role,MovementId) VALUES(@salesId,@purchaseId,@role,@movementId)", new { salesId = sale ? (long?)returnId : null, purchaseId = sale ? null : (long?)returnId, role, movementId = movement.Id }, tx, cancellationToken: ct)).ConfigureAwait(false);
        }
    }
}
