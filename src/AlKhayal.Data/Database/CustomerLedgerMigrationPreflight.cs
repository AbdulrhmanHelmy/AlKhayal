using System;
using System.Data.SQLite;
using System.Globalization;

namespace AlKhayal.Data.Database
{
    internal static class CustomerLedgerMigrationPreflight
    {
        internal static void Run(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Validate(connection, transaction, "Customers", "Balance");
            Validate(connection, transaction, "CustomerPayments", "Amount");
        }

        private static void Validate(SQLiteConnection connection, SQLiteTransaction transaction, string table, string column)
        {
            var sql = "SELECT Id, CAST(" + column + " AS TEXT) FROM " + table;
            using (var command = new SQLiteCommand(sql, connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var text = reader.IsDBNull(1) ? null : reader.GetString(1);
                    decimal value; long scaled;
                    if (!TryScale4(text, out value, out scaled) || value < 0)
                        throw new InvalidOperationException("تعذر ترحيل أرصدة العملاء: القيمة في " + table + " رقم " + reader.GetInt64(0) + " غير قابلة للتمثيل الدقيق بأربع خانات عشرية. لم تُغيّر أي بيانات؛ راجع القيمة الأصلية: " + (text ?? "NULL"));
                }
            }
        }

        internal static bool TryScale4(string text, out decimal value, out long scaled)
        {
            scaled = 0;
            if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            try
            {
                var exact = checked(value * 10000m);
                if (exact != decimal.Truncate(exact)) return false;
                scaled = checked(decimal.ToInt64(exact));
                return true;
            }
            catch (OverflowException) { return false; }
        }
    }
}
