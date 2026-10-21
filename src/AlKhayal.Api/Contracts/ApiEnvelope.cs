namespace AlKhayal.Api.Contracts
{
    public sealed class ApiEnvelope<T> { public bool Success { get; set; } public T Data { get; set; } public ApiError Error { get; set; } public static ApiEnvelope<T> Ok(T data) { return new ApiEnvelope<T> { Success=true, Data=data }; } public static ApiEnvelope<T> Fail(string code, string message) { return new ApiEnvelope<T> { Success=false, Error=new ApiError { Code=code, Message=message } }; } }
    public sealed class ApiError { public string Code { get; set; } public string Message { get; set; } }
    public sealed class LoginRequest { public string Username { get; set; } public string Password { get; set; } }
    public sealed class UserResponse { public long Id { get; set; } public string Username { get; set; } public string DisplayName { get; set; } public string Role { get; set; } public string CsrfToken { get; set; } }
    public sealed class SaveBusinessActivitiesRequest { public System.Collections.Generic.IReadOnlyList<string> ActivityKeys { get; set; } public string PrimaryActivityKey { get; set; } public bool ApplyRecommendations { get; set; } }
    public sealed class SetFeatureRequest { public bool? IsEnabled { get; set; } }
    public sealed class FeatureRecommendationsRequest { public System.Collections.Generic.IReadOnlyList<string> ActivityKeys { get; set; } }
    public sealed class CreateUserRequest { public string Username { get; set; } public string DisplayName { get; set; } public string Role { get; set; } public string Password { get; set; } public bool IsActive { get; set; } = true; }
    public sealed class UpdateUserRequest { public string Username { get; set; } public string DisplayName { get; set; } public string Role { get; set; } public bool IsActive { get; set; } }
    public sealed class SetUserStatusRequest { public bool? IsActive { get; set; } }
    public sealed class ResetPasswordRequest { public string Password { get; set; } }
    public sealed class ScreenPermissionsRequest { public System.Collections.Generic.IReadOnlyList<string> ScreenIds { get; set; } }
}
