using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Workforce;
using Dapper;

namespace AlKhayal.Data.Workforce
{
    public sealed class SqliteWorkforceStore : IWorkforceStore
    {
        private readonly string _connectionString;
        public SqliteWorkforceStore(string connectionString) { _connectionString = connectionString; }
        private static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        private static CommandDefinition Cmd(string sql, object p, SQLiteTransaction tx, CancellationToken ct) => new CommandDefinition(sql, p, tx, cancellationToken: ct);
        private async Task<SQLiteConnection> OpenAsync(CancellationToken ct)
        {
            var c = new SQLiteConnection(_connectionString);
            try { await c.OpenAsync(ct).ConfigureAwait(false); await c.ExecuteAsync(Cmd("PRAGMA foreign_keys=ON;", null, null, ct)).ConfigureAwait(false); return c; }
            catch { c.Dispose(); throw; }
        }
        private static WorkforceException Conflict(string message) => new WorkforceException("conflict", message);
        private static WorkforceException Missing() => new WorkforceException("not_found", "السجل غير موجود أو غير متاح لك.");
        public async Task<long> ExecuteAsync(string action, WorkforceCommand x, long? amount, string hash, long actorId, bool administrator, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false))
            using (var tx = c.BeginTransaction())
            {
                var prior = await c.QuerySingleOrDefaultAsync<Operation>(Cmd("SELECT RequestHash,EntityId FROM WorkforceOperations WHERE OperationKey=@OperationKey", new { x.OperationKey }, tx, ct)).ConfigureAwait(false);
                if (prior != null) { if (prior.RequestHash != hash) throw Conflict("رقم العملية مستخدم ببيانات أخرى."); tx.Commit(); return prior.EntityId; }
                var feature = action == "shift-open" ? "advanced-shifts" : action == "attendance-in" ? "employee-attendance" : action == "employee-save" ? "employee-management" : null;
                if (feature != null && await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM ModuleConfiguration WHERE ModuleKey=@feature AND IsEnabled=0", new { feature }, tx, ct)).ConfigureAwait(false) != 0)
                    throw Conflict("تم تعطيل الوحدة أثناء العمل. حدّث الصفحة.");
                var now = Now(); long id;
                var detail = x.Reason ?? "";
                if (action == "employee-save")
                {
                    if (x.LinkedUserId.HasValue)
                    {
                        if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Users WHERE Id=@LinkedUserId", new { x.LinkedUserId }, tx, ct)).ConfigureAwait(false) != 1) throw Missing();
                        if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Employees WHERE LinkedUserId=@LinkedUserId AND Id<>@Id", new { x.LinkedUserId, x.Id }, tx, ct)).ConfigureAwait(false) != 0) throw Conflict("حساب الدخول مرتبط بموظف آخر.");
                    }
                    if (x.Id > 0)
                    {
                        var before = await c.QuerySingleOrDefaultAsync<Employee>(Cmd("SELECT * FROM Employees WHERE Id=@Id", new { x.Id }, tx, ct)).ConfigureAwait(false);
                        if (before == null) throw Missing();
                        if (before.Version != x.Version) throw Conflict("تغير سجل الموظف. حدّث البيانات قبل الحفظ.");
                        if ((!x.IsActive || !x.AttendanceEnabled) && await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM EmployeeAttendance WHERE EmployeeId=@Id AND CheckOutUtc IS NULL", new { x.Id }, tx, ct)).ConfigureAwait(false) > 0) throw Conflict("سجّل الانصراف قبل تعطيل الموظف أو الحضور.");
                        detail = "before=" + EmployeeAudit(before);
                    }
                    var p = new { x.Id, x.Version, x.Name, x.Phone, x.JobTitle, x.HireDate, x.Address, x.Notes, x.LinkedUserId, x.IsActive, x.AttendanceEnabled, SalaryScaled = amount, x.SalaryFrequency, x.Payday, x.CompensationNotes, now };
                    if (x.Id == 0)
                        id = await c.ExecuteScalarAsync<long>(Cmd(@"INSERT INTO Employees(Name,Phone,JobTitle,HireDate,Address,Notes,LinkedUserId,IsActive,AttendanceEnabled,SalaryScaled,SalaryFrequency,Payday,CompensationNotes,CreatedUtc,UpdatedUtc)
                            VALUES(@Name,@Phone,@JobTitle,@HireDate,@Address,@Notes,@LinkedUserId,@IsActive,@AttendanceEnabled,@SalaryScaled,@SalaryFrequency,@Payday,@CompensationNotes,@now,@now); SELECT last_insert_rowid();", p, tx, ct)).ConfigureAwait(false);
                    else
                    {
                        await c.ExecuteAsync(Cmd(@"UPDATE Employees SET Name=@Name,Phone=@Phone,JobTitle=@JobTitle,HireDate=@HireDate,Address=@Address,Notes=@Notes,LinkedUserId=@LinkedUserId,IsActive=@IsActive,AttendanceEnabled=@AttendanceEnabled,SalaryScaled=@SalaryScaled,SalaryFrequency=@SalaryFrequency,Payday=@Payday,CompensationNotes=@CompensationNotes,UpdatedUtc=@now,Version=Version+1 WHERE Id=@Id AND Version=@Version", p, tx, ct)).ConfigureAwait(false);
                        id = x.Id;
                    }
                    detail += " after=" + EmployeeAudit(await c.QuerySingleAsync<Employee>(Cmd("SELECT * FROM Employees WHERE Id=@id", new { id }, tx, ct)).ConfigureAwait(false));
                }
                else if (action.StartsWith("attendance-", StringComparison.Ordinal))
                {
                    if (action == "attendance-in")
                    {
                        if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Employees WHERE Id=@EmployeeId AND IsActive=1 AND AttendanceEnabled=1", new { x.EmployeeId }, tx, ct)).ConfigureAwait(false) != 1) throw Conflict("الحضور غير مفعل لهذا الموظف أو أن الموظف غير نشط.");
                        await CheckOverlap(c, tx, x.EmployeeId, 0, now, null, ct).ConfigureAwait(false);
                        id = await c.ExecuteScalarAsync<long>(Cmd("INSERT INTO EmployeeAttendance(EmployeeId,CheckInUtc) VALUES(@EmployeeId,@now); SELECT last_insert_rowid();", new { x.EmployeeId, now }, tx, ct)).ConfigureAwait(false);
                        detail = now;
                    }
                    else
                    {
                        var before = await c.QuerySingleOrDefaultAsync<AttendanceRecord>(Cmd("SELECT * FROM EmployeeAttendance WHERE Id=@Id", new { x.Id }, tx, ct)).ConfigureAwait(false);
                        if (before == null) throw Missing();
                        if (action == "attendance-out" && before.CheckOutUtc != null) throw Conflict("تم تسجيل الانصراف بالفعل.");
                        if (action == "attendance-correct" && before.Version != x.Version) throw Conflict("تغير سجل الحضور. حدّث البيانات.");
                        var start = action == "attendance-out" ? before.CheckInUtc : x.CheckInUtc;
                        var end = action == "attendance-out" ? now : x.CheckOutUtc;
                        if (end != null && string.CompareOrdinal(end, start) <= 0) throw Conflict("الانصراف يجب أن يكون بعد الحضور.");
                        await CheckOverlap(c, tx, before.EmployeeId, before.Id, start, end, ct).ConfigureAwait(false);
                        await c.ExecuteAsync(Cmd("UPDATE EmployeeAttendance SET CheckInUtc=@start,CheckOutUtc=@end,Version=Version+1 WHERE Id=@Id", new { start, end, x.Id }, tx, ct)).ConfigureAwait(false);
                        id = x.Id;
                        detail += " before=" + before.CheckInUtc + "/" + before.CheckOutUtc + " after=" + start + "/" + end;
                    }
                }
                else if (action == "shift-open")
                {
                    if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Users WHERE Id=@CashierId AND IsActive=1 AND Role IN ('cashier','administrator')", new { x.CashierId }, tx, ct)).ConfigureAwait(false) != 1) throw Missing();
                    // Standalone has exactly one supported register. Do not trust a browser-provided device ID.
                    if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM CashierShifts WHERE ClosedUtc IS NULL AND (CashierId=@CashierId OR RegisterId='standalone')", new { x.CashierId }, tx, ct)).ConfigureAwait(false) > 0) throw Conflict("توجد وردية مفتوحة للكاشير أو لهذا الجهاز. أغلقها أولًا.");
                    id = await c.ExecuteScalarAsync<long>(Cmd("INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled,OpeningNotes) VALUES(@CashierId,'standalone',@now,@actorId,@amount,@Notes); SELECT last_insert_rowid();", new { x.CashierId, now, actorId, amount, x.Notes }, tx, ct)).ConfigureAwait(false);
                    detail = "opening=" + amount + ";cashier=" + x.CashierId;
                }
                else
                {
                    var shift = await c.QuerySingleOrDefaultAsync<CashierShift>(Cmd(ShiftSql + " WHERE s.Id=@Id", new { x.Id }, tx, ct)).ConfigureAwait(false);
                    if (shift == null || !administrator && shift.CashierId != actorId) throw Missing();
                    if (shift.ClosedUtc != null) throw Conflict("الوردية مغلقة ولا تقبل معاملات جديدة.");
                    if (action == "shift-close")
                    {
                        await c.ExecuteAsync(Cmd("UPDATE CashierShifts SET ClosedUtc=@now,ClosedBy=@actorId,CountedScaled=@amount,ClosingExpectedScaled=@expected,ClosingNotes=@Notes WHERE Id=@Id AND ClosedUtc IS NULL", new { now, actorId, amount, expected = shift.ExpectedScaled, x.Notes, x.Id }, tx, ct)).ConfigureAwait(false);
                        detail = "expected=" + shift.ExpectedScaled + ";counted=" + amount;
                    }
                    else
                    {
                        var expected = x.Direction == "in" ? checked(shift.ExpectedScaled + amount.Value) : checked(shift.ExpectedScaled - amount.Value);
                        if (expected < 0) throw Conflict("المبلغ يتجاوز النقد المتوقع في الوردية.");
                        await c.ExecuteAsync(Cmd("INSERT INTO CashierShiftEvents(ShiftId,Kind,AmountScaled,ActorUserId,Reason,CreatedUtc) VALUES(@Id,@Direction,@amount,@actorId,@Reason,@now)", new { x.Id, x.Direction, amount, actorId, x.Reason, now }, tx, ct)).ConfigureAwait(false);
                        detail += ";direction=" + x.Direction + ";amount=" + amount;
                    }
                    id = x.Id;
                }
                await c.ExecuteAsync(Cmd("INSERT INTO WorkforceOperations(OperationKey,Action,RequestHash,EntityId,ActorUserId,CreatedUtc) VALUES(@OperationKey,@action,@hash,@id,@actorId,@now); INSERT INTO WorkforceAudit(Action,EntityId,ActorUserId,Detail,CreatedUtc) VALUES(@action,@id,@actorId,@detail,@now)", new { x.OperationKey, action, hash, id, actorId, now, detail }, tx, ct)).ConfigureAwait(false);
                tx.Commit(); return id;
            }
        }
        private static string EmployeeAudit(Employee e) => string.Join("|", new object[] { e.Version, e.Name, e.Phone, e.JobTitle, e.HireDate, e.Address, e.Notes, e.LinkedUserId, e.IsActive, e.AttendanceEnabled, e.Salary, e.SalaryFrequency, e.Payday, e.CompensationNotes });
        private static async Task CheckOverlap(SQLiteConnection c, SQLiteTransaction tx, long employeeId, long id, string start, string end, CancellationToken ct)
        {
            if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM EmployeeAttendance WHERE EmployeeId=@employeeId AND Id<>@id AND (@end IS NULL OR CheckInUtc<@end) AND (CheckOutUtc IS NULL OR CheckOutUtc>@start)", new { employeeId, id, start, end }, tx, ct)).ConfigureAwait(false) != 0) throw Conflict("يوجد حضور متداخل لهذا الموظف. راجع سجل الحضور.");
        }
        public async Task<Employee> EmployeeAsync(long id, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return await c.QuerySingleOrDefaultAsync<Employee>(Cmd("SELECT * FROM Employees WHERE Id=@id", new { id }, null, ct)).ConfigureAwait(false) ?? throw Missing();
        }
        public Task<WorkforcePage<Employee>> EmployeesAsync(WorkforceQuery q, CancellationToken ct)
        {
            const string where = " WHERE (@search='' OR instr(e.Name,@search)>0 OR instr(e.Phone,@search)>0 OR ('EMP-' || printf('%06d',e.Id)) LIKE @pattern) AND (@status='' OR e.IsActive=CASE WHEN @status='active' THEN 1 ELSE 0 END)";
            return PageAsync<Employee>("SELECT e.* FROM Employees e", "Employees e", where, q, null, "e.Id DESC", ct);
        }
        public Task<WorkforcePage<AttendanceRecord>> AttendanceAsync(WorkforceQuery q, CancellationToken ct)
        {
            const string from = "EmployeeAttendance a JOIN Employees e ON e.Id=a.EmployeeId";
            const string where = " WHERE (@employee IS NULL OR a.EmployeeId=@employee) AND (@search='' OR instr(e.Name,@search)>0) AND (@from IS NULL OR COALESCE(a.CheckOutUtc,'9999')>@from) AND (@to IS NULL OR a.CheckInUtc<@to) AND (@status='' OR (@status='open' AND a.CheckOutUtc IS NULL) OR (@status='closed' AND a.CheckOutUtc IS NOT NULL))";
            return PageAsync<AttendanceRecord>("SELECT a.*,e.Name EmployeeName FROM " + from, from, where, q, null, "a.CheckInUtc DESC,a.Id DESC", ct);
        }
        // Correlated sums use the covering shift index and avoid multiplying invoices by payments.
        private const string ShiftSql = @"SELECT s.*,u.DisplayName CashierName,
            (SELECT COUNT(*) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind IN ('cash_sale','credit_sale')) SalesCount,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='cash_sale'),0) CashSalesScaled,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='credit_sale'),0) CreditSalesScaled,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='collection'),0) CollectionsScaled,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='in'),0) CashInScaled,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='out'),0) CashOutScaled
            FROM CashierShifts s JOIN Users u ON u.Id=s.CashierId";
        public Task<WorkforcePage<CashierShift>> ShiftsAsync(WorkforceQuery q, long? ownerId, CancellationToken ct)
        {
            const string where = @" WHERE (@owner IS NULL OR s.CashierId=@owner) AND (@cashier IS NULL OR s.CashierId=@cashier)
                AND (@from IS NULL OR s.OpenedUtc>=@from) AND (@to IS NULL OR s.OpenedUtc<@to)
                AND (@status='' OR (@status='open' AND s.ClosedUtc IS NULL) OR (@status='closed' AND s.ClosedUtc IS NOT NULL))
                AND (@discrepancies=0 OR (s.ClosedUtc IS NOT NULL AND s.CountedScaled<>s.ClosingExpectedScaled))";
            return PageAsync<CashierShift>(ShiftSql, "CashierShifts s", where, q, ownerId, "s.OpenedUtc DESC,s.Id DESC", ct);
        }
        private async Task<WorkforcePage<T>> PageAsync<T>(string select, string from, string where, WorkforceQuery q, long? ownerId, string order, CancellationToken ct)
        {
            var p = new { search = q.Search ?? "", pattern = "%" + q.Search + "%", status = q.Status ?? "", employee = q.EmployeeId, cashier = q.CashierId, from = q.FromUtc, to = q.ToUtc, owner = ownerId, discrepancies = q.DiscrepanciesOnly, limit = q.PageSize, offset = (q.Page - 1) * q.PageSize };
            using (var c = await OpenAsync(ct).ConfigureAwait(false))
            using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
            {
                var total = await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM " + from + where, p, tx, ct)).ConfigureAwait(false);
                var rows = (await c.QueryAsync<T>(Cmd(select + where + " ORDER BY " + order + " LIMIT @limit OFFSET @offset", p, tx, ct)).ConfigureAwait(false)).ToList();
                tx.Commit(); return new WorkforcePage<T> { Items = rows, Total = total, Page = q.Page, PageSize = q.PageSize };
            }
        }
        public async Task<CashierShift> ShiftAsync(long id, long? ownerId, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return await c.QuerySingleOrDefaultAsync<CashierShift>(Cmd(ShiftSql + " WHERE s.Id=@id AND (@ownerId IS NULL OR s.CashierId=@ownerId)", new { id, ownerId }, null, ct)).ConfigureAwait(false) ?? throw Missing();
        }
        public async Task<IReadOnlyList<ShiftEvent>> EventsAsync(long id, int page, long? ownerId, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return (await c.QueryAsync<ShiftEvent>(Cmd("SELECT e.* FROM CashierShiftEvents e JOIN CashierShifts s ON s.Id=e.ShiftId WHERE s.Id=@id AND (@ownerId IS NULL OR s.CashierId=@ownerId) ORDER BY e.Id DESC LIMIT 50 OFFSET @offset", new { id, ownerId, offset = (page - 1) * 50 }, null, ct)).ConfigureAwait(false)).ToList();
        }
        public async Task<IReadOnlyList<AttendanceAuditRecord>> AttendanceAuditAsync(long id, int page, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return (await c.QueryAsync<AttendanceAuditRecord>(Cmd("SELECT Action,ActorUserId,Detail,CreatedUtc FROM WorkforceAudit WHERE EntityId=@id AND Action IN ('attendance-in','attendance-out','attendance-correct') ORDER BY Id DESC LIMIT 50 OFFSET @offset", new { id, offset = (page - 1) * 50 }, null, ct)).ConfigureAwait(false)).ToList();
        }
        // Called only while posting a NEW source document, inside its existing immediate writer transaction.
        public static async Task AssociateAsync(SQLiteConnection c, SQLiteTransaction tx, long actor, string kind, long amount, long? invoiceId, long? paymentId, CancellationToken ct)
        {
            var shift = await c.QuerySingleOrDefaultAsync<CashierShift>(Cmd(ShiftSql + " WHERE s.CashierId=@actor AND s.ClosedUtc IS NULL", new { actor }, tx, ct)).ConfigureAwait(false);
            if (shift == null) return; // Optional lifecycle: no shift requirement for unrelated workflows.
            if (kind == "cash_sale" || kind == "collection") { var next = checked(shift.ExpectedScaled + amount); }
            else { var next = checked(shift.CreditSalesScaled + amount); }
            await c.ExecuteAsync(Cmd("INSERT INTO CashierShiftEvents(ShiftId,Kind,AmountScaled,SalesInvoiceId,SalesPaymentId,ActorUserId,Reason,CreatedUtc) VALUES(@shiftId,@kind,@amount,@invoiceId,@paymentId,@actor,'',@now)", new { shiftId = shift.Id, kind, amount, invoiceId, paymentId, actor, now = Now() }, tx, ct)).ConfigureAwait(false);
        }
        private sealed class Operation { public string RequestHash { get; set; } public long EntityId { get; set; } }
    }
}

