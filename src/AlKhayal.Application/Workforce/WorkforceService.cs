using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;

namespace AlKhayal.Application.Workforce
{
    public sealed class WorkforceService
    {
        private readonly IWorkforceStore _store;
        public WorkforceService(IWorkforceStore store) { _store = store; }
        public Task<WorkforcePage<Employee>> EmployeesAsync(WorkforceQuery q, CancellationToken ct) => _store.EmployeesAsync(Query(q), ct);
        public Task<Employee> EmployeeAsync(long id, CancellationToken ct) => _store.EmployeeAsync(id, ct);
        public Task<System.Collections.Generic.IReadOnlyList<AttendanceAuditRecord>> AttendanceAuditAsync(long id, int page, CancellationToken ct) => _store.AttendanceAuditAsync(id, Math.Max(1, Math.Min(1000000, page)), ct);
        public Task<WorkforcePage<AttendanceRecord>> AttendanceAsync(WorkforceQuery q, CancellationToken ct) => _store.AttendanceAsync(Query(q), ct);
        public Task<WorkforcePage<CashierShift>> ShiftsAsync(WorkforceQuery q, long? owner, CancellationToken ct) => _store.ShiftsAsync(Query(q), owner, ct);
        public Task<CashierShift> ShiftAsync(long id, long? owner, CancellationToken ct) => _store.ShiftAsync(id, owner, ct);
        public Task<System.Collections.Generic.IReadOnlyList<ShiftEvent>> EventsAsync(long id, int page, long? owner, CancellationToken ct) => _store.EventsAsync(id, Math.Max(1, Math.Min(1000000, page)), owner, ct);
        public Task<long> ExecuteAsync(string action, WorkforceCommand x, long actor, bool admin, CancellationToken ct)
        {
            if (x == null) throw Invalid();
            if ((action.StartsWith("employee", StringComparison.Ordinal) || action.StartsWith("attendance", StringComparison.Ordinal)) && !admin)
                throw new WorkforceException("forbidden", "هذه العملية متاحة لمسؤول النظام فقط.");
            if (action != "employee-save" && action != "attendance-in" && action != "attendance-out" && action != "attendance-correct" && action != "shift-open" && action != "shift-close" && action != "shift-adjust") throw Invalid();
            x.OperationKey = Text(x.OperationKey, 100, true);
            x.Notes = Text(x.Notes, 1000);
            x.Reason = Text(x.Reason, 500, action == "attendance-correct" || action == "shift-adjust");
            long? amount = null;
            if (action == "employee-save")
            {
                x.Name = Text(x.Name, 160, true); x.Phone = Text(x.Phone, 60); x.JobTitle = Text(x.JobTitle, 120);
                x.Address = Text(x.Address, 500); x.CompensationNotes = Text(x.CompensationNotes, 1000);
                x.SalaryFrequency = Text(x.SalaryFrequency, 20);
                if (x.SalaryFrequency != "" && x.SalaryFrequency != "monthly" && x.SalaryFrequency != "weekly" && x.SalaryFrequency != "other") throw Invalid();
                if (x.Payday.HasValue && (x.Payday < 1 || x.Payday > 31) || x.LinkedUserId <= 0 || x.Id < 0 || x.Id > 0 && x.Version < 1) throw Invalid();
                if (!string.IsNullOrWhiteSpace(x.HireDate))
                {
                    DateTime date;
                    if (!DateTime.TryParseExact(x.HireDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) throw Invalid();
                }
                else x.HireDate = null;
                if (!string.IsNullOrWhiteSpace(x.Salary)) amount = ParseMoney(x.Salary);
            }
            if (action == "attendance-in" && x.EmployeeId <= 0 || (action == "attendance-out" || action == "attendance-correct" || action == "shift-close" || action == "shift-adjust") && x.Id <= 0) throw Invalid();
            if (action == "attendance-correct")
            {
                x.CheckInUtc = Timestamp(x.CheckInUtc, true);
                x.CheckOutUtc = Timestamp(x.CheckOutUtc, false);
                if (x.CheckOutUtc != null && string.CompareOrdinal(x.CheckOutUtc, x.CheckInUtc) <= 0 || x.Version < 1) throw Invalid();
            }
            if (action == "shift-open" || action == "shift-close" || action == "shift-adjust") amount = ParseMoney(x.Amount);
            if (action == "shift-adjust" && (amount <= 0 || x.Direction != "in" && x.Direction != "out")) throw Invalid();
            if (action == "shift-open")
            {
                if (!admin && x.CashierId.HasValue && x.CashierId != actor) throw new WorkforceException("forbidden", "يمكنك فتح ورديتك فقط.");
                x.CashierId = x.CashierId ?? actor;
            }
            var canonical = new StringBuilder();
            foreach (var field in new object[] { action, actor, x.Id, x.Version, x.Name, x.Phone, x.JobTitle, x.HireDate, x.Address, x.Notes, x.LinkedUserId, x.IsActive, x.AttendanceEnabled, amount, x.SalaryFrequency, x.Payday, x.CompensationNotes, x.EmployeeId, x.CashierId, x.Direction, x.Reason, x.CheckInUtc, x.CheckOutUtc })
            { var s = Convert.ToString(field, CultureInfo.InvariantCulture) ?? ""; canonical.Append(s.Length).Append(':').Append(s); }
            string hash;
            using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
            return _store.ExecuteAsync(action, x, amount, hash, actor, admin, ct);
        }
        private static long ParseMoney(string value) { long n; if (!MoneyPolicy.TryParseUserAmount(value, false, out n)) throw new WorkforceException("validation", "أدخل مبلغًا غير سالب بدقة خانتين عشريتين بحد أقصى."); return n; }
        private static WorkforceQuery Query(WorkforceQuery q)
        {
            q = q ?? new WorkforceQuery(); q.Page = Math.Max(1, Math.Min(1000000, q.Page)); q.PageSize = Math.Max(1, Math.Min(100, q.PageSize));
            q.Search = Text(q.Search, 160); q.Status = Text(q.Status, 20);
            if (q.Status != "" && q.Status != "active" && q.Status != "inactive" && q.Status != "open" && q.Status != "closed") throw Invalid();
            q.FromUtc = Timestamp(q.FromUtc, false, true); q.ToUtc = Timestamp(q.ToUtc, false, true);
            if (q.FromUtc != null && q.ToUtc != null && string.CompareOrdinal(q.FromUtc, q.ToUtc) >= 0) throw Invalid();
            return q;
        }
        private static string Timestamp(string value, bool required, bool allowFuture = false)
        {
            if (string.IsNullOrWhiteSpace(value)) { if (required) throw Invalid(); return null; }
            DateTimeOffset timestamp;
            if (!(value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(value, @"[+-]\d{2}:\d{2}$")) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp) || !allowFuture && timestamp > DateTimeOffset.UtcNow.AddMinutes(1)) throw Invalid();
            return timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        }
        private static string Text(string s, int max, bool required = false) { s = (s ?? "").Trim(); if (s.Length > max || required && s.Length == 0) throw Invalid(); return s; }
        private static WorkforceException Invalid() => new WorkforceException("validation", "راجع الحقول المطلوبة والمبالغ والتواريخ المدخلة.");
    }
}

