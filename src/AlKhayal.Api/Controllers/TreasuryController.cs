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
using AlKhayal.Application.Treasury;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/treasury")]
    public sealed class TreasuryController : ApiController
    {
        private readonly TreasuryService _treasury;
        private readonly AuthService _auth;
        private readonly UserManagementService _users;
        private readonly FeatureService _features;
        public TreasuryController(TreasuryService treasury, AuthService auth, UserManagementService users, FeatureService features)
        { _treasury = treasury; _auth = auth; _users = users; _features = features; }

        [HttpGet, Route("accounts")]
        public Task<IHttpActionResult> List(CancellationToken ct) => Run(new[] { "accounts-list", "account-deposit", "account-withdraw", "purchase-management", "supplier-payment", "expense-add" }, false,
            async user => Ok(ApiEnvelope<IReadOnlyList<TreasuryAccount>>.Ok(await _treasury.ListAsync(ct).ConfigureAwait(false))), ct);

        [HttpPost, Route("accounts")]
        public Task<IHttpActionResult> Create(TreasuryAccountCommand command, CancellationToken ct) => Run(new[] { "accounts-list" }, true,
            async user => Content(HttpStatusCode.Created, ApiEnvelope<TreasuryAccount>.Ok(await _treasury.SaveAsync(null, command, user.Id, ct).ConfigureAwait(false))), ct);

        [HttpPut, Route("accounts/{id:long}")]
        public Task<IHttpActionResult> Update(long id, TreasuryAccountCommand command, CancellationToken ct) => Run(new[] { "accounts-list" }, true,
            async user => Ok(ApiEnvelope<TreasuryAccount>.Ok(await _treasury.SaveAsync(id, command, user.Id, ct).ConfigureAwait(false))), ct);

        [HttpDelete, Route("accounts/{id:long}")]
        public Task<IHttpActionResult> Delete(long id, long version, CancellationToken ct) => Run(new[] { "accounts-list" }, true, async user =>
        {
            await _treasury.DeleteAsync(id, version, user.Id, ct).ConfigureAwait(false);
            return Ok(ApiEnvelope<object>.Ok(new { deleted = true }));
        }, ct);

        [HttpPost, Route("deposit")]
        public Task<IHttpActionResult> Deposit(TreasuryMovementCommand command, CancellationToken ct) => Post(command, "deposit", "account-deposit", ct);
        [HttpPost, Route("withdraw")]
        public Task<IHttpActionResult> Withdraw(TreasuryMovementCommand command, CancellationToken ct) => Post(command, "withdraw", "account-withdraw", ct);
        private Task<IHttpActionResult> Post(TreasuryMovementCommand command, string type, string route, CancellationToken ct) => Run(new[] { route }, true,
            async user => Content(HttpStatusCode.Created, ApiEnvelope<TreasuryMovement>.Ok(await _treasury.PostAsync(command, type, user.Id, ct).ConfigureAwait(false))), ct);

        private async Task<IHttpActionResult> Run(string[] routes, bool mutation, Func<UserIdentity, Task<IHttpActionResult>> action, CancellationToken ct)
        {
            var token = ReadToken();
            var user = await _auth.GetCurrentUserAsync(token, ct).ConfigureAwait(false);
            if (user == null) return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولًا."));
            if (user.Role != UserRoles.Administrator)
            {
                var access = await _users.GetEffectiveAccessAsync(user, await _features.GetEffectiveAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                if (!routes.Any(route => access.ScreenIds.Contains(route, StringComparer.OrdinalIgnoreCase)))
                    return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("screen_forbidden", "لا تملك صلاحية هذه العملية في الخزنة والبنك."));
            }
            if (mutation && !await _auth.ValidateCsrfAsync(token, Header("X-CSRF-Token"), ct).ConfigureAwait(false))
                return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            if (!ModelState.IsValid) return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", "بيانات الطلب غير صالحة."));
            try { return await action(user).ConfigureAwait(false); }
            catch (TreasuryException ex)
            {
                return Content(ex.Code == "not_found" ? HttpStatusCode.NotFound : ex.Code == "validation" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict,
                    ApiEnvelope<object>.Fail(ex.Code, ex.Message));
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                System.Diagnostics.Trace.TraceError("Treasury database operation failed: SQLite code {0}.", ex.ResultCode);
                return Content(HttpStatusCode.ServiceUnavailable, ApiEnvelope<object>.Fail("database_unavailable", "تعذر إتمام الحفظ في قاعدة البيانات. أعد المحاولة بنفس البيانات للتحقق من العملية."));
            }
        }
        private string ReadToken()
        {
            IEnumerable<string> values;
            if (!Request.Headers.TryGetValues("Cookie", out values)) return null;
            foreach (var header in values) foreach (var part in header.Split(';'))
            {
                var pair = part.Trim().Split(new[] { '=' }, 2);
                if (pair.Length == 2 && pair[0] == "alkhayal_session") return pair[1];
            }
            return null;
        }
        private string Header(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
    }
}
