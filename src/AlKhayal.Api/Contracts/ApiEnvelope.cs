namespace AlKhayal.Api.Contracts
{
    public sealed class ApiEnvelope<T> { public bool Success { get; set; } public T Data { get; set; } public ApiError Error { get; set; } public static ApiEnvelope<T> Ok(T data) { return new ApiEnvelope<T> { Success=true, Data=data }; } public static ApiEnvelope<T> Fail(string code, string message) { return new ApiEnvelope<T> { Success=false, Error=new ApiError { Code=code, Message=message } }; } }
    public sealed class ApiError { public string Code { get; set; } public string Message { get; set; } }
    public sealed class LoginRequest { public string Username { get; set; } public string Password { get; set; } }
    public sealed class UserResponse { public long Id { get; set; } public string Username { get; set; } public string DisplayName { get; set; } public string Role { get; set; } public string CsrfToken { get; set; } }
}
