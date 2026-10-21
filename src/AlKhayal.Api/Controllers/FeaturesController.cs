using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Features;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/features")]
    public sealed class FeaturesController : ApiController
    {
        private const string CookieName = "alkhayal_session";
        private readonly FeatureService _features;
        private readonly AuthService _auth;
        public FeaturesController(FeatureService features, AuthService auth) { _features = features; _auth = auth; }

        [HttpGet, Route("")]
        public async Task<IHttpActionResult> Catalog(CancellationToken ct)
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            return Ok(ApiEnvelope<IReadOnlyList<FeatureDefinition>>.Ok(_features.ListAll()));
        }

        [HttpGet, Route("effective")]
        public async Task<IHttpActionResult> Effective(CancellationToken ct)
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            return Ok(ApiEnvelope<FeatureSnapshot>.Ok(await _features.GetEffectiveAsync(ct).ConfigureAwait(false)));
        }

        [HttpPut, Route("modules/{key}")]
        public Task<IHttpActionResult> SetModule(string key, SetFeatureRequest request, CancellationToken ct) { return Set(FeatureKinds.Module, key, request, ct); }

        [HttpPut, Route("capabilities/{key}")]
        public Task<IHttpActionResult> SetCapability(string key, SetFeatureRequest request, CancellationToken ct) { return Set(FeatureKinds.Capability, key, request, ct); }

        [HttpPost, Route("recommendations/preview")]
        public async Task<IHttpActionResult> Preview(FeatureRecommendationsRequest request, CancellationToken ct)
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            if (request == null || request.ActivityKeys == null) return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", "الأنشطة مطلوبة لمعاينة التوصيات."));
            try { return Ok(ApiEnvelope<FeatureSnapshot>.Ok(await _features.PreviewAsync(request.ActivityKeys, ct).ConfigureAwait(false))); }
            catch (ArgumentException exception) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("invalid_activity", exception.Message)); }
        }

        [HttpPost, Route("recommendations/apply")]
        public async Task<IHttpActionResult> Apply(FeatureRecommendationsRequest request, CancellationToken ct)
        {
            var user = await AuthorizedAdministrator(ct).ConfigureAwait(false);
            if (user.Error != null) return user.Error;
            var result = await _features.ApplyAsync(request == null ? null : new ApplyFeatureRecommendationsCommand { ActivityKeys = request.ActivityKeys, ChangedByUserId = user.User.Id }, ct).ConfigureAwait(false);
            if (!result.Succeeded) return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail(result.ErrorCode, result.ErrorMessage));
            return Ok(ApiEnvelope<FeatureSnapshot>.Ok(result.Value));
        }

        private async Task<IHttpActionResult> Set(string kind, string key, SetFeatureRequest request, CancellationToken ct)
        {
            var user = await AuthorizedAdministrator(ct).ConfigureAwait(false);
            if (user.Error != null) return user.Error;
            if (request == null || !request.IsEnabled.HasValue) return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", "يجب تحديد حالة الميزة."));
            var result = await _features.SetAsync(new SetFeatureCommand { Kind = kind, Key = key, IsEnabled = request.IsEnabled.Value, ChangedByUserId = user.User.Id }, ct).ConfigureAwait(false);
            if (!result.Succeeded) return Content(result.ErrorCode == "dependent_feature" || result.ErrorCode == "missing_dependency" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail(result.ErrorCode, result.ErrorMessage));
            return Ok(ApiEnvelope<FeatureSnapshot>.Ok(result.Value));
        }

        private async Task<AuthCheck> AuthorizedAdministrator(CancellationToken ct)
        {
            var user = await CurrentUser(ct).ConfigureAwait(false);
            if (user == null) return new AuthCheck { Error = UnauthorizedResult() };
            if (!string.Equals(user.Role, "administrator", StringComparison.OrdinalIgnoreCase)) return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("forbidden", "إدارة الوحدات والميزات متاحة لمسؤول النظام فقط.")) };
            if (!await _auth.ValidateCsrfAsync(ReadToken(), ReadHeader("X-CSRF-Token"), ct).ConfigureAwait(false)) return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى.")) };
            return new AuthCheck { User = user };
        }

        private async Task<UserIdentity> CurrentUser(CancellationToken ct) { return await _auth.GetCurrentUserAsync(ReadToken(), ct).ConfigureAwait(false); }
        private IHttpActionResult UnauthorizedResult() { return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولاً.")); }
        private string ReadToken() { IEnumerable<string> values; if (!Request.Headers.TryGetValues("Cookie", out values)) return null; foreach (var header in values) foreach (var part in header.Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == CookieName) return pair[1]; } return null; }
        private string ReadHeader(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
        private sealed class AuthCheck { public UserIdentity User { get; set; } public IHttpActionResult Error { get; set; } }
    }
}
