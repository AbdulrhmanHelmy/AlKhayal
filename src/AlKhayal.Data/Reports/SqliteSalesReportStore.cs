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
    public sealed class SqliteSalesReportStore : ISalesReportStore
    {
        private readonly string _connectionString;
        public SqliteSalesReportStore(string connectionString) { _connectionString = connectionString; }
        // An unassigned historical transfer is not a wallet. Later collections are never joined here.
        internal const string Source = @" FROM SalesInvoices i
            JOIN Users u ON u.Id=i.CashierUserId LEFT JOIN Customers c ON c.Id=i.CustomerId
            LEFT JOIN SalePaymentSelections s ON s.SalesInvoiceId=i.Id
            LEFT JOIN (SELECT SalesInvoiceId,CASE WHEN COUNT(*)=1 THEN MAX(PaymentMethod) ELSE NULL END PaymentMethod FROM SalesPayments WHERE EventType='cash_sale' GROUP BY SalesInvoiceId) p ON p.SalesInvoiceId=i.Id
            WHERE i.Status='posted' AND (@Search IS NULL OR i.InvoiceNumber LIKE @Like)
              AND (@CashierId IS NULL OR i.CashierUserId=@CashierId)
              AND (@Method IS NULL OR (CASE WHEN i.PaymentMode='credit' THEN 'credit'
                  WHEN s.Method IN ('cash','wallet','card') THEN s.Method
                  WHEN p.PaymentMethod IN ('cash','card') THEN p.PaymentMethod ELSE 'unclassified' END)=@Method)";
        internal const string Method = "CASE WHEN i.PaymentMode='credit' THEN 'credit' WHEN s.Method IN ('cash','wallet','card') THEN s.Method WHEN p.PaymentMethod IN ('cash','card') THEN p.PaymentMethod ELSE 'unclassified' END";
        private static string Money(long value) { return (value / 10000m).ToString("0.0000", CultureInfo.InvariantCulture); }
        public async Task<SalesReportResult> QueryAsync(SalesReportQuery q, CancellationToken ct)
        {
            var args = new { q.StartUtc, q.EndUtc, q.CashierId, q.Method, Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(), Like = "%" + (q.Search ?? "").Trim() + "%", Limit = q.PageSize, Offset = (q.Page - 1) * q.PageSize };
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction())
                {
                    var posting = Source + " AND i.PostedUtc>=@StartUtc AND i.PostedUtc<@EndUtc";
                    var summary = await c.QuerySingleAsync<Summary>(new CommandDefinition("SELECT COUNT(*) Count,COALESCE(SUM(i.TotalScaled),0) Total,COALESCE(SUM(CASE WHEN " + Method + "='cash' THEN i.TotalScaled ELSE 0 END),0) Cash,COALESCE(SUM(CASE WHEN " + Method + "='wallet' THEN i.TotalScaled ELSE 0 END),0) Wallet,COALESCE(SUM(CASE WHEN " + Method + "='card' THEN i.TotalScaled ELSE 0 END),0) Card,COALESCE(SUM(CASE WHEN " + Method + "='credit' THEN i.TotalScaled ELSE 0 END),0) Credit,COALESCE(SUM(CASE WHEN " + Method + "='unclassified' THEN i.TotalScaled ELSE 0 END),0) Unclassified" + posting, args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    var rows = (await c.QueryAsync<Row>(new CommandDefinition("SELECT i.Id,i.InvoiceNumber,i.InvoiceDate,i.PostedUtc,COALESCE(c.Name,'') CustomerName,u.DisplayName CashierName," + Method + " Method,i.TotalScaled,CASE WHEN EXISTS(SELECT 1 FROM SalesCancellations sc WHERE sc.SalesInvoiceId=i.Id) THEN 'canceled' ELSE 'posted' END Status" + posting + " ORDER BY i.PostedUtc DESC,i.Id DESC LIMIT @Limit OFFSET @Offset", args, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    var returns = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COALESCE(SUM(r.TotalScaled),0) FROM SalesReturns r JOIN (SELECT i.Id" + Source + ") f ON f.Id=r.SalesInvoiceId WHERE r.CreatedUtc>=@StartUtc AND r.CreatedUtc<@EndUtc", args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    var cancellations = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COALESCE(SUM(f.TotalScaled),0) FROM SalesCancellations sc JOIN (SELECT i.Id,i.TotalScaled" + Source + ") f ON f.Id=sc.SalesInvoiceId WHERE sc.CreatedUtc>=@StartUtc AND sc.CreatedUtc<@EndUtc", args, tx, cancellationToken: ct)).ConfigureAwait(false);
                    tx.Commit();
                    return new SalesReportResult { Count=summary.Count,Total=Money(summary.Total),Cash=Money(summary.Cash),Wallet=Money(summary.Wallet),Card=Money(summary.Card),Credit=Money(summary.Credit),Unclassified=Money(summary.Unclassified),Returns=Money(returns),Cancellations=Money(cancellations),NetEvents=((summary.Total-(decimal)returns-cancellations)/10000m).ToString("0.0000",CultureInfo.InvariantCulture),Items=rows.Select(x=>new SalesReportRow { Id=x.Id,InvoiceNumber=x.InvoiceNumber,InvoiceDate=x.InvoiceDate,PostedUtc=x.PostedUtc,CustomerName=x.CustomerName,CashierName=x.CashierName,Method=x.Method,Total=Money(x.TotalScaled),Status=x.Status }).ToList() };
                }
            }
        }
        private sealed class Summary { public long Count { get; set; } public long Total { get; set; } public long Cash { get; set; } public long Wallet { get; set; } public long Card { get; set; } public long Credit { get; set; } public long Unclassified { get; set; } }
        private sealed class Row { public long Id { get; set; } public string InvoiceNumber { get; set; } public string InvoiceDate { get; set; } public string PostedUtc { get; set; } public string CustomerName { get; set; } public string CashierName { get; set; } public string Method { get; set; } public long TotalScaled { get; set; } public string Status { get; set; } }
    }
}
