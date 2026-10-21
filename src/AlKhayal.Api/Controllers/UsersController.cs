using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Features;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/users")]
    public sealed class UsersController : ApiController
    {
        private const string CookieName = "alkhayal_session";
        private readonly UserManagementService _users; private readonly FeatureService _features; private readonly AuthService _auth;
        public UsersController(UserManagementService users, FeatureService features, AuthService auth) { _users = users; _features = features; _auth = auth; }

        [HttpGet, Route("")] public async Task<IHttpActionResult> List(CancellationToken ct) { var check = await Administrator(ct, false); if (check.Error != null) return check.Error; return Ok(ApiEnvelope<IReadOnlyList<ManagedUser>>.Ok(await _users.ListAsync(ct))); }
        [HttpGet, Route("{id:long}")] public async Task<IHttpActionResult> Get(long id, CancellationToken ct) { var check = await Administrator(ct, false); if (check.Error != null) return check.Error; var user = await _users.GetAsync(id, ct); return user == null ? NotFoundResult() : Ok(ApiEnvelope<ManagedUser>.Ok(user)); }
        [HttpPost, Route("")] public async Task<IHttpActionResult> Create(CreateUserRequest request, CancellationToken ct) { var check = await Administrator(ct, true); if (check.Error != null) return check.Error; var result = await _users.CreateAsync(request == null ? null : new CreateUserCommand { Username=request.Username, DisplayName=request.DisplayName, Role=request.Role, Password=request.Password, IsActive=request.IsActive, ActingUserId=check.User.Id }, ct); return ToResult(result, HttpStatusCode.Created); }
        [HttpPut, Route("{id:long}")] public async Task<IHttpActionResult> Update(long id, UpdateUserRequest request, CancellationToken ct) { var check = await Administrator(ct, true); if (check.Error != null) return check.Error; var result = await _users.UpdateAsync(request == null ? null : new UpdateUserCommand { UserId=id, Username=request.Username, DisplayName=request.DisplayName, Role=request.Role, IsActive=request.IsActive, ActingUserId=check.User.Id }, ct); return ToResult(result); }
        [HttpPut, Route("{id:long}/status")] public async Task<IHttpActionResult> Status(long id, SetUserStatusRequest request, CancellationToken ct) { var check = await Administrator(ct, true); if (check.Error != null) return check.Error; var result = await _users.SetStatusAsync(request == null || !request.IsActive.HasValue ? null : new SetUserStatusCommand { UserId=id, IsActive=request.IsActive.Value, ActingUserId=check.User.Id }, ct); return ToResult(result); }
        [HttpPost, Route("{id:long}/reset-password")] public async Task<IHttpActionResult> ResetPassword(long id, ResetPasswordRequest request, CancellationToken ct) { var check = await Administrator(ct, true); if (check.Error != null) return check.Error; var result = await _users.ResetPasswordAsync(request == null ? null : new ResetUserPasswordCommand { UserId=id, Password=request.Password, ActingUserId=check.User.Id }, ct); return ToResult(result); }
        [HttpGet, Route("{id:long}/screen-permissions")] public async Task<IHttpActionResult> Permissions(long id, CancellationToken ct) { var check = await Administrator(ct, false); if (check.Error != null) return check.Error; var result = await _users.GetPermissionsAsync(id, await _features.GetEffectiveAsync(ct), ct); return ToResult(result); }
        [HttpPut, Route("{id:long}/screen-permissions")] public async Task<IHttpActionResult> Permissions(long id, ScreenPermissionsRequest request, CancellationToken ct) { var check = await Administrator(ct, true); if (check.Error != null) return check.Error; var result = await _users.SetPermissionsAsync(request == null ? null : new SetScreenPermissionsCommand { UserId=id, ScreenIds=request.ScreenIds, ActingUserId=check.User.Id }, ct); return ToResult(result); }

        private async Task<AuthCheck> Administrator(CancellationToken ct, bool requireCsrf)
        {
            var user = await _auth.GetCurrentUserAsync(ReadToken(), ct); if (user == null) return new AuthCheck { Error = Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولاً.")) };
            if (user.Role != UserRoles.Administrator) return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("forbidden", "إدارة المستخدمين متاحة لمسؤول النظام فقط.")) };
            if (requireCsrf && !await _auth.ValidateCsrfAsync(ReadToken(), Header("X-CSRF-Token"), ct)) return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى.")) };
            return new AuthCheck { User = user };
        }
        private IHttpActionResult ToResult<T>(UserManagementResult<T> result, HttpStatusCode success = HttpStatusCode.OK) { if (result.Succeeded) return Content(success, ApiEnvelope<T>.Ok(result.Value)); var status=result.ErrorCode=="not_found"?HttpStatusCode.NotFound:result.ErrorCode=="duplicate_username"||result.ErrorCode=="last_active_administrator"?HttpStatusCode.Conflict:HttpStatusCode.BadRequest; return Content(status, ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage)); }
        private IHttpActionResult NotFoundResult() { return Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", "المستخدم غير موجود.")); }
        private string ReadToken() { IEnumerable<string> values; if(!Request.Headers.TryGetValues("Cookie",out values)) return null; foreach(var header in values) foreach(var part in header.Split(';')) { var pair=part.Trim().Split(new[]{'='},2); if(pair.Length==2 && pair[0]==CookieName) return pair[1]; } return null; }
        private string Header(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name,out values) ? values.FirstOrDefault() : null; }
        private sealed class AuthCheck { public UserIdentity User { get; set; } public IHttpActionResult Error { get; set; } }
    }
}
