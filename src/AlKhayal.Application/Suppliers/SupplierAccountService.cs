using System;
using System.Collections.Generic;
using System.Globalization;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using AlKhayal.Application.Finance;
using System.Security.Cryptography;
using System.Text;

namespace AlKhayal.Application.Suppliers
{
    public sealed class SupplierAccountService
    {
        private readonly ISupplierAccountRepository _accounts;
        public SupplierAccountService(ISupplierAccountRepository accounts) { _accounts = accounts; }

        public SupplierInvoice AddInvoice(long supplierId, SupplierInvoice invoice, string operationKey = null, long? actorUserId = null)
        {
            if (invoice == null) throw new ArgumentException("بيانات الفاتورة مطلوبة.");
            if (supplierId <= 0) throw new ArgumentException("اختر موردًا صحيحًا.");
            if (!MoneyPolicy.HasAtMostTwoDecimals(invoice.Total)) throw new ArgumentException("إجمالي الفاتورة يجب ألا يتجاوز خانتين عشريتين.");
            var total = invoice.Total;
            if (total <= 0) throw new ArgumentException("إجمالي الفاتورة يجب أن يكون أكبر من صفر.");
            var invoiceDate = NormalizeDate(invoice.InvoiceDate, "تاريخ الفاتورة غير صحيح.", true);
            var dueDate = NormalizeDate(invoice.DueDate, "تاريخ الاستحقاق غير صحيح.", false);
            if (dueDate != null && string.CompareOrdinal(dueDate, invoiceDate) < 0) throw new ArgumentException("تاريخ الاستحقاق لا يمكن أن يسبق تاريخ الفاتورة.");
            var number = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? null : invoice.InvoiceNumber.Trim();
            if (number != null && number.Length > 60) throw new ArgumentException("رقم الفاتورة يجب ألا يتجاوز 60 حرفًا.");
            if (invoice.Notes != null && invoice.Notes.Length > 1000) throw new ArgumentException("الملاحظات يجب ألا تتجاوز 1000 حرف.");
            if (operationKey != null && (string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 120)) throw new ArgumentException("مفتاح إضافة الفاتورة غير صالح.");
            string hash = null;
            if (operationKey != null)
            {
                var text = new StringBuilder("supplier-invoice-v1:");
                foreach (var field in new[] { supplierId.ToString(CultureInfo.InvariantCulture), number ?? "", invoiceDate, dueDate ?? "", checked((long)(total * 10000m)).ToString(CultureInfo.InvariantCulture), invoice.Notes ?? "", actorUserId?.ToString(CultureInfo.InvariantCulture) ?? "" })
                    text.Append(field.Length).Append(':').Append(field);
                using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
            }
            return _accounts.AddInvoice(supplierId, number, invoiceDate, dueDate, total, invoice.Notes, operationKey?.Trim(), hash, actorUserId);
        }

        public SupplierPayment AddPayment(long supplierId, SupplierPayment payment)
        {
            if (payment == null) throw new ArgumentException("بيانات الدفعة مطلوبة.");
            if (supplierId <= 0) throw new ArgumentException("اختر موردًا صحيحًا.");
            if (!MoneyPolicy.HasAtMostTwoDecimals(payment.Amount)) throw new ArgumentException("قيمة الدفعة يجب ألا تتجاوز خانتين عشريتين.");
            var amount = payment.Amount;
            if (amount <= 0) throw new ArgumentException("قيمة الدفعة يجب أن تكون أكبر من صفر.");
            if (payment.InvoiceId.HasValue && payment.InvoiceId.Value <= 0) throw new ArgumentException("الفاتورة المختارة غير صحيحة.");
            if (string.IsNullOrWhiteSpace(payment.Method)) throw new ArgumentException("طريقة السداد مطلوبة.");
            if (string.IsNullOrWhiteSpace(payment.Account)) throw new ArgumentException("حساب السداد مطلوب.");
            if (payment.Notes != null && payment.Notes.Length > 1000) throw new ArgumentException("الملاحظات يجب ألا تتجاوز 1000 حرف.");
            var paymentDate = NormalizeDate(payment.PaymentDate, "تاريخ الدفعة غير صحيح.", true);
            return _accounts.AddPayment(supplierId, payment.InvoiceId, amount, paymentDate, payment.Method.Trim(), payment.Account.Trim(), payment.Notes);
        }

        public List<SupplierInvoice> GetInvoices(long supplierId) { return _accounts.GetInvoices(supplierId); }
        public List<SupplierPayment> GetPayments(long supplierId) { return _accounts.GetPayments(supplierId); }

        public SupplierStatement GetStatement(long supplierId, string from, string to)
        {
            var fromDate = NormalizeDate(from, "تاريخ البداية غير صحيح.", false);
            var toDate = NormalizeDate(to, "تاريخ النهاية غير صحيح.", false);
            if (fromDate != null && toDate != null && string.CompareOrdinal(fromDate, toDate) > 0) throw new ArgumentException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
            return _accounts.GetStatement(supplierId, fromDate, toDate);
        }

        // Deterministic money policy: two decimals, midpoint away from zero.
        private static decimal Round(decimal value) { return Math.Round(value, 2, MidpointRounding.AwayFromZero); }

        private static string NormalizeDate(string value, string message, bool required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required) throw new ArgumentException(message);
                return null;
            }
            DateTime parsed;
            if (!DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException(message);
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
