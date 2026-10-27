using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.BusinessActivities;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.BusinessActivities;
using AlKhayal.Data.Database;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class BusinessActivityPersistenceTests
    {
        [Fact]
        public async Task Selection_primary_and_override_survive_real_sqlite_and_activity_removal()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = Db.ConnectionStringFor(path); Migrator.Run(cs);
                var userId = await new SqliteUserStore(cs).CreateAsync("activity-admin", "مسؤول الأنشطة", new PasswordHasher().Hash("secret"), "administrator", CancellationToken.None);
                using (var connection = new SQLiteConnection(cs))
                    await connection.ExecuteAsync("INSERT INTO CapabilityConfiguration (CapabilityKey, IsEnabled, OverrideState, UpdatedUtc, ChangedByUserId) VALUES ('sizes', 0, 'administrator', @Now, @UserId)", new { Now = DateTime.UtcNow.ToString("o"), UserId = userId });
                var service = new BusinessActivityService(new SqliteBusinessActivityStore(cs));
                var result = await service.SaveAsync(new SaveBusinessActivitiesCommand { ActivityKeys = new[] { "clothing", "mobile-phones" }, PrimaryActivityKey = "clothing", ChangedByUserId = userId, ApplyRecommendations = true }, CancellationToken.None);
                Assert.True(result.Succeeded); Assert.Equal("clothing", result.Value.PrimaryActivityKey);
                var restarted = new BusinessActivityService(new SqliteBusinessActivityStore(cs));
                var persisted = await restarted.GetSelectedAsync(CancellationToken.None);
                Assert.Equal(2, persisted.ActivityKeys.Count); Assert.Equal("clothing", persisted.PrimaryActivityKey);
                await restarted.SaveAsync(new SaveBusinessActivitiesCommand { ActivityKeys = new[] { "clothing" }, PrimaryActivityKey = "clothing", ChangedByUserId = userId }, CancellationToken.None);
                using (var connection = new SQLiteConnection(cs))
                {
                    Assert.Equal(0L, await connection.ExecuteScalarAsync<long>("SELECT IsEnabled FROM CapabilityConfiguration WHERE CapabilityKey='sizes'"));
                    Assert.Equal("administrator", await connection.ExecuteScalarAsync<string>("SELECT OverrideState FROM CapabilityConfiguration WHERE CapabilityKey='sizes'"));
                    Assert.Equal(1L, await connection.ExecuteScalarAsync<long>("SELECT IsEnabled FROM CapabilityConfiguration WHERE CapabilityKey='serial-numbers'"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Migration_is_repeatable_and_adds_business_activity_schema_to_existing_database()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = Db.ConnectionStringFor(path);
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    connection.Execute("CREATE TABLE SchemaVersion (Name TEXT PRIMARY KEY, AppliedAt TEXT NOT NULL); CREATE TABLE Products (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Price NUMERIC NOT NULL);");
                    connection.Execute("INSERT INTO SchemaVersion (Name, AppliedAt) VALUES ('AlKhayal.Data.Migrations.001_create_products.sql', @At)", new { At = DateTime.UtcNow.ToString("o") });
                }
                Migrator.Run(cs); Migrator.Run(cs);
                using (var connection = new SQLiteConnection(cs))
                {
                    Assert.Equal(1L, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='BusinessActivities'"));
                    Assert.Equal(1L, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CapabilityConfiguration'"));
                    Assert.Equal(23L, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
