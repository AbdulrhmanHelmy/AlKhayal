using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using Xunit;
using AlKhayal.Data.Database;

namespace AlKhayal.IntegrationTests
{
    public sealed class CatalogMigrationTests
    {
        [Fact]
        public void Fresh_and_repeated_migration_create_catalog_schema_and_default_unit()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                Migrator.Run(cs);
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    Assert.Equal(34,connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Equal("قطعة", connection.ExecuteScalar<string>("SELECT Name FROM Units WHERE Id=1"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("PRAGMA foreign_keys"));
                    Assert.Equal("ok", connection.ExecuteScalar<string>("PRAGMA integrity_check"));
                    Assert.Empty(connection.Query<string>("PRAGMA foreign_key_check"));

                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductDetails'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductVariants'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductAttributes'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductAttributeValues'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductAttributeAssignments'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductAttributeValueAssignments'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductVariantValues'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductCapabilityAssignments'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductUnits'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductSkus'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductBarcodes'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CatalogAuditLog'"));

                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_table_info('ProductDetails') WHERE name='PurchasePriceScaled'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_table_info('ProductDetails') WHERE name='VariantMode'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_table_info('ProductVariants') WHERE name='CombinationKey'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_table_info('ProductVariants') WHERE name='ArchivedUtc'"));

                    Assert.Equal(2, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductAttributes WHERE IsSystem=1"));
                    Assert.Equal("SIZE", connection.ExecuteScalar<string>("SELECT AttributeKey FROM ProductAttributes WHERE Id=1"));
                    Assert.Equal("COLOR", connection.ExecuteScalar<string>("SELECT AttributeKey FROM ProductAttributes WHERE Id=2"));
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Upgrade_preserves_legacy_ids_prices_and_quarantines_invalid_rows()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    connection.Execute("CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL); PRAGMA foreign_keys=ON;");
                    var assembly = typeof(Migrator).Assembly;
                    foreach (var resource in assembly.GetManifestResourceNames()
                        .Where(name => name.EndsWith(".sql") &&
                                       !name.EndsWith("008_product_catalog_foundation.sql") &&
                                       !name.EndsWith("009_product_purchase_price.sql") &&
                                       !name.EndsWith("010_product_variants_and_identifiers.sql") &&
                                       !name.EndsWith("011_inventory_ledger.sql") && !name.EndsWith("016_inventory_fifo.sql") && !name.EndsWith("023_directory_query_indexes.sql"))
                        .OrderBy(name => name, StringComparer.Ordinal))
                    {
                        string sql;
                        using (var stream = assembly.GetManifestResourceStream(resource))
                        using (var reader = new StreamReader(stream)) { sql = reader.ReadToEnd(); }
                        using (var transaction = connection.BeginTransaction())
                        {
                            connection.Execute(sql, transaction: transaction);
                            connection.Execute("INSERT INTO SchemaVersion(Name,AppliedAt) VALUES(@Name,'2026-01-01T00:00:00Z')", new { Name = resource }, transaction);
                            transaction.Commit();
                        }
                    }
                    connection.Execute("INSERT INTO Products(Id,Name,Price) VALUES(41,'منتج قديم',12.34567),(42,'سعر غير صالح','EGP 8'),(43,'',5)");
                }
                Migrator.Run(cs);
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    Assert.Equal(3, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Products"));
                    Assert.Equal(123457, connection.ExecuteScalar<long>("SELECT SellPriceScaled FROM ProductDetails WHERE ProductId=41"));
                    Assert.Null(connection.ExecuteScalar<long?>("SELECT PurchasePriceScaled FROM ProductDetails WHERE ProductId=41"));
                    Assert.Equal("ready", connection.ExecuteScalar<string>("SELECT MigrationState FROM ProductDetails WHERE ProductId=41"));
                    Assert.Equal("review_required", connection.ExecuteScalar<string>("SELECT MigrationState FROM ProductDetails WHERE ProductId=42"));
                    Assert.Equal(0, connection.ExecuteScalar<long>("SELECT IsActive FROM ProductDetails WHERE ProductId=42"));
                    Assert.Equal(2, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductMigrationIssues WHERE ResolvedUtc IS NULL"));
                    Assert.Equal(3, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductVariants WHERE IsDefault=1"));
                    Assert.Equal(3, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductUnits WHERE IsBase=1"));
                    Assert.Equal("text", connection.ExecuteScalar<string>("SELECT typeof(Price) FROM Products WHERE Id=42"));
                    Assert.Equal("'EGP 8'", connection.ExecuteScalar<string>("SELECT quote(Price) FROM Products WHERE Id=42"));
                    Assert.Empty(connection.Query<string>("PRAGMA foreign_key_check"));
                    Assert.Equal("ok", connection.ExecuteScalar<string>("PRAGMA integrity_check"));
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Upgrade_from_009_backfills_base_product_units_and_preserves_identifiers()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    connection.Execute("CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL); PRAGMA foreign_keys=ON;");
                    var assembly = typeof(Migrator).Assembly;
                    foreach (var resource in assembly.GetManifestResourceNames()
                        .Where(name => name.EndsWith(".sql") && !name.EndsWith("010_product_variants_and_identifiers.sql") && !name.EndsWith("011_inventory_ledger.sql") && !name.EndsWith("016_inventory_fifo.sql") && !name.EndsWith("023_directory_query_indexes.sql"))
                        .OrderBy(name => name, StringComparer.Ordinal))
                    {
                        string sql;
                        using (var stream = assembly.GetManifestResourceStream(resource))
                        using (var reader = new StreamReader(stream)) { sql = reader.ReadToEnd(); }
                        using (var transaction = connection.BeginTransaction())
                        {
                            connection.Execute(sql, transaction: transaction);
                            if (resource.EndsWith("008_product_catalog_foundation.sql"))
                                CatalogLegacyBackfill.Run(connection, transaction);
                            connection.Execute("INSERT INTO SchemaVersion(Name,AppliedAt) VALUES(@Name,'2026-01-01T00:00:00Z')", new { Name = resource }, transaction);
                            transaction.Commit();
                        }
                    }

                    connection.Execute("INSERT INTO Products(Id,Name,Price) VALUES(101,'منتج تسعة',50)");
                    connection.Execute(@"INSERT INTO ProductDetails (ProductId, Description, CategoryId, BaseUnitId, ProductType, SellPriceScaled, PurchasePriceScaled, MoneyScale, IsActive, ArchivedUtc, MigrationState, Version, CreatedUtc, UpdatedUtc)
                        VALUES(101, 'وصف', NULL, 1, 'stock', 500000, 300000, 4, 1, NULL, 'ready', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                        INSERT INTO ProductVariants (ProductId, Name, NormalizedName, IsDefault, IsActive, CreatedUtc, UpdatedUtc)
                        VALUES(101, 'افتراضي', 'DEFAULT', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");
                }

                Migrator.Run(cs);

                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    Assert.Equal(34,connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    var baseUnit = connection.QuerySingleOrDefault<dynamic>("SELECT UnitId, ToBaseNumerator, ToBaseDenominator, IsBase, IsSaleUnit, IsPurchaseUnit FROM ProductUnits WHERE ProductId=101");
                    Assert.NotNull(baseUnit);
                    Assert.Equal(1L, (long)baseUnit.UnitId);
                    Assert.Equal(1L, (long)baseUnit.ToBaseNumerator);
                    Assert.Equal(1L, (long)baseUnit.ToBaseDenominator);
                    Assert.Equal(1L, (long)baseUnit.IsBase);
                    Assert.Equal(1L, (long)baseUnit.IsSaleUnit);
                    Assert.Equal(1L, (long)baseUnit.IsPurchaseUnit);
                    Assert.Empty(connection.Query<string>("PRAGMA foreign_key_check"));
                    Assert.Equal("ok", connection.ExecuteScalar<string>("PRAGMA integrity_check"));
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Preflight_fails_fast_and_rolls_back_if_product_lacks_default_variant()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    connection.Execute("CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL); PRAGMA foreign_keys=ON;");
                    var assembly = typeof(Migrator).Assembly;
                    foreach (var resource in assembly.GetManifestResourceNames()
                        .Where(name => name.EndsWith(".sql") && !name.EndsWith("010_product_variants_and_identifiers.sql") && !name.EndsWith("011_inventory_ledger.sql") && !name.EndsWith("016_inventory_fifo.sql") && !name.EndsWith("023_directory_query_indexes.sql"))
                        .OrderBy(name => name, StringComparer.Ordinal))
                    {
                        string sql;
                        using (var stream = assembly.GetManifestResourceStream(resource))
                        using (var reader = new StreamReader(stream)) { sql = reader.ReadToEnd(); }
                        using (var transaction = connection.BeginTransaction())
                        {
                            connection.Execute(sql, transaction: transaction);
                            if (resource.EndsWith("008_product_catalog_foundation.sql"))
                                CatalogLegacyBackfill.Run(connection, transaction);
                            connection.Execute("INSERT INTO SchemaVersion(Name,AppliedAt) VALUES(@Name,'2026-01-01T00:00:00Z')", new { Name = resource }, transaction);
                            transaction.Commit();
                        }
                    }

                    // Corrupt preflight condition: create a product without default variant
                    connection.Execute("INSERT INTO Products(Id,Name,Price) VALUES(99,'معطوب',10)");
                    connection.Execute(@"INSERT INTO ProductDetails (ProductId, Description, CategoryId, BaseUnitId, ProductType, SellPriceScaled, PurchasePriceScaled, MoneyScale, IsActive, ArchivedUtc, MigrationState, Version, CreatedUtc, UpdatedUtc)
                        VALUES(99, NULL, NULL, 1, 'stock', 100000, NULL, 4, 1, NULL, 'ready', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')");
                    // No default variant inserted!
                }

                var ex = Assert.Throws<InvalidOperationException>(() => Migrator.Run(cs));
                Assert.Contains("Migration 010 preflight failed", ex.Message);

                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    // Migration 010 should NOT be applied
                    var applied010 = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%010_product_variants_and_identifiers.sql'");
                    Assert.Equal(0, applied010);
                    // ProductUnits table should NOT exist because migration 010 was rolled back
                    Assert.Equal(0, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProductUnits'"));
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Post_migration_legacy_rows_are_reconciled_on_next_start_without_guessing()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    connection.Execute("INSERT INTO Products(Name,Price) VALUES('متأخر','?')");
                }
                Migrator.Run(cs);
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    Assert.Equal("review_required", connection.ExecuteScalar<string>("SELECT MigrationState FROM ProductDetails d JOIN Products p ON p.Id=d.ProductId WHERE p.Name='متأخر'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductUnits u JOIN Products p ON p.Id=u.ProductId WHERE p.Name='متأخر' AND u.IsBase=1"));
                }
            }
            finally { Cleanup(path); }
        }

        private static string Temp() { return Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db"); }
        private static void Cleanup(string path) { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
