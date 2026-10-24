using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;

namespace AlKhayal.Application.Purchasing
{
    public sealed class PurchaseService
    {
        private readonly IPurchaseRepository _purchases;
        private readonly ISupplierRepository _suppliers;

        public PurchaseService(IPurchaseRepository purchases, ISupplierRepository suppliers)
        { _purchases = purchases; _suppliers = suppliers; }

        public async Task<PurchaseInvoice> CreateDraftAsync(PurchaseDraftCommand command, long userId, CancellationToken ct)
        {
            var total = Validate(command, userId);
            if (_suppliers.GetById(command.SupplierId) == null) throw new ArgumentException("المورد المحدد غير موجود.");
            return await _purchases.CreateDraftAsync(command, total, userId, ct).ConfigureAwait(false);
        }

        public async Task<PurchaseInvoice> UpdateDraftAsync(long id, PurchaseDraftCommand command, long userId, CancellationToken ct)
        {
            var total = Validate(command, userId);
            if (_suppliers.GetById(command.SupplierId) == null) throw new ArgumentException("المورد المحدد غير موجود.");
            return await _purchases.UpdateDraftAsync(id, command, total, userId, ct).ConfigureAwait(false);
        }

        public Task<PurchaseInvoice> GetAsync(long id, CancellationToken ct) { return _purchases.GetAsync(id, ct); }
        public Task<IReadOnlyList<PurchaseReturnLine>> ReturnableLinesAsync(long id, long warehouseId, CancellationToken ct) { return _purchases.ReturnableLinesAsync(id, warehouseId, ct); }
        public Task<IReadOnlyList<PurchaseReturn>> ReturnsAsync(long id, CancellationToken ct) { return _purchases.ReturnsAsync(id, ct); }
        public Task<IReadOnlyList<PurchaseInvoice>> ListAsync(long? supplierId, string from, string to, CancellationToken ct)
        {
            ValidateDate(from, false); ValidateDate(to, false);
            if (from != null && to != null && string.CompareOrdinal(from, to) > 0) throw new ArgumentException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
            return _purchases.ListAsync(supplierId, from, to, ct);
        }

        public async Task<PurchasePostResult> PostAsync(long id, string operationKey, long userId, CancellationToken ct)
        {
            if (id <= 0 || userId <= 0 || string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 120)
                throw new ArgumentException("تعذر التحقق من الفاتورة أو مفتاح العملية.");
            var invoice = await _purchases.GetAsync(id, ct).ConfigureAwait(false);
            if (invoice == null) return null;
            return await _purchases.PostAsync(id, invoice.Version, operationKey.Trim(), HashPost(invoice, operationKey.Trim(), userId), userId, ct).ConfigureAwait(false);
        }

        public async Task<PurchaseReturn> PostReturnAsync(long id, PurchaseReturnCommand command, string operationKey, long userId, CancellationToken ct)
        {
            if (id <= 0 || userId <= 0 || command == null || command.WarehouseId <= 0 || string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 120)
                throw new ArgumentException("اختر فاتورة ومخزنًا ومفتاح عملية صالحًا.");
            if (command.ReturnMode != "cash_refund" && command.ReturnMode != "supplier_credit") throw new ArgumentException("اختر ردًا نقديًا أو رصيدًا لدى المورد.");
            if (command.Lines == null || command.Lines.Count == 0 || command.Lines.Count > 200 || command.Lines.Any(x => x == null || x.PurchaseInvoiceLineId <= 0)) throw new ArgumentException("اختر بندًا واحدًا على الأقل للمرتجع.");
            if (command.Lines.Select(x => x.PurchaseInvoiceLineId).Distinct().Count() != command.Lines.Count) throw new ArgumentException("لا تكرر البند نفسه في المرتجع.");
            foreach (var line in command.Lines)
            {
                long quantity;
                if (!TryScale(line.Quantity, 6, out quantity) || quantity <= 0) throw new ArgumentException("أدخل كمية مرتجع صحيحة بدقة لا تتجاوز ست خانات عشرية.");
                line.PhysicalUnits = line.PhysicalUnits ?? new List<PurchasePhysicalUnit>();
                if (line.PhysicalUnits.Any(x => x == null || string.IsNullOrWhiteSpace(x.Serial))) throw new ArgumentException("راجع الأرقام التسلسلية للمرتجع.");
            }
            return await _purchases.PostReturnAsync(id, command, operationKey.Trim(), HashReturn(id, command, operationKey.Trim(), userId), userId, ct).ConfigureAwait(false);
        }

        private static long Validate(PurchaseDraftCommand command, long userId)
        {
            if (command == null || userId <= 0 || command.SupplierId <= 0 || command.WarehouseId <= 0) throw new ArgumentException("اختر المورد والمخزن.");
            ValidateDate(command.InvoiceDate, true);
            if (command.PaymentMode != "cash" && command.PaymentMode != "credit") throw new ArgumentException("اختر نقدي أو آجل.");
            if (command.InvoiceNumber != null && command.InvoiceNumber.Trim().Length > 80) throw new ArgumentException("رقم الفاتورة طويل جدًا.");
            if (command.Notes != null && command.Notes.Length > 1000) throw new ArgumentException("الملاحظات يجب ألا تتجاوز 1000 حرف.");
            if (command.Lines == null || command.Lines.Count == 0 || command.Lines.Count > 500) throw new ArgumentException("أضف بندًا واحدًا على الأقل.");
            long total = 0;
            foreach (var line in command.Lines)
            {
                long quantity, cost, sale;
                if (line == null || line.VariantId <= 0 || line.ProductUnitId <= 0 || !TryScale(line.Quantity, 6, out quantity) || quantity <= 0)
                    throw new ArgumentException("أدخل صنفًا ووحدة وكمية صحيحة لكل بند.");
                if (!TryScale(line.PurchaseUnitPrice, 4, out cost) || !TryScale(line.SaleUnitPrice, 4, out sale))
                    throw new ArgumentException("أدخل سعر شراء وبيع صالحين بدقة أربع خانات عشرية.");
                if (line.LotNumber != null && line.LotNumber.Length > 80) throw new ArgumentException("رقم التشغيلة طويل جدًا.");
                if (line.ExpirationDate != null) ValidateDate(line.ExpirationDate, false);
                var lineTotal = checked((long)Math.Round((decimal)quantity * cost / 1000000m, 0, MidpointRounding.AwayFromZero));
                line.LineTotalScaled = lineTotal;
                total = checked(total + lineTotal);
                line.PhysicalUnits = line.PhysicalUnits ?? new List<PurchasePhysicalUnit>();
                if (line.PhysicalUnits.Any(x => x == null || string.IsNullOrWhiteSpace(x.Serial))) throw new ArgumentException("راجع الأرقام التسلسلية المدخلة.");
            }
            if (total <= 0) throw new ArgumentException("إجمالي الفاتورة يجب أن يكون أكبر من صفر.");
            return total;
        }

        private static bool TryScale(string value, int places, out long scaled)
        {
            scaled = 0; decimal parsed;
            if (string.IsNullOrWhiteSpace(value) || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed) || parsed < 0) return false;
            try { var factor = places == 6 ? 1000000m : 10000m; var result = parsed * factor; if (result != decimal.Truncate(result)) return false; scaled = checked((long)result); return true; }
            catch (OverflowException) { return false; }
        }

        private static void ValidateDate(string value, bool required)
        {
            DateTime parsed;
            if (string.IsNullOrWhiteSpace(value) ? required : !DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                throw new ArgumentException("تاريخ الفاتورة غير صحيح.");
        }

        private static string HashPost(PurchaseInvoice invoice, string key, long userId)
        {
            var text = new StringBuilder("purchase-post-v1|");
            text.Append(invoice.Id).Append('|').Append(invoice.TotalScaled).Append('|').Append(invoice.SupplierId).Append('|').Append(invoice.WarehouseId).Append('|').Append(invoice.PaymentMode).Append('|').Append(key).Append('|').Append(userId);
            foreach (var line in invoice.Lines.OrderBy(x => x.Id))
            {
                text.Append('|').Append(line.Id).Append(':').Append(line.VariantId).Append(':').Append(line.ProductUnitId).Append(':').Append(line.Quantity).Append(':').Append(line.PurchaseUnitPrice).Append(':').Append(line.SaleUnitPrice).Append(':').Append(line.LotNumber).Append(':').Append(line.ExpirationDate);
                foreach (var unit in line.PhysicalUnits) { text.Append(":SN:").Append(unit.Serial); foreach (var imei in unit.Imeis) text.Append(":IMEI:").Append(imei); }
            }
            using (var sha = SHA256.Create()) return "v2:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
        }

        private static string HashReturn(long invoiceId, PurchaseReturnCommand command, string key, long userId)
        {
            var text = new StringBuilder("purchase-return-v1|").Append(invoiceId).Append('|').Append(command.WarehouseId).Append('|').Append(command.ReturnMode).Append('|').Append(key).Append('|').Append(userId);
            foreach (var line in command.Lines.OrderBy(x => x.PurchaseInvoiceLineId))
            {
                text.Append('|').Append(line.PurchaseInvoiceLineId).Append(':').Append(line.Quantity);
                foreach (var unit in line.PhysicalUnits) { text.Append(":SN:").Append(unit.Serial); foreach (var imei in unit.Imeis) text.Append(":IMEI:").Append(imei); }
            }
            using (var sha = SHA256.Create()) return "v2:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
        }
    }
}
