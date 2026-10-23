using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AlKhayal.Data.Database
{
    public static class Migrator
    {
        public static void Run(string connectionString)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();

                Execute(conn, null,
                    "CREATE TABLE IF NOT EXISTS SchemaVersion (Name TEXT PRIMARY KEY, AppliedAt TEXT NOT NULL)");
                Execute(conn, null, "PRAGMA foreign_keys = ON;");

                var assembly = Assembly.GetExecutingAssembly();
                var scripts = assembly.GetManifestResourceNames()
                    .Where(n => n.EndsWith(".sql"))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                foreach (var script in scripts)
                {
                    if (IsApplied(conn, script)) continue;

                    if (script.EndsWith("010_product_variants_and_identifiers.sql", StringComparison.Ordinal))
                    {
                        if (HasAppliedEndingWith(conn, "008_product_catalog_foundation.sql"))
                        {
                            using (var reconcileTx = conn.BeginTransaction())
                            {
                                CatalogLegacyBackfill.Run(conn, reconcileTx);
                                reconcileTx.Commit();
                            }
                        }
                    }

                    string sql;
                    using (var stream = assembly.GetManifestResourceStream(script))
                    using (var reader = new StreamReader(stream))
                    {
                        sql = reader.ReadToEnd();
                    }

                    using (var tx = conn.BeginTransaction())
                    {
                        if (script.EndsWith("010_product_variants_and_identifiers.sql", StringComparison.Ordinal))
                            CatalogTask22Preflight.Run(conn, tx);
                        if (script.EndsWith("015_customer_transactions.sql", StringComparison.Ordinal))
                            CustomerLedgerMigrationPreflight.Run(conn, tx);

                        Execute(conn, tx, sql);

                        if (script.EndsWith("008_product_catalog_foundation.sql", StringComparison.Ordinal))
                            CatalogLegacyBackfill.Run(conn, tx);

                        using (var cmd = new SQLiteCommand(
                            "INSERT INTO SchemaVersion (Name, AppliedAt) VALUES (@name, @at)", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@name", script);
                            cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                }

                // Reconcile rows produced by an older binary after migration 008 without
                // modifying or guessing their legacy values.
                using (var tx = conn.BeginTransaction())
                {
                    CatalogLegacyBackfill.Run(conn, tx);
                    tx.Commit();
                }
            }
        }

        private static bool IsApplied(SQLiteConnection conn, string name)
        {
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM SchemaVersion WHERE Name = @name", conn))
            {
                cmd.Parameters.AddWithValue("@name", name);
                return (long)cmd.ExecuteScalar() > 0;
            }
        }

        private static bool HasAppliedEndingWith(SQLiteConnection conn, string suffix)
        {
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE @suffix", conn))
            {
                cmd.Parameters.AddWithValue("@suffix", "%" + suffix);
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
        }

        private static void Execute(SQLiteConnection conn, SQLiteTransaction tx, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, conn, tx))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }
}
