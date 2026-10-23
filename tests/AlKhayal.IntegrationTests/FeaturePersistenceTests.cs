using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.BusinessActivities;
using AlKhayal.Application.Features;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.BusinessActivities;
using AlKhayal.Data.Database;
using AlKhayal.Data.Features;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class FeaturePersistenceTests
    {
        [Fact]
        public async Task Feature_states_overrides_audit_and_reenable_survive_real_sqlite()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = Db.ConnectionStringFor(path); Migrator.Run(cs);
                var userId = await new SqliteUserStore(cs).CreateAsync("feature-admin", "مسؤول الميزات", new PasswordHasher().Hash("secret"), "administrator", CancellationToken.None);
                var activityStore = new SqliteBusinessActivityStore(cs); var service = new FeatureService(new SqliteFeatureStore(cs), activityStore);
                Assert.True((await service.SetAsync(new SetFeatureCommand { Key = "installments", Kind = FeatureKinds.Module, IsEnabled = true, ChangedByUserId = userId }, CancellationToken.None)).Succeeded);
                Assert.True((await service.SetAsync(new SetFeatureCommand { Key = "installments", Kind = FeatureKinds.Module, IsEnabled = false, ChangedByUserId = userId }, CancellationToken.None)).Succeeded);
                Assert.True((await service.SetAsync(new SetFeatureCommand { Key = "installments", Kind = FeatureKinds.Module, IsEnabled = true, ChangedByUserId = userId }, CancellationToken.None)).Succeeded);
                await service.SetAsync(new SetFeatureCommand { Key = "sizes", Kind = FeatureKinds.Capability, IsEnabled = false, ChangedByUserId = userId }, CancellationToken.None);
                await service.ApplyAsync(new BusinessActivityService(activityStore).Recommend(new[] { "clothing" }), userId, CancellationToken.None);
                var restarted = new FeatureService(new SqliteFeatureStore(cs), activityStore); var snapshot = await restarted.GetEffectiveAsync(CancellationToken.None);
                Assert.True(Assert.Single(snapshot.Modules, item => item.Key == "installments").IsEnabled);
                Assert.False(Assert.Single(snapshot.Capabilities, item => item.Key == "sizes").IsEnabled);
                using (var connection = new SQLiteConnection(cs))
                {
                    Assert.True(await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM FeatureAuditLog") >= 5);
                    Assert.Equal("administrator", await connection.ExecuteScalarAsync<string>("SELECT OverrideState FROM CapabilityConfiguration WHERE CapabilityKey='sizes'"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public async Task Invalid_user_rolls_back_state_and_audit_and_migration_is_repeatable()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = Db.ConnectionStringFor(path); Migrator.Run(cs); Migrator.Run(cs);
                await Assert.ThrowsAsync<SQLiteException>(() => new SqliteFeatureStore(cs).SetStateAsync(FeatureKinds.Module, "installments", true, long.MaxValue, CancellationToken.None));
                using (var connection = new SQLiteConnection(cs))
                {
                    Assert.Equal(0L, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM ModuleConfiguration"));
                    Assert.Equal(0L, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM FeatureAuditLog"));
                    Assert.Equal(16L, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM SchemaVersion"));
                }
            }
            finally { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
