using System;
using System.Data.SQLite;

namespace AlKhayal.Data.Database
{
    // Some databases recorded migration 017 with an earlier ledger shape. Repair only
    // the missing link, and only when every historical payment has one exact match.
    internal static class SupplierLedgerLegacyPaymentCompatibility
    {
        public static void Run(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            if (Scalar(connection, transaction, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SupplierLedgerEntries'") != 1)
                throw new InvalidOperationException("Supplier ledger migration stopped: the ledger table is missing although migration 017 is recorded.");

            using (var command = new SQLiteCommand("PRAGMA table_info(SupplierLedgerEntries)", connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    if (string.Equals(reader.GetString(1), "LegacyPaymentId", StringComparison.OrdinalIgnoreCase))
                        return;
            }

            const string matchingPayments = @"SELECT COUNT(*) FROM SupplierPayments p
                WHERE p.SupplierId=e.SupplierId AND p.Reference=e.Reference
                  AND p.PaymentDate=e.OccurredOn
                  AND -CAST(ROUND(p.Amount*10000,0) AS INTEGER)=e.AmountScaled";
            const string matchingEntries = @"SELECT COUNT(*) FROM SupplierLedgerEntries e
                WHERE e.EntryType='legacy_payment' AND e.SupplierId=p.SupplierId
                  AND e.Reference=p.Reference AND e.OccurredOn=p.PaymentDate
                  AND e.AmountScaled=-CAST(ROUND(p.Amount*10000,0) AS INTEGER)";

            var unmatchedEntries = Scalar(connection, transaction,
                "SELECT COUNT(*) FROM SupplierLedgerEntries e WHERE e.EntryType='legacy_payment' AND (" + matchingPayments + ")<>1");
            var unmatchedPayments = Scalar(connection, transaction,
                "SELECT COUNT(*) FROM SupplierPayments p WHERE (" + matchingEntries + ")<>1");
            if (unmatchedEntries != 0 || unmatchedPayments != 0)
                throw new InvalidOperationException("Supplier ledger migration stopped: historical payment links are missing or ambiguous; no ledger data was changed.");

            Execute(connection, transaction, "ALTER TABLE SupplierLedgerEntries ADD COLUMN LegacyPaymentId INTEGER NULL REFERENCES SupplierPayments(Id) ON DELETE RESTRICT");
            Execute(connection, transaction, "CREATE UNIQUE INDEX IF NOT EXISTS UX_SupplierLedger_LegacyPayment ON SupplierLedgerEntries(LegacyPaymentId) WHERE LegacyPaymentId IS NOT NULL");
            Execute(connection, transaction, "DROP TRIGGER IF EXISTS TR_SupplierLedgerEntries_NoUpdate");
            Execute(connection, transaction, @"UPDATE SupplierLedgerEntries AS e SET LegacyPaymentId=(
                SELECT p.Id FROM SupplierPayments p WHERE p.SupplierId=e.SupplierId AND p.Reference=e.Reference
                  AND p.PaymentDate=e.OccurredOn
                  AND -CAST(ROUND(p.Amount*10000,0) AS INTEGER)=e.AmountScaled)
                WHERE e.EntryType='legacy_payment'");
            Execute(connection, transaction, "CREATE TRIGGER TR_SupplierLedgerEntries_NoUpdate BEFORE UPDATE ON SupplierLedgerEntries BEGIN SELECT RAISE(ABORT,'supplier_ledger_immutable'); END");
        }

        private static long Scalar(SQLiteConnection connection, SQLiteTransaction transaction, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection, transaction))
                return Convert.ToInt64(command.ExecuteScalar());
        }

        private static void Execute(SQLiteConnection connection, SQLiteTransaction transaction, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection, transaction))
                command.ExecuteNonQuery();
        }
    }
}
