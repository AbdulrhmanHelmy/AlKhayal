using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Dapper;

namespace AlKhayal.Data.Suppliers
{
    public sealed class SqliteSupplierRepository : ISupplierRepository
    {
        private const string SelectColumns = "s.Id, s.Code, s.Name, s.Contact, s.Phone, s.Address, s.Payable, s.SupplierCredit, s.Status, COALESCE((SELECT SUM(CASE WHEN l.AmountScaled<0 AND l.EntryType IN ('payment','legacy_payment') THEN -l.AmountScaled ELSE 0 END) FROM SupplierLedgerEntries l WHERE l.SupplierId=s.Id),0) AS PaidScaled";
        private readonly string _connectionString;
        public SqliteSupplierRepository(string connectionString) { _connectionString = connectionString; }

        public List<Supplier> GetAll()
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Query<SupplierRow>("SELECT " + SelectColumns + " FROM Suppliers s ORDER BY s.Id DESC").Select(ToSupplier).ToList();
        }

        public async Task<SupplierPage> ListAsync(string search, int page, int pageSize, CancellationToken ct)
        {
            var escaped=(search??string.Empty).Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");
            var where=" WHERE (@Search='' OR s.Name LIKE @Like ESCAPE '\\' OR s.Code LIKE @Like ESCAPE '\\' OR s.Phone LIKE @Like ESCAPE '\\' OR s.Contact LIKE @Like ESCAPE '\\')";
            var args=new{Search=search??string.Empty,Like="%"+escaped+"%",Limit=pageSize,Offset=(page-1)*pageSize};
            using(var connection=new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var count=await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Suppliers s"+where,args,cancellationToken:ct)).ConfigureAwait(false);
                var rows=await connection.QueryAsync<SupplierRow>(new CommandDefinition("SELECT "+SelectColumns+" FROM Suppliers s"+where+" ORDER BY s.Name COLLATE NOCASE,s.Id LIMIT @Limit OFFSET @Offset",args,cancellationToken:ct)).ConfigureAwait(false);
                return new SupplierPage{Items=rows.Select(ToSupplier).ToList(),TotalCount=count,Page=page,PageSize=pageSize};
            }
        }

        public Supplier GetById(long id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                var row = connection.QuerySingleOrDefault<SupplierRow>("SELECT " + SelectColumns + " FROM Suppliers s WHERE s.Id = @Id", new { Id = id });
                return row == null ? null : ToSupplier(row);
            }
        }

        public long Add(Supplier supplier)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open(); using (var tx = connection.BeginTransaction())
                {
                    var now = DateTime.UtcNow.ToString("o");
                    var id = connection.ExecuteScalar<long>("INSERT INTO Suppliers (Code, Name, Contact, Phone, Address, Payable, Status, CreatedAt, UpdatedAt) VALUES (@Code, @Name, @Contact, @Phone, @Address, @Payable, @Status, @Now, @Now); SELECT last_insert_rowid();", new { supplier.Code, supplier.Name, Contact = supplier.Contact ?? string.Empty, supplier.Phone, Address = supplier.Address ?? string.Empty, supplier.Payable, supplier.Status, Now = now }, tx);
                    if (connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SupplierLedgerEntries'", transaction: tx) != 0)
                    {
                        var scaled = checked((long)Math.Round(supplier.Payable * 10000m, 0, MidpointRounding.AwayFromZero));
                        if (scaled > 0) connection.Execute("INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,Notes,CreatedUtc) VALUES(@Id,'opening',@Amount,@Date,@Reference,'رصيد افتتاحي للمورد',@Now)", new { Id = id, Amount = scaled, Date = DateTime.UtcNow.ToString("yyyy-MM-dd"), Reference = "OPEN-" + id, Now = now }, tx);
                    }
                    tx.Commit(); return id;
                }
            }
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

        private static Supplier ToSupplier(SupplierRow row)
        {
            return new Supplier
            {
                Id = row.Id,
                Code = row.Code,
                Name = row.Name,
                Contact = row.Contact,
                Phone = row.Phone,
                Address = row.Address,
                Payable = row.Payable,
                SupplierCredit = row.SupplierCredit,
                Status = row.Status,
                Paid = row.PaidScaled / 10000m
            };
        }

        private sealed class SupplierRow
        {
            public long Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public string Contact { get; set; }
            public string Phone { get; set; }
            public string Address { get; set; }
            public decimal Payable { get; set; }
            public decimal SupplierCredit { get; set; }
            public string Status { get; set; }
            public long PaidScaled { get; set; }
        }
    }
}
