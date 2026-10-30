using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Payments
{
    public sealed class PaymentInstrument
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public string Provider { get; set; }
        public string Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public long Version { get; set; }
        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }
    }
    public sealed class PaymentHistoryQuery
    {
        public long? InstrumentId { get; set; }
        public long? CashierId { get; set; }
        public string CashierName { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string InvoiceNumber { get; set; }
        public string Status { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
    public sealed class PaymentHistoryRow
    {
        public long InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string InvoiceDate { get; set; }
        public long TotalScaled { get; set; }
        public long AmountScaled { get; set; }
        public string Method { get; set; }
        public long? InstrumentId { get; set; }
        public string InstrumentName { get; set; }
        public string CashierName { get; set; }
        public string PaymentStatus { get; set; }
        public string Status { get; set; }
    }
    public sealed class PaymentHistoryPage
    {
        public long Count { get; set; }
        public long InvoiceCount { get; set; }
        public long TotalScaled { get; set; }
        public IReadOnlyList<PaymentHistoryRow> Items { get; set; }
    }
    public sealed class PaymentOverview
    {
        public long ActiveWallets { get; set; }
        public long ActiveTerminals { get; set; }
        public long CashScaled { get; set; }
        public long WalletScaled { get; set; }
        public long CardScaled { get; set; }
    }
    public interface IPaymentInstrumentStore
    {
        Task<IReadOnlyList<PaymentInstrument>> ListAsync(string type, bool activeOnly, CancellationToken ct);
        Task<PaymentInstrument> SaveAsync(PaymentInstrument instrument, string identity, long actor, CancellationToken ct);
        Task<PaymentHistoryPage> HistoryAsync(PaymentHistoryQuery query, CancellationToken ct);
        Task<PaymentOverview> OverviewAsync(CancellationToken ct);
    }
    public sealed class PaymentInstrumentService
    {
        private readonly IPaymentInstrumentStore _store;
        public PaymentInstrumentService(IPaymentInstrumentStore store) { _store = store; }
        public Task<IReadOnlyList<PaymentInstrument>> ListAsync(string type, bool activeOnly, CancellationToken ct)
        { if (type != null && type != "wallet" && type != "card") throw new ArgumentException("نوع وسيلة الدفع غير صحيح."); return _store.ListAsync(type, activeOnly, ct); }
        public Task<PaymentInstrument> SaveAsync(PaymentInstrument value, long actor, CancellationToken ct)
        {
            if (value == null || actor <= 0 || value.Id < 0 || value.Type != "wallet" && value.Type != "card") throw new ArgumentException("وسيلة الدفع غير صحيحة.");
            value.Name = (value.Name ?? "").Trim(); value.Number = (value.Number ?? "").Trim(); value.Provider = (value.Provider ?? "").Trim(); value.Notes = (value.Notes ?? "").Trim();
            if (value.Name.Length == 0 || value.Name.Length > 120 || value.Number.Length == 0 || value.Number.Length > 80 || value.Provider.Length > 120 || value.Notes.Length > 1000) throw new ArgumentException("أدخل الاسم والرقم وراجع أطوال الحقول.");
            // Ignore whitespace, normalize Unicode and provider casing, preserve leading zeros.
            var provider = Normalize(value.Provider); var number = Normalize(value.Number);
            var identity = provider.Length.ToString(CultureInfo.InvariantCulture) + ":" + provider + number;
            return _store.SaveAsync(value, identity, actor, ct);
        }
        private static string Normalize(string value) { var b = new StringBuilder(); foreach (var ch in value.Normalize(NormalizationForm.FormKC).ToUpperInvariant()) if (!char.IsWhiteSpace(ch)) b.Append(ch); return b.ToString(); }
        public Task<PaymentHistoryPage> HistoryAsync(PaymentHistoryQuery q, CancellationToken ct)
        {
            if (q == null || q.Page < 1 || q.Page > 1000000 || q.PageSize < 1 || q.PageSize > 100 || q.InstrumentId <= 0 || q.CashierId <= 0 || q.Status != null && q.Status != "posted" && q.Status != "returned" && q.Status != "canceled") throw new ArgumentException("مرشحات السجل غير صحيحة.");
            foreach (var text in new[] { q.From, q.To }) { DateTime date; if (text != null && !DateTime.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)) throw new ArgumentException("التاريخ غير صحيح."); }
            if (q.From != null && q.To != null && string.CompareOrdinal(q.From,q.To)>0) throw new ArgumentException("راجع نطاق التاريخ.");
            return _store.HistoryAsync(q, ct);
        }
        public Task<PaymentOverview> OverviewAsync(CancellationToken ct) { return _store.OverviewAsync(ct); }
    }
}
