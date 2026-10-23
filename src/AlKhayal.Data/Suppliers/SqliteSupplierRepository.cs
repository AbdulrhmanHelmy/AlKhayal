using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Dapper;

namespace AlKhayal.Data.Suppliers
{
    public sealed class SqliteSupplierRepository : ISupplierRepository
    {
        private const string SelectColumns = "s.Id, s.Code, s.Name, s.Contact, s.Phone, s.Address, s.Payable, s.Status, COALESCE((SELECT SUM(p.Amount) FROM SupplierPayments p WHERE p.SupplierId = s.Id), 0) AS Paid";
        private readonly string _connectionString;
        public SqliteSupplierRepository(string connectionString) { _connectionString = connectionString; }

        public List<Supplier> GetAll()
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Query<Supplier>("SELECT " + SelectColumns + " FROM Suppliers s ORDER BY s.Id DESC").ToList();
        }

        public Supplier GetById(long id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.QuerySingleOrDefault<Supplier>("SELECT " + SelectColumns + " FROM Suppliers s WHERE s.Id = @Id", new { Id = id });
        }

        public long Add(Supplier supplier)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.ExecuteScalar<long>("INSERT INTO Suppliers (Code, Name, Contact, Phone, Address, Payable, Status, CreatedAt, UpdatedAt) VALUES (@Code, @Name, @Contact, @Phone, @Address, @Payable, @Status, @Now, @Now); SELECT last_insert_rowid();", new { supplier.Code, supplier.Name, supplier.Contact, supplier.Phone, supplier.Address, supplier.Payable, supplier.Status, Now = DateTime.UtcNow.ToString("o") });
        }

        public bool Update(Supplier supplier)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Execute("UPDATE Suppliers SET Code=@Code, Name=@Name, Contact=@Contact, Phone=@Phone, Address=@Address, Status=@Status, UpdatedAt=@Now WHERE Id=@Id", new { supplier.Id, supplier.Code, supplier.Name, supplier.Contact, supplier.Phone, supplier.Address, supplier.Status, Now = DateTime.UtcNow.ToString("o") }) == 1;
        }

        public bool Delete(long id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Execute("DELETE FROM Suppliers WHERE Id=@Id", new { Id = id }) == 1;
        }
    }
}
