using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Features;
using AlKhayal.Application.Returns;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Users;
using AlKhayal.Core.Models;

namespace AlKhayal.Api.Controllers
{
    public sealed class ManagedReturnRequest
    {
        public string Type { get; set; }
        public long InvoiceId { get; set; }
        public string OperationKey { get; set; }
        public string Reason { get; set; }
        public string ReturnMode { get; set; }
        public List<ManagedReturnLine> Lines { get; set; }
    }
    public sealed class ManagedReturnLine { public long InvoiceLineId { get; set; } public string Quantity { get; set; } public List<ReturnUnit> Units { get; set; } }
    [RoutePrefix("api/returns")]
    public sealed class ReturnsController : ApiController
    {
        private readonly ReturnService _returns; private readonly SalesService _sales; private readonly PurchaseService _purchases;
        private readonly AuthService _auth; private readonly UserManagementService _users; private readonly FeatureService _features;
        public ReturnsController(ReturnService returns, SalesService sales, PurchaseService purchases, AuthService auth, UserManagementService users, FeatureService features)
        { _returns = returns; _sales = sales; _purchases = purchases; _auth = auth; _users = users; _features = features; }
        [HttpGet, Route("")]
        // Web API's simple-string action binder rejects an explicitly empty query value.
        // Read optional search text at the HTTP boundary; Application normalizes null/whitespace.
        public Task<IHttpActionResult> List(CancellationToken ct) => Run(false, async user => Ok(ApiEnvelope<object>.Ok(await _returns.ListAsync(Request.GetQueryNameValuePairs().FirstOrDefault(x => string.Equals(x.Key, "search", StringComparison.OrdinalIgnoreCase)).Value, ct).ConfigureAwait(false))), ct);
        [HttpGet, Route("invoice")]
        public Task<IHttpActionResult> Lookup(string type, string number, CancellationToken ct) => Run(false, async user => { var result = await _returns.LookupAsync(type, number, ct).ConfigureAwait(false); return result == null ? Missing("لم يتم العثور على فاتورة منشورة بهذا الرقم.") : Ok(ApiEnvelope<object>.Ok(result)); }, ct);
        [HttpGet, Route("{type}/{id:long}")]
        public Task<IHttpActionResult> Detail(string type, long id, CancellationToken ct) => Run(false, async user => { var result = await _returns.GetAsync(type, id, ct).ConfigureAwait(false); return result == null ? Missing("المرتجع غير موجود.") : Ok(ApiEnvelope<object>.Ok(result)); }, ct);
        [HttpPost, Route("")]
        public Task<IHttpActionResult> Create(ManagedReturnRequest request, CancellationToken ct) => Run(true, async user =>
        {
            if (request == null || request.InvoiceId <= 0 || request.Lines == null || request.Lines.Any(x => x == null || (x.Units != null && x.Units.Any(u => u == null)))) throw new ArgumentException("راجع الفاتورة وبنود المرتجع.");
            if (request.Type == "sales")
            {
                var result = await _sales.ReturnAsync(request.InvoiceId, new SaleReturnCommand { OperationKey = request.OperationKey, Reason = request.Reason, Lines = request.Lines.Select(x => new SaleReturnLineCommand { InvoiceLineId = x.InvoiceLineId, Quantity = x.Quantity, Serials = (x.Units ?? new List<ReturnUnit>()).Select(u => u.Serial).ToList() }).ToList() }, user.Id, ct).ConfigureAwait(false);
                if (result == null) return Missing("الفاتورة غير موجودة."); if (!result.Succeeded) return Conflict(result.ErrorCode, result.ErrorMessage);
                return Content(HttpStatusCode.Created, ApiEnvelope<object>.Ok(new { type = "sales", id = result.ReturnId }));
            }
            if (request.Type != "purchase") throw new ArgumentException("اختر فاتورة بيع أو شراء.");
            var invoice = await _purchases.GetAsync(request.InvoiceId, ct).ConfigureAwait(false); if (invoice == null) return Missing("الفاتورة غير موجودة.");
            var purchase = await _purchases.PostReturnAsync(request.InvoiceId, new PurchaseReturnCommand { WarehouseId = invoice.WarehouseId, ReturnMode = request.ReturnMode, Lines = request.Lines.Select(x => new PurchaseReturnLine { PurchaseInvoiceLineId = x.InvoiceLineId, Quantity = x.Quantity, PhysicalUnits = (x.Units ?? new List<ReturnUnit>()).Select(u => new PurchasePhysicalUnit { Serial = u.Serial, Imeis = u.Imeis }).ToList() }).ToList() }, request.OperationKey, user.Id, ct).ConfigureAwait(false);
            if (purchase == null) return Conflict("return_unavailable", "الكمية أو الفاتورة غير متاحة للإرجاع. حدّث الفاتورة وراجع الرصيد المتاح."); if (!purchase.Succeeded) return Conflict(purchase.ErrorCode, purchase.ErrorMessage);
            return Content(HttpStatusCode.Created, ApiEnvelope<object>.Ok(new { type = "purchase", id = purchase.Id }));
        }, ct);
        private async Task<IHttpActionResult> Run(bool mutation, Func<UserIdentity, Task<IHttpActionResult>> action, CancellationToken ct)
        {
            var token = Cookie(); var user = await _auth.GetCurrentUserAsync(token, ct).ConfigureAwait(false);
            if (user == null) return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولًا."));
            if (user.Role != UserRoles.Administrator)
            {
                var access = await _users.GetEffectiveAccessAsync(user, await _features.GetEffectiveAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                if (!access.ScreenIds.Contains("returns-overview", StringComparer.OrdinalIgnoreCase)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("screen_forbidden", "لا تملك صلاحية إدارة المرتجعات."));
            }
            if (mutation && !await _auth.ValidateCsrfAsync(token, Header("X-CSRF-Token"), ct).ConfigureAwait(false)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة."));
            if (!ModelState.IsValid) return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", "بيانات غير صالحة."));
            try { return await action(user).ConfigureAwait(false); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (System.Data.SQLite.SQLiteException ex) { System.Diagnostics.Trace.TraceError("Return management database failure: {0}", ex.ResultCode); return Content(HttpStatusCode.ServiceUnavailable, ApiEnvelope<object>.Fail("database_unavailable", "تعذر تأكيد الحفظ. أعد المحاولة بنفس البيانات للتحقق من النتيجة.")); }
        }
        private IHttpActionResult Missing(string message) => Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", message));
        private IHttpActionResult Conflict(string code, string message) => Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail(code, message));
        private string Header(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
        private string Cookie() { var cookie = Header("Cookie"); if (cookie == null) return null; foreach (var part in cookie.Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == "alkhayal_session") return pair[1]; } return null; }
    }
}
