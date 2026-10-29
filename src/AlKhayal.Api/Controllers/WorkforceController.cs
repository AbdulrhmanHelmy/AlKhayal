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
using AlKhayal.Application.Users;
using AlKhayal.Application.Workforce;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/workforce")]
    public sealed class WorkforceController : ApiController
    {
        private readonly WorkforceService _service;
        private readonly AuthService _auth;
        private readonly UserManagementService _users;
        private readonly FeatureService _features;
        public WorkforceController(WorkforceService service, AuthService auth, UserManagementService users, FeatureService features)
        { _service = service; _auth = auth; _users = users; _features = features; }

        [HttpGet, Route("employees")]
        public Task<IHttpActionResult> Employees([FromUri] WorkforceQuery query, CancellationToken ct) => Run("employee-management", true, false, async u => Ok(ApiEnvelope<WorkforcePage<Employee>>.Ok(await _service.EmployeesAsync(query, ct).ConfigureAwait(false))), ct);
        [HttpGet, Route("employees/{id:long}")]
        public Task<IHttpActionResult> Employee(long id, CancellationToken ct) => Run("employee-management", true, false, async u => Ok(ApiEnvelope<Employee>.Ok(await _service.EmployeeAsync(id, ct).ConfigureAwait(false))), ct);
        [HttpPost, Route("employees/save")]
        public Task<IHttpActionResult> Save(WorkforceCommand command, CancellationToken ct) => Execute("employee-save", "employee-management", true, command, ct);
        [HttpGet, Route("attendance")]
        public Task<IHttpActionResult> Attendance([FromUri] WorkforceQuery query, CancellationToken ct) => Run("employee-attendance", true, false, async u => Ok(ApiEnvelope<WorkforcePage<AttendanceRecord>>.Ok(await _service.AttendanceAsync(query, ct).ConfigureAwait(false))), ct);
        [HttpGet, Route("attendance/{id:long}/audit")]
        public Task<IHttpActionResult> AttendanceAudit(long id, CancellationToken ct, int page = 1) => Run("employee-attendance", true, false, async u => Ok(ApiEnvelope<IReadOnlyList<AttendanceAuditRecord>>.Ok(await _service.AttendanceAuditAsync(id, page, ct).ConfigureAwait(false))), ct);
        [HttpPost, Route("attendance/in")]
        public Task<IHttpActionResult> CheckIn(WorkforceCommand command, CancellationToken ct) => Execute("attendance-in", "employee-attendance", true, command, ct);
        [HttpPost, Route("attendance/out")]
        public Task<IHttpActionResult> CheckOut(WorkforceCommand command, CancellationToken ct) => Execute("attendance-out", "employee-attendance", true, command, ct);
        [HttpPost, Route("attendance/correct")]
        public Task<IHttpActionResult> Correct(WorkforceCommand command, CancellationToken ct) => Execute("attendance-correct", "employee-attendance", true, command, ct);
        [HttpGet, Route("shifts")]
        public Task<IHttpActionResult> Shifts([FromUri] WorkforceQuery query, CancellationToken ct) => Run("advanced-shifts", false, false, async u => Ok(ApiEnvelope<WorkforcePage<CashierShift>>.Ok(await _service.ShiftsAsync(query, Owner(u), ct).ConfigureAwait(false))), ct);
        [HttpGet, Route("shifts/current")]
        public Task<IHttpActionResult> Current(CancellationToken ct) => Run("advanced-shifts", false, false, async u => Ok(ApiEnvelope<CashierShift>.Ok((await _service.ShiftsAsync(new WorkforceQuery { Status = "open", CashierId = u.Id }, u.Id, ct).ConfigureAwait(false)).Items.FirstOrDefault())), ct);
        [HttpGet, Route("shifts/{id:long}")]
        public Task<IHttpActionResult> Shift(long id, CancellationToken ct) => Run("advanced-shifts", false, false, async u => Ok(ApiEnvelope<CashierShift>.Ok(await _service.ShiftAsync(id, Owner(u), ct).ConfigureAwait(false))), ct);
        [HttpGet, Route("shifts/{id:long}/events")]
        public Task<IHttpActionResult> Events(long id, CancellationToken ct, int page = 1) => Run("advanced-shifts", false, false, async u => Ok(ApiEnvelope<IReadOnlyList<ShiftEvent>>.Ok(await _service.EventsAsync(id, page, Owner(u), ct).ConfigureAwait(false))), ct);
        [HttpPost, Route("shifts/open")]
        public Task<IHttpActionResult> Open(WorkforceCommand command, CancellationToken ct) => Execute("shift-open", "advanced-shifts", false, command, ct);
        [HttpPost, Route("shifts/close")]
        public Task<IHttpActionResult> Close(WorkforceCommand command, CancellationToken ct) => Execute("shift-close", "advanced-shifts", false, command, ct);
        [HttpPost, Route("shifts/adjust")]
        public Task<IHttpActionResult> Adjust(WorkforceCommand command, CancellationToken ct) => Execute("shift-adjust", "advanced-shifts", false, command, ct);
        private Task<IHttpActionResult> Execute(string action, string feature, bool admin, WorkforceCommand command, CancellationToken ct) => Run(feature, admin, true, async u => Ok(ApiEnvelope<object>.Ok(new { id = await _service.ExecuteAsync(action, command, u.Id, u.Role == UserRoles.Administrator, ct).ConfigureAwait(false) })), ct);
        private static long? Owner(UserIdentity u) => u.Role == UserRoles.Administrator ? (long?)null : u.Id;
        private async Task<IHttpActionResult> Run(string feature, bool admin, bool mutation, Func<UserIdentity, Task<IHttpActionResult>> action, CancellationToken ct)
        {
            var token = Token();
            var user = await _auth.GetCurrentUserAsync(token, ct).ConfigureAwait(false);
            if (user == null) return Fail(HttpStatusCode.Unauthorized, "unauthorized", "يرجى تسجيل الدخول أولًا.");
            if (admin && user.Role != UserRoles.Administrator) return Fail(HttpStatusCode.Forbidden, "forbidden", "هذه العملية متاحة لمسؤول النظام فقط.");
            var features = await _features.GetEffectiveAsync(ct).ConfigureAwait(false);
            if (!features.Modules.Any(f => f.Key == feature && f.IsEnabled)) return Fail(HttpStatusCode.Forbidden, "module_disabled", "الوحدة معطلة. راجع إعدادات الوحدات.");
            if (user.Role != UserRoles.Administrator)
            {
                var access = await _users.GetEffectiveAccessAsync(user, features, ct).ConfigureAwait(false);
                if (!access.ScreenIds.Any(r => new[] { "pos", "shifts-list", "shift-add", "shift-reports", "shift-details" }.Contains(r))) return Fail(HttpStatusCode.Forbidden, "screen_forbidden", "لا تملك صلاحية إدارة الوردية.");
            }
            if (mutation && !await _auth.ValidateCsrfAsync(token, Header("X-CSRF-Token"), ct).ConfigureAwait(false)) return Fail(HttpStatusCode.Forbidden, "invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مجددًا.");
            if (!ModelState.IsValid) return Fail(HttpStatusCode.BadRequest, "validation", "بيانات الطلب غير صالحة.");
            try { return await action(user).ConfigureAwait(false); }
            catch (WorkforceException ex) { return Fail(ex.Code == "validation" ? HttpStatusCode.BadRequest : ex.Code == "forbidden" ? HttpStatusCode.Forbidden : ex.Code == "not_found" ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, ex.Code, ex.Message); }
            catch (OverflowException) { return Fail(HttpStatusCode.Conflict, "numeric_overflow", "المبلغ يتجاوز الحد المسموح."); }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                System.Diagnostics.Trace.TraceError("Workforce write/read failed: SQLite code {0}", ex.ResultCode);
                return Fail(HttpStatusCode.ServiceUnavailable, "database_unavailable", "تعذر تأكيد النتيجة. أعد المحاولة بنفس البيانات للتحقق من الحفظ.");
            }
        }
        private IHttpActionResult Fail(HttpStatusCode status, string code, string message) => Content(status, ApiEnvelope<object>.Fail(code, message));
        private string Header(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
        private string Token()
        {
            foreach (var part in (Header("Cookie") ?? "").Split(';'))
            { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == "alkhayal_session") return pair[1]; }
            return null;
        }
    }
}
