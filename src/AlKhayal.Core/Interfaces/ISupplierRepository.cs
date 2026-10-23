using System.Collections.Generic;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface ISupplierRepository
    {
        List<Supplier> GetAll();
        Supplier GetById(long id);
        long Add(Supplier supplier);
        // Updates identity/contact fields only; Payable is never changed here.
        bool Update(Supplier supplier);
        bool Delete(long id);
    }
}
