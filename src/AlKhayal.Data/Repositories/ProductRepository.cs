using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Dapper;

namespace AlKhayal.Data.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly string _connectionString;

        public ProductRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public long Add(Product product)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                return conn.ExecuteScalar<long>(
                    "INSERT INTO Products (Name, Price) VALUES (@Name, @Price); SELECT last_insert_rowid();",
                    product);
            }
        }

        public List<Product> GetAll()
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                return conn.Query<Product>("SELECT Id, Name, Price FROM Products ORDER BY Id").ToList();
            }
        }
    }
}
