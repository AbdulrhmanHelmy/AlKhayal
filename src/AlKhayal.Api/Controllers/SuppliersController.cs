using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Suppliers;
using AlKhayal.Core.Models;
using AlKhayal.Application.Users;
using AlKhayal.Application.Features;
using AlKhayal.Application.Treasury;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/suppliers")]
    public sealed class SuppliersController : ApiController
    {
        private const string CookieName = "alkhayal_session";
        private readonly SupplierService _suppliers;
        private readonly AuthService _auth;
        private readonly SupplierAccountService _accounts;
        private readonly SupplierLedgerService _ledger;
        private readonly UserManagementService _users;
        private readonly FeatureService _features;
        public SuppliersController(SupplierService suppliers, AuthService auth, SupplierAccountService accounts, SupplierLedgerService ledger, UserManagementService users, FeatureService features) { _suppliers = suppliers; _auth = auth; _accounts = accounts; _ledger = ledger; _users=users; _features=features; }

        [HttpGet, Route("")]
        public async Task<IHttpActionResult> GetAll(string search=null,int? page=null,int? pageSize=null,CancellationToken ct=default(CancellationToken))
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if(page.HasValue||pageSize.HasValue)return Ok(ApiEnvelope<SupplierPage>.Ok(await _suppliers.ListAsync(search,page??1,pageSize??25,ct).ConfigureAwait(false)));
            return Ok(ApiEnvelope<object>.Ok(_suppliers.GetAll()));
        }

        [HttpPost, Route("")]
        public async Task<IHttpActionResult> Create(Supplier supplier, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return CsrfResult();
            try { return Content(HttpStatusCode.Created, ApiEnvelope<Supplier>.Ok(_suppliers.Create(supplier))); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (System.Data.SQLite.SQLiteException) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("supplier_conflict", "كود المورد مستخدم بالفعل أو توجد بيانات غير صالحة.")); }
        }

        [HttpPut, Route("{id:long}")]
        public async Task<IHttpActionResult> Update(long id, Supplier supplier, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return CsrfResult();
            try { var updated = _suppliers.Update(id, supplier); return updated == null ? NotFoundResult("المورد غير موجود.") : Ok(ApiEnvelope<Supplier>.Ok(updated)); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (System.Data.SQLite.SQLiteException) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("supplier_conflict", "كود المورد مستخدم بالفعل أو توجد بيانات غير صالحة.")); }
        }

        [HttpDelete, Route("{id:long}")]
        public async Task<IHttpActionResult> Delete(long id, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return CsrfResult();
            try { return _suppliers.Delete(id) ? (IHttpActionResult)Ok(ApiEnvelope<object>.Ok(new { deleted = true })) : NotFoundResult("المورد غير موجود."); }
            catch (System.Data.SQLite.SQLiteException) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("supplier_in_use", "لا يمكن حذف مورد لديه فواتير أو مدفوعات مسجلة.")); }
        }

        [HttpGet, Route("{id:long}/statement")]
        public async Task<IHttpActionResult> GetStatement(long id, string from = null, string to = null, CancellationToken ct = default(CancellationToken))
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            try
            {
                var statement = await _ledger.StatementAsync(id, from, to, ct);
                return statement == null ? NotFoundResult("المورد غير موجود.") : Ok(ApiEnvelope<object>.Ok(statement));
            }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
        }

        [HttpGet, Route("{id:long}/ledger")]
        public async Task<IHttpActionResult> GetLedger(long id, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (_suppliers.GetById(id) == null) return NotFoundResult("المورد غير موجود.");
            return Ok(ApiEnvelope<object>.Ok(await _ledger.HistoryAsync(id, ct)));
        }

        [HttpGet, Route("{id:long}/invoices")]
        public async Task<IHttpActionResult> GetInvoices(long id, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (_suppliers.GetById(id) == null) return NotFoundResult("المورد غير موجود.");
            return Ok(ApiEnvelope<object>.Ok(_accounts.GetInvoices(id)));
        }

        [HttpPost, Route("{id:long}/invoices")]
        public async Task<IHttpActionResult> CreateInvoice(long id, SupplierInvoiceRequest request, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return CsrfResult();
            try
            {
                var actor = await _auth.GetCurrentUserAsync(ReadCookie(), ct).ConfigureAwait(false);
                if (actor == null) return UnauthorizedResult();
                var invoice = _accounts.AddInvoice(id, new SupplierInvoice { InvoiceNumber = request == null ? null : request.InvoiceNumber, InvoiceDate = request == null ? null : request.InvoiceDate, DueDate = request == null ? null : request.DueDate, Total = request == null ? 0 : request.Total, Notes = request == null ? null : request.Notes }, request?.OperationKey, actor.Id);
                return Content(HttpStatusCode.Created, ApiEnvelope<SupplierInvoice>.Ok(invoice));
            }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (KeyNotFoundException ex) { return NotFoundResult(ex.Message); }
            catch (InvalidOperationException ex) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("operation_conflict", ex.Message)); }
            catch (OverflowException) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", "المبلغ يتجاوز الحد المسموح.")); }
        }

        [HttpPost, Route("{id:long}/payments")]
        public async Task<IHttpActionResult> CreatePayment(long id, SupplierPaymentRequest request, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return CsrfResult();
            var actor=await _auth.GetCurrentUserAsync(ReadCookie(),ct).ConfigureAwait(false);
            if(actor==null)return UnauthorizedResult();
            if(actor.Role!=UserRoles.Administrator)
            {
                var access=await _users.GetEffectiveAccessAsync(actor,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);
                if(!access.ScreenIds.Contains("supplier-payment",StringComparer.OrdinalIgnoreCase))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية سداد حسابات الموردين."));
            }
            try
            {
                if (request == null) throw new ArgumentException("بيانات الدفع غير مكتملة.");
                var entry = await _ledger.AddTreasuryPaymentAsync(id, request.InvoiceId, request.PurchaseInvoiceId, request.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), request.PaymentDate, request.Method, request.Account, request.Notes, request.OperationKey, request.TreasuryAccountId,actor.Id,ct);
                return Content(HttpStatusCode.Created, ApiEnvelope<SupplierLedgerEntry>.Ok(entry));
            }
            catch (TreasuryException ex) { return Content(HttpStatusCode.Conflict,ApiEnvelope<object>.Fail("treasury_"+ex.Code,ex.Message)); }
            catch (System.Data.SQLite.SQLiteException ex) { System.Diagnostics.Trace.TraceError("Supplier payment database failure: SQLite code {0}",ex.ResultCode);return Content(HttpStatusCode.ServiceUnavailable,ApiEnvelope<object>.Fail("database_unavailable","تعذر إتمام السداد. أعد المحاولة بنفس البيانات للتحقق من الحفظ.")); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (KeyNotFoundException ex) { return NotFoundResult(ex.Message); }
            catch (InvalidOperationException ex) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("operation_conflict", ex.Message)); }
        }

        private IHttpActionResult NotFoundResult(string message) { return Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", message)); }
        private IHttpActionResult CsrfResult() { return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى.")); }
        private async Task<bool> IsAuthenticated(CancellationToken ct) { var token = ReadCookie(); return await _auth.GetCurrentUserAsync(token, ct) != null; }
        private async Task<bool> HasValidCsrf(CancellationToken ct) { return await _auth.ValidateCsrfAsync(ReadCookie(), ReadCsrf(), ct); }
        private IHttpActionResult UnauthorizedResult() { return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولاً.")); }
        private string ReadCookie()
        {
            IEnumerable<string> values;
            if (!Request.Headers.TryGetValues("Cookie", out values)) return null;
            foreach (var header in values) foreach (var part in header.Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == CookieName) return pair[1]; }
            return null;
        }
        private string ReadCsrf() { IEnumerable<string> values; return Request.Headers.TryGetValues("X-CSRF-Token", out values) ? values.FirstOrDefault() : null; }
    }
}
