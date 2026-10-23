using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Dapper;

namespace AlKhayal.Data.Customers
{
    public sealed class SqliteCustomerRepository : ICustomerRepository
    {
        private readonly string _connectionString;
        public SqliteCustomerRepository(string connectionString) { _connectionString = connectionString; }

        public List<Customer> GetAll()
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Query<Customer>("SELECT Id, Code, Name, Phone, Address, Balance, CreditLimit, Subscription, Status FROM Customers ORDER BY Id DESC").ToList();
        }

        public Customer GetById(long id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.QuerySingleOrDefault<Customer>("SELECT Id, Code, Name, Phone, Address, Balance, CreditLimit, Subscription, Status FROM Customers WHERE Id = @Id", new { Id = id });
        }

        public long Add(Customer customer)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.ExecuteScalar<long>("INSERT INTO Customers (Code, Name, Phone, Address, Balance, CreditLimit, Subscription, Status, CreatedAt, UpdatedAt) VALUES (@Code, @Name, @Phone, @Address, @Balance, @CreditLimit, @Subscription, @Status, @Now, @Now); SELECT last_insert_rowid();", new { customer.Code, customer.Name, customer.Phone, customer.Address, customer.Balance, customer.CreditLimit, customer.Subscription, customer.Status, Now = System.DateTime.UtcNow.ToString("o") });
        }

        public bool Update(Customer customer)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Execute("UPDATE Customers SET Code=@Code, Name=@Name, Phone=@Phone, Address=@Address, Subscription=@Subscription, Status=@Status, UpdatedAt=@Now WHERE Id=@Id", new { customer.Id, customer.Code, customer.Name, customer.Phone, customer.Address, customer.Subscription, customer.Status, Now = System.DateTime.UtcNow.ToString("o") }) == 1;
        }

        public bool Delete(long id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Execute("DELETE FROM Customers WHERE Id=@Id", new { Id = id }) == 1;
        }
    }
}