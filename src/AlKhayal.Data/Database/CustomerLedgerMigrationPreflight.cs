using System;
using System.Data.SQLite;
using System.Globalization;

namespace AlKhayal.Data.Database
{
    internal static class CustomerLedgerMigrationPreflight
    {
        internal static void Run(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            using(var command=new SQLiteCommand("SELECT Balance FROM Customers UNION ALL SELECT Amount FROM CustomerPayments",connection,transaction))
            using(var reader=command.ExecuteReader())
            {
                while(reader.Read())
                {
                    var value=Convert.ToDecimal(reader.GetValue(0),CultureInfo.InvariantCulture);
                    if(value<0||decimal.Truncate(value*10000m)!=value*10000m)
                        throw new InvalidOperationException("تعذر ترحيل أرصدة العملاء: توجد قيمة لا تطابق دقة أربع خانات عشرية، ولم تُغيّر أي بيانات.");
                }
            }
        }
    }
}
