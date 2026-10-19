using System.Collections.Generic;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface IProductRepository
    {
        long Add(Product product);
        List<Product> GetAll();
    }
}
