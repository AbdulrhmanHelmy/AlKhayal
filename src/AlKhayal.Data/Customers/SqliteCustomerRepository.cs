using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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
        public async Task<IReadOnlyList<Customer>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            var escaped=(query??string.Empty).Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");
            using(var connection=new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var rows=await connection.QueryAsync<Customer>(new CommandDefinition(@"SELECT Id, Code, Name, Phone, Status FROM Customers
                    WHERE Status='نشط' AND (@Query='' OR Name LIKE @Like ESCAPE '\' OR Code LIKE @Like ESCAPE '\' OR Phone LIKE @Like ESCAPE '\')
                    ORDER BY Name, Id LIMIT @Limit",new{Query=query,Like="%"+escaped+"%",Limit=limit},cancellationToken:ct)).ConfigureAwait(false);
                return rows.ToList();
            }
        }

        public Customer GetById(long id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.QuerySingleOrDefault<Customer>("SELECT Id, Code, Name, Phone, Address, Balance, CreditLimit, Subscription, Status FROM Customers WHERE Id = @Id", new { Id = id });
        }

        public long Add(Customer customer)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            { connection.Open();connection.Execute("PRAGMA busy_timeout=5000;");using(var tx=connection.BeginTransaction(System.Data.IsolationLevel.Serializable)) { var now=System.DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture); if(string.IsNullOrWhiteSpace(customer.Code))customer.Code=NextCode(connection,tx);var id=connection.ExecuteScalar<long>("INSERT INTO Customers (Code, Name, Phone, Address, Balance, CreditLimit, Subscription, Status, CreatedAt, UpdatedAt) VALUES (@Code, @Name, @Phone, @Address, @Balance, @CreditLimit, @Subscription, @Status, @Now, @Now); SELECT last_insert_rowid();", new { customer.Code, customer.Name, customer.Phone, customer.Address, customer.Balance, customer.CreditLimit, customer.Subscription, customer.Status, Now=now },tx);if(connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CustomerTransactions'",transaction:tx)>0&&customer.Balance>0)connection.Execute("INSERT INTO CustomerTransactions(CustomerId,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc) VALUES(@Id,'opening',@Amount,@Date,'رصيد افتتاحي',@Now)",new{Id=id,Amount=checked((long)(customer.Balance*10000m)),Date=System.DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Now=now},tx);tx.Commit();return id;}}
        }

        private static string NextCode(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // The transaction serializes writers. Historical free-form codes remain untouched;
            // only numeric CUS- codes participate in this sequence.
            var next=connection.ExecuteScalar<long>("SELECT COALESCE(MAX(CAST(SUBSTR(Code,5) AS INTEGER)),0)+1 FROM Customers WHERE UPPER(Code) GLOB 'CUS-[0-9]*' AND SUBSTR(Code,5) NOT GLOB '*[^0-9]*'",transaction:transaction);
            while(true)
            {
                var code="CUS-"+next.ToString("000",CultureInfo.InvariantCulture);
                if(connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Customers WHERE Code=@Code",new{Code=code},transaction)==0)return code;
                next++;
            }
        }

        public bool Update(Customer customer)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Execute("UPDATE Customers SET Code=@Code, Name=@Name, Phone=@Phone, Address=@Address, Subscription=@Subscription, Status=@Status, UpdatedAt=@Now WHERE Id=@Id", new { customer.Id, customer.Code, customer.Name, customer.Phone, customer.Address, customer.Subscription, customer.Status, Now = System.DateTime.UtcNow.ToString("o") }) == 1;
        }

        public bool Delete(long id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            { connection.Open();using(var tx=connection.BeginTransaction()){if(connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CustomerTransactions'",transaction:tx)>0){var other=connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType<>'opening'",new{Id=id},tx);if(other>0){tx.Rollback();return false;}connection.Execute("DELETE FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='opening'",new{Id=id},tx);}var deleted=connection.Execute("DELETE FROM Customers WHERE Id=@Id",new { Id=id},tx)==1;tx.Commit();return deleted;} }
        }
    }
}
