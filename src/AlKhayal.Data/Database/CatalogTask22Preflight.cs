using System;
using System.Data.SQLite;
using System.Globalization;

namespace AlKhayal.Data.Database
{
    internal static class CatalogTask22Preflight
    {
        public static void Run(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 1. ProductDetails without a matching Products row or valid Units row.
            using (var cmd = new SQLiteCommand(@"
                SELECT COUNT(*) 
                FROM ProductDetails d
                LEFT JOIN Products p ON p.Id = d.ProductId
                LEFT JOIN Units u ON u.Id = d.BaseUnitId
                WHERE p.Id IS NULL OR u.Id IS NULL", connection, transaction))
            {
                var count = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (count > 0)
                {
                    throw new InvalidOperationException($"Migration 010 preflight failed: {count} ProductDetails row(s) lack a matching product or valid base unit.");
                }
            }

            // 2. Any product with other than exactly one IsDefault=1 variant.
            using (var cmd = new SQLiteCommand(@"
                SELECT COUNT(*) FROM (
                    SELECT d.ProductId
                    FROM ProductDetails d
                    LEFT JOIN ProductVariants v ON v.ProductId = d.ProductId AND v.IsDefault = 1
                    GROUP BY d.ProductId
                    HAVING COUNT(v.Id) != 1
                    UNION
                    SELECT v.ProductId
                    FROM ProductVariants v
                    GROUP BY v.ProductId
                    HAVING SUM(CASE WHEN v.IsDefault = 1 THEN 1 ELSE 0 END) != 1
                )", connection, transaction))
            {
                var count = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (count > 0)
                {
                    throw new InvalidOperationException($"Migration 010 preflight failed: {count} product(s) do not have exactly one default variant.");
                }
            }

            // 3. Any ProductVariants.ProductId without ProductDetails.
            using (var cmd = new SQLiteCommand(@"
                SELECT COUNT(*) 
                FROM ProductVariants v
                LEFT JOIN ProductDetails d ON d.ProductId = v.ProductId
                WHERE d.ProductId IS NULL", connection, transaction))
            {
                var count = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (count > 0)
                {
                    throw new InvalidOperationException($"Migration 010 preflight failed: {count} ProductVariants row(s) lack matching ProductDetails.");
                }
            }
        }
    }
}
