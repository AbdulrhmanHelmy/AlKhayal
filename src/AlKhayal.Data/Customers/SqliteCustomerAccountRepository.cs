using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Dapper;
namespace AlKhayal.Data.Customers
{
    public sealed class SqliteCustomerAccountRepository : ICustomerAccountRepository
    {
        private readonly string _connectionString;
        public SqliteCustomerAccountRepository(string connectionString) { _connectionString = connectionString; }

        public CustomerPayment AddPayment(long customerId, decimal amount, string paymentDate, string method, string account, string notes)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var tx = connection.BeginTransaction())
                {
                    var customer = connection.QuerySingleOrDefault<Customer>("SELECT Id, Name, Balance, CreditLimit FROM Customers WHERE Id=@Id", new { Id = customerId }, tx);
                    if (customer == null) throw new KeyNotFoundException("العميل غير موجود.");
                    if (amount <= 0) throw new ArgumentException("قيمة الدفعة يجب أن تكون أكبر من صفر.");
                    if (amount > customer.Balance) throw new ArgumentException("قيمة الدفعة أكبر من المديونية الحالية للعميل.");
                    DateTime parsed;
                    if (!DateTime.TryParseExact(paymentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException("تاريخ الدفعة غير صحيح.");
                    var now = DateTime.UtcNow.ToString("o");
                    var reference = "CP-" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant();
                    connection.Execute("INSERT INTO CustomerPayments(CustomerId, Reference, Amount, PaymentDate, Method, Account, Notes, CreatedAt) VALUES(@CustomerId,@Reference,@Amount,@PaymentDate,@Method,@Account,@Notes,@CreatedAt)", new { CustomerId = customerId, Reference = reference, Amount = amount, PaymentDate = parsed.ToString("yyyy-MM-dd"), Method = method, Account = account, Notes = notes ?? string.Empty, CreatedAt = now }, tx);
                    var id = connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
                    var updated = connection.Execute("UPDATE Customers SET Balance=Balance-@Amount, UpdatedAt=@Now WHERE Id=@Id AND Balance>=@Amount", new { Amount = amount, Now = now, Id = customerId }, tx);
                    if (updated != 1) throw new InvalidOperationException("تعذر تحديث رصيد العميل؛ لم يتم اعتماد الدفعة.");
                    tx.Commit();
                    return new CustomerPayment { Id = id, CustomerId = customerId, CustomerName = customer.Name, Reference = reference, Amount = amount, PaymentDate = parsed.ToString("yyyy-MM-dd"), Method = method, Account = account, Notes = notes ?? string.Empty, CreatedAt = now };
                }
            }
        }

        public CustomerStatement GetStatement(long customerId, string from, string to)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                var customer = connection.QuerySingleOrDefault<Customer>("SELECT Id, Name, Balance, CreditLimit FROM Customers WHERE Id=@Id", new { Id = customerId });
                if (customer == null) return null;
                var payments = GetPaymentsInternal(connection, customerId);
                var totalPayments = payments.Sum(x => x.Amount);
                var openingTotal = customer.Balance + totalPayments;
                DateTime fromDate, toDate;
                bool hasFrom = DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fromDate);
                bool hasTo = DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out toDate);
                var startBalance = openingTotal - payments.Where(p => hasFrom && string.CompareOrdinal(p.PaymentDate, fromDate.ToString("yyyy-MM-dd")) < 0).Sum(p => p.Amount);
                var statement = new CustomerStatement { CustomerId = customer.Id, CustomerName = customer.Name, From = hasFrom ? fromDate.ToString("yyyy-MM-dd") : null, To = hasTo ? toDate.ToString("yyyy-MM-dd") : null, OpeningBalance = startBalance, CreditLimit = customer.CreditLimit };
                statement.Movements.Add(new CustomerStatementMovement { Date = hasFrom ? fromDate.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd"), Reference = "OPEN", Description = "رصيد مرحل من قبل الفترة", Debit = 0, Credit = 0, Balance = startBalance });
                var inRange = payments.Where(p => (!hasFrom || string.CompareOrdinal(p.PaymentDate, fromDate.ToString("yyyy-MM-dd")) >= 0) && (!hasTo || string.CompareOrdinal(p.PaymentDate, toDate.ToString("yyyy-MM-dd")) <= 0)).OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToList();
                var balance = startBalance;
                foreach (var p in inRange)
                {
                    balance -= p.Amount;
                    statement.TotalCredit += p.Amount;
                    statement.Movements.Add(new CustomerStatementMovement { Date = p.PaymentDate, Reference = p.Reference, Description = "تحصيل دفعة من العميل - " + p.Method, Debit = 0, Credit = p.Amount, Balance = balance });
                }
                statement.ClosingBalance = balance;
                return statement;
            }
        }
        public List<CustomerPayment> GetPayments(long customerId) { using (var c = new SQLiteConnection(_connectionString)) return GetPaymentsInternal(c, customerId); }
        private static List<CustomerPayment> GetPaymentsInternal(SQLiteConnection connection, long customerId)
        {
            return connection.Query<CustomerPayment>("SELECT p.Id, p.CustomerId, p.Reference, p.Amount, p.PaymentDate, p.Method, p.Account, p.Notes, p.CreatedAt, c.Name AS CustomerName FROM CustomerPayments p JOIN Customers c ON c.Id=p.CustomerId WHERE p.CustomerId=@CustomerId ORDER BY p.PaymentDate, p.Id", new { CustomerId = customerId }).ToList();
        }
    }
}
