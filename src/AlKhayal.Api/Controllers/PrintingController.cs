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
using AlKhayal.Application.Printing;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/printing")]
    public sealed class PrintingController : ApiController
    {
        private readonly PrintingService _printing;
        private readonly AuthService _auth;
        private readonly UserManagementService _users;
        private readonly FeatureService _features;
        public PrintingController(PrintingService printing, AuthService auth, UserManagementService users, FeatureService features) { _printing = printing; _auth = auth; _users = users; _features = features; }
        [HttpGet, Route("settings")]
        public Task<IHttpActionResult> Settings(CancellationToken ct) => Run(true, false, null, ct, async u => Ok(ApiEnvelope<PrintingSettings>.Ok(await _printing.SettingsAsync(ct).ConfigureAwait(false))));
        [HttpPut, Route("settings")]
        public Task<IHttpActionResult> Save(PrintingSettings settings, CancellationToken ct) => Run(true, true, null, ct, async u => Ok(ApiEnvelope<PrintingSettings>.Ok(await _printing.SaveAsync(settings, u.Id, ct).ConfigureAwait(false))));
        [HttpGet, Route("printers")]
        public Task<IHttpActionResult> Printers(CancellationToken ct) => Run(true, false, null, ct, async u => Ok(ApiEnvelope<IReadOnlyList<PrinterInfo>>.Ok(await _printing.PrintersAsync(ct).ConfigureAwait(false))));
        [HttpGet, Route("defaults")]
        public Task<IHttpActionResult> Defaults(CancellationToken ct) => Run(true, false, null, ct, u => Task.FromResult<IHttpActionResult>(Ok(ApiEnvelope<PrintingSettings>.Ok(PrintingService.Defaults()))));
        [HttpGet, Route("jobs")]
        public Task<IHttpActionResult> Jobs(CancellationToken ct) => Run(true, false, null, ct, async u => Ok(ApiEnvelope<IReadOnlyList<PrintJob>>.Ok(await _printing.JobsAsync(ct).ConfigureAwait(false))));
        [HttpPost, Route("preview")]
        public Task<IHttpActionResult> Preview(PrintTemplate template, CancellationToken ct) => Run(true, true, null, ct, async u => Ok(ApiEnvelope<object>.Ok(new { pngBase64 = Convert.ToBase64String(await _printing.PreviewAsync(template, ct).ConfigureAwait(false)), message = "معاينة الصفحة الأولى ببيانات اختبار من محرّك C#" })));
        [HttpPost, Route("test/{type}")]
        public Task<IHttpActionResult> Test(string type, PrintRequest request, CancellationToken ct) => Run(true, true, null, ct, async u => Ok(ApiEnvelope<PrintJob>.Ok(await _printing.TestAsync(type, request?.OperationKey, u.Id, ct).ConfigureAwait(false))));
        [HttpPost, Route("jobs/{id:long}/review")]
        public Task<IHttpActionResult> Review(long id, CancellationToken ct) => Run(true, true, null, ct, async u => { await _printing.ReviewAsync(id, u.Id, ct).ConfigureAwait(false); return Ok(ApiEnvelope<object>.Ok(new { message = "سُجلت المراجعة. إعادة الطباعة متاحة يدويًا من المستند." })); });
        [HttpPost, Route("jobs")]
        public Task<IHttpActionResult> Submit(PrintRequest request, CancellationToken ct) => Run(false, true, request?.DocumentType, ct, async u => Ok(ApiEnvelope<PrintJob>.Ok(await _printing.SubmitAsync(request, u.Id, u.Role == UserRoles.Administrator, ct).ConfigureAwait(false))));
        private async Task<IHttpActionResult> Run(bool adminOnly, bool mutation, string type, CancellationToken ct, Func<UserIdentity, Task<IHttpActionResult>> action)
        {
            var token = Cookie(); var user = await _auth.GetCurrentUserAsync(token, ct).ConfigureAwait(false);
            if (user == null) return Fail(HttpStatusCode.Unauthorized, "unauthorized", "يرجى تسجيل الدخول أولًا.");
            if (adminOnly && user.Role != UserRoles.Administrator) return Fail(HttpStatusCode.Forbidden, "forbidden", "إعدادات الطباعة متاحة لمسؤول النظام فقط.");
            if (!adminOnly)
            {
                if (!PrintTypes.All.Contains(type)) return Fail(HttpStatusCode.BadRequest, "validation", "اختر نوع مستند صحيحًا.");
                var features = await _features.GetEffectiveAsync(ct).ConfigureAwait(false);
                if (type.StartsWith("shift-", StringComparison.Ordinal) && !features.Modules.Any(m => m.Key == "advanced-shifts" && m.IsEnabled)) return Fail(HttpStatusCode.Forbidden, "feature_disabled", "وحدة الورديات معطلة.");
                if (user.Role != UserRoles.Administrator)
                {
                    var access = await _users.GetEffectiveAccessAsync(user, features, ct).ConfigureAwait(false);
                    var grants = type == "sale" ? new[] { "pos", "sales-management", "sales-list" } : type == "sales-return" ? new[] { "sales-returns", "returns-overview" } : type == "purchase" ? new[] { "purchase-management", "purchases-list" } : type == "purchase-return" ? new[] { "purchase-returns", "returns-overview" } : type == "customer-payment" ? new[] { "customer-statement", "customer-credit-payment" } : type == "supplier-payment" ? new[] { "supplier-payment" } : new[] { "pos", "shifts-list", "shift-reports", "shift-details" };
                    if (!grants.Any(x => access.ScreenIds.Contains(x, StringComparer.OrdinalIgnoreCase))) return Fail(HttpStatusCode.Forbidden, "screen_forbidden", "لا تملك صلاحية طباعة هذا المستند.");
                }
            }
            if (mutation)
            {
                var origin = Header("Origin"); Uri originUri;
                if (origin != null && (!Uri.TryCreate(origin, UriKind.Absolute, out originUri) || originUri.GetLeftPart(UriPartial.Authority) != Request.RequestUri.GetLeftPart(UriPartial.Authority))) return Fail(HttpStatusCode.Forbidden, "origin_forbidden", "مصدر طلب الطباعة غير مسموح.");
                if (!await _auth.ValidateCsrfAsync(token, Header("X-CSRF-Token"), ct).ConfigureAwait(false)) return Fail(HttpStatusCode.Forbidden, "invalid_csrf", "حدّث الصفحة للتحقق من الطلب.");
            }
            if (!ModelState.IsValid) return Fail(HttpStatusCode.BadRequest, "validation", "بيانات الطلب غير صالحة.");
            try { return await action(user).ConfigureAwait(false); }
            catch (PrintException ex) { return Fail(ex.Code == "validation" || ex.Code == "invalid_logo" ? HttpStatusCode.BadRequest : ex.Code == "not_found" ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, ex.Code, ex.Message); }
            catch (System.Data.SQLite.SQLiteException ex) { System.Diagnostics.Trace.TraceError("Printing database failed: {0}", ex.ResultCode); return Fail(HttpStatusCode.ServiceUnavailable, "database_unavailable", "تعذر تأكيد حالة الطباعة. راجع سجل المهام قبل الإعادة."); }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { System.Diagnostics.Trace.TraceError("Printing API failed: {0}", ex.GetType().Name); return Fail(HttpStatusCode.ServiceUnavailable, "printing_unavailable", "تعذر الوصول إلى خدمة الطباعة أو التعريف. راجع إعدادات Windows وسجل المهام."); }
        }
        private IHttpActionResult Fail(HttpStatusCode status, string code, string message) => Content(status, ApiEnvelope<object>.Fail(code, message));
        private string Header(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
        private string Cookie() { foreach (var part in (Header("Cookie") ?? "").Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == "alkhayal_session") return pair[1]; } return null; }
    }
}
