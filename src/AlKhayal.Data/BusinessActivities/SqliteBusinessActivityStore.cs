using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.BusinessActivities;
using Dapper;

namespace AlKhayal.Data.BusinessActivities
{
    public sealed class SqliteBusinessActivityStore : IBusinessActivityStore
    {
        private readonly string _connectionString;
        public SqliteBusinessActivityStore(string connectionString) { _connectionString = connectionString; }

        public async Task<IReadOnlyList<SelectedBusinessActivity>> GetSelectedAsync(CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                var rows = await connection.QueryAsync<SelectedBusinessActivity>(new CommandDefinition(
                    "SELECT ActivityKey, IsPrimary, SelectedUtc FROM BusinessActivities ORDER BY IsPrimary DESC, SelectedUtc, ActivityKey",
                    cancellationToken: ct)).ConfigureAwait(false);
                return rows.ToList();
            }
        }

        public async Task SaveSelectedAsync(IReadOnlyList<string> activityKeys, string primaryActivityKey, long changedByUserId, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    await connection.ExecuteAsync(new CommandDefinition("DELETE FROM BusinessActivities", transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);
                    var now = DateTime.UtcNow.ToString("o");
                    foreach (var key in activityKeys)
                        await connection.ExecuteAsync(new CommandDefinition(
                            "INSERT INTO BusinessActivities (ActivityKey, IsPrimary, SelectedUtc, ChangedByUserId) VALUES (@ActivityKey, @IsPrimary, @SelectedUtc, @ChangedByUserId)",
                            new { ActivityKey = key, IsPrimary = string.Equals(key, primaryActivityKey, StringComparison.OrdinalIgnoreCase), SelectedUtc = now, ChangedByUserId = changedByUserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    transaction.Commit();
                }
            }
        }

        public async Task<IReadOnlyList<CapabilityConfiguration>> GetCapabilityConfigurationAsync(CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                var rows = await connection.QueryAsync<CapabilityConfiguration>(new CommandDefinition(
                    "SELECT CapabilityKey, IsEnabled, OverrideState FROM CapabilityConfiguration ORDER BY CapabilityKey",
                    cancellationToken: ct)).ConfigureAwait(false);
                return rows.ToList();
            }
        }

        public async Task ApplyRecommendationsAsync(IReadOnlyList<CapabilityRecommendation> recommendations, long changedByUserId, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    var now = DateTime.UtcNow.ToString("o");
                    foreach (var recommendation in recommendations)
                        await connection.ExecuteAsync(new CommandDefinition(
                            "INSERT INTO CapabilityConfiguration (CapabilityKey, IsEnabled, OverrideState, UpdatedUtc, ChangedByUserId) " +
                            "VALUES (@CapabilityKey, 1, 'recommended', @UpdatedUtc, @ChangedByUserId) " +
                            "ON CONFLICT(CapabilityKey) DO UPDATE SET IsEnabled=1, OverrideState='recommended', UpdatedUtc=@UpdatedUtc, ChangedByUserId=@ChangedByUserId " +
                            "WHERE CapabilityConfiguration.OverrideState <> 'administrator'",
                            new { recommendation.CapabilityKey, UpdatedUtc = now, ChangedByUserId = changedByUserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    transaction.Commit();
                }
            }
        }
    }
}
