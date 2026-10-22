using System;
using System.IO;
using Xunit;
using AlKhayal.Core.Models;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Database;

namespace AlKhayal.IntegrationTests
{
    public sealed class CustomerPersistenceTests
    {
        [Fact]
        public void Customer_address_and_fields_survive_repository_recreation()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var connectionString = Db.ConnectionStringFor(path);
                Migrator.Run(connectionString);
                var repository = new SqliteCustomerRepository(connectionString);
                var id = repository.Add(new Customer { Code = "CUS-TEST", Name = "عميل اختبار", Phone = "01000000000", Address = "شارع التحرير", Balance = 25, CreditLimit = 500, Subscription = "أساسي", Status = "نشط" });

                var reopenedRepository = new SqliteCustomerRepository(connectionString);
                var customer = reopenedRepository.GetById(id);
                Assert.NotNull(customer);
                Assert.Equal("شارع التحرير", customer.Address);
                Assert.Equal("عميل اختبار", customer.Name);
                Assert.Equal(25m, customer.Balance);

                customer.Address = "شارع النيل";
                Assert.True(reopenedRepository.Update(customer));
                Assert.Equal("شارع النيل", new SqliteCustomerRepository(connectionString).GetById(id).Address);
                Assert.True(reopenedRepository.Delete(id));
                Assert.Null(reopenedRepository.GetById(id));
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
