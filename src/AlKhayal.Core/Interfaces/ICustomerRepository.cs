using System.Collections.Generic;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface ICustomerRepository
    {
        List<Customer> GetAll();
        Customer GetById(long id);
        long Add(Customer customer);
        bool Update(Customer customer);
        bool Delete(long id);
    }
}
