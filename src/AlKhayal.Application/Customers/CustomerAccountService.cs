using System;
using System.Collections.Generic;
using System.Globalization;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
namespace AlKhayal.Application.Customers
{
    public sealed class CustomerAccountService
    {
        private readonly ICustomerAccountRepository _accounts;
        public CustomerAccountService(ICustomerAccountRepository accounts) { _accounts = accounts; }
        public CustomerPayment AddPayment(long customerId, CustomerPayment payment)
        {
            if (payment == null) throw new ArgumentException("بيانات الدفعة مطلوبة.");
            if (customerId <= 0) throw new ArgumentException("اختر عميلًا صحيحًا.");
            if (payment.Amount <= 0) throw new ArgumentException("قيمة الدفعة يجب أن تكون أكبر من صفر.");
            if (string.IsNullOrWhiteSpace(payment.Method)) throw new ArgumentException("طريقة السداد مطلوبة.");
            if (string.IsNullOrWhiteSpace(payment.Account)) throw new ArgumentException("حساب السداد مطلوب.");
            if (payment.Notes != null && payment.Notes.Length > 1000) throw new ArgumentException("الملاحظات يجب ألا تتجاوز 1000 حرف.");
            return _accounts.AddPayment(customerId, payment.Amount, payment.PaymentDate, payment.Method.Trim(), payment.Account.Trim(), payment.Notes);
        }
        public CustomerStatement GetStatement(long customerId, string from, string to)
        {
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(from) && !DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException("تاريخ البداية غير صحيح.");
            if (!string.IsNullOrWhiteSpace(to) && !DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException("تاريخ النهاية غير صحيح.");
            if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to) && string.CompareOrdinal(from, to) > 0) throw new ArgumentException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
            return _accounts.GetStatement(customerId, from, to);
        }
        public List<CustomerPayment> GetPayments(long customerId) { return _accounts.GetPayments(customerId); }
    }
}
