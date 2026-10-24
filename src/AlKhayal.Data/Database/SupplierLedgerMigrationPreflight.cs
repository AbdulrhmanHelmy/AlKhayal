using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AlKhayal.Data.Database
{
    internal static class SupplierLedgerMigrationPreflight
    {
        public static void Run(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var suppliers = ReadSuppliers(connection, transaction);
            var invoices = ReadInvoices(connection, transaction);
            var payments = ReadPayments(connection, transaction);

            foreach (var payment in payments)
            {
                if (payment.AmountScaled <= 0) Fail("historical payment " + payment.Id + " has a non-positive or unsupported amount");
                Invoice invoice;
                if (payment.InvoiceId.HasValue && (!invoices.TryGetValue(payment.InvoiceId.Value, out invoice) || invoice.SupplierId != payment.SupplierId))
                    Fail("historical payment " + payment.Id + " is linked to an invoice owned by another supplier");
            }
            foreach (var invoice in invoices.Values)
            {
                if (invoice.TotalScaled <= 0 || invoice.PaidScaled < 0 || invoice.PaidScaled > invoice.TotalScaled) Fail("historical invoice " + invoice.Id + " has invalid totals");
                long linked = 0;
                foreach (var payment in payments) if (payment.InvoiceId == invoice.Id) linked = checked(linked + payment.AmountScaled);
                if (linked != invoice.PaidScaled) Fail("historical invoice " + invoice.Id + " paid total does not match its payments");
            }
            foreach (var supplier in suppliers.Values)
            {
                if (supplier.PayableScaled < 0) Fail("supplier " + supplier.Id + " has a negative payable");
                long invoiceTotal = 0, paymentTotal = 0;
                foreach (var invoice in invoices.Values) if (invoice.SupplierId == supplier.Id) invoiceTotal = checked(invoiceTotal + invoice.TotalScaled);
                foreach (var payment in payments) if (payment.SupplierId == supplier.Id) paymentTotal = checked(paymentTotal + payment.AmountScaled);
                if (checked(supplier.PayableScaled - invoiceTotal + paymentTotal) < 0) Fail("supplier " + supplier.Id + " has a negative derived opening balance");
            }
        }

        private static Dictionary<long, Supplier> ReadSuppliers(SQLiteConnection c, SQLiteTransaction tx)
        {
            var result = new Dictionary<long, Supplier>();
            using (var command = new SQLiteCommand("SELECT Id, CAST(Payable AS TEXT) FROM Suppliers", c, tx))
            using (var reader = command.ExecuteReader()) while (reader.Read())
            { var id = reader.GetInt64(0); result.Add(id, new Supplier { Id = id, PayableScaled = Scale(reader.IsDBNull(1) ? null : reader.GetString(1), "supplier " + id + " payable") }); }
            return result;
        }

        private static Dictionary<long, Invoice> ReadInvoices(SQLiteConnection c, SQLiteTransaction tx)
        {
            var result = new Dictionary<long, Invoice>();
            using (var command = new SQLiteCommand("SELECT Id, SupplierId, CAST(Total AS TEXT), CAST(PaidAmount AS TEXT) FROM SupplierInvoices", c, tx))
            using (var reader = command.ExecuteReader()) while (reader.Read())
            { var id = reader.GetInt64(0); result.Add(id, new Invoice { Id = id, SupplierId = reader.GetInt64(1), TotalScaled = Scale(reader.GetString(2), "invoice " + id + " total"), PaidScaled = Scale(reader.GetString(3), "invoice " + id + " paid amount") }); }
            return result;
        }

        private static List<Payment> ReadPayments(SQLiteConnection c, SQLiteTransaction tx)
        {
            var result = new List<Payment>();
            using (var command = new SQLiteCommand("SELECT Id, SupplierId, InvoiceId, CAST(Amount AS TEXT) FROM SupplierPayments", c, tx))
            using (var reader = command.ExecuteReader()) while (reader.Read())
            { var id = reader.GetInt64(0); result.Add(new Payment { Id = id, SupplierId = reader.GetInt64(1), InvoiceId = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2), AmountScaled = Scale(reader.GetString(3), "payment " + id + " amount") }); }
            return result;
        }

        private static long Scale(string text, string label)
        {
            decimal ignored; long scaled;
            if (!CustomerLedgerMigrationPreflight.TryScale4(text, out ignored, out scaled)) Fail(label + " cannot be represented exactly at scale 4; original value remains unchanged: " + (text ?? "NULL"));
            return scaled;
        }

        private static void Fail(string detail) { throw new InvalidOperationException("Supplier ledger migration stopped: " + detail + ". No historical data was changed."); }
        private sealed class Supplier { public long Id; public long PayableScaled; }
        private sealed class Invoice { public long Id; public long SupplierId; public long TotalScaled; public long PaidScaled; }
        private sealed class Payment { public long Id; public long SupplierId; public long? InvoiceId; public long AmountScaled; }
    }
}
