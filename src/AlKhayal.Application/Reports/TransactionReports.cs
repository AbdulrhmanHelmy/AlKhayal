using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Reports
{
    public class ReportQuery
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        // Set only by Application validation; never bound from HTTP.
        public string StartUtc { get; set; }
        public string EndUtc { get; set; }
    }

    public sealed class TransactionReportQuery : ReportQuery
    {
        public long? SupplierId { get; set; }
        public long? CategoryId { get; set; }
        public string Party { get; set; }
        public string Type { get; set; }
    }

    public sealed class ExpenseReportRow
    {
        public long Id { get; set; }
        public string ExpenseDate { get; set; }
        public string PostedUtc { get; set; }
        public string Category { get; set; }
        public string Payee { get; set; }
        public string Notes { get; set; }
        public string UserName { get; set; }
        public string Amount { get; set; }
    }

    public sealed class ExpenseReportCategory
    {
        public long CategoryId { get; set; }
        public string Category { get; set; }
        public long Count { get; set; }
        public string Total { get; set; }
    }

    public sealed class ExpenseReportResult
    {
        public long Count { get; set; }
        public string Total { get; set; }
        public IReadOnlyList<ExpenseReportRow> Items { get; set; }
        public IReadOnlyList<ExpenseReportCategory> Categories { get; set; }
    }

    public static class ReportPeriod
    {
        public static void Validate(ReportQuery q, TimeZoneInfo zone)
        {
            if (q == null) throw new ArgumentException("مرشحات التقرير غير صحيحة.");
            if (q.From == null && q.To == null)
            {
                var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
                q.From = new DateTime(today.Year, today.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                q.To = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            DateTime from, to;
            if (!DateTime.TryParseExact(q.From, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from) ||
                !DateTime.TryParseExact(q.To, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out to) ||
                from > to || to >= DateTime.MaxValue.Date || q.Page < 1 || q.Page > 1000000 || q.PageSize < 1 || q.PageSize > 100 ||
                (q.Search != null && q.Search.Length > 120))
                throw new ArgumentException("مرشحات التقرير غير صحيحة.");
            // Convert each boundary independently; include the first occurrence of a repeated
            // midnight, or the first valid instant when clocks skip midnight.
            q.StartUtc = BoundaryUtc(from, zone).ToString("o", CultureInfo.InvariantCulture);
            q.EndUtc = BoundaryUtc(to.AddDays(1), zone).ToString("o", CultureInfo.InvariantCulture);
        }
        private static DateTime BoundaryUtc(DateTime local, TimeZoneInfo zone)
        {
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            if (zone.IsAmbiguousTime(local))
            {
                var offsets = zone.GetAmbiguousTimeOffsets(local);
                return new DateTimeOffset(local, offsets[0] > offsets[1] ? offsets[0] : offsets[1]).UtcDateTime;
            }
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
    }

    public sealed class TransactionReportRow
    {
        public long Id { get; set; }
        public long InvoiceId { get; set; }
        public string Reference { get; set; }
        public string InvoiceNumber { get; set; }
        public string EventUtc { get; set; }
        public string Party { get; set; }
        public string Type { get; set; }
        public string Total { get; set; }
        public string Status { get; set; }
        public string PaymentMode { get; set; }
    }

    public sealed class InstrumentReportRow
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public bool IsActive { get; set; }
        public long InvoiceCount { get; set; }
        public string Total { get; set; }
    }

    public sealed class TransactionReportResult
    {
        public long Count { get; set; }
        public string Total { get; set; }
        public string Returns { get; set; }
        public string NetEvents { get; set; }
        public long SalesReturnCount { get; set; }
        public long PurchaseReturnCount { get; set; }
        public string SalesReturns { get; set; }
        public string PurchaseReturns { get; set; }
        public string Cash { get; set; }
        public string Wallet { get; set; }
        public string Card { get; set; }
        public string Credit { get; set; }
        public string Unclassified { get; set; }
        public IReadOnlyList<TransactionReportRow> Items { get; set; } = new List<TransactionReportRow>();
        public IReadOnlyList<InstrumentReportRow> Instruments { get; set; } = new List<InstrumentReportRow>();
    }

    public interface ITransactionReportStore
    {
        Task<TransactionReportResult> PurchasesAsync(TransactionReportQuery query, CancellationToken ct);
        Task<TransactionReportResult> ReturnsAsync(TransactionReportQuery query, CancellationToken ct);
        Task<TransactionReportResult> PaymentsAsync(TransactionReportQuery query, CancellationToken ct);
        Task<ExpenseReportResult> ExpensesAsync(TransactionReportQuery query, CancellationToken ct);
    }

    public sealed class TransactionReportService
    {
        private readonly ITransactionReportStore _store;
        private readonly TimeZoneInfo _zone;
        public TransactionReportService(ITransactionReportStore store, TimeZoneInfo zone = null) { _store = store; _zone = zone ?? TimeZoneInfo.Local; }
        private void Validate(TransactionReportQuery q)
        {
            ReportPeriod.Validate(q, _zone);
            if (q.SupplierId <= 0 || (q.Party != null && q.Party.Length > 120)) throw new ArgumentException("مرشحات التقرير غير صحيحة.");
        }
        public Task<TransactionReportResult> PurchasesAsync(TransactionReportQuery q, CancellationToken ct)
        { Validate(q); return _store.PurchasesAsync(q, ct); }
        public Task<TransactionReportResult> ReturnsAsync(TransactionReportQuery q, CancellationToken ct)
        { Validate(q); if (q.Type != null && q.Type != "sales" && q.Type != "purchase") throw new ArgumentException("نوع المرتجع غير صحيح."); return _store.ReturnsAsync(q, ct); }
        public Task<TransactionReportResult> PaymentsAsync(TransactionReportQuery q, CancellationToken ct)
        { Validate(q); if (q.Type != null && q.Type != "wallet" && q.Type != "card") throw new ArgumentException("نوع الوسيلة غير صحيح."); return _store.PaymentsAsync(q, ct); }
        public Task<ExpenseReportResult> ExpensesAsync(TransactionReportQuery q, CancellationToken ct)
        { Validate(q); if (q.CategoryId <= 0) throw new ArgumentException("نوع المصروف غير صحيح."); return _store.ExpensesAsync(q, ct); }
    }
}
