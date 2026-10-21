using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Users;
using Dapper;

namespace AlKhayal.Data.Users
{
    public sealed class SqliteUserManagementStore : IUserManagementStore
    {
        private readonly string _connectionString;
        public SqliteUserManagementStore(string connectionString) { _connectionString = connectionString; }

        public async Task<IReadOnlyList<ManagedUser>> ListAsync(CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                var rows = await connection.QueryAsync<ManagedUser>(new CommandDefinition("SELECT Id, Username, DisplayName, Role, IsActive, CreatedUtc, UpdatedUtc, LastLoginUtc FROM Users ORDER BY DisplayName COLLATE NOCASE, Username COLLATE NOCASE", cancellationToken: ct)).ConfigureAwait(false);
                return rows.ToList();
            }
        }

        public async Task<ManagedUser> FindAsync(long userId, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString)) return await Find(connection, null, userId, ct).ConfigureAwait(false);
        }

        public async Task<ManagedUser> CreateAsync(CreateUserCommand command, string passwordHash, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var now = DateTime.UtcNow.ToString("o");
                        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO Users (Username, DisplayName, PasswordHash, Role, IsActive, CreatedUtc, UpdatedUtc) VALUES (@Username,@DisplayName,@PasswordHash,@Role,@IsActive,@Now,@Now); SELECT last_insert_rowid();", new { command.Username, command.DisplayName, PasswordHash = passwordHash, command.Role, command.IsActive, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        await Audit(connection, transaction, command.ActingUserId, id, "user_created", "role=" + command.Role + ";active=" + command.IsActive, now, ct).ConfigureAwait(false);
                        transaction.Commit();
                        return await FindAsync(id, ct).ConfigureAwait(false);
                    }
                    catch (SQLiteException exception) when (IsUnique(exception)) { throw new InvalidOperationException("اسم المستخدم مستخدم بالفعل.", exception); }
                }
            }
        }

        public async Task<UserManagementResult<ManagedUser>> UpdateAsync(UpdateUserCommand command, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    var existing = await Find(connection, transaction, command.UserId, ct).ConfigureAwait(false);
                    if (existing == null) return UserManagementResult<ManagedUser>.Fail("not_found", "المستخدم غير موجود.");
                    if (WouldRemoveLastAdmin(existing, command.Role, command.IsActive) && await ActiveAdminCount(connection, transaction, ct).ConfigureAwait(false) <= 1) return UserManagementResult<ManagedUser>.Fail("last_active_administrator", "لا يمكن تعطيل أو تخفيض صلاحية آخر مسؤول نظام نشط.");
                    try
                    {
                        var now = DateTime.UtcNow.ToString("o");
                        await connection.ExecuteAsync(new CommandDefinition("UPDATE Users SET Username=@Username, DisplayName=@DisplayName, Role=@Role, IsActive=@IsActive, UpdatedUtc=@Now WHERE Id=@UserId", new { command.Username, command.DisplayName, command.Role, command.IsActive, Now = now, command.UserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (existing.Role != command.Role || existing.IsActive != command.IsActive) await connection.ExecuteAsync(new CommandDefinition("DELETE FROM Sessions WHERE UserId=@UserId", new { command.UserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        await Audit(connection, transaction, command.ActingUserId, command.UserId, "user_updated", "username=" + command.Username, now, ct).ConfigureAwait(false);
                        if (existing.Role != command.Role) await Audit(connection, transaction, command.ActingUserId, command.UserId, "role_changed", "role=" + command.Role, now, ct).ConfigureAwait(false);
                        if (existing.IsActive != command.IsActive) await Audit(connection, transaction, command.ActingUserId, command.UserId, command.IsActive ? "user_activated" : "user_deactivated", "active=" + command.IsActive, now, ct).ConfigureAwait(false);
                        transaction.Commit();
                    }
                    catch (SQLiteException exception) when (IsUnique(exception)) { throw new InvalidOperationException("اسم المستخدم مستخدم بالفعل.", exception); }
                }
            }
            return UserManagementResult<ManagedUser>.Ok(await FindAsync(command.UserId, ct).ConfigureAwait(false));
        }

        public async Task<UserManagementResult<ManagedUser>> SetStatusAsync(SetUserStatusCommand command, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    var existing = await Find(connection, transaction, command.UserId, ct).ConfigureAwait(false);
                    if (existing == null) return UserManagementResult<ManagedUser>.Fail("not_found", "المستخدم غير موجود.");
                    if (existing.Role == UserRoles.Administrator && existing.IsActive && !command.IsActive && await ActiveAdminCount(connection, transaction, ct).ConfigureAwait(false) <= 1) return UserManagementResult<ManagedUser>.Fail("last_active_administrator", "لا يمكن تعطيل آخر مسؤول نظام نشط.");
                    var now = DateTime.UtcNow.ToString("o");
                    await connection.ExecuteAsync(new CommandDefinition("UPDATE Users SET IsActive=@IsActive, UpdatedUtc=@Now WHERE Id=@UserId; DELETE FROM Sessions WHERE UserId=@UserId;", new { command.IsActive, Now = now, command.UserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    await Audit(connection, transaction, command.ActingUserId, command.UserId, command.IsActive ? "user_activated" : "user_deactivated", "active=" + command.IsActive, now, ct).ConfigureAwait(false);
                    transaction.Commit();
                }
            }
            return UserManagementResult<ManagedUser>.Ok(await FindAsync(command.UserId, ct).ConfigureAwait(false));
        }

        public async Task<UserManagementResult<bool>> ResetPasswordAsync(ResetUserPasswordCommand command, string passwordHash, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    if (await Find(connection, transaction, command.UserId, ct).ConfigureAwait(false) == null) return UserManagementResult<bool>.Fail("not_found", "المستخدم غير موجود.");
                    var now = DateTime.UtcNow.ToString("o");
                    await connection.ExecuteAsync(new CommandDefinition("UPDATE Users SET PasswordHash=@PasswordHash, UpdatedUtc=@Now WHERE Id=@UserId; DELETE FROM Sessions WHERE UserId=@UserId;", new { PasswordHash = passwordHash, Now = now, command.UserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    await Audit(connection, transaction, command.ActingUserId, command.UserId, "password_reset", null, now, ct).ConfigureAwait(false);
                    transaction.Commit();
                    return UserManagementResult<bool>.Ok(true);
                }
            }
        }

        public async Task<IReadOnlyList<string>> GetScreenIdsAsync(long userId, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                var rows = await connection.QueryAsync<string>(new CommandDefinition("SELECT ScreenId FROM UserScreenPermissions WHERE UserId=@UserId ORDER BY ScreenId", new { UserId = userId }, cancellationToken: ct)).ConfigureAwait(false);
                return rows.ToList();
            }
        }

        public async Task<UserManagementResult<bool>> SetScreenIdsAsync(SetScreenPermissionsCommand command, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    var user = await Find(connection, transaction, command.UserId, ct).ConfigureAwait(false);
                    if (user == null) return UserManagementResult<bool>.Fail("not_found", "المستخدم غير موجود.");
                    if (user.Role != UserRoles.Cashier) return UserManagementResult<bool>.Fail("invalid_role", "صلاحيات الشاشات متاحة لحسابات الكاشير فقط.");
                    var now = DateTime.UtcNow.ToString("o");
                    await connection.ExecuteAsync(new CommandDefinition("DELETE FROM UserScreenPermissions WHERE UserId=@UserId", new { command.UserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    foreach (var screenId in command.ScreenIds) await connection.ExecuteAsync(new CommandDefinition("INSERT INTO UserScreenPermissions (UserId, ScreenId, GrantedUtc, ChangedByUserId) VALUES (@UserId,@ScreenId,@Now,@ActingUserId)", new { command.UserId, ScreenId = screenId, Now = now, command.ActingUserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    await Audit(connection, transaction, command.ActingUserId, command.UserId, "screen_permissions_updated", "screenCount=" + command.ScreenIds.Count, now, ct).ConfigureAwait(false);
                    transaction.Commit();
                    return UserManagementResult<bool>.Ok(true);
                }
            }
        }

        private static Task<ManagedUser> Find(SQLiteConnection connection, SQLiteTransaction transaction, long userId, CancellationToken ct) { return connection.QuerySingleOrDefaultAsync<ManagedUser>(new CommandDefinition("SELECT Id, Username, DisplayName, Role, IsActive, CreatedUtc, UpdatedUtc, LastLoginUtc FROM Users WHERE Id=@UserId", new { UserId = userId }, transaction, cancellationToken: ct)); }
        private static Task<long> ActiveAdminCount(SQLiteConnection connection, SQLiteTransaction transaction, CancellationToken ct) { return connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Users WHERE Role='administrator' AND IsActive=1", transaction: transaction, cancellationToken: ct)); }
        private static bool WouldRemoveLastAdmin(ManagedUser existing, string nextRole, bool nextActive) { return existing.Role == UserRoles.Administrator && existing.IsActive && (nextRole != UserRoles.Administrator || !nextActive); }
        private static Task<int> Audit(SQLiteConnection connection, SQLiteTransaction transaction, long actor, long target, string action, string details, string now, CancellationToken ct) { return connection.ExecuteAsync(new CommandDefinition("INSERT INTO UserAuditLog (ActingUserId, TargetUserId, Action, ChangeDetails, CreatedUtc) VALUES (@Actor,@Target,@Action,@Details,@Now)", new { Actor = actor, Target = target, Action = action, Details = details, Now = now }, transaction, cancellationToken: ct)); }
        private static bool IsUnique(SQLiteException exception) { return exception.ResultCode == SQLiteErrorCode.Constraint || exception.ResultCode == SQLiteErrorCode.Constraint_Unique; }
    }
}
