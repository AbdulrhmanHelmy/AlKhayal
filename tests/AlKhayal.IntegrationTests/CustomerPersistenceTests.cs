using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Data.SQLite;
using Dapper;
using AlKhayal.Application.Customers;
using Xunit;
using AlKhayal.Core.Models;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Database;

namespace AlKhayal.IntegrationTests
{
    public sealed class CustomerPersistenceTests
    {
        [Fact]
        public async Task Generated_customer_codes_are_unique_under_concurrent_creation_and_preserve_existing_codes()
        {
            var path=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"alkhayal-customer-sequence-"+Guid.NewGuid().ToString("N")+".db"));
            var app=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AlKhayal"));
            Assert.False(path.StartsWith(app+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));Assert.False(File.Exists(path));
            try
            {
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);
                var service=new CustomerService(new SqliteCustomerRepository(cs));
                var existing=service.Create(new Customer{Code="CUS-009",Name="قديم",Phone="010"});
                var tasks=Enumerable.Range(0,16).Select(i=>Task.Run(()=>new CustomerService(new SqliteCustomerRepository(cs)).Create(new Customer{Name="عميل "+i,Phone="010"+i}))).ToArray();
                var created=await Task.WhenAll(tasks);
                Assert.Equal(16,created.Select(x=>x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Assert.Contains(created,x=>x.Code=="CUS-010");Assert.All(created,x=>Assert.StartsWith("CUS-",x.Code));
                Assert.Equal("CUS-009",service.GetById(existing.Id).Code);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(17,c.ExecuteScalar<long>("SELECT COUNT(*) FROM Customers"));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Pos_customer_lookup_is_bounded_and_searches_name_code_phone()
        {
            var path=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"alkhayal-pos-customer-"+Guid.NewGuid().ToString("N")+".db"));
            var operatorPath=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AlKhayal","alkhayal.db"));
            Assert.False(string.Equals(path,operatorPath,StringComparison.OrdinalIgnoreCase));Assert.False(path.StartsWith(Path.GetDirectoryName(operatorPath)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
            try
            {
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var repository=new SqliteCustomerRepository(cs);
                for(var i=0;i<25;i++)repository.Add(new Customer{Code="POS-"+i,Name="عميل "+i,Phone="010"+i,Address="",Subscription="أساسي",Status="نشط"});
                repository.Add(new Customer{Code="HIDDEN",Name="عميل مخفي",Phone="0999",Address="",Subscription="أساسي",Status="غير نشط"});
                Assert.Equal(20,(await repository.SearchAsync("",20,CancellationToken.None)).Count);
                Assert.Single(await repository.SearchAsync("عميل 24",20,CancellationToken.None));
                Assert.Single(await repository.SearchAsync("POS-21",20,CancellationToken.None));
                Assert.Single(await repository.SearchAsync("01023",20,CancellationToken.None));
                Assert.Empty(await repository.SearchAsync("مخفي",20,CancellationToken.None));
            }
            finally{System.Data.SQLite.SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
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
                customer.Balance = 0;
                customer.CreditLimit = 0;
                Assert.True(reopenedRepository.Update(customer));
                var updated = new SqliteCustomerRepository(connectionString).GetById(id);
                Assert.Equal("شارع النيل", updated.Address);
                Assert.Equal(25m, updated.Balance);
                Assert.Equal(500m, updated.CreditLimit);
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
