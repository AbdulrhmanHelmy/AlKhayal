using System;
using System.Collections.Generic;

namespace AlKhayal.Application.Users
{
    public static class UserRoles
    {
        public const string Administrator = "administrator";
        public const string Cashier = "cashier";
        public static bool IsValid(string role) { return role == Administrator || role == Cashier; }
    }

    public sealed class ManagedUser
    {
        public long Id { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime? LastLoginUtc { get; set; }
    }

    public sealed class ScreenDefinition
    {
        public string Id { get; set; }
        public string ArabicName { get; set; }
        public string Module { get; set; }
        public string FeatureKey { get; set; }
        public bool AdministratorOnly { get; set; }
    }

    public sealed class CashierScreenPermission
    {
        public string ScreenId { get; set; }
        public string ArabicName { get; set; }
        public string Module { get; set; }
        public string FeatureKey { get; set; }
        public bool IsGranted { get; set; }
        public bool IsModuleEnabled { get; set; }
        public bool IsEffective { get; set; }
    }

    public sealed class EffectiveScreenAccess
    {
        public string Role { get; set; }
        public IReadOnlyList<string> ScreenIds { get; set; }
        public DateTime GeneratedUtc { get; set; }
    }

    public sealed class CreateUserCommand { public string Username { get; set; } public string DisplayName { get; set; } public string Role { get; set; } public string Password { get; set; } public bool IsActive { get; set; } public long ActingUserId { get; set; } }
    public sealed class UpdateUserCommand { public long UserId { get; set; } public string Username { get; set; } public string DisplayName { get; set; } public string Role { get; set; } public bool IsActive { get; set; } public long ActingUserId { get; set; } }
    public sealed class SetUserStatusCommand { public long UserId { get; set; } public bool IsActive { get; set; } public long ActingUserId { get; set; } }
    public sealed class ResetUserPasswordCommand { public long UserId { get; set; } public string Password { get; set; } public long ActingUserId { get; set; } }
    public sealed class SetScreenPermissionsCommand { public long UserId { get; set; } public IReadOnlyList<string> ScreenIds { get; set; } public long ActingUserId { get; set; } }

    public sealed class UserManagementResult<T>
    {
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public T Value { get; set; }
        public static UserManagementResult<T> Ok(T value) { return new UserManagementResult<T> { Succeeded = true, Value = value }; }
        public static UserManagementResult<T> Fail(string code, string message) { return new UserManagementResult<T> { ErrorCode = code, ErrorMessage = message }; }
    }
}
