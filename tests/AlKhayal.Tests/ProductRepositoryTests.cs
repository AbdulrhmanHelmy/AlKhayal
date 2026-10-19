using System;
using System.Data.SQLite;
using System.IO;
using AlKhayal.Core.Models;
using AlKhayal.Data.Database;
using AlKhayal.Data.Repositories;
using Xunit;

namespace AlKhayal.Tests
{
    public class ProductRepositoryTests
    {
        [Fact]
        public void Add_then_GetAll_returns_the_product()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            var cs = Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                var repo = new ProductRepository(cs);

                repo.Add(new Product { Name = "Tea", Price = 25m });
                var all = repo.GetAll();

                Assert.Single(all);
                Assert.Equal("Tea", all[0].Name);
                Assert.Equal(25m, all[0].Price);
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
