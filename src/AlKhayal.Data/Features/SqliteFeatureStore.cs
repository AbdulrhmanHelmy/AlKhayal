using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Features;
using Dapper;

namespace AlKhayal.Data.Features
{
    public sealed class SqliteFeatureStore : IFeatureStore
    {
        private readonly string _connectionString;
        public SqliteFeatureStore(string connectionString) { _connectionString = connectionString; }

        public Task<IReadOnlyList<StoredFeatureState>> GetModuleStatesAsync(CancellationToken ct) { return ReadAsync("ModuleConfiguration", "ModuleKey", ct); }
        public Task<IReadOnlyList<StoredFeatureState>> GetCapabilityStatesAsync(CancellationToken ct) { return ReadAsync("CapabilityConfiguration", "CapabilityKey", ct); }

        public async Task SetStateAsync(string kind, string key, bool enabled, long changedByUserId, CancellationToken ct)
        {
            var module = kind == FeatureKinds.Module;
            if (!module && kind != FeatureKinds.Capability) throw new ArgumentException("Unknown feature kind.", "kind");
            var table = module ? "ModuleConfiguration" : "CapabilityConfiguration";
            var keyColumn = module ? "ModuleKey" : "CapabilityKey";
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    // Serialize lifecycle disabling with opening/check-in on the same SQLite writer lock.
                    if (module && !enabled && key == "advanced-shifts" && await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM CashierShifts WHERE ClosedUtc IS NULL", transaction: transaction, cancellationToken: ct)).ConfigureAwait(false) > 0)
                        throw new FeatureStateConflictException("أغلق وردية الكاشير المفتوحة قبل تعطيل إدارة الورديات.");
                    if (module && !enabled && (key == "employee-attendance" || key == "employee-management") && await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM EmployeeAttendance WHERE CheckOutUtc IS NULL", transaction: transaction, cancellationToken: ct)).ConfigureAwait(false) > 0)
                        throw new FeatureStateConflictException("سجّل انصراف الموظفين الحاضرين قبل تعطيل الوحدة.");
                    var previous = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition("SELECT IsEnabled FROM " + table + " WHERE " + keyColumn + "=@Key", new { Key = key }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    var now = DateTime.UtcNow.ToString("o");
                    await connection.ExecuteAsync(new CommandDefinition("INSERT INTO " + table + " (" + keyColumn + ", IsEnabled, OverrideState, UpdatedUtc, ChangedByUserId) VALUES (@Key, @Enabled, 'administrator', @Now, @UserId) ON CONFLICT(" + keyColumn + ") DO UPDATE SET IsEnabled=@Enabled, OverrideState='administrator', UpdatedUtc=@Now, ChangedByUserId=@UserId", new { Key = key, Enabled = enabled, Now = now, UserId = changedByUserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    await Audit(connection, transaction, kind, key, previous, enabled, "administrator", changedByUserId, now, ct).ConfigureAwait(false);
                    transaction.Commit();
                }
            }
        }

        public async Task ApplyRecommendationsAsync(IReadOnlyList<string> moduleKeys, IReadOnlyList<string> capabilityKeys, long changedByUserId, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    var now = DateTime.UtcNow.ToString("o");
                    foreach (var key in moduleKeys) await Recommend(connection, transaction, FeatureKinds.Module, "ModuleConfiguration", "ModuleKey", key, changedByUserId, now, ct).ConfigureAwait(false);
                    foreach (var key in capabilityKeys) await Recommend(connection, transaction, FeatureKinds.Capability, "CapabilityConfiguration", "CapabilityKey", key, changedByUserId, now, ct).ConfigureAwait(false);
                    await Audit(connection, transaction, "recommendations", "activity-recommendations", null, null, "recommended", changedByUserId, now, ct).ConfigureAwait(false);
                    transaction.Commit();
                }
            }
        }

        private async Task<IReadOnlyList<StoredFeatureState>> ReadAsync(string table, string keyColumn, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                var rows = await connection.QueryAsync<StoredFeatureState>(new CommandDefinition("SELECT " + keyColumn + " AS [Key], IsEnabled, OverrideState AS Source, UpdatedUtc FROM " + table + " ORDER BY " + keyColumn, cancellationToken: ct)).ConfigureAwait(false);
                return rows.ToList();
            }
        }

        private static async Task Recommend(SQLiteConnection connection, SQLiteTransaction transaction, string kind, string table, string keyColumn, string key, long userId, string now, CancellationToken ct)
        {
            var row = await connection.QuerySingleOrDefaultAsync<ExistingState>(new CommandDefinition("SELECT IsEnabled, OverrideState FROM " + table + " WHERE " + keyColumn + "=@Key", new { Key = key }, transaction, cancellationToken: ct)).ConfigureAwait(false);
            if (row != null && row.OverrideState == "administrator") return;
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO " + table + " (" + keyColumn + ", IsEnabled, OverrideState, UpdatedUtc, ChangedByUserId) VALUES (@Key, 1, 'recommended', @Now, @UserId) ON CONFLICT(" + keyColumn + ") DO UPDATE SET IsEnabled=1, OverrideState='recommended', UpdatedUtc=@Now, ChangedByUserId=@UserId WHERE " + table + ".OverrideState <> 'administrator'", new { Key = key, Now = now, UserId = userId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
            await Audit(connection, transaction, kind, key, row == null ? (long?)null : row.IsEnabled, true, "recommended", userId, now, ct).ConfigureAwait(false);
        }

        private static Task<int> Audit(SQLiteConnection connection, SQLiteTransaction transaction, string kind, string key, long? previous, bool? next, string source, long userId, string now, CancellationToken ct)
        {
            return connection.ExecuteAsync(new CommandDefinition("INSERT INTO FeatureAuditLog (FeatureKind, FeatureKey, PreviousState, NewState, Source, ChangedByUserId, ChangedUtc) VALUES (@Kind, @Key, @Previous, @Next, @Source, @UserId, @Now)", new { Kind = kind, Key = key, Previous = previous, Next = next, Source = source, UserId = userId, Now = now }, transaction, cancellationToken: ct));
        }

        private sealed class ExistingState { public long IsEnabled { get; set; } public string OverrideState { get; set; } }
    }
}
