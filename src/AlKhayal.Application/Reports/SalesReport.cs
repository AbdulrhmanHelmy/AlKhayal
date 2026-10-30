using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Reports
{
    public sealed class SalesReportQuery : ReportQuery
    {
        public string Method { get; set; }
        public long? CashierId { get; set; }
    }

    public sealed class SalesReportRow
    {
        public long Id { get; set; }
        public string InvoiceNumber { get; set; }
        public string InvoiceDate { get; set; }
        public string PostedUtc { get; set; }
        public string CustomerName { get; set; }
        public string CashierName { get; set; }
        public string Method { get; set; }
        public string Total { get; set; }
        public string Status { get; set; }
    }

    public sealed class SalesReportResult
    {
        public long Count { get; set; }
        public string Total { get; set; }
        public string Cash { get; set; }
        public string Wallet { get; set; }
        public string Card { get; set; }
        public string Credit { get; set; }
        public string Unclassified { get; set; }
        public string Returns { get; set; }
        public string Cancellations { get; set; }
        public string NetEvents { get; set; }
        public IReadOnlyList<SalesReportRow> Items { get; set; }
    }

    public interface ISalesReportStore
    {
        Task<SalesReportResult> QueryAsync(SalesReportQuery query, CancellationToken ct);
    }

    public sealed class SalesReportService
    {
        private readonly ISalesReportStore _store;
        private readonly TimeZoneInfo _zone;
        public SalesReportService(ISalesReportStore store, TimeZoneInfo zone = null) { _store = store; _zone = zone ?? TimeZoneInfo.Local; }
        public Task<SalesReportResult> QueryAsync(SalesReportQuery q, CancellationToken ct)
        {
            ReportPeriod.Validate(q, _zone);
            if (q.CashierId <= 0 ||
                (q.Method != null && q.Method != "cash" && q.Method != "wallet" && q.Method != "card" && q.Method != "credit" && q.Method != "unclassified"))
                throw new ArgumentException("مرشحات التقرير غير صحيحة.");
            return _store.QueryAsync(q, ct);
        }
    }
}
