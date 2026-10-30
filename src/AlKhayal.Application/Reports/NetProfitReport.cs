using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Reports
{
    public sealed class NetProfitQuery : ReportQuery
    {
        public string Breakdown { get; set; } = "daily";
        public IReadOnlyList<ProfitPeriod> Periods { get; set; }
    }
    public sealed class ProfitPeriod
    {
        public string Period { get; set; }
        public string StartUtc { get; set; }
        public string EndUtc { get; set; }
    }
    public sealed class ProfitFigures
    {
        public string Period { get; set; }
        public string Revenue { get; set; }
        public string Cogs { get; set; }
        public string GrossProfit { get; set; }
        public string Expenses { get; set; }
        public string NetProfit { get; set; }
        public string NetMargin { get; set; }
        public string UnclassifiedAmount { get; set; }
        public long UnclassifiedCount { get; set; }
        public string ExcludedExpenses { get; set; }
        public long MissingRevenueCount { get; set; }
        public long MissingCostCount { get; set; }
        public string IncompleteGrossAmount { get; set; }
        public bool Complete { get; set; }
        public long EventCount { get; set; }
    }
    public sealed class ClassifiedExpenseRow
    {
        public long Id { get; set; }
        public string Category { get; set; }
        public string Payee { get; set; }
        public string ExpenseDate { get; set; }
        public string PostedUtc { get; set; }
        public string Amount { get; set; }
        public string Classification { get; set; }
        public long Revision { get; set; }
        public string Reason { get; set; }
        public string ClassifiedBy { get; set; }
        public string ClassifiedUtc { get; set; }
    }
    public sealed class NetProfitResult
    {
        public const string MetricScope = "صافي ربح المبيعات بعد المصروفات المسجلة";
        public string Scope => MetricScope;
        public ProfitFigures Summary { get; set; }
        public IReadOnlyList<ProfitFigures> Items { get; set; }
        public long ExpenseCount { get; set; }
        public IReadOnlyList<ClassifiedExpenseRow> ExpenseItems { get; set; }
        public bool CanClassify { get; set; }
    }
    public sealed class ExpenseClassificationCommand
    {
        public string Classification { get; set; }
        public string Reason { get; set; }
        public long ExpectedRevision { get; set; }
        public string OperationKey { get; set; }
    }
    public interface INetProfitStore
    {
        Task<NetProfitResult> QueryAsync(NetProfitQuery query, CancellationToken ct);
        Task ClassifyAsync(long voucherId, ExpenseClassificationCommand command, long actorId, CancellationToken ct);
    }
    public sealed class NetProfitService
    {
        private readonly INetProfitStore _store;
        private readonly TimeZoneInfo _zone;
        public NetProfitService(INetProfitStore store, TimeZoneInfo zone = null) { _store = store; _zone = zone ?? TimeZoneInfo.Local; }
        public Task<NetProfitResult> QueryAsync(NetProfitQuery q, CancellationToken ct)
        {
            ReportPeriod.Validate(q, _zone);
            if (q.Breakdown != "daily" && q.Breakdown != "monthly") throw new ArgumentException("اختر تفصيلًا يوميًا أو شهريًا.");
            var from = DateTime.ParseExact(q.From, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to = DateTime.ParseExact(q.To, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if ((to - from).TotalDays > 3659) throw new ArgumentException("اختر فترة لا تتجاوز 3660 يومًا.");
            var periods = new List<ProfitPeriod>();
            for (var day = from; day <= to;)
            {
                // Clip the last month before advancing, including December of year 9999.
                // ReportPeriod has already validated that the inclusive end can advance a day.
                var next = q.Breakdown == "daily" ? day.AddDays(1) :
                    day.Year == to.Year && day.Month == to.Month ? to.AddDays(1) :
                    new DateTime(day.Year, day.Month, 1).AddMonths(1);
                var last = next.AddDays(-1) > to ? to : next.AddDays(-1);
                var range = new ReportQuery { From = day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture), To = last.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) };
                ReportPeriod.Validate(range, _zone);
                periods.Add(new ProfitPeriod { Period = day.ToString(q.Breakdown == "daily" ? "yyyy-MM-dd" : "yyyy-MM",CultureInfo.InvariantCulture), StartUtc = range.StartUtc, EndUtc = range.EndUtc });
                day = next;
            }
            q.Periods = periods;
            return _store.QueryAsync(q, ct);
        }
        public Task ClassifyAsync(long voucherId, ExpenseClassificationCommand command, long actorId, CancellationToken ct)
        {
            if (voucherId <= 0 || actorId <= 0 || command == null || command.ExpectedRevision < 0 || command.ExpectedRevision == long.MaxValue ||
                (command.Classification != "included" && command.Classification != "excluded" && command.Classification != "unclassified") ||
                string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length < 2 || command.Reason.Trim().Length > 500 ||
                string.IsNullOrWhiteSpace(command.OperationKey) || command.OperationKey.Length > 100)
                throw new ArgumentException("اختر التصنيف وأدخل سببًا من حرفين إلى 500 حرف.");
            command.Reason = command.Reason.Trim();
            return _store.ClassifyAsync(voucherId, command, actorId, ct);
        }
    }
}
