using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Customers;
using AlKhayal.Core.Models;
using AlKhayal.Application.Users;
using AlKhayal.Application.Features;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/customers")]
    public sealed class CustomersController : ApiController
    {
        private const string CookieName = "alkhayal_session";
        private readonly CustomerService _customers;
        private readonly AuthService _auth;
        private readonly CustomerAccountService _accounts;
        private readonly InstallmentService _installments;
        private readonly UserManagementService _users;
        private readonly FeatureService _features;
        public CustomersController(CustomerService customers, AuthService auth, CustomerAccountService accounts, InstallmentService installments, UserManagementService users, FeatureService features) { _customers = customers; _auth = auth; _accounts = accounts; _installments=installments; _users=users; _features=features; }

        [HttpGet, Route("")]
        public async Task<IHttpActionResult> GetAll(CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            return Ok(ApiEnvelope<object>.Ok(_customers.GetAll()));
        }
        [HttpGet, Route("search")]
        public async Task<IHttpActionResult> Search(string query="",CancellationToken ct=default(CancellationToken))
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            return Ok(ApiEnvelope<object>.Ok(await _customers.SearchAsync(query,ct).ConfigureAwait(false)));
        }

        [HttpPost, Route("")]
        public async Task<IHttpActionResult> Create(Customer customer, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            try { return Content(HttpStatusCode.Created, ApiEnvelope<Customer>.Ok(_customers.Create(customer))); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (System.Data.SQLite.SQLiteException ex) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("customer_conflict", (System.Data.SQLite.SQLiteErrorCode)ex.ErrorCode == System.Data.SQLite.SQLiteErrorCode.Constraint ? "كود العميل مستخدم بالفعل أو توجد بيانات غير صالحة." : "تعذر حفظ العميل في قاعدة البيانات.")); }
        }

        [HttpPut, Route("{id:long}")]
        public async Task<IHttpActionResult> Update(long id, Customer customer, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            try { var updated = _customers.Update(id, customer); return updated == null ? (IHttpActionResult)Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", "العميل غير موجود.")) : Ok(ApiEnvelope<Customer>.Ok(updated)); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (System.Data.SQLite.SQLiteException) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("customer_conflict", "كود العميل مستخدم بالفعل أو توجد بيانات غير صالحة.")); }
        }

        [HttpDelete, Route("{id:long}")]
        public async Task<IHttpActionResult> Delete(long id, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            return _customers.Delete(id) ? (IHttpActionResult)Ok(ApiEnvelope<object>.Ok(new { deleted = true })) : Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", "العميل غير موجود."));
        }


        [HttpGet, Route("{id:long}/statement")]
        public async Task<IHttpActionResult> GetStatement(long id, string from = null, string to = null, CancellationToken ct = default(CancellationToken))
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if(!await CanUseStatement(ct))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية كشف حساب العميل."));
            try
            {
                var statement = _accounts.GetStatement(id, from, to);
                return statement == null ? (IHttpActionResult)Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", "العميل غير موجود.")) : Ok(ApiEnvelope<object>.Ok(statement));
            }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
        }

        [HttpPost, Route("{id:long}/payments")]
        public async Task<IHttpActionResult> CreatePayment(long id, CustomerPaymentRequest request, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            try
            {
                var payment = _accounts.AddPayment(id, new CustomerPayment { Amount = request == null ? 0 : request.Amount, PaymentDate = request == null ? null : request.PaymentDate, Method = request == null ? null : request.Method, Account = request == null ? null : request.Account, Notes = request == null ? null : request.Notes });
                return Content(HttpStatusCode.Created, ApiEnvelope<CustomerPayment>.Ok(payment));
            }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (System.Collections.Generic.KeyNotFoundException ex) { return Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", ex.Message)); }
        }

        [HttpPost, Route("{id:long}/transactions")]
        public async Task<IHttpActionResult> PostTransaction(long id, CustomerTransactionRequest request, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            if(!await CanUseStatement(ct))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية كشف حساب العميل."));
            try { return Content(HttpStatusCode.Created, ApiEnvelope<CustomerStatementTransaction>.Ok(_accounts.PostTransaction(id,request == null ? null : request.Type,request == null ? 0 : request.Amount,request == null ? null : request.OccurredOn,request == null ? null : request.Notes,request == null ? null : request.OperationKey))); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            // QA-101: a reused operation key with a changed payload is a conflict, not a replay.
            catch (CustomerOperationKeyConflictException ex) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("operation_key_conflict", ex.Message)); }
            catch (OverflowException) { return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation","المبلغ يتجاوز الحد المسموح لمديونية العميل.")); }
            catch (System.Data.SQLite.SQLiteException ex) { System.Diagnostics.Trace.TraceError("Customer account transaction failed: SQLite code {0}",ex.ResultCode);return Content(HttpStatusCode.ServiceUnavailable,ApiEnvelope<object>.Fail("database_unavailable","تعذر تأكيد حفظ العملية. أعد المحاولة بنفس البيانات للتحقق من الحفظ.")); }
            catch (System.Collections.Generic.KeyNotFoundException ex) { return Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", ex.Message)); }
        }

        [HttpGet, Route("{id:long}/installments")]
        public async Task<IHttpActionResult> GetInstallments(long id, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await CanUseScreen("installment-payment", ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("screen_forbidden", "لا تملك صلاحية سداد الأقساط."));
            try { return Ok(ApiEnvelope<object>.Ok(_installments.GetByCustomer(id))); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
        }

        [HttpPost, Route("{id:long}/installments")]
        public async Task<IHttpActionResult> CreateInstallment(long id, InstallmentSaleRequest request, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            if (!await CanUseScreen("installment-system", ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("screen_forbidden", "لا تملك صلاحية البيع بالتقسيط."));
            try
            {
                var result = _installments.Create(id, request == null ? 0 : request.ProductId, request == null ? 0 : request.ProductPrice, request == null ? 0 : request.DownPayment, request == null ? 0 : request.InterestRate, request == null ? 0 : request.InstallmentCount, request == null ? 0 : request.IntervalMonths, request == null ? null : request.FirstDueOn, request == null ? null : request.OperationKey);
                return Content(HttpStatusCode.Created, ApiEnvelope<CustomerInstallmentContract>.Ok(result));
            }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (CustomerOperationKeyConflictException ex) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("operation_key_conflict", ex.Message)); }
            catch (OverflowException) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", "قيمة العقد تتجاوز الحد المسموح.")); }
            catch (System.Collections.Generic.KeyNotFoundException ex) { return Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", ex.Message)); }
            catch (System.Data.SQLite.SQLiteException ex) { System.Diagnostics.Trace.TraceError("Installment contract failed: SQLite code {0}", ex.ResultCode); return Content(HttpStatusCode.ServiceUnavailable, ApiEnvelope<object>.Fail("database_unavailable", "تعذر حفظ عقد التقسيط. أعد المحاولة بنفس المفتاح.")); }
        }

        [HttpPost, Route("{id:long}/installments/{contractId:long}/payments/{number:int}")]
        public async Task<IHttpActionResult> PayInstallment(long id, long contractId, int number, InstallmentPaymentRequest request, CancellationToken ct)
        {
            if (!await IsAuthenticated(ct)) return UnauthorizedResult();
            if (!await HasValidCsrf(ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            if (!await CanUseScreen("installment-payment", ct)) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("screen_forbidden", "لا تملك صلاحية سداد الأقساط."));
            try { return Ok(ApiEnvelope<CustomerInstallmentContract>.Ok(_installments.Pay(id, contractId, number, request == null ? null : request.PaidOn, request == null ? null : request.OperationKey))); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
            catch (CustomerOperationKeyConflictException ex) { return Content(HttpStatusCode.Conflict, ApiEnvelope<object>.Fail("operation_key_conflict", ex.Message)); }
            catch (OverflowException) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", "قيمة السداد تتجاوز الحد المسموح.")); }
            catch (System.Collections.Generic.KeyNotFoundException ex) { return Content(HttpStatusCode.NotFound, ApiEnvelope<object>.Fail("not_found", ex.Message)); }
            catch (System.Data.SQLite.SQLiteException ex) { System.Diagnostics.Trace.TraceError("Installment payment failed: SQLite code {0}", ex.ResultCode); return Content(HttpStatusCode.ServiceUnavailable, ApiEnvelope<object>.Fail("database_unavailable", "تعذر تأكيد سداد القسط. أعد المحاولة بنفس المفتاح.")); }
        }

        private async Task<bool> CanUseStatement(CancellationToken ct)
        {
            return await CanUseScreen("customer-statement", ct).ConfigureAwait(false);
        }
        private async Task<bool> CanUseScreen(string screenId, CancellationToken ct)
        {
            var user=await _auth.GetCurrentUserAsync(ReadCookie(),ct).ConfigureAwait(false);
            if(user==null)return false;if(user.Role==UserRoles.Administrator)return true;
            var access=await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);
            return access.ScreenIds.Contains(screenId,StringComparer.OrdinalIgnoreCase);
        }
        private async Task<bool> IsAuthenticated(CancellationToken ct) { var token = ReadCookie(); return await _auth.GetCurrentUserAsync(token, ct) != null; }
        private async Task<bool> HasValidCsrf(CancellationToken ct) { return await _auth.ValidateCsrfAsync(ReadCookie(), ReadCsrf(), ct); }
        private IHttpActionResult UnauthorizedResult() { return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولاً.")); }
        private string ReadCookie()
        {
            System.Collections.Generic.IEnumerable<string> values;
            if (!Request.Headers.TryGetValues("Cookie", out values)) return null;
            foreach (var header in values) foreach (var part in header.Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == CookieName) return pair[1]; }
            return null;
        }
        private string ReadCsrf() { System.Collections.Generic.IEnumerable<string> values; return Request.Headers.TryGetValues("X-CSRF-Token", out values) ? values.FirstOrDefault() : null; }
    }
}
