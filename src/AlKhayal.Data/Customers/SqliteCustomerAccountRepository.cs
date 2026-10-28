using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using AlKhayal.Application.Customers;
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
                    if(connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CustomerTransactions'",transaction:tx)>0)
                        connection.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc) VALUES(@CustomerId,@Reference,@Reference,'collection',@Scaled,@PaymentDate,@Notes,@CreatedAt)",new{CustomerId=customerId,Reference=reference,Scaled=checked((long)(amount*10000m)),PaymentDate=parsed.ToString("yyyy-MM-dd"),Notes=notes??string.Empty,CreatedAt=now},tx);
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
                if (connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CustomerTransactions'") > 0)
                    return GetLedgerStatement(connection, customer, from, to);
                var payments = GetPaymentsInternal(connection, customerId);
                var totalPayments = payments.Sum(x => x.Amount);
                var openingTotal = customer.Balance + totalPayments;
                DateTime fromDate, toDate;
                bool hasFrom = DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fromDate);
                bool hasTo = DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out toDate);
                var startBalance = openingTotal - payments.Where(p => hasFrom && string.CompareOrdinal(p.PaymentDate, fromDate.ToString("yyyy-MM-dd")) < 0).Sum(p => p.Amount);
                var statement = new CustomerStatement { CustomerId = customer.Id, CustomerName = customer.Name, From = hasFrom ? fromDate.ToString("yyyy-MM-dd") : null, To = hasTo ? toDate.ToString("yyyy-MM-dd") : null, OpeningBalance = startBalance, CurrentBalance = customer.Balance, CreditLimit = customer.CreditLimit };
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

        public CustomerStatementTransaction PostTransaction(long customerId,string type,decimal amount,string occurredOn,string notes,string operationKey)
        {
            using(var connection=new SQLiteConnection(_connectionString))
            {
                connection.Open();using(var tx=connection.BeginTransaction())
                {
                    var customer=connection.QuerySingleOrDefault<Customer>("SELECT Id,Name,Balance FROM Customers WHERE Id=@Id",new{Id=customerId},tx);
                    if(customer==null)throw new KeyNotFoundException("العميل غير موجود.");
                    // QA-101: the lookup stays keyed on OperationKey, but a hit is a
                    // successful replay only when the key is owned by this posting
                    // path and the incoming request matches the stored authoritative
                    // fields. Ownership is decided inside this same transaction from
                    // actual stored metadata, never from the shape (prefix) of the
                    // operation key; see IsSamePosting. Field comparison uses the
                    // scale-4 integer AmountScaled (never a floating-point value),
                    // the CustomerId, the transaction type, the occurrence date and
                    // the notes compared exactly (null→empty on both sides, no
                    // trimming). Stored rows keep their RequestHash as written
                    // (RequestHash==OperationKey for rows created by this path); no
                    // stored data is rewritten here.
                    var existing=connection.QueryFirstOrDefault<StoredPostingRow>("SELECT Id,CustomerId,TransactionType Type,AmountScaled,OccurredOn,Notes,Reference,SalesInvoiceId,SalesReturnId,RequestHash,OperationKey FROM CustomerTransactions WHERE OperationKey=@Key",new{Key=operationKey},tx);
                    if(existing!=null)
                    {
                        // QA-101: consulted inside this same transaction — an existing
                        // CustomerPayments row whose Reference equals the looked-up key
                        // means the key belongs to the customer-payment flow (AddPayment
                        // keys its ledger row by the payment reference while keeping its
                        // original INSERT, which never populates CustomerTransactions.
                        // Reference), so the key can never replay as a manual posting.
                        var legacyPaymentRows=connection.ExecuteScalar<long>(
                            "SELECT COUNT(*) FROM CustomerPayments p WHERE p.Reference=@Key",
                            new { Key = operationKey }, tx);
                        var replayedScaled=checked((long)(amount*10000m));
                        if(legacyPaymentRows!=0L||!IsSamePosting(existing,customerId,type,replayedScaled,occurredOn,notes))
                            throw new CustomerOperationKeyConflictException("مفتاح العملية مستخدم لطلب مختلف.");
                        tx.Commit();
                        return new CustomerStatementTransaction{Id=existing.Id,CustomerId=existing.CustomerId,Type=existing.Type,Amount=existing.AmountScaled/10000m,OccurredOn=existing.OccurredOn,Notes=existing.Notes,Replayed=true};
                    }
                    var total=connection.ExecuteScalar<long>("SELECT COALESCE(SUM(CASE WHEN TransactionType IN ('opening','increase') THEN AmountScaled ELSE -AmountScaled END),0) FROM CustomerTransactions WHERE CustomerId=@Id",new{Id=customerId},tx);
                    var scaled=checked((long)(amount*10000m));
                    if((type=="collection"||type=="reduction")&&total<scaled)throw new ArgumentException("المبلغ أكبر من رصيد العميل الحالي.");
                    var next=type=="increase"?checked(total+scaled):checked(total-scaled);
                    var now=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);
                    connection.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc) VALUES(@CustomerId,@OperationKey,@OperationKey,@Type,@Scaled,@OccurredOn,@Notes,@Now)",new{CustomerId=customerId,OperationKey=operationKey,Type=type,Scaled=scaled,OccurredOn=occurredOn,Notes=notes??string.Empty,Now=now},tx);
                    var id=connection.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);
                    connection.Execute("UPDATE Customers SET Balance=@Balance,UpdatedAt=@Now WHERE Id=@Id",new{Balance=next/10000m,Now=now,Id=customerId},tx);
                    tx.Commit();return new CustomerStatementTransaction{Id=id,CustomerId=customerId,Type=type,Amount=amount,OccurredOn=occurredOn,Notes=notes??string.Empty};
                }
            }
        }

        private static CustomerStatement GetLedgerStatement(SQLiteConnection connection,Customer customer,string from,string to)
        {
            DateTime fromDate,toDate;var hasFrom=DateTime.TryParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out fromDate);var hasTo=DateTime.TryParseExact(to,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out toDate);
            var all=connection.Query<LedgerRow>("SELECT Id,TransactionType Type,AmountScaled,OccurredOn,Notes,Reference FROM CustomerTransactions WHERE CustomerId=@Id ORDER BY OccurredOn,Id",new{Id=customer.Id}).ToList();
            var cutoff=hasFrom?fromDate.ToString("yyyy-MM-dd"):null;long running=all.Where(x=>cutoff!=null&&string.CompareOrdinal(x.OccurredOn,cutoff)<0).Sum(Signed);
            var statement=new CustomerStatement{CustomerId=customer.Id,CustomerName=customer.Name,CurrentBalance=all.Sum(Signed)/10000m,From=hasFrom?cutoff:null,To=hasTo?toDate.ToString("yyyy-MM-dd"):null,OpeningBalance=running/10000m,CreditLimit=customer.CreditLimit};
            statement.Movements.Add(new CustomerStatementMovement{Date=hasFrom?cutoff:"—",Description="رصيد افتتاحي",Balance=statement.OpeningBalance,Kind="opening"});
            foreach(var row in all.Where(x=>(!hasFrom||string.CompareOrdinal(x.OccurredOn,cutoff)>=0)&&(!hasTo||string.CompareOrdinal(x.OccurredOn,toDate.ToString("yyyy-MM-dd"))<=0)))
            {
                var add=row.Type=="opening"||row.Type=="increase";running=checked(running+Signed(row));var amount=row.AmountScaled/10000m;
                if(add)statement.TotalDebit+=amount;else statement.TotalCredit+=amount;
                statement.Movements.Add(new CustomerStatementMovement{Date=row.OccurredOn,Reference=row.Reference,Notes=row.Notes,Description=row.Type=="reduction"&&row.Notes!=null&&row.Notes.StartsWith("إلغاء فاتورة بيع",StringComparison.Ordinal)?"إلغاء فاتورة بيع":Description(row.Type,row.Reference),Debit=add?amount:0,Credit=add?0:amount,Balance=running/10000m,Kind=row.Type});
            }
            statement.ClosingBalance=running/10000m;return statement;
        }
        private sealed class LedgerRow{public long Id{get;set;}public string Type{get;set;}public long AmountScaled{get;set;}public string OccurredOn{get;set;}public string Notes{get;set;}public string Reference{get;set;}}
        private sealed class StoredPostingRow{public long Id{get;set;}public long CustomerId{get;set;}public string Type{get;set;}public long AmountScaled{get;set;}public string OccurredOn{get;set;}public string Notes{get;set;}public string Reference{get;set;}public long? SalesInvoiceId{get;set;}public long? SalesReturnId{get;set;}public string RequestHash{get;set;}public string OperationKey{get;set;}}
        private static long Signed(LedgerRow x){return x.Type=="opening"||x.Type=="increase"?x.AmountScaled:-x.AmountScaled;}

        // QA-101: replay only an identical retry of a request this posting path
        // itself wrote, decided purely from stored ownership metadata — never from
        // the shape (prefix) of the operation key, because a manually chosen key
        // may legitimately begin with "CP-" or "sale-" and still replays
        // identically whenever it matches this path's own row. Ownership checks,
        // per actual writer behavior:
        //   - rows written here always use one of the three posting types
        //     ('increase'/'collection'/'reduction'); 'opening'/'legacy_payment'
        //     rows are migration-generated, never written by this path (the
        //     writer always supplies an operation key, so this guard is
        //     defensive);
        //   - rows written by other financial paths (sales invoices/collections/
        //     returns) populate the provenance columns added by migration 020
        //     (Reference, SalesInvoiceId, SalesReturnId); this path and AddPayment
        //     never do — AddPayment's INSERT is unchanged and leaves Reference
        //     NULL, so column inspection alone cannot identify customer payments;
        //   - AddPayment keys its ledger row by the payment reference, proven by
        //     the CustomerPayments.Reference equality query the caller runs in the
        //     same transaction;
        //   - this writer records RequestHash==OperationKey; a sales writer
        //     records its payload hash instead.
        // Field comparison is exact: CustomerId, transaction type (ordinal), the
        // scale-4 integer AmountScaled (money never compared as floating point),
        // the occurrence date (ordinal, already normalized by the service), and
        // notes (null→empty on both sides, whitespace never trimmed, so any
        // whitespace-only difference is a conflict).
        private static bool IsSamePosting(StoredPostingRow stored,long customerId,string type,long scaled,string occurredOn,string notes)
        {
            if(stored.Type!="increase"&&stored.Type!="collection"&&stored.Type!="reduction")return false;
            if(!string.IsNullOrEmpty(stored.Reference))return false;
            if(stored.SalesInvoiceId.HasValue&&stored.SalesInvoiceId.Value!=0)return false;
            if(stored.SalesReturnId.HasValue&&stored.SalesReturnId.Value!=0)return false;
            if(!string.Equals(stored.RequestHash,stored.OperationKey,StringComparison.Ordinal))return false;
            return stored.CustomerId==customerId
                &&string.Equals(stored.Type,type,StringComparison.Ordinal)
                &&stored.AmountScaled==scaled
                &&string.Equals(stored.OccurredOn??string.Empty,occurredOn??string.Empty,StringComparison.Ordinal)
                &&string.Equals(stored.Notes??string.Empty,notes??string.Empty,StringComparison.Ordinal);
        }
        private static string Description(string type,string reference){return type=="opening"?"رصيد مرحّل من البيانات القديمة":type=="increase"?(string.IsNullOrWhiteSpace(reference)?"مبلغ اتضاف":"فاتورة بيع"):type=="collection"?(string.IsNullOrWhiteSpace(reference)?"مبلغ اتدفع":"تحصيل فاتورة بيع"):type=="reduction"?(string.IsNullOrWhiteSpace(reference)?"تخفيض الرصيد":"إرجاع فاتورة بيع"):"رصيد مرحّل من البيانات القديمة";}
        public List<CustomerPayment> GetPayments(long customerId) { using (var c = new SQLiteConnection(_connectionString)) return GetPaymentsInternal(c, customerId); }
        private static List<CustomerPayment> GetPaymentsInternal(SQLiteConnection connection, long customerId)
        {
            return connection.Query<CustomerPayment>("SELECT p.Id, p.CustomerId, p.Reference, p.Amount, p.PaymentDate, p.Method, p.Account, p.Notes, p.CreatedAt, c.Name AS CustomerName FROM CustomerPayments p JOIN Customers c ON c.Id=p.CustomerId WHERE p.CustomerId=@CustomerId ORDER BY p.PaymentDate, p.Id", new { CustomerId = customerId }).ToList();
        }
    }
}
