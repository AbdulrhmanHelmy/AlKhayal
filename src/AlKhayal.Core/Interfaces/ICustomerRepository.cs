using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface ICustomerRepository
    {
        List<Customer> GetAll();
        Task<IReadOnlyList<Customer>> SearchAsync(string query, int limit, CancellationToken ct);
        Customer GetById(long id);
        long Add(Customer customer);
        bool Update(Customer customer);
        bool Delete(long id);
    }
}
