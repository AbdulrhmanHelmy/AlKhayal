using System;

namespace AlKhayal.Application.Authentication
{
    public sealed class LoginCommand { public string Username { get; set; } public string Password { get; set; } }
    public sealed class UserIdentity { public long Id { get; set; } public string Username { get; set; } public string DisplayName { get; set; } public string Role { get; set; } }
    public sealed class AuthResult { public bool Succeeded { get; set; } public string ErrorCode { get; set; } public string ErrorMessage { get; set; } public UserIdentity User { get; set; } public string SessionToken { get; set; } public string CsrfToken { get; set; } }
    public sealed class SessionRecord { public string Token { get; set; } public string CsrfToken { get; set; } public long UserId { get; set; } public DateTime ExpiresUtc { get; set; } public string Username { get; set; } public string DisplayName { get; set; } public string Role { get; set; } }
    public sealed class UserRecord { public long Id { get; set; } public string Username { get; set; } public string DisplayName { get; set; } public string PasswordHash { get; set; } public string Role { get; set; } public bool IsActive { get; set; } }
}
