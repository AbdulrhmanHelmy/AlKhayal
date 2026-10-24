using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Dapper;

namespace AlKhayal.Data.Suppliers
{
    public sealed class SqliteSupplierLedgerRepository : ISupplierLedgerRepository
    {
        private readonly string _connectionString;
        public SqliteSupplierLedgerRepository(string connectionString) { _connectionString = connectionString; }

        public async Task<SupplierStatement> StatementAsync(long supplierId, string from, string to, CancellationToken ct)
        {
            using (var c = Open())
            {
                var supplier = await c.QuerySingleOrDefaultAsync<Supplier>(Cmd("SELECT Id,Name,Payable,SupplierCredit FROM Suppliers WHERE Id=@Id", new { Id = supplierId }, null, ct)).ConfigureAwait(false);
                if (supplier == null) return null;
                var all = (await c.QueryAsync<SupplierLedgerEntry>(Cmd(EntryQuery + " WHERE e.SupplierId=@Id ORDER BY e.OccurredOn,e.Id", new { Id = supplierId }, null, ct)).ConfigureAwait(false)).ToList();
                var opening = all.Where(x => from != null && string.CompareOrdinal(x.OccurredOn, from) < 0).Sum(x => x.AmountScaled);
                var statement = new SupplierStatement { SupplierId = supplier.Id, SupplierName = supplier.Name, From = from, To = to, OpeningBalance = opening / 10000m };
                var balance = opening;
                foreach (var entry in all.Where(x => (from == null || string.CompareOrdinal(x.OccurredOn, from) >= 0) && (to == null || string.CompareOrdinal(x.OccurredOn, to) <= 0)))
                {
                    balance = checked(balance + entry.AmountScaled);
                    if (entry.AmountScaled > 0) statement.TotalInvoices += entry.AmountScaled / 10000m;
                    else statement.TotalPaid += -entry.AmountScaled / 10000m;
                    statement.Movements.Add(new SupplierStatementMovement { Date = entry.OccurredOn, Reference = entry.Reference,
                        Description = Description(entry), Debit = entry.AmountScaled < 0 ? -entry.AmountScaled / 10000m : 0,
                        Credit = entry.AmountScaled > 0 ? entry.AmountScaled / 10000m : 0, Balance = balance / 10000m });
                }
                statement.ClosingBalance = balance / 10000m;
                return statement;
            }
        }

        public async Task<IReadOnlyList<SupplierLedgerEntry>> HistoryAsync(long supplierId, CancellationToken ct)
        {
            using (var c = Open()) return (await c.QueryAsync<SupplierLedgerEntry>(Cmd(EntryQuery + " WHERE e.SupplierId=@Id ORDER BY e.OccurredOn DESC,e.Id DESC", new { Id = supplierId }, null, ct)).ConfigureAwait(false)).ToList();
        }

        public async Task<SupplierLedgerEntry> AddPaymentAsync(SupplierLedgerPaymentCommand command, CancellationToken ct)
        {
            using (var c = Open()) using (var tx = c.BeginTransaction())
            {
                var replay = await c.QuerySingleOrDefaultAsync<SupplierLedgerEntry>(Cmd(EntryQuery + " WHERE e.OperationKey=@Key", new { Key = command.OperationKey }, tx, ct)).ConfigureAwait(false);
                if (replay != null)
                {
                    if (!string.Equals(replay.RequestHash, command.RequestHash, StringComparison.Ordinal)) throw new InvalidOperationException("مفتاح العملية مستخدم لدفعة مختلفة.");
                    replay.Replayed = true; tx.Commit(); return replay;
                }
                var supplier = await c.QuerySingleOrDefaultAsync<SupplierBalance>(Cmd("SELECT Payable,SupplierCredit FROM Suppliers WHERE Id=@Id", new { Id = command.SupplierId }, tx, ct)).ConfigureAwait(false);
                if (supplier == null) throw new KeyNotFoundException("المورد غير موجود.");
                if (command.SupplierInvoiceId.HasValue)
                {
                    var invoice = await c.QuerySingleOrDefaultAsync<LegacyInvoice>(Cmd(@"SELECT i.Id,i.Total,i.PaidAmount+COALESCE((SELECT -SUM(e.AmountScaled)/10000.0 FROM SupplierLedgerEntries e WHERE e.SupplierInvoiceId=i.Id AND e.EntryType='payment'),0) PaidAmount
                        FROM SupplierInvoices i WHERE i.Id=@Id AND i.SupplierId=@SupplierId", new { Id = command.SupplierInvoiceId.Value, SupplierId = command.SupplierId }, tx, ct)).ConfigureAwait(false);
                    if (invoice == null) throw new KeyNotFoundException("الفاتورة غير موجودة لهذا المورد.");
                    if (command.AmountScaled > Scale(invoice.Total - invoice.PaidAmount)) throw new ArgumentException("قيمة الدفعة أكبر من المتبقي على الفاتورة.");
                }
                else if (command.PurchaseInvoiceId.HasValue)
                {
                    var invoice = await c.QuerySingleOrDefaultAsync<PurchaseBalance>(Cmd(@"SELECT p.Id,p.TotalScaled-COALESCE((SELECT -SUM(e.AmountScaled) FROM SupplierLedgerEntries e WHERE e.PurchaseInvoiceId=p.Id AND e.EntryType='payment'),0) Remaining
                        FROM PurchaseInvoices p WHERE p.Id=@Id AND p.SupplierId=@SupplierId AND p.Status='posted'", new { Id = command.PurchaseInvoiceId.Value, SupplierId = command.SupplierId }, tx, ct)).ConfigureAwait(false);
                    if (invoice == null) throw new KeyNotFoundException("فاتورة الشراء غير موجودة لهذا المورد.");
                    if (command.AmountScaled > invoice.Remaining) throw new ArgumentException("قيمة الدفعة أكبر من المتبقي على الفاتورة.");
                }
                var net = checked(Scale(supplier.Payable) - Scale(supplier.SupplierCredit));
                if (command.AmountScaled > net) throw new ArgumentException("قيمة الدفعة أكبر من المستحق الحالي للمورد.");
                var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                await c.ExecuteAsync(Cmd(@"INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,SupplierInvoiceId,PurchaseInvoiceId,PaymentMethod,Notes,OperationKey,RequestHash,CreatedUtc)
                    VALUES(@SupplierId,'payment',@Amount,@Date,@Reference,@SupplierInvoiceId,@PurchaseInvoiceId,@Method,@Notes,@Key,@Hash,@Now)", new { command.SupplierId, Amount = -command.AmountScaled, Date = command.PaymentDate, Reference = "PAY-" + command.OperationKey, command.SupplierInvoiceId, command.PurchaseInvoiceId, Method = string.IsNullOrEmpty(command.Account) ? command.Method : command.Method + " — " + command.Account, command.Notes, Key = command.OperationKey, Hash = command.RequestHash, Now = now }, tx, ct)).ConfigureAwait(false);
                var newNet = net - command.AmountScaled;
                await c.ExecuteAsync(Cmd("UPDATE Suppliers SET Payable=@Payable,SupplierCredit=@Credit,UpdatedAt=@Now WHERE Id=@Id", new { Payable = Math.Max(newNet, 0) / 10000m, Credit = Math.Max(-newNet, 0) / 10000m, Now = now, Id = command.SupplierId }, tx, ct)).ConfigureAwait(false);
                tx.Commit();
                return new SupplierLedgerEntry { SupplierId = command.SupplierId, EntryType = "payment", AmountScaled = -command.AmountScaled, OccurredOn = command.PaymentDate, Reference = "PAY-" + command.OperationKey, Notes = command.Notes, SupplierInvoiceId = command.SupplierInvoiceId, PurchaseInvoiceId = command.PurchaseInvoiceId, PaymentMethod = command.Method, Replayed = false };
            }
        }

        private const string EntryQuery = @"SELECT e.Id,e.SupplierId,e.EntryType,e.AmountScaled,e.OccurredOn,e.Reference,e.Notes,e.PurchaseInvoiceId,e.SupplierInvoiceId,e.PaymentMethod,e.RequestHash,
            p.InvoiceNumber PurchaseInvoiceNumber FROM SupplierLedgerEntries e LEFT JOIN PurchaseInvoices p ON p.Id=e.PurchaseInvoiceId";
        private static string Description(SupplierLedgerEntry e)
        {
            switch (e.EntryType)
            {
                case "opening": return "رصيد افتتاحي مرحّل";
                case "legacy_invoice": return "فاتورة تاريخية";
                case "legacy_payment": return "دفعة تاريخية";
                case "purchase": return "فاتورة شراء " + (e.PurchaseInvoiceNumber ?? string.Empty);
                case "payment": return "دفعة للمورد" + (string.IsNullOrEmpty(e.PaymentMethod) ? string.Empty : " — " + e.PaymentMethod);
                case "purchase_return_credit": return "مرتجع شراء — رصيد لدى المورد";
                case "purchase_return_refund": return "مرتجع شراء — مبلغ مسترد مسجل (دون حركة خزينة)";
                default: return "حركة حساب المورد";
            }
        }
        private static long Scale(decimal value) { var scaled = value * 10000m; if (scaled != decimal.Truncate(scaled)) throw new InvalidOperationException("رصيد المورد غير ممثل بدقة أربع خانات عشرية."); return checked((long)scaled); }
        private SQLiteConnection Open() { var c = new SQLiteConnection(_connectionString); c.Open(); using (var cmd = new SQLiteCommand("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", c)) cmd.ExecuteNonQuery(); return c; }
        private static CommandDefinition Cmd(string sql, object args, SQLiteTransaction tx, CancellationToken ct) { return new CommandDefinition(sql, args, tx, cancellationToken: ct); }
        private sealed class SupplierBalance { public decimal Payable { get; set; } public decimal SupplierCredit { get; set; } }
        private sealed class LegacyInvoice { public long Id { get; set; } public decimal Total { get; set; } public decimal PaidAmount { get; set; } }
        private sealed class PurchaseBalance { public long Id { get; set; } public long Remaining { get; set; } }
    }
}
