using System;
using System.Data.SQLite;
using System.IO;
using AlKhayal.Data.Database;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class MigrationTests
    {
        [Fact]
        public void Authentication_profile_and_csrf_columns_are_migrated()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs); Migrator.Run(cs);
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    var userColumns = connection.Query<string>("SELECT name FROM pragma_table_info('Users')");
                    var sessionColumns = connection.Query<string>("SELECT name FROM pragma_table_info('Sessions')");
                    Assert.Contains("DisplayName", userColumns); Assert.Contains("Role", userColumns); Assert.Contains("UpdatedUtc", userColumns); Assert.Contains("LastLoginUtc", userColumns); Assert.Contains("CsrfToken", sessionColumns);
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UserScreenPermissions'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UserAuditLog'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CompanyProfile'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ApplicationSettings'"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SettingsAuditLog'"));
                    Assert.Equal(23, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
