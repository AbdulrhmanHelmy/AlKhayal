using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SQLite;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Treasury;
using AlKhayal.Data.Treasury;
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore
    {
        private static async Task DepositCashSaleAsync(SQLiteConnection c, SQLiteTransaction tx, SaleInvoice invoice, string key, string hash, long userId, CancellationToken ct)
        {
            // A fully discounted sale has no cash receipt and creates no treasury/account row.
            if (invoice.TotalScaled == 0) return;
            var accountId = await EnsureShiftAccountAsync(c, tx, userId, ct).ConfigureAwait(false);
            var receipt = await SqliteTreasuryStore.PostWithinTransactionAsync(c, tx, new TreasuryMovementCommand {
                AccountId = accountId, OperationKey = "sale-revenue:" + key, Notes = "إيراد فاتورة بيع " + invoice.InvoiceNumber
            }, "deposit", invoice.TotalScaled, hash, userId, ct).ConfigureAwait(false);
            if (receipt.Replayed) throw new TreasuryException("operation_conflict", "رقم العملية مرتبط بحركة مالية أخرى.");
            await c.ExecuteAsync(Cmd("INSERT INTO SalesTreasuryReceipts(SalesInvoiceId,TreasuryAccountId,TreasuryMovementId,CreatedUtc) VALUES(@Id,@accountId,@movementId,@now)",
                new { invoice.Id, accountId, movementId = receipt.Id, now = Now() }, tx, ct)).ConfigureAwait(false);
        }
        internal static async Task<long> EnsureShiftAccountAsync(SQLiteConnection c, SQLiteTransaction tx, long userId, CancellationToken ct)
        {
            var accountId = await c.ExecuteScalarAsync<long?>(Cmd("SELECT AccountId FROM ShiftRevenueTreasury WHERE Id=1", null, tx, ct)).ConfigureAwait(false);
            if (!accountId.HasValue)
            {
                var rows = (await c.QueryAsync<RevenueAccount>(Cmd("SELECT Id,Name,Type,IsDeleted,NormalizedCode FROM TreasuryAccounts", null, tx, ct)).ConfigureAwait(false)).ToList();
                var named = rows.Where(x => x.IsDeleted == 0 && ShiftRevenuePolicy.MatchesName(x.Name)).ToList();
                if (named.Count > 1 || named.Any(x => x.Type != "cash"))
                    throw new TreasuryException("shift_account_conflict", "يوجد أكثر من حساب باسم إيراد الوردية أو حساب بنكي بهذا الاسم. راجع قائمة الخزنة والبنك.");
                var now = Now();
                if (named.Count == 1) accountId = named[0].Id;
                else
                {
                    if (rows.Any(x => x.NormalizedCode == ShiftRevenuePolicy.AccountCode))
                        throw new TreasuryException("shift_account_conflict", "كود خزنة إيراد الوردية مستخدم بحساب آخر. راجع قائمة الخزنة والبنك.");
                    accountId = await c.ExecuteScalarAsync<long>(Cmd(@"INSERT INTO TreasuryAccounts(Code,NormalizedCode,Name,Type,Notes,CreatedUtc,UpdatedUtc)
                        VALUES(@Code,@Code,@Name,'cash','إيراد المبيعات النقدية',@now,@now); SELECT last_insert_rowid();",
                        new { Code = ShiftRevenuePolicy.AccountCode, Name = ShiftRevenuePolicy.AccountName, now }, tx, ct)).ConfigureAwait(false);
                }
                await c.ExecuteAsync(Cmd("INSERT INTO ShiftRevenueTreasury(Id,AccountId,CreatedUtc) VALUES(1,@accountId,@now)", new { accountId, now }, tx, ct)).ConfigureAwait(false);
                await c.ExecuteAsync(Cmd("INSERT INTO TreasuryAudit(AccountId,MovementId,Action,Detail,UserId,CreatedUtc) VALUES(@accountId,NULL,'shift_revenue_link',@detail,@userId,@now)",
                    new { accountId, detail = "ربط خزنة إيراد الوردية بالمبيعات النقدية", userId, now }, tx, ct)).ConfigureAwait(false);
            }
            var account = await c.QuerySingleOrDefaultAsync<RevenueAccount>(Cmd("SELECT Id,Name,Type,IsDeleted FROM TreasuryAccounts WHERE Id=@accountId", new { accountId }, tx, ct)).ConfigureAwait(false);
            if (account == null || account.IsDeleted != 0 || account.Type != "cash")
                throw new TreasuryException("shift_account_unavailable", "خزنة إيراد الوردية غير متاحة. راجع قائمة الخزنة والبنك.");
            return accountId.Value;
        }
        private sealed class RevenueAccount
        {
            public long Id { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public long IsDeleted { get; set; }
            public string NormalizedCode { get; set; }
        }
    }
}
