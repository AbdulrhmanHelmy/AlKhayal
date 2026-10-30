using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Reports
{
    public sealed class OperationalReportQuery : ReportQuery
    {
        public long? PersonId { get; set; }
        public string Status { get; set; }
        public string Today { get; set; }
    }
    public sealed class OperationalReportResult
    {
        public long Count { get; set; }
        public IDictionary<string,string> Summary { get; set; }
        public IReadOnlyList<IDictionary<string,object>> Items { get; set; }
    }
    public interface IOperationalReportStore
    {
        Task<OperationalReportResult> QueryAsync(string report, OperationalReportQuery query, CancellationToken ct);
    }
    public sealed class OperationalReportService
    {
        private readonly IOperationalReportStore _store;
        private readonly TimeZoneInfo _zone;
        public OperationalReportService(IOperationalReportStore store, TimeZoneInfo zone = null) { _store=store; _zone=zone ?? TimeZoneInfo.Local; }
        public static readonly string[] Reports = { "customers", "suppliers", "shifts", "attendance", "maintenance", "installments" };
        public static string Feature(string report)
        {
            return report=="shifts" ? "advanced-shifts" : report=="attendance" ? "employee-attendance" : report=="maintenance" ? "maintenance-management" : report=="installments" ? "installments" : null;
        }
        public Task<OperationalReportResult> QueryAsync(string report, OperationalReportQuery q, CancellationToken ct)
        {
            if(Array.IndexOf(Reports,report)<0) throw new ArgumentException("التقرير غير معروف.");
            ReportPeriod.Validate(q,_zone);
            string[] statuses = report=="customers" || report=="suppliers" ? new[]{"debt"} : report=="shifts" || report=="attendance" ? new[]{"open","closed"} : report=="installments" ? new[]{"active","completed","due","overdue"} : new[]{"تم الاستلام","قيد الفحص","قيد الإصلاح","بانتظار قطعة","جاهز للتسليم","تم التسليم"};
            if(q.PersonId<=0 || (q.Status!=null && Array.IndexOf(statuses,q.Status)<0)) throw new ArgumentException("مرشحات التقرير غير صحيحة.");
            q.Today=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,_zone).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            return _store.QueryAsync(report,q,ct);
        }
    }
}
