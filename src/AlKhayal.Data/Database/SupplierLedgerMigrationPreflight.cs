using System;
using System.Data.SQLite;

namespace AlKhayal.Data.Database
{
    internal static class SupplierLedgerMigrationPreflight
    {
        public static void Run(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var invalid = Scalar(connection, transaction, @"SELECT COUNT(*) FROM Suppliers
                WHERE Payable<0 OR ABS(Payable*10000-ROUND(Payable*10000,0))>0.000001
                OR ROUND((Payable-COALESCE((SELECT SUM(i.Total) FROM SupplierInvoices i WHERE i.SupplierId=Suppliers.Id),0)
                    +COALESCE((SELECT SUM(p.Amount) FROM SupplierPayments p WHERE p.SupplierId=Suppliers.Id),0))*10000,0)<0
                OR ABS((Payable-COALESCE((SELECT SUM(i.Total) FROM SupplierInvoices i WHERE i.SupplierId=Suppliers.Id),0)
                    +COALESCE((SELECT SUM(p.Amount) FROM SupplierPayments p WHERE p.SupplierId=Suppliers.Id),0))*10000
                    -ROUND((Payable-COALESCE((SELECT SUM(i.Total) FROM SupplierInvoices i WHERE i.SupplierId=Suppliers.Id),0)
                    +COALESCE((SELECT SUM(p.Amount) FROM SupplierPayments p WHERE p.SupplierId=Suppliers.Id),0))*10000,0))>0.000001");
            if (invalid != 0) throw new InvalidOperationException("Supplier ledger migration stopped: a payable balance cannot be reconciled exactly with legacy invoices and payments.");
            var badPaymentLinks = Scalar(connection, transaction, @"SELECT COUNT(*) FROM SupplierPayments p
                JOIN SupplierInvoices i ON i.Id=p.InvoiceId WHERE i.SupplierId<>p.SupplierId");
            if (badPaymentLinks != 0) throw new InvalidOperationException("Supplier ledger migration stopped: historical payment-to-invoice ownership is inconsistent.");
            var invoicePaymentMismatch = Scalar(connection, transaction, @"SELECT COUNT(*) FROM SupplierInvoices i
                WHERE ABS(i.PaidAmount-COALESCE((SELECT SUM(p.Amount) FROM SupplierPayments p WHERE p.InvoiceId=i.Id),0))>0.000001");
            if (invoicePaymentMismatch != 0) throw new InvalidOperationException("Supplier ledger migration stopped: historical invoice paid totals do not match their recorded payments.");
            var badAmounts = Scalar(connection, transaction, @"SELECT (SELECT COUNT(*) FROM SupplierInvoices WHERE Total<=0 OR PaidAmount<0 OR PaidAmount>Total
                    OR ABS(Total*10000-ROUND(Total*10000,0))>0.000001 OR ABS(PaidAmount*10000-ROUND(PaidAmount*10000,0))>0.000001)
                + (SELECT COUNT(*) FROM SupplierPayments WHERE Amount<=0 OR ABS(Amount*10000-ROUND(Amount*10000,0))>0.000001)");
            if (badAmounts != 0) throw new InvalidOperationException("Supplier ledger migration stopped: historical supplier transaction amounts failed validation.");
        }

        private static long Scalar(SQLiteConnection connection, SQLiteTransaction transaction, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection, transaction))
                return Convert.ToInt64(command.ExecuteScalar());
        }
    }
}
