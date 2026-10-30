using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Payments;
using Dapper;

namespace AlKhayal.Data.Payments
{
    public sealed class SqlitePaymentInstrumentStore : IPaymentInstrumentStore
    {
        private readonly string _cs;
        public SqlitePaymentInstrumentStore(string connectionString) { _cs = connectionString; }
        private async Task<SQLiteConnection> OpenAsync(CancellationToken ct) { var c = new SQLiteConnection(_cs); try { await c.OpenAsync(ct).ConfigureAwait(false); await c.ExecuteAsync(new CommandDefinition("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;",cancellationToken:ct)).ConfigureAwait(false); return c; } catch { c.Dispose(); throw; } }
        public async Task<IReadOnlyList<PaymentInstrument>> ListAsync(string type, bool activeOnly, CancellationToken ct)
        { using (var c = await OpenAsync(ct).ConfigureAwait(false)) return (await c.QueryAsync<PaymentInstrument>(new CommandDefinition("SELECT * FROM PaymentInstruments WHERE (@type IS NULL OR Type=@type) AND (@activeOnly=0 OR IsActive=1) ORDER BY Name,Id",new{type,activeOnly},cancellationToken:ct)).ConfigureAwait(false)).ToList(); }
        public async Task<PaymentInstrument> SaveAsync(PaymentInstrument value, string identity, long actor, CancellationToken ct)
        {
            try {
                using (var c = await OpenAsync(ct).ConfigureAwait(false)) using (var tx = c.BeginTransaction()) {
                    var now = DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);
                    var args = new { value.Id,value.Type,value.Name,value.Number,value.Provider,value.Notes,value.IsActive,value.Version,identity,actor,now };
                    long id;
                    if (value.Id == 0) id = await c.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO PaymentInstruments(Type,Name,Number,Provider,Notes,IsActive,IdentityKey,CreatedUtc,UpdatedUtc) VALUES(@Type,@Name,@Number,@Provider,@Notes,@IsActive,@identity,@now,@now); SELECT last_insert_rowid();",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    else {
                        var changed = await c.ExecuteAsync(new CommandDefinition("UPDATE PaymentInstruments SET Name=@Name,Number=@Number,Provider=@Provider,Notes=@Notes,IsActive=@IsActive,IdentityKey=@identity,UpdatedUtc=@now,Version=Version+1 WHERE Id=@Id AND Type=@Type AND Version=@Version",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                        if (changed != 1) throw new InvalidOperationException("تغيرت الوسيلة أو لم تعد موجودة. حدّث القائمة وأعد المحاولة."); id=value.Id;
                    }
                    await c.ExecuteAsync(new CommandDefinition("INSERT INTO PaymentInstrumentAudit(InstrumentId,ActorUserId,Version,Name,Number,Provider,Notes,IsActive,CreatedUtc) SELECT Id,@actor,Version,Name,Number,Provider,Notes,IsActive,@now FROM PaymentInstruments WHERE Id=@id",new{id,actor,now},tx,cancellationToken:ct)).ConfigureAwait(false);
                    var saved=await c.QuerySingleAsync<PaymentInstrument>(new CommandDefinition("SELECT * FROM PaymentInstruments WHERE Id=@id",new{id},tx,cancellationToken:ct)).ConfigureAwait(false); tx.Commit(); return saved;
                }
            } catch (SQLiteException ex) when (ex.ResultCode==SQLiteErrorCode.Constraint) { throw new ArgumentException("رقم الوسيلة مستخدم بالفعل لدى مقدم الخدمة، أو تعذر حفظ بياناتها.",ex); }
        }
        // Authoritative existing payments, including legacy unassigned records. No second ledger.
        private const string History = @"SELECT p.Id PaymentId,i.Id InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.TotalScaled,p.AmountScaled,
            CASE WHEN s.Method IN ('wallet','card') AND p.EventType='cash_sale' THEN s.Method ELSE p.PaymentMethod END Method,
            CASE WHEN p.EventType='cash_sale' THEN s.InstrumentId END InstrumentId,
            CASE WHEN p.EventType='cash_sale' THEN pi.Name END InstrumentName,u.DisplayName CashierName,i.CashierUserId,
            CASE WHEN i.PaidScaled>=i.TotalScaled THEN 'paid' WHEN i.PaidScaled>0 THEN 'partial' ELSE 'unpaid' END PaymentStatus,
            CASE WHEN EXISTS(SELECT 1 FROM SalesCancellations sc WHERE sc.SalesInvoiceId=i.Id) THEN 'canceled'
            WHEN EXISTS(SELECT 1 FROM SalesReturns r WHERE r.SalesInvoiceId=i.Id) THEN 'returned' ELSE i.Status END Status
            FROM SalesPayments p JOIN SalesInvoices i ON i.Id=p.SalesInvoiceId JOIN Users u ON u.Id=i.CashierUserId
            LEFT JOIN SalePaymentSelections s ON s.SalesInvoiceId=i.Id LEFT JOIN PaymentInstruments pi ON pi.Id=s.InstrumentId
            WHERE i.Status='posted' AND p.EventType IN ('cash_sale','customer_collection')";
        public async Task<PaymentHistoryPage> HistoryAsync(PaymentHistoryQuery q, CancellationToken ct)
        {
            var source=History+(q.InstrumentId.HasValue?" AND s.InstrumentId=@InstrumentId AND p.EventType='cash_sale'":"");
            var filter=" FROM ("+source+") h WHERE (@InstrumentId IS NULL OR InstrumentId=@InstrumentId) AND (@CashierId IS NULL OR CashierUserId=@CashierId) AND (@CashierName IS NULL OR CashierName LIKE @CashierSearch) AND (@From IS NULL OR InvoiceDate>=@From) AND (@To IS NULL OR InvoiceDate<=@To) AND (@InvoiceNumber IS NULL OR InvoiceNumber LIKE @Search) AND (@Status IS NULL OR Status=@Status)";
            var args=new{q.InstrumentId,q.CashierId,q.CashierName,CashierSearch="%"+(q.CashierName??"")+"%",q.From,q.To,q.InvoiceNumber,Search="%"+(q.InvoiceNumber??"")+"%",q.Status,Limit=q.PageSize,Offset=(q.Page-1)*q.PageSize};
            using(var c=await OpenAsync(ct).ConfigureAwait(false)) using(var tx=c.BeginTransaction()) {
                var result=await c.QuerySingleAsync<PaymentHistoryPage>(new CommandDefinition("SELECT COUNT(*) Count,COUNT(DISTINCT InvoiceId) InvoiceCount,COALESCE(SUM(AmountScaled),0) TotalScaled"+filter,args,tx,cancellationToken:ct)).ConfigureAwait(false);
                result.Items=(await c.QueryAsync<PaymentHistoryRow>(new CommandDefinition("SELECT *"+filter+" ORDER BY InvoiceDate DESC,PaymentId DESC LIMIT @Limit OFFSET @Offset",args,tx,cancellationToken:ct)).ConfigureAwait(false)).ToList();tx.Commit();return result;
            }
        }
        public async Task<PaymentOverview> OverviewAsync(CancellationToken ct)
        {
            using(var c=await OpenAsync(ct).ConfigureAwait(false)) return await c.QuerySingleAsync<PaymentOverview>(new CommandDefinition(@"SELECT
            (SELECT COUNT(*) FROM PaymentInstruments WHERE Type='wallet' AND IsActive=1) ActiveWallets,
            (SELECT COUNT(*) FROM PaymentInstruments WHERE Type='card' AND IsActive=1) ActiveTerminals,
            COALESCE(SUM(CASE WHEN Method='cash' THEN AmountScaled ELSE 0 END),0) CashScaled,
            COALESCE(SUM(CASE WHEN Method='wallet' THEN AmountScaled ELSE 0 END),0) WalletScaled,
            COALESCE(SUM(CASE WHEN Method='card' THEN AmountScaled ELSE 0 END),0) CardScaled FROM ("+History+") h",cancellationToken:ct)).ConfigureAwait(false);
        }
    }
}
