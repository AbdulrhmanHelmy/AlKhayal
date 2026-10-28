using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Dapper;

namespace AlKhayal.Data.Suppliers
{
    public sealed class SqliteSupplierAccountRepository : ISupplierAccountRepository
    {
        private const string PaidStatus = "مسدد";
        private const string PartialStatus = "مسدد جزئيًا";
        private const string OpenStatus = "غير مسدد";
        private readonly string _connectionString;
        public SqliteSupplierAccountRepository(string connectionString) { _connectionString = connectionString; }

        public SupplierInvoice AddInvoice(long supplierId, string invoiceNumber, string invoiceDate, string dueDate, decimal total, string notes, string operationKey = null, string requestHash = null, long? actorUserId = null)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var tx = connection.BeginTransaction())
                {
                    if (operationKey != null)
                    {
                        var previous = connection.QuerySingleOrDefault<SupplierLedgerEntry>("SELECT * FROM SupplierLedgerEntries WHERE OperationKey=@operationKey", new { operationKey }, tx);
                        if (previous != null)
                        {
                            if (previous.SupplierId != supplierId || previous.EntryType != "legacy_invoice" || !previous.SupplierInvoiceId.HasValue || previous.RequestHash != requestHash)
                                throw new InvalidOperationException("مفتاح العملية مستخدم لفاتورة أو حركة مختلفة.");
                            var saved = connection.QuerySingle<SupplierInvoice>("SELECT * FROM SupplierInvoices WHERE Id=@Id AND SupplierId=@supplierId", new { Id = previous.SupplierInvoiceId.Value, supplierId }, tx);
                            saved.Replayed = true; saved.DocumentStatus = "posted"; saved.InvoiceType = "legacy";
                            tx.Commit(); return Shape(saved);
                        }
                    }
                    var supplier = connection.QuerySingleOrDefault<Supplier>("SELECT Id, Name, Payable, SupplierCredit FROM Suppliers WHERE Id=@Id", new { Id = supplierId }, tx);
                    if (supplier == null) throw new KeyNotFoundException("المورد غير موجود.");
                    if (total <= 0) throw new ArgumentException("إجمالي الفاتورة يجب أن يكون أكبر من صفر.");
                    var number = string.IsNullOrWhiteSpace(invoiceNumber) ? "SI-" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant() : invoiceNumber;
                    var duplicates = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierInvoices WHERE SupplierId=@SupplierId AND InvoiceNumber=@Number", new { SupplierId = supplierId, Number = number }, tx);
                    if (duplicates > 0) throw new ArgumentException("رقم الفاتورة مسجل بالفعل لهذا المورد.");
                    var now = DateTime.UtcNow.ToString("o");
                    var id = connection.ExecuteScalar<long>("INSERT INTO SupplierInvoices(SupplierId, InvoiceNumber, InvoiceDate, DueDate, Total, PaidAmount, Notes, CreatedAt) VALUES(@SupplierId,@Number,@InvoiceDate,@DueDate,@Total,0,@Notes,@Now); SELECT last_insert_rowid();", new { SupplierId = supplierId, Number = number, InvoiceDate = invoiceDate, DueDate = dueDate, Total = total, Notes = notes ?? string.Empty, Now = now }, tx);
                    var newNet = checked(supplier.Payable - supplier.SupplierCredit + total);
                    var checkedNetScaled = checked((long)(newNet * 10000m));
                    var updated = connection.Execute("UPDATE Suppliers SET Payable=@Payable,SupplierCredit=@Credit, UpdatedAt=@Now WHERE Id=@Id", new { Payable = Math.Max(newNet,0), Credit = Math.Max(-newNet,0), Now = now, Id = supplierId }, tx);
                    if (updated != 1) throw new InvalidOperationException("تعذر تحديث رصيد المورد؛ لم يتم اعتماد الفاتورة.");
                    connection.Execute("INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,SupplierInvoiceId,OperationKey,RequestHash,Notes,CreatedUtc) VALUES(@SupplierId,'legacy_invoice',@Amount,@Date,@Reference,@id,@operationKey,@requestHash,@Notes,@Now)", new { SupplierId = supplierId, Amount = checked((long)(total * 10000m)), Date = invoiceDate, Reference = "SI-" + id, id, operationKey, requestHash, Notes = "فاتورة مديونية مورد" + (actorUserId.HasValue ? " — سجلها المستخدم " + actorUserId.Value : ""), Now = now }, tx);
                    tx.Commit();
                    return Shape(new SupplierInvoice { Id = id, SupplierId = supplierId, InvoiceNumber = number, InvoiceDate = invoiceDate, DueDate = dueDate, Total = total, PaidAmount = 0, Notes = notes ?? string.Empty, CreatedAt = now, InvoiceType = "legacy", DocumentStatus = "posted" });
                }
            }
        }

        public SupplierPayment AddPayment(long supplierId, long? invoiceId, decimal amount, string paymentDate, string method, string account, string notes)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var tx = connection.BeginTransaction())
                {
                    var supplier = connection.QuerySingleOrDefault<Supplier>("SELECT Id, Name, Payable FROM Suppliers WHERE Id=@Id", new { Id = supplierId }, tx);
                    if (supplier == null) throw new KeyNotFoundException("المورد غير موجود.");
                    if (amount <= 0) throw new ArgumentException("قيمة الدفعة يجب أن تكون أكبر من صفر.");
                    if (amount > supplier.Payable) throw new ArgumentException("قيمة الدفعة أكبر من المستحق الحالي للمورد.");
                    var now = DateTime.UtcNow.ToString("o");
                    string invoiceNumber = null;
                    if (invoiceId.HasValue)
                    {
                        var invoice = connection.QuerySingleOrDefault<SupplierInvoice>("SELECT Id, InvoiceNumber, Total, PaidAmount FROM SupplierInvoices WHERE Id=@Id AND SupplierId=@SupplierId", new { Id = invoiceId.Value, SupplierId = supplierId }, tx);
                        if (invoice == null) throw new KeyNotFoundException("الفاتورة غير موجودة لهذا المورد.");
                        var remaining = Math.Round(invoice.Total - invoice.PaidAmount, 2, MidpointRounding.AwayFromZero);
                        if (amount > remaining) throw new ArgumentException("قيمة الدفعة أكبر من المتبقي على الفاتورة.");
                        var newPaid = Math.Round(invoice.PaidAmount + amount, 2, MidpointRounding.AwayFromZero);
                        var invoiceUpdated = connection.Execute("UPDATE SupplierInvoices SET PaidAmount=@Paid WHERE Id=@Id AND SupplierId=@SupplierId", new { Paid = newPaid, Id = invoiceId.Value, SupplierId = supplierId }, tx);
                        if (invoiceUpdated != 1) throw new InvalidOperationException("تعذر تحديث الفاتورة؛ لم يتم اعتماد الدفعة.");
                        invoiceNumber = invoice.InvoiceNumber;
                    }
                    var reference = "SP-" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant();
                    var id = connection.ExecuteScalar<long>("INSERT INTO SupplierPayments(SupplierId, InvoiceId, Reference, Amount, PaymentDate, Method, Account, Notes, CreatedAt) VALUES(@SupplierId,@InvoiceId,@Reference,@Amount,@PaymentDate,@Method,@Account,@Notes,@Now); SELECT last_insert_rowid();", new { SupplierId = supplierId, InvoiceId = invoiceId, Reference = reference, Amount = amount, PaymentDate = paymentDate, Method = method, Account = account, Notes = notes ?? string.Empty, Now = now }, tx);
                    var newPayable = Math.Round(supplier.Payable - amount, 2, MidpointRounding.AwayFromZero);
                    var updated = connection.Execute("UPDATE Suppliers SET Payable=@Payable, UpdatedAt=@Now WHERE Id=@Id", new { Payable = newPayable, Now = now, Id = supplierId }, tx);
                    if (updated != 1) throw new InvalidOperationException("تعذر تحديث رصيد المورد؛ لم يتم اعتماد الدفعة.");
                    connection.Execute("INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,Notes,CreatedUtc) VALUES(@SupplierId,'legacy_payment',@Amount,@Date,@Reference,@Notes,@Now)", new { SupplierId = supplierId, Amount = -checked((long)Math.Round(amount * 10000m, 0, MidpointRounding.AwayFromZero)), Date = paymentDate, Reference = reference, Notes = "دفعة مورد محفوظة عبر سجل المدفوعات السابق", Now = now }, tx);
                    tx.Commit();
                    return new SupplierPayment { Id = id, SupplierId = supplierId, InvoiceId = invoiceId, InvoiceNumber = invoiceNumber, SupplierName = supplier.Name, Reference = reference, Amount = amount, PaymentDate = paymentDate, Method = method, Account = account, Notes = notes ?? string.Empty, CreatedAt = now };
                }
            }
        }

        public List<SupplierInvoice> GetInvoices(long supplierId)
        {
            using (var connection = new SQLiteConnection(_connectionString)) return GetInvoicesInternal(connection, supplierId);
        }

        public List<SupplierPayment> GetPayments(long supplierId)
        {
            using (var connection = new SQLiteConnection(_connectionString)) return GetPaymentsInternal(connection, supplierId);
        }

        public SupplierStatement GetStatement(long supplierId, string from, string to)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                var supplier = connection.QuerySingleOrDefault<Supplier>("SELECT Id, Name, Payable FROM Suppliers WHERE Id=@Id", new { Id = supplierId });
                if (supplier == null) return null;
                var invoices = GetInvoicesInternal(connection, supplierId);
                var payments = GetPaymentsInternal(connection, supplierId);
                // Balance before any recorded movement = current payable - invoices + payments.
                var baseOpening = Round(supplier.Payable - invoices.Sum(i => i.Total) + payments.Sum(p => p.Amount));
                bool hasFrom = !string.IsNullOrEmpty(from), hasTo = !string.IsNullOrEmpty(to);
                Func<string, bool> before = date => hasFrom && string.CompareOrdinal(date, from) < 0;
                Func<string, bool> inRange = date => !before(date) && (!hasTo || string.CompareOrdinal(date, to) <= 0);
                var startBalance = Round(baseOpening + invoices.Where(i => before(i.InvoiceDate)).Sum(i => i.Total) - payments.Where(p => before(p.PaymentDate)).Sum(p => p.Amount));
                var statement = new SupplierStatement { SupplierId = supplier.Id, SupplierName = supplier.Name, From = hasFrom ? from : null, To = hasTo ? to : null, OpeningBalance = startBalance };
                statement.Movements.Add(new SupplierStatementMovement { Date = hasFrom ? from : DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Reference = "OPEN", Description = "رصيد مرحل من قبل الفترة", Debit = 0, Credit = 0, Balance = startBalance });
                var entries = invoices.Where(i => inRange(i.InvoiceDate)).Select(i => new { Date = i.InvoiceDate, Kind = 0, i.Id, Reference = i.InvoiceNumber, Description = "فاتورة شراء آجلة", Debit = 0m, Credit = i.Total })
                    .Concat(payments.Where(p => inRange(p.PaymentDate)).Select(p => new { Date = p.PaymentDate, Kind = 1, p.Id, Reference = p.Reference, Description = "سداد للمورد - " + p.Method + (string.IsNullOrEmpty(p.InvoiceNumber) ? string.Empty : " (فاتورة " + p.InvoiceNumber + ")"), Debit = p.Amount, Credit = 0m }))
                    .OrderBy(e => e.Date, StringComparer.Ordinal).ThenBy(e => e.Kind).ThenBy(e => e.Id).ToList();
                var balance = startBalance;
                foreach (var e in entries)
                {
                    balance = Round(balance + e.Credit - e.Debit);
                    statement.TotalInvoices += e.Credit;
                    statement.TotalPaid += e.Debit;
                    statement.Movements.Add(new SupplierStatementMovement { Date = e.Date, Reference = e.Reference, Description = e.Description, Debit = e.Debit, Credit = e.Credit, Balance = balance });
                }
                statement.ClosingBalance = balance;
                return statement;
            }
        }

        internal static List<SupplierInvoice> GetInvoicesInternal(SQLiteConnection connection, long supplierId, SQLiteTransaction tx = null)
        {
            // SQLite UNION expressions can change numeric provider types between rows. Read exact
            // decimal/integer text and convert in C# instead of relying on binary REAL or Dapper casts.
            var records = connection.Query<InvoiceReadRecord>(@"SELECT i.Id,i.SupplierId,i.InvoiceNumber,i.InvoiceDate,i.DueDate,
                CAST(i.Total AS TEXT) TotalText,CAST(i.PaidAmount AS TEXT) LegacyPaidText,
                CAST(COALESCE((SELECT -SUM(l.AmountScaled) FROM SupplierLedgerEntries l WHERE l.SupplierInvoiceId=i.Id AND l.EntryType='payment'),0)+COALESCE((SELECT SUM(a.AmountScaled) FROM SupplierPaymentAllocations a JOIN SupplierLedgerEntries e ON e.Id=a.SupplierLedgerEntryId WHERE a.SupplierInvoiceId=i.Id AND e.SupplierInvoiceId IS NULL AND e.PurchaseInvoiceId IS NULL),0) AS TEXT) PaymentScaledText,
                i.Notes,i.CreatedAt,'legacy' InvoiceType,NULL PurchaseInvoiceId,'posted' DocumentStatus,'0' ReturnedScaledText
                FROM SupplierInvoices i WHERE i.SupplierId=@SupplierId
                UNION ALL
                SELECT p.Id,p.SupplierId,p.InvoiceNumber,p.InvoiceDate,NULL,CAST(p.TotalScaled AS TEXT),'0',
                    CAST(COALESCE((SELECT -SUM(l.AmountScaled) FROM SupplierLedgerEntries l WHERE l.PurchaseInvoiceId=p.Id AND l.EntryType='payment'),0)+COALESCE((SELECT SUM(a.AmountScaled) FROM SupplierPaymentAllocations a JOIN SupplierLedgerEntries e ON e.Id=a.SupplierLedgerEntryId WHERE a.PurchaseInvoiceId=p.Id AND e.SupplierInvoiceId IS NULL AND e.PurchaseInvoiceId IS NULL),0) AS TEXT),
                    p.Notes,p.CreatedUtc,'purchase',p.Id,p.Status,
                    CAST(COALESCE((SELECT SUM(r.TotalScaled) FROM PurchaseReturns r WHERE r.PurchaseInvoiceId=p.Id AND r.Status='posted'),0) AS TEXT)
                FROM PurchaseInvoices p WHERE p.SupplierId=@SupplierId ORDER BY InvoiceDate DESC,Id DESC", new { SupplierId=supplierId },tx);
            return records.Select(row => Shape(new SupplierInvoice {
                Id=row.Id,SupplierId=row.SupplierId,InvoiceNumber=row.InvoiceNumber,InvoiceDate=row.InvoiceDate,DueDate=row.DueDate,
                Total=Exact(row.TotalText)/(row.InvoiceType=="purchase"?10000m:1m),
                PaidAmount=Exact(row.LegacyPaidText)+Exact(row.PaymentScaledText)/10000m,
                ReturnedAmount=Exact(row.ReturnedScaledText)/10000m,Notes=row.Notes,CreatedAt=row.CreatedAt,
                InvoiceType=row.InvoiceType,PurchaseInvoiceId=row.PurchaseInvoiceId,DocumentStatus=row.DocumentStatus
            })).ToList();
        }
        private static decimal Exact(string text) => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        private sealed class InvoiceReadRecord
        {
            public long Id { get; set; } public long SupplierId { get; set; } public string InvoiceNumber { get; set; }
            public string InvoiceDate { get; set; } public string DueDate { get; set; } public string TotalText { get; set; }
            public string LegacyPaidText { get; set; } public string PaymentScaledText { get; set; } public string ReturnedScaledText { get; set; }
            public string Notes { get; set; } public string CreatedAt { get; set; } public string InvoiceType { get; set; }
            public long? PurchaseInvoiceId { get; set; } public string DocumentStatus { get; set; }
        }
        private static List<SupplierPayment> GetPaymentsInternal(SQLiteConnection connection, long supplierId)
        {
            return connection.Query<SupplierPayment>(@"SELECT p.Id,p.SupplierId,p.InvoiceId,p.Reference,p.Amount,p.PaymentDate,p.Method,p.Account,p.Notes,p.CreatedAt,s.Name SupplierName,i.InvoiceNumber
                FROM SupplierPayments p JOIN Suppliers s ON s.Id=p.SupplierId LEFT JOIN SupplierInvoices i ON i.Id=p.InvoiceId WHERE p.SupplierId=@SupplierId
                UNION ALL
                SELECT -e.Id,e.SupplierId,e.SupplierInvoiceId,e.Reference,-e.AmountScaled/10000.0,e.OccurredOn,e.PaymentMethod,'',e.Notes,e.CreatedUtc,s.Name,COALESCE(i.InvoiceNumber,pi.InvoiceNumber)
                FROM SupplierLedgerEntries e JOIN Suppliers s ON s.Id=e.SupplierId LEFT JOIN SupplierInvoices i ON i.Id=e.SupplierInvoiceId LEFT JOIN PurchaseInvoices pi ON pi.Id=e.PurchaseInvoiceId
                WHERE e.SupplierId=@SupplierId AND e.EntryType='payment' ORDER BY 6,1", new { SupplierId = supplierId }).ToList();
        }

        private static SupplierInvoice Shape(SupplierInvoice invoice)
        {
            invoice.Total = Round4(invoice.Total);
            invoice.PaidAmount = Round4(invoice.PaidAmount);
            invoice.Remaining = Math.Max(0,Round4(invoice.Total - invoice.PaidAmount - invoice.ReturnedAmount));
            invoice.Status = invoice.Remaining <= 0 ? PaidStatus : invoice.PaidAmount > 0 ? PartialStatus : OpenStatus;
            return invoice;
        }

        private static decimal Round(decimal value) { return Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        private static decimal Round4(decimal value) { return Math.Round(value, 4, MidpointRounding.AwayFromZero); }
    }
}
