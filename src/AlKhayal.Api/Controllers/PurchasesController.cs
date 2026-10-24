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
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Users;
using AlKhayal.Core.Models;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/purchases")]
    public sealed class PurchasesController : ApiController
    {
        private readonly PurchaseService _purchases;
        private readonly AuthService _auth;
        private readonly UserManagementService _users;
        private readonly FeatureService _features;
        public PurchasesController(PurchaseService purchases, AuthService auth, UserManagementService users, FeatureService features)
        { _purchases = purchases; _auth = auth; _users = users; _features = features; }

        [HttpGet, Route("")]
        public async Task<IHttpActionResult> List(long? supplierId = null, string from = null, string to = null, CancellationToken ct = default(CancellationToken))
        {
            var access = await Require(ct, false).ConfigureAwait(false); if (access.Error != null) return access.Error;
            try { return Ok(ApiEnvelope<object>.Ok(await _purchases.ListAsync(supplierId, from, to, ct).ConfigureAwait(false))); }
            catch (ArgumentException ex) { return BadRequestEnvelope(ex.Message); }
        }

        [HttpGet, Route("{id:long}")]
        public async Task<IHttpActionResult> Get(long id, CancellationToken ct)
        {
            var access = await Require(ct, false).ConfigureAwait(false); if (access.Error != null) return access.Error;
            var invoice = await _purchases.GetAsync(id, ct).ConfigureAwait(false);
            return invoice == null ? NotFoundEnvelope() : Ok(ApiEnvelope<PurchaseInvoice>.Ok(invoice));
        }

        [HttpPost, Route("drafts")]
        public async Task<IHttpActionResult> CreateDraft(PurchaseDraftRequest request, CancellationToken ct)
        {
            var access = await Require(ct, true).ConfigureAwait(false); if (access.Error != null) return access.Error;
            try { return Content(HttpStatusCode.Created, ApiEnvelope<PurchaseInvoice>.Ok(await _purchases.CreateDraftAsync(Map(request), access.User.Id, ct).ConfigureAwait(false))); }
            catch (ArgumentException ex) { return BadRequestEnvelope(ex.Message); }
            catch (System.Data.SQLite.SQLiteException ex) { return ConflictEnvelope(ex.ResultCode == System.Data.SQLite.SQLiteErrorCode.Constraint ? "رقم الفاتورة مكرر أو توجد بيانات غير صالحة." : "تعذر حفظ المسودة."); }
        }

        [HttpPut, Route("{id:long}")]
        public async Task<IHttpActionResult> UpdateDraft(long id, PurchaseDraftRequest request, CancellationToken ct)
        {
            var access = await Require(ct, true).ConfigureAwait(false); if (access.Error != null) return access.Error;
            try
            {
                var invoice = await _purchases.UpdateDraftAsync(id, Map(request), access.User.Id, ct).ConfigureAwait(false);
                return invoice == null ? NotFoundEnvelope() : Ok(ApiEnvelope<PurchaseInvoice>.Ok(invoice));
            }
            catch (ArgumentException ex) { return BadRequestEnvelope(ex.Message); }
            catch (InvalidOperationException ex) { return ConflictEnvelope(ex.Message); }
            catch (System.Data.SQLite.SQLiteException) { return ConflictEnvelope("رقم الفاتورة مكرر أو توجد بيانات غير صالحة."); }
        }

        [HttpPost, Route("{id:long}/post")]
        public async Task<IHttpActionResult> Post(long id, PurchasePostRequest request, CancellationToken ct)
        {
            var access = await Require(ct, true).ConfigureAwait(false); if (access.Error != null) return access.Error;
            try
            {
                var result = await _purchases.PostAsync(id, request == null ? null : request.OperationKey, access.User.Id, ct).ConfigureAwait(false);
                if (result == null) return NotFoundEnvelope();
                if (!result.Succeeded) return Content(result.ErrorCode == "not_found" ? HttpStatusCode.NotFound : result.ErrorCode == "invalid_quantity" || result.ErrorCode == "invalid_cost" || result.ErrorCode == "cost_required" || result.ErrorCode == "sale_price_required" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, ApiEnvelope<object>.Fail(result.ErrorCode, result.ErrorMessage));
                return Ok(ApiEnvelope<PurchasePostResult>.Ok(result));
            }
            catch (ArgumentException ex) { return BadRequestEnvelope(ex.Message); }
        }

        [HttpGet, Route("{id:long}/returnable-lines")]
        public async Task<IHttpActionResult> ReturnableLines(long id, long warehouseId, CancellationToken ct)
        {
            var access = await Require(ct, false).ConfigureAwait(false); if (access.Error != null) return access.Error;
            if (warehouseId <= 0) return BadRequestEnvelope("اختر المخزن.");
            return Ok(ApiEnvelope<object>.Ok(await _purchases.ReturnableLinesAsync(id, warehouseId, ct).ConfigureAwait(false)));
        }

        [HttpGet, Route("{id:long}/returns")]
        public async Task<IHttpActionResult> Returns(long id, CancellationToken ct)
        {
            var access = await Require(ct, false).ConfigureAwait(false); if (access.Error != null) return access.Error;
            return Ok(ApiEnvelope<object>.Ok(await _purchases.ReturnsAsync(id, ct).ConfigureAwait(false)));
        }

        [HttpPost, Route("{id:long}/returns")]
        public async Task<IHttpActionResult> PostReturn(long id, PurchaseReturnRequest request, CancellationToken ct)
        {
            var access = await Require(ct, true).ConfigureAwait(false); if (access.Error != null) return access.Error;
            try
            {
                var command = request == null ? null : new PurchaseReturnCommand { WarehouseId = request.WarehouseId, ReturnMode = request.ReturnMode,
                    Lines = (request.Lines ?? new List<PurchaseReturnLineRequest>()).Select(x => x == null ? null : new PurchaseReturnLine { PurchaseInvoiceLineId = x.PurchaseInvoiceLineId, Quantity = x.Quantity,
                        PhysicalUnits = (x.PhysicalUnits ?? new List<PurchasePhysicalUnitRequest>()).Select(u => u == null ? null : new PurchasePhysicalUnit { Serial = u.Serial, Imeis = u.Imeis ?? new List<string>() }).ToList() }).ToList() };
                var result = await _purchases.PostReturnAsync(id, command, request == null ? null : request.OperationKey, access.User.Id, ct).ConfigureAwait(false);
                if (result == null) return NotFoundEnvelope();
                if (!result.Succeeded) return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail(result.ErrorCode, result.ErrorMessage));
                return Content(HttpStatusCode.Created, ApiEnvelope<PurchaseReturn>.Ok(result));
            }
            catch (ArgumentException ex) { return BadRequestEnvelope(ex.Message); }
        }

        private async Task<AuthCheck> Require(CancellationToken ct, bool csrf)
        {
            var token = ReadCookie(); var user = await _auth.GetCurrentUserAsync(token, ct).ConfigureAwait(false);
            if (user == null) return new AuthCheck { Error = Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولًا.")) };
            if (user.Role != UserRoles.Administrator)
            {
                var effective = await _users.GetEffectiveAccessAsync(user, await _features.GetEffectiveAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                if (!effective.ScreenIds.Contains(csrf ? "purchase-management" : "purchases-list", StringComparer.OrdinalIgnoreCase) && !effective.ScreenIds.Contains("purchase-management", StringComparer.OrdinalIgnoreCase))
                    return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("screen_forbidden", "لا تملك صلاحية شاشة المشتريات.")) };
            }
            if (csrf && !await _auth.ValidateCsrfAsync(token, ReadHeader("X-CSRF-Token"), ct).ConfigureAwait(false)) return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى.")) };
            return new AuthCheck { User = user };
        }

        private static PurchaseDraftCommand Map(PurchaseDraftRequest request)
        {
            if (request == null) return null;
            return new PurchaseDraftCommand { SupplierId = request.SupplierId, WarehouseId = request.WarehouseId, InvoiceNumber = request.InvoiceNumber, InvoiceDate = request.InvoiceDate, PaymentMode = request.PaymentMode, Notes = request.Notes,
                Lines = (request.Lines ?? new List<PurchaseLineRequest>()).Select(x => x == null ? null : new PurchaseInvoiceLine { VariantId = x.VariantId, ProductUnitId = x.ProductUnitId, Quantity = x.Quantity, PurchaseUnitPrice = x.PurchaseUnitPrice, SaleUnitPrice = x.SaleUnitPrice, LotNumber = x.LotNumber, ExpirationDate = x.ExpirationDate,
                    PhysicalUnits = (x.PhysicalUnits ?? new List<PurchasePhysicalUnitRequest>()).Select(u => u == null ? null : new PurchasePhysicalUnit { Serial = u.Serial, Imeis = u.Imeis ?? new List<string>() }).ToList() }).ToList() };
        }
        private IHttpActionResult NotFoundEnvelope() { return Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", "الفاتورة غير موجودة.")); }
        private IHttpActionResult BadRequestEnvelope(string message) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", message)); }
        private IHttpActionResult ConflictEnvelope(string message) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("purchase_conflict", message)); }
        private string ReadCookie() { IEnumerable<string> values; if (!Request.Headers.TryGetValues("Cookie", out values)) return null; foreach (var header in values) foreach (var part in header.Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == "alkhayal_session") return pair[1]; } return null; }
        private string ReadHeader(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
        private sealed class AuthCheck { public UserIdentity User { get; set; } public IHttpActionResult Error { get; set; } }
    }
}
