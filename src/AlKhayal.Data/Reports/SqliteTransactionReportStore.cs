using System;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Reports;
using Dapper;

namespace AlKhayal.Data.Reports
{
    public sealed class SqliteTransactionReportStore : ITransactionReportStore
    {
        private readonly string _cs;
        public SqliteTransactionReportStore(string connectionString) { _cs = connectionString; }
        private static string Money(decimal scaled) => (scaled / 10000m).ToString("0.0000", CultureInfo.InvariantCulture);
        private static object Args(TransactionReportQuery q) => new {
            q.StartUtc, q.EndUtc, q.SupplierId, q.CategoryId, q.Type,
            Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(), Like = "%" + (q.Search ?? "").Trim() + "%",
            Party = string.IsNullOrWhiteSpace(q.Party) ? null : q.Party.Trim(), PartyLike = "%" + (q.Party ?? "").Trim() + "%",
            Limit = q.PageSize, Offset = (q.Page - 1) * q.PageSize
        };
        private const string ExpenseSource = @" FROM ExpenseVouchers v JOIN Users u ON u.Id=v.UserId
            WHERE v.CreatedUtc>=@StartUtc AND v.CreatedUtc<@EndUtc
            AND (@CategoryId IS NULL OR v.CategoryId=@CategoryId)
            AND (@Search IS NULL OR v.CategoryName LIKE @Like OR v.Payee LIKE @Like OR v.Notes LIKE @Like)";
        public async Task<ExpenseReportResult> ExpensesAsync(TransactionReportQuery q, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction())
                {
                    var args = Args(q);
                    var total = await c.QuerySingleAsync<Totals>(new CommandDefinition("SELECT COUNT(*) Count,COALESCE(SUM(v.AmountScaled),0) TotalScaled" + ExpenseSource, args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    var rows = (await c.QueryAsync<ExpenseRow>(new CommandDefinition("SELECT v.Id,v.ExpenseDate,v.CreatedUtc PostedUtc,v.CategoryName Category,v.Payee,v.Notes,u.Username UserName,v.AmountScaled" + ExpenseSource + " ORDER BY v.CreatedUtc DESC,v.Id DESC LIMIT @Limit OFFSET @Offset", args, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    var categories = (await c.QueryAsync<ExpenseCategoryRow>(new CommandDefinition("SELECT v.CategoryId,v.CategoryName Category,COUNT(*) Count,SUM(v.AmountScaled) TotalScaled" + ExpenseSource + " GROUP BY v.CategoryId,v.CategoryName ORDER BY TotalScaled DESC,v.CategoryId LIMIT 100", args, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    tx.Commit();
                    return new ExpenseReportResult { Count=total.Count, Total=Money(total.TotalScaled), Items=rows.Select(x => new ExpenseReportRow { Id=x.Id, ExpenseDate=x.ExpenseDate, PostedUtc=x.PostedUtc, Category=x.Category, Payee=x.Payee, Notes=x.Notes, UserName=x.UserName, Amount=Money(x.AmountScaled) }).ToList(), Categories=categories.Select(x => new ExpenseReportCategory { CategoryId=x.CategoryId, Category=x.Category, Count=x.Count, Total=Money(x.TotalScaled) }).ToList() };
                }
            }
        }
        private sealed class ExpenseRow { public long Id { get; set; } public string ExpenseDate { get; set; } public string PostedUtc { get; set; } public string Category { get; set; } public string Payee { get; set; } public string Notes { get; set; } public string UserName { get; set; } public long AmountScaled { get; set; } }
        private sealed class ExpenseCategoryRow { public long CategoryId { get; set; } public string Category { get; set; } public long Count { get; set; } public long TotalScaled { get; set; } }
        private const string Purchases = @" FROM PurchaseInvoices i JOIN Suppliers s ON s.Id=i.SupplierId
            WHERE i.Status='posted' AND (@SupplierId IS NULL OR i.SupplierId=@SupplierId)
            AND (@Search IS NULL OR i.InvoiceNumber LIKE @Like) AND (@Party IS NULL OR s.Name LIKE @PartyLike)";
        public async Task<TransactionReportResult> PurchasesAsync(TransactionReportQuery q, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction())
                {
                    var args = Args(q);
                    var period = Purchases + " AND i.PostedUtc>=@StartUtc AND i.PostedUtc<@EndUtc";
                    var totals = await c.QuerySingleAsync<Totals>(new CommandDefinition("SELECT COUNT(*) Count,COALESCE(SUM(i.TotalScaled),0) TotalScaled" + period, args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    var returns = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COALESCE(SUM(r.TotalScaled),0) FROM PurchaseReturns r JOIN (SELECT i.Id" + Purchases + ") f ON f.Id=r.PurchaseInvoiceId WHERE r.Status='posted' AND r.PostedUtc>=@StartUtc AND r.PostedUtc<@EndUtc", args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    var rows = (await c.QueryAsync<Row>(new CommandDefinition("SELECT i.Id,i.Id InvoiceId,i.InvoiceNumber Reference,i.InvoiceNumber,i.PostedUtc EventUtc,s.Name Party,i.TotalScaled,i.Status,i.PaymentMode" + period + " ORDER BY i.PostedUtc DESC,i.Id DESC LIMIT @Limit OFFSET @Offset", args, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    tx.Commit();
                    return new TransactionReportResult { Count = totals.Count, Total = Money(totals.TotalScaled), Returns = Money(returns), NetEvents = Money(totals.TotalScaled - (decimal)returns), Items = rows.Select(Map).ToList() };
                }
            }
        }

        // One row per successful return header, never joined to return lines or cost allocations.
        // Purchases have no separate saved return number: display the persisted return ID with its type.
        private const string ReturnSource = @" FROM (
            SELECT r.Id,r.SalesInvoiceId InvoiceId,r.ReturnNumber Reference,i.InvoiceNumber,r.CreatedUtc EventUtc,
                COALESCE(c.Name,'') Party,'sales' Type,r.TotalScaled,'posted' Status
            FROM SalesReturns r JOIN SalesInvoices i ON i.Id=r.SalesInvoiceId LEFT JOIN Customers c ON c.Id=i.CustomerId
            UNION ALL
            SELECT r.Id,r.PurchaseInvoiceId InvoiceId,CAST(r.Id AS TEXT) Reference,i.InvoiceNumber,r.PostedUtc EventUtc,
                s.Name Party,'purchase' Type,r.TotalScaled,r.Status
            FROM PurchaseReturns r JOIN PurchaseInvoices i ON i.Id=r.PurchaseInvoiceId JOIN Suppliers s ON s.Id=r.SupplierId WHERE r.Status='posted'
            ) h WHERE EventUtc>=@StartUtc AND EventUtc<@EndUtc AND (@Type IS NULL OR Type=@Type)
            AND (@Search IS NULL OR Reference LIKE @Like OR InvoiceNumber LIKE @Like) AND (@Party IS NULL OR Party LIKE @PartyLike)";
        public async Task<TransactionReportResult> ReturnsAsync(TransactionReportQuery q, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction())
                {
                    var args = Args(q);
                    var totals = await c.QuerySingleAsync<Totals>(new CommandDefinition(@"SELECT COUNT(*) Count,
                        COALESCE(SUM(CASE WHEN Type='sales' THEN 1 ELSE 0 END),0) SalesReturnCount,
                        COALESCE(SUM(CASE WHEN Type='purchase' THEN 1 ELSE 0 END),0) PurchaseReturnCount,
                        COALESCE(SUM(CASE WHEN Type='sales' THEN TotalScaled ELSE 0 END),0) SalesReturnsScaled,
                        COALESCE(SUM(CASE WHEN Type='purchase' THEN TotalScaled ELSE 0 END),0) PurchaseReturnsScaled" + ReturnSource, args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    var rows = (await c.QueryAsync<Row>(new CommandDefinition("SELECT *" + ReturnSource + " ORDER BY EventUtc DESC,Type,Id DESC LIMIT @Limit OFFSET @Offset", args, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    tx.Commit();
                    return new TransactionReportResult { Count = totals.Count, SalesReturnCount = totals.SalesReturnCount, PurchaseReturnCount = totals.PurchaseReturnCount, SalesReturns = Money(totals.SalesReturnsScaled), PurchaseReturns = Money(totals.PurchaseReturnsScaled), Items = rows.Select(Map).ToList() };
                }
            }
        }

        // Immediate payment events use payment time. Credit is a separate posted invoice value.
        private const string PaymentSource = @" FROM SalesPayments p JOIN SalesInvoices i ON i.Id=p.SalesInvoiceId
            LEFT JOIN SalePaymentSelections s ON s.SalesInvoiceId=i.Id
            WHERE i.Status='posted' AND i.PaymentMode<>'credit' AND p.EventType='cash_sale'
              AND p.CreatedUtc>=@StartUtc AND p.CreatedUtc<@EndUtc";
        private const string PaymentMethod = "CASE WHEN s.Method IN ('cash','wallet','card') THEN s.Method WHEN p.PaymentMethod IN ('cash','card') THEN p.PaymentMethod ELSE 'unclassified' END";
        public async Task<TransactionReportResult> PaymentsAsync(TransactionReportQuery q, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction())
                {
                    var args = Args(q);
                    // Directory filters also scope electronic totals. With filters, unassigned/cash/credit
                    // cannot match a configured electronic instrument and are therefore zero.
                    var instrumentFilter = " FROM PaymentInstruments pi WHERE (@Type IS NULL OR pi.Type=@Type) AND (@Search IS NULL OR pi.Name LIKE @Like OR pi.Number LIKE @Like OR pi.Provider LIKE @Like)";
                    var paymentFilter = PaymentSource + " AND ((@Type IS NULL AND @Search IS NULL) OR s.InstrumentId IN (SELECT pi.Id" + instrumentFilter + "))";
                    var totals = await c.QuerySingleAsync<Totals>(new CommandDefinition("SELECT " + string.Join(",", new[] { "cash", "wallet", "card", "unclassified" }.Select(m => "COALESCE(SUM(CASE WHEN " + PaymentMethod + "='" + m + "' THEN p.AmountScaled ELSE 0 END),0) " + m + "Scaled")) + paymentFilter, args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    var credit = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COALESCE(SUM(TotalScaled),0) FROM SalesInvoices WHERE Status='posted' AND PaymentMode='credit' AND PostedUtc>=@StartUtc AND PostedUtc<@EndUtc AND @Type IS NULL AND @Search IS NULL", args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    var count = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*)" + instrumentFilter, args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    // Aggregate payments before joining the directory; zero-activity and inactive entries survive.
                    var instruments = (await c.QueryAsync<InstrumentRow>(new CommandDefinition(@"SELECT pi.Id,pi.Type,pi.Name,pi.Number,pi.IsActive,COALESCE(a.InvoiceCount,0) InvoiceCount,COALESCE(a.TotalScaled,0) TotalScaled
                        FROM (SELECT pi.*" + instrumentFilter + @" ORDER BY pi.Name,pi.Id LIMIT @Limit OFFSET @Offset) pi
                        LEFT JOIN (SELECT s.InstrumentId,COUNT(DISTINCT i.Id) InvoiceCount,SUM(p.AmountScaled) TotalScaled" + PaymentSource + @" AND s.InstrumentId IS NOT NULL GROUP BY s.InstrumentId) a ON a.InstrumentId=pi.Id ORDER BY pi.Name,pi.Id", args, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    tx.Commit();
                    return new TransactionReportResult { Count = count, Cash = Money(totals.CashScaled), Wallet = Money(totals.WalletScaled), Card = Money(totals.CardScaled), Credit = Money(credit), Unclassified = Money(totals.UnclassifiedScaled), Instruments = instruments.Select(x => new InstrumentReportRow { Id = x.Id, Type = x.Type, Name = x.Name, Number = x.Number, IsActive = x.IsActive, InvoiceCount = x.InvoiceCount, Total = Money(x.TotalScaled) }).ToList() };
                }
            }
        }
        private static TransactionReportRow Map(Row x) => new TransactionReportRow { Id = x.Id, InvoiceId = x.InvoiceId, Reference = x.Reference, InvoiceNumber = x.InvoiceNumber, EventUtc = x.EventUtc, Party = x.Party, Type = x.Type, Total = Money(x.TotalScaled), Status = x.Status, PaymentMode = x.PaymentMode };
        private sealed class Totals
        {
            public long Count { get; set; } public long TotalScaled { get; set; }
            public long SalesReturnCount { get; set; } public long PurchaseReturnCount { get; set; }
            public long SalesReturnsScaled { get; set; } public long PurchaseReturnsScaled { get; set; }
            public long CashScaled { get; set; } public long WalletScaled { get; set; } public long CardScaled { get; set; } public long UnclassifiedScaled { get; set; }
        }
        private sealed class Row
        {
            public long Id { get; set; } public long InvoiceId { get; set; } public string Reference { get; set; }
            public string InvoiceNumber { get; set; } public string EventUtc { get; set; } public string Party { get; set; }
            public string Type { get; set; } public long TotalScaled { get; set; } public string Status { get; set; } public string PaymentMode { get; set; }
        }
        private sealed class InstrumentRow
        {
            public long Id { get; set; } public string Type { get; set; } public string Name { get; set; }
            public string Number { get; set; } public bool IsActive { get; set; } public long InvoiceCount { get; set; } public long TotalScaled { get; set; }
        }
    }
}
