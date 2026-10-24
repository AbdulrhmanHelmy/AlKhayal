using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;

namespace AlKhayal.Application.Suppliers
{
    public sealed class SupplierLedgerService
    {
        private readonly ISupplierLedgerRepository _repository;
        public SupplierLedgerService(ISupplierLedgerRepository repository) { _repository = repository; }
        public Task<SupplierStatement> StatementAsync(long supplierId, string from, string to, CancellationToken ct)
        {
            ValidateDate(from, false); ValidateDate(to, false);
            if (from != null && to != null && string.CompareOrdinal(from, to) > 0) throw new ArgumentException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
            return _repository.StatementAsync(supplierId, from, to, ct);
        }
        public Task<IReadOnlyList<SupplierLedgerEntry>> HistoryAsync(long supplierId, CancellationToken ct) { return _repository.HistoryAsync(supplierId, ct); }
        public Task<SupplierLedgerEntry> AddPaymentAsync(long supplierId, long? supplierInvoiceId, long? purchaseInvoiceId, string amount, string paymentDate, string method, string account, string notes, string operationKey, CancellationToken ct)
        {
            long scaled;
            if (supplierId <= 0 || supplierInvoiceId.HasValue && supplierInvoiceId <= 0 || purchaseInvoiceId.HasValue && purchaseInvoiceId <= 0 || supplierInvoiceId.HasValue && purchaseInvoiceId.HasValue || !TryScale(amount, out scaled) || scaled <= 0)
                throw new ArgumentException("أدخل المورد والفاتورة وقيمة دفع صحيحة.");
            ValidateDate(paymentDate, true);
            if (string.IsNullOrWhiteSpace(method) || method.Trim().Length > 60 || account != null && account.Trim().Length > 100 || notes != null && notes.Length > 1000)
                throw new ArgumentException("أدخل طريقة الدفع ومرجعًا صحيحًا.");
            if (string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 120) throw new ArgumentException("مفتاح الدفع غير صالح؛ أعد المحاولة.");
            var command = new SupplierLedgerPaymentCommand { SupplierId = supplierId, SupplierInvoiceId = supplierInvoiceId, PurchaseInvoiceId = purchaseInvoiceId, AmountScaled = scaled,
                PaymentDate = paymentDate, Method = method.Trim(), Account = (account ?? "").Trim(), Notes = notes ?? "", OperationKey = operationKey.Trim() };
            command.RequestHash = Hash(command);
            return _repository.AddPaymentAsync(command, ct);
        }
        private static bool TryScale(string value, out long scaled)
        {
            scaled = 0; decimal parsed;
            if (string.IsNullOrWhiteSpace(value) || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed) || parsed < 0) return false;
            try { var result = parsed * 10000m; if (result != decimal.Truncate(result)) return false; scaled = checked((long)result); return true; } catch (OverflowException) { return false; }
        }
        private static void ValidateDate(string value, bool required)
        {
            DateTime parsed;
            if (string.IsNullOrWhiteSpace(value) ? required : !DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException("تاريخ العملية غير صحيح.");
        }
        private static string Hash(SupplierLedgerPaymentCommand c)
        {
            var text = new StringBuilder("supplier-payment-v1|").Append(c.SupplierId).Append('|').Append(c.SupplierInvoiceId).Append('|').Append(c.PurchaseInvoiceId).Append('|').Append(c.AmountScaled).Append('|').Append(c.PaymentDate).Append('|').Append(c.Method).Append('|').Append(c.Account).Append('|').Append(c.Notes).Append('|').Append(c.OperationKey);
            using (var sha = SHA256.Create()) return "v2:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
        }
    }
}
