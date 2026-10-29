using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Features;
using AlKhayal.Application.Printing;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/labels")]
    public sealed class LabelsController : ApiController
    {
        private readonly LabelService _labels; private readonly BarcodeService _barcodes; private readonly AuthService _auth; private readonly UserManagementService _users; private readonly FeatureService _features;
        public LabelsController(LabelService labels, BarcodeService barcodes, AuthService auth, UserManagementService users, FeatureService features) { _labels = labels; _barcodes = barcodes; _auth = auth; _users = users; _features = features; }
        [HttpGet, Route("settings")]
        public Task<IHttpActionResult> Settings(CancellationToken ct) => Run(true, false, false, ct, async u => Ok(ApiEnvelope<LabelSettings>.Ok(await _labels.SettingsAsync(ct).ConfigureAwait(false))));
        [HttpGet, Route("options")]
        public Task<IHttpActionResult> Options(CancellationToken ct) => Run(false, true, false, ct, async u => { var s = await _labels.SettingsAsync(ct).ConfigureAwait(false); return Ok(ApiEnvelope<object>.Ok(new { s.DefaultProfileId, profiles = s.Profiles.Select(p => new { p.Id, p.Name, p.TemplateId, p.Copies }), templates = s.Templates.Select(t => new { t.Id, t.Name }) })); });
        [HttpGet, Route("defaults")]
        public Task<IHttpActionResult> Defaults(CancellationToken ct) => Run(true, false, false, ct, u => Task.FromResult<IHttpActionResult>(Ok(ApiEnvelope<LabelSettings>.Ok(LabelService.Defaults()))));
        [HttpPut, Route("settings")]
        public Task<IHttpActionResult> Save(LabelSettings settings, CancellationToken ct) => Run(true, false, true, ct, async u => Ok(ApiEnvelope<LabelSettings>.Ok(await _labels.SaveAsync(settings, u.Id, ct).ConfigureAwait(false))));
        [HttpPost, Route("preview")]
        public Task<IHttpActionResult> Preview(LabelTemplate template, CancellationToken ct) => Run(true, false, true, ct, async u => Ok(ApiEnvelope<object>.Ok(new { pngBase64 = Convert.ToBase64String(await _labels.PreviewAsync(template, ct).ConfigureAwait(false)) })));
        [HttpPost, Route("jobs")]
        public Task<IHttpActionResult> Submit(LabelBatchRequest request, CancellationToken ct) => Run(false, true, true, ct, async u => Ok(ApiEnvelope<PrintJob>.Ok(await _labels.SubmitAsync(request, u.Id, ct).ConfigureAwait(false))));
        [HttpPost, Route("test/{profileId}")]
        public Task<IHttpActionResult> Test(string profileId, PrintRequest request, CancellationToken ct) => Run(true, true, true, ct, async u => Ok(ApiEnvelope<PrintJob>.Ok(await _labels.TestAsync(profileId, request?.OperationKey, u.Id, ct).ConfigureAwait(false))));
        [HttpPost, Route("barcodes")]
        public Task<IHttpActionResult> Barcode(BarcodeChange request, CancellationToken ct) => Run(false, false, true, ct, async u => Ok(ApiEnvelope<BarcodeChangeResult>.Ok(await _barcodes.SaveAsync(request, u.Id, ct).ConfigureAwait(false))));
        private async Task<IHttpActionResult> Run(bool admin, bool labelsEnabled, bool mutation, CancellationToken ct, Func<UserIdentity, Task<IHttpActionResult>> action)
        {
            var token = Cookie(); var u = await _auth.GetCurrentUserAsync(token, ct).ConfigureAwait(false); if (u == null) return Fail(HttpStatusCode.Unauthorized, "unauthorized", "يرجى تسجيل الدخول أولًا.");
            if (admin && u.Role != UserRoles.Administrator) return Fail(HttpStatusCode.Forbidden, "forbidden", "إعدادات الملصقات متاحة لمسؤول النظام فقط.");
            var features = await _features.GetEffectiveAsync(ct).ConfigureAwait(false);
            if (labelsEnabled && !features.Modules.Any(f => f.Key == "barcode-label-printing" && f.IsEnabled)) return Fail(HttpStatusCode.Forbidden, "feature_disabled", "فعّل وحدة طباعة ملصقات الباركود من الوحدات والميزات.");
            if (!admin && u.Role != UserRoles.Administrator)
            {
                var access = await _users.GetEffectiveAccessAsync(u, features, ct).ConfigureAwait(false);
                if (!access.ScreenIds.Any(x => x == "products-list" || x == "product-edit" || x == "product-add" || x == "product-quick-add")) return Fail(HttpStatusCode.Forbidden, "screen_forbidden", "لا تملك صلاحية إدارة باركود المنتجات وطباعة ملصقاتها.");
                if (!labelsEnabled && !access.ScreenIds.Any(x => x == "products-list" || x == "product-edit")) return Fail(HttpStatusCode.Forbidden, "screen_forbidden", "تعديل الباركود يتطلب صلاحية تحرير المنتج.");
            }
            if (mutation)
            {
                var origin = Header("Origin"); Uri uri;
                if (origin != null && (!Uri.TryCreate(origin, UriKind.Absolute, out uri) || uri.GetLeftPart(UriPartial.Authority) != Request.RequestUri.GetLeftPart(UriPartial.Authority))) return Fail(HttpStatusCode.Forbidden, "origin_forbidden", "مصدر الطلب غير مسموح.");
                if (!await _auth.ValidateCsrfAsync(token, Header("X-CSRF-Token"), ct).ConfigureAwait(false)) return Fail(HttpStatusCode.Forbidden, "invalid_csrf", "حدّث الصفحة للتحقق من الطلب.");
            }
            if (!ModelState.IsValid) return Fail(HttpStatusCode.BadRequest, "validation", "بيانات الطلب غير صالحة.");
            try { return await action(u).ConfigureAwait(false); }
            catch (ArgumentException ex) { return Fail(HttpStatusCode.BadRequest, "validation", ex.Message); }
            catch (PrintException ex) { return Fail(ex.Code == "not_found" ? HttpStatusCode.NotFound : ex.Code == "conflict" || ex.Code == "key_conflict" || ex.Code == "print_busy" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, ex.Code, ex.Message); }
            catch (CatalogStoreException ex) { return Fail(ex.Code == "not_found" ? HttpStatusCode.NotFound : ex.Code.Contains("conflict") || ex.Code == "duplicate_barcode" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, ex.Code, ex.Message); }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { System.Diagnostics.Trace.TraceError("Label/barcode operation failed: {0}", ex.GetType().Name); return Fail(HttpStatusCode.ServiceUnavailable, "labels_unavailable", "تعذر تأكيد العملية. احتفظ بالبيانات وأعد المحاولة بنفس مفتاح العملية أو راجع سجل الطباعة."); }
        }
        private IHttpActionResult Fail(HttpStatusCode status, string code, string message) => Content(status, ApiEnvelope<object>.Fail(code, message));
        private string Header(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
        private string Cookie() { foreach (var part in (Header("Cookie") ?? "").Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == "alkhayal_session") return pair[1]; } return null; }
    }
}
