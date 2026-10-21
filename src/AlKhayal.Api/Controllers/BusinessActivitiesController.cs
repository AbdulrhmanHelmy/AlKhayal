using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.BusinessActivities;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/business-activities")]
    public sealed class BusinessActivitiesController : ApiController
    {
        private const string CookieName = "alkhayal_session";
        private readonly BusinessActivityService _activities;
        private readonly AuthService _auth;
        public BusinessActivitiesController(BusinessActivityService activities, AuthService auth) { _activities = activities; _auth = auth; }

        [HttpGet, Route("")]
        public async Task<IHttpActionResult> List(CancellationToken ct)
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            return Ok(ApiEnvelope<IReadOnlyList<BusinessActivityDefinition>>.Ok(_activities.ListSupported()));
        }

        [HttpGet, Route("selected")]
        public async Task<IHttpActionResult> Selected(CancellationToken ct)
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            return Ok(ApiEnvelope<BusinessActivitySelection>.Ok(await _activities.GetSelectedAsync(ct).ConfigureAwait(false)));
        }

        [HttpGet, Route("recommendations")]
        public async Task<IHttpActionResult> Recommendations(string activityKeys = null, CancellationToken ct = default(CancellationToken))
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            IEnumerable<string> keys;
            if (string.IsNullOrWhiteSpace(activityKeys)) keys = (await _activities.GetSelectedAsync(ct).ConfigureAwait(false)).ActivityKeys;
            else keys = activityKeys.Split(',').Select(key => key.Trim()).Where(key => key.Length > 0);
            try { return Ok(ApiEnvelope<RecommendationComparison>.Ok(await _activities.CompareAsync(keys, ct).ConfigureAwait(false))); }
            catch (ArgumentException exception) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("invalid_activity", exception.Message)); }
        }

        [HttpPut, Route("selected")]
        public async Task<IHttpActionResult> Save(SaveBusinessActivitiesRequest request, CancellationToken ct)
        {
            var user = await CurrentUser(ct).ConfigureAwait(false);
            if (user == null) return UnauthorizedResult();
            if (!string.Equals(user.Role, "administrator", StringComparison.OrdinalIgnoreCase)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("forbidden", "إدارة نوع النشاط متاحة لمسؤول النظام فقط."));
            if (!await _auth.ValidateCsrfAsync(ReadToken(), ReadHeader("X-CSRF-Token"), ct).ConfigureAwait(false)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            var result = await _activities.SaveAsync(request == null ? null : new SaveBusinessActivitiesCommand
            {
                ActivityKeys = request.ActivityKeys,
                PrimaryActivityKey = request.PrimaryActivityKey,
                ApplyRecommendations = request.ApplyRecommendations,
                ChangedByUserId = user.Id
            }, ct).ConfigureAwait(false);
            if (!result.Succeeded) return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail(result.ErrorCode, result.ErrorMessage));
            return Ok(ApiEnvelope<BusinessActivitySelection>.Ok(result.Value));
        }

        private async Task<UserIdentity> CurrentUser(CancellationToken ct) { return await _auth.GetCurrentUserAsync(ReadToken(), ct).ConfigureAwait(false); }
        private IHttpActionResult UnauthorizedResult() { return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولاً.")); }
        private string ReadToken()
        {
            IEnumerable<string> values;
            if (!Request.Headers.TryGetValues("Cookie", out values)) return null;
            foreach (var header in values) foreach (var part in header.Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == CookieName) return pair[1]; }
            return null;
        }
        private string ReadHeader(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
    }
}
