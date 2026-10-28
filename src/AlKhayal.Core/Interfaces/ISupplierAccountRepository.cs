using System.Collections.Generic;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface ISupplierAccountRepository
    {
        // Atomically records the invoice and increases the supplier's payable balance.
        SupplierInvoice AddInvoice(long supplierId, string invoiceNumber, string invoiceDate, string dueDate, decimal total, string notes, string operationKey = null, string requestHash = null, long? actorUserId = null);
        // Atomically records the payment, reduces the supplier balance and (when invoiceId is set) the invoice's remaining amount.
        SupplierPayment AddPayment(long supplierId, long? invoiceId, decimal amount, string paymentDate, string method, string account, string notes);
        List<SupplierInvoice> GetInvoices(long supplierId);
        List<SupplierPayment> GetPayments(long supplierId);
        SupplierStatement GetStatement(long supplierId, string from, string to);
    }
}
