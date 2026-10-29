using System.Collections.Generic;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface ICustomerInstallmentRepository
    {
        CustomerInstallmentContract Create(long customerId, long productId, decimal productPrice, decimal downPayment,
            decimal interestRate, int count, int intervalMonths, string firstDueOn, decimal totalAmount,
            IReadOnlyList<CustomerInstallmentRow> schedule, string operationKey, string requestHash);
        List<CustomerInstallmentContract> GetByCustomer(long customerId);
        CustomerInstallmentContract Pay(long customerId, long contractId, int installmentNumber, string paidOn,
            string operationKey, string requestHash);
    }
}
