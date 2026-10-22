using System;
using System.Data.SQLite;
using System.Collections.Generic;
using System.Globalization;

namespace AlKhayal.Data.Database
{
    public static class CatalogLegacyBackfill
    {
        private const long MoneyScale = 10000L;

        public static void Run(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            if (!TableExists(connection, transaction, "ProductDetails")) return;

            var rows = new List<LegacyRow>();
            using (var command = new SQLiteCommand(
                "SELECT p.Id, p.Name, typeof(p.Price), CAST(p.Price AS TEXT) " +
                "FROM Products p LEFT JOIN ProductDetails d ON d.ProductId=p.Id " +
                "WHERE d.ProductId IS NULL ORDER BY p.Id", connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    rows.Add(new LegacyRow { Id=reader.GetInt64(0), Name=reader.IsDBNull(1) ? null : reader.GetString(1), PriceType=reader.IsDBNull(2) ? "null" : reader.GetString(2), PriceText=reader.IsDBNull(3) ? null : reader.GetString(3) });
            }

            foreach (var row in rows)
            {
                    var id = row.Id;
                    var name = row.Name;
                    var priceType = row.PriceType;
                    var priceText = row.PriceText;
                    long scaled = 0;
                    string issue;
                    var valid = ValidName(name, out issue) && TryScale(priceType, priceText, out scaled, out issue);
                    var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

                    Execute(connection, transaction,
                        "INSERT INTO ProductDetails (ProductId, Description, CategoryId, BaseUnitId, ProductType, SellPriceScaled, MoneyScale, IsActive, ArchivedUtc, MigrationState, Version, CreatedUtc, UpdatedUtc) " +
                        "VALUES (@ProductId, NULL, NULL, 1, 'stock', @Price, 4, @Active, NULL, @State, 1, @Now, @Now)",
                        new SQLiteParameter("@ProductId", id),
                        new SQLiteParameter("@Price", valid ? scaled : 0L),
                        new SQLiteParameter("@Active", valid ? 1 : 0),
                        new SQLiteParameter("@State", valid ? "ready" : "review_required"),
                        new SQLiteParameter("@Now", now));
                    Execute(connection, transaction,
                        "INSERT INTO ProductVariants (ProductId, Name, NormalizedName, IsDefault, IsActive, CreatedUtc, UpdatedUtc) VALUES (@ProductId, 'افتراضي', 'DEFAULT', 1, @Active, @Now, @Now)",
                        new SQLiteParameter("@ProductId", id), new SQLiteParameter("@Active", valid ? 1 : 0), new SQLiteParameter("@Now", now));

                    if (!valid)
                        Execute(connection, transaction,
                            "INSERT INTO ProductMigrationIssues (ProductId, IssueCode, Details, OriginalPriceType, OriginalPriceText, CreatedUtc) VALUES (@ProductId, 'legacy_product_invalid', @Details, @PriceType, @PriceText, @Now)",
                            new SQLiteParameter("@ProductId", id), new SQLiteParameter("@Details", issue),
                            new SQLiteParameter("@PriceType", (object)priceType ?? DBNull.Value), new SQLiteParameter("@PriceText", (object)priceText ?? DBNull.Value),
                            new SQLiteParameter("@Now", now));
            }

            if (TableExists(connection, transaction, "ProductUnits"))
            {
                Execute(connection, transaction,
                    "INSERT INTO ProductUnits (ProductId, UnitId, ToBaseNumerator, ToBaseDenominator, IsBase, IsSaleUnit, IsPurchaseUnit, IsActive, FirstUsedUtc, Version, CreatedUtc, UpdatedUtc) " +
                    "SELECT d.ProductId, d.BaseUnitId, 1, 1, 1, 1, 1, 1, NULL, 1, d.CreatedUtc, d.UpdatedUtc " +
                    "FROM ProductDetails d " +
                    "WHERE NOT EXISTS (SELECT 1 FROM ProductUnits u WHERE u.ProductId = d.ProductId AND u.IsBase = 1)");
            }
        }

        private static bool ValidName(string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            {
                error = "اسم المنتج القديم فارغ أو يتجاوز 200 حرف.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryScale(string storageType, string text, out long scaled, out string error)
        {
            scaled = 0;
            if (storageType != "integer" && storageType != "real" && storageType != "text")
            {
                error = "نوع سعر المنتج القديم غير قابل للتحويل الآمن.";
                return false;
            }
            decimal value;
            if (string.IsNullOrWhiteSpace(text) || !decimal.TryParse(text,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out value) || value < 0)
            {
                error = "سعر المنتج القديم ليس قيمة عشرية غير سالبة بصيغة ثابتة.";
                return false;
            }
            try
            {
                var rounded = decimal.Round(checked(value * MoneyScale), 0, MidpointRounding.AwayFromZero);
                if (rounded > long.MaxValue) throw new OverflowException();
                scaled = decimal.ToInt64(rounded);
                error = null;
                return true;
            }
            catch (OverflowException)
            {
                error = "سعر المنتج القديم يتجاوز نطاق التخزين ذي المقياس 4.";
                return false;
            }
        }

        private static bool TableExists(SQLiteConnection connection, SQLiteTransaction transaction, string name)
        {
            using (var command = new SQLiteCommand("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@Name", connection, transaction))
            {
                command.Parameters.AddWithValue("@Name", name);
                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
            }
        }

        private static void Execute(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params SQLiteParameter[] parameters)
        {
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddRange(parameters);
                command.ExecuteNonQuery();
            }
        }

        private sealed class LegacyRow
        {
            public long Id { get; set; }
            public string Name { get; set; }
            public string PriceType { get; set; }
            public string PriceText { get; set; }
        }
    }
}
