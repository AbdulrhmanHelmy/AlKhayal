using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Returns;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Settings;
using AlKhayal.Application.Workforce;

namespace AlKhayal.Application.Printing
{
    public interface IPrintPaymentReader
    {
        Task<PrintDocumentData> GetAsync(string type, long id, CancellationToken ct);
    }
    public sealed class PrintDocumentProvider : IPrintDocumentProvider
    {
        private readonly SalesService _sales;
        private readonly PurchaseService _purchases;
        private readonly ReturnService _returns;
        private readonly WorkforceService _workforce;
        private readonly SettingsService _settings;
        private readonly IPrintPaymentReader _payments;
        public PrintDocumentProvider(SalesService sales, PurchaseService purchases, ReturnService returns, WorkforceService workforce, SettingsService settings, IPrintPaymentReader payments)
        { _sales = sales; _purchases = purchases; _returns = returns; _workforce = workforce; _settings = settings; _payments = payments; }
        public async Task<PrintDocumentData> GetAsync(string type, long id, long actorId, bool administrator, CancellationToken ct)
        {
            var d = new PrintDocumentData { Type = type, Id = id, Title = PrintTypes.Titles[Array.IndexOf(PrintTypes.All, type)] };
            if (type == "sale")
            {
                var s = await _sales.GetAsync(id, ct).ConfigureAwait(false); if (s == null || s.Status != "posted") return null;
                d.Reference = s.InvoiceNumber; d.Fields["date"] = DocumentDate(s.InvoiceDate, s.PostedUtc); d.Fields["cashier"] = s.CashierName; d.Fields["party"] = s.CustomerName ?? "نقدي مباشر";
                d.Fields["subtotal"] = Money(s.SubtotalScaled); d.Fields["discountTotal"] = Money(s.DiscountScaled); d.Fields["taxTotal"] = Money(s.TaxScaled); d.Fields["grandTotal"] = Money(s.TotalScaled); d.Fields["paid"] = Money(s.PaidScaled); d.Fields["remaining"] = Money(s.OutstandingScaled);
                d.Items = s.Lines.Select(l => new PrintItem { Name = l.ProductName, Variant = l.VariantName + " / " + l.UnitName, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Discount = l.Discount, Tax = Money(l.TaxScaled), Total = Money(l.LineTotalScaled) }).ToList();
            }
            else if (type == "purchase")
            {
                var p = await _purchases.GetAsync(id, ct).ConfigureAwait(false); if (p == null || p.Status != "posted") return null;
                d.Reference = p.InvoiceNumber; d.Fields["party"] = p.SupplierName; d.Fields["date"] = DocumentDate(p.InvoiceDate, p.PostedUtc); d.Fields["grandTotal"] = Money(p.TotalScaled);
                d.Items = p.Lines.Select(l => new PrintItem { Name = l.ProductName, Variant = l.VariantName + " / " + l.UnitName, Quantity = l.Quantity, UnitPrice = l.PurchaseUnitPrice, Total = Money(l.LineTotalScaled) }).ToList();
            }
            else if (type == "sales-return" || type == "purchase-return")
            {
                var r = await _returns.GetAsync(type == "sales-return" ? "sales" : "purchase", id, ct).ConfigureAwait(false); if (r == null) return null;
                d.Reference = r.Number; d.Fields["party"] = r.PartyName; d.Fields["date"] = Local(r.CreatedUtc); d.Fields["cashier"] = r.ActorName;
                d.Fields["header"] = "الفاتورة الأصلية: " + r.InvoiceNumber + "\n" + r.Reason;
                d.Fields["grandTotal"] = r.Total; d.Fields["paid"] = r.Cash; d.Fields["remaining"] = r.AccountAdjustment;
                d.Items = r.Lines.Select(l => new PrintItem { Name = l.ProductName, Variant = l.UnitName, Quantity = l.Quantity, Total = l.Amount }).ToList();
            }
            else if (type.StartsWith("shift-", StringComparison.Ordinal))
            {
                CashierShift s;
                try { s = await _workforce.ShiftAsync(id, administrator ? (long?)null : actorId, ct).ConfigureAwait(false); }
                catch (WorkforceException ex) when (ex.Code == "not_found") { return null; }
                if (s == null || type == "shift-closing" && s.ClosedUtc == null) return null;
                d.Reference = "SHIFT-" + s.Id.ToString(CultureInfo.InvariantCulture); d.Fields["cashier"] = s.CashierName;
                d.Fields["date"] = Local(s.OpenedUtc) + (s.ClosedUtc == null ? " — مفتوحة" : " — " + Local(s.ClosedUtc));
                d.Fields["party"] = "نقطة البيع: " + s.RegisterId + " / عدد المبيعات: " + s.SalesCount;
                d.Fields["grandTotal"] = s.Expected; d.Fields["paid"] = s.Counted; d.Fields["remaining"] = s.Difference;
                var labels = new[] { "افتتاح الدرج", "المبيعات النقدية", "المبيعات الآجلة", "التحصيلات", "إدخال نقد", "إخراج نقد" };
                var amounts = new[] { s.Opening, s.CashSales, s.CreditSales, s.Collections, s.CashIn, s.CashOut };
                for (var i = 0; i < labels.Length; i++) d.Items.Add(new PrintItem { Name = labels[i], Quantity = "1", Total = amounts[i] });
            }
            else
            {
                d = await _payments.GetAsync(type, id, ct).ConfigureAwait(false); if (d == null) return null;
                d.Fields["date"] = Local(d.Fields["date"]);
            }
            var company = await _settings.GetCompanyAsync(ct).ConfigureAwait(false);
            d.Fields["company"] = company.BusinessName; d.Fields["address"] = company.Address; d.Fields["phone"] = company.Phone;
            d.Fields["taxInfo"] = company.TaxRegistrationNumber; d.Fields["footer"] = company.FooterText;
            var general = await _settings.GetGeneralAsync(ct).ConfigureAwait(false);
            string header; d.Fields.TryGetValue("header", out header);
            d.Fields["header"] = (string.IsNullOrWhiteSpace(header) ? "" : header + "\n") + "العملة: " + general.CurrencyCode + " " + general.CurrencySymbol;
            return d;
        }
        private static string DocumentDate(string date, string posted) => Local(date) + (string.IsNullOrWhiteSpace(posted) ? "" : "\nترحيل: " + Local(posted));
        private static string Money(long scaled) => MoneyPolicy.FormatForUser(scaled);
        private static string Local(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 10) return value;
            DateTimeOffset time; return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time) ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : value;
        }
    }
}
