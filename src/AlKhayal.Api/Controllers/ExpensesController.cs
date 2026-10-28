using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Expenses;
using AlKhayal.Application.Features;
using AlKhayal.Application.Treasury;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/expenses")]
    public sealed class ExpensesController : ApiController
    {
        private readonly ExpenseService _expenses;
        private readonly AuthService _auth;
        private readonly UserManagementService _users;
        private readonly FeatureService _features;
        public ExpensesController(ExpenseService expenses, AuthService auth, UserManagementService users, FeatureService features)
        { _expenses = expenses; _auth = auth; _users = users; _features = features; }
        [HttpGet, Route("categories")]
        public Task<IHttpActionResult> Categories(CancellationToken ct) => Run(new[] { "expenses-list", "expense-add" }, false,
            async user => Ok(ApiEnvelope<IReadOnlyList<ExpenseCategory>>.Ok(await _expenses.CategoriesAsync(ct).ConfigureAwait(false))), ct);
        [HttpPost, Route("categories")]
        public Task<IHttpActionResult> AddCategory(ExpenseCategoryCommand command, CancellationToken ct) => Run(new[] { "expenses-list" }, true,
            async user => Content(HttpStatusCode.Created, ApiEnvelope<ExpenseCategory>.Ok(await _expenses.AddCategoryAsync(command, user.Id, ct).ConfigureAwait(false))), ct);
        [HttpGet, Route("vouchers")]
        public Task<IHttpActionResult> Vouchers(CancellationToken ct) => Run(new[] { "expense-add" }, false,
            async user => Ok(ApiEnvelope<IReadOnlyList<ExpenseVoucher>>.Ok(await _expenses.VouchersAsync(ct).ConfigureAwait(false))), ct);
        [HttpPost, Route("vouchers")]
        public Task<IHttpActionResult> Post(ExpenseCommand command, CancellationToken ct) => Run(new[] { "expense-add" }, true,
            async user => Content(HttpStatusCode.Created, ApiEnvelope<ExpenseVoucher>.Ok(await _expenses.PostAsync(command, user.Id, ct).ConfigureAwait(false))), ct);
        private async Task<IHttpActionResult> Run(string[] routes, bool mutation, Func<UserIdentity, Task<IHttpActionResult>> action, CancellationToken ct)
        {
            var token = ReadToken();
            var user = await _auth.GetCurrentUserAsync(token, ct).ConfigureAwait(false);
            if (user == null) return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولًا."));
            if (user.Role != UserRoles.Administrator)
            {
                var access = await _users.GetEffectiveAccessAsync(user, await _features.GetEffectiveAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                if (!routes.Any(route => access.ScreenIds.Contains(route, StringComparer.OrdinalIgnoreCase)))
                    return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("screen_forbidden", "لا تملك صلاحية هذه العملية في المصروفات."));
            }
            if (mutation && !await _auth.ValidateCsrfAsync(token, Header("X-CSRF-Token"), ct).ConfigureAwait(false))
                return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            if (!ModelState.IsValid) return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", "بيانات الطلب غير صالحة."));
            try { return await action(user).ConfigureAwait(false); }
            catch (ExpenseException ex) { return Failure(ex.Code, ex.Message); }
            catch (TreasuryException ex) { return Failure(ex.Code, ex.Message); }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                System.Diagnostics.Trace.TraceError("Expense operation failed: SQLite code {0}.", ex.ResultCode);
                return Content(HttpStatusCode.ServiceUnavailable, ApiEnvelope<object>.Fail("database_unavailable", "تعذر تأكيد الحفظ. أعد المحاولة بنفس بيانات السند للتحقق من النتيجة."));
            }
        }
        private IHttpActionResult Failure(string code, string message) => Content(code == "validation" ? HttpStatusCode.BadRequest : code == "not_found" ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, ApiEnvelope<object>.Fail(code, message));
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
