using System.Collections.Generic;
using AlKhayal.Core.Models;
namespace AlKhayal.Core.Interfaces
{
    public interface ICustomerAccountRepository
    {
        CustomerPayment AddPayment(long customerId, decimal amount, string paymentDate, string method, string account, string notes);
        CustomerStatement GetStatement(long customerId, string from, string to);
        List<CustomerPayment> GetPayments(long customerId);
    }
}
