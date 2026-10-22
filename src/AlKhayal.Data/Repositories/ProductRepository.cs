using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using AlKhayal.Application.Catalog;
using Dapper;
using System;
using System.Globalization;

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
            long scaled;
            if (product == null || string.IsNullOrWhiteSpace(product.Name) || !CatalogService.TryScaleMoney(product.Price.ToString(CultureInfo.InvariantCulture), out scaled))
                throw new ArgumentException("Product name and a non-negative price are required.", "product");
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    var id=conn.ExecuteScalar<long>("INSERT INTO Products (Name, Price) VALUES (@Name, @Price); SELECT last_insert_rowid();", product, transaction);
                    var hasCatalog=conn.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductDetails'",transaction:transaction)==1;
                    if(hasCatalog)
                    {
                        var now=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);
                        conn.Execute(@"INSERT INTO ProductDetails(ProductId,BaseUnitId,ProductType,SellPriceScaled,MoneyScale,IsActive,MigrationState,Version,CreatedUtc,UpdatedUtc) VALUES(@Id,1,'stock',@Scaled,4,1,'ready',1,@Now,@Now);
                            INSERT INTO ProductVariants(ProductId,Name,NormalizedName,IsDefault,IsActive,CreatedUtc,UpdatedUtc) VALUES(@Id,'افتراضي','DEFAULT',1,1,@Now,@Now);",new{Id=id,Scaled=scaled,Now=now},transaction);
                    }
                    transaction.Commit();
                    return id;
                }
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
