using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Workforce;
using AlKhayal.Data.Database;
using AlKhayal.Data.Workforce;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class WorkforcePersistenceTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        internal static WorkforceService Service(TreasuryPersistenceTests.Fixture f) => new WorkforceService(new SqliteWorkforceStore(f.ConnectionString));
        private static WorkforceCommand Command(string name = null) => new WorkforceCommand { Name = name ?? "موظف", OperationKey = Guid.NewGuid().ToString("N") };
        private static Task<long> Run(TreasuryPersistenceTests.Fixture f, string action, WorkforceCommand x) => Service(f).ExecuteAsync(action, x, f.UserId, true, Ct);
        private static WorkforceCommand Cash(long id = 0, string amount = "100", string reason = null, string direction = null) => new WorkforceCommand { Id = id, Amount = amount, Reason = reason, Direction = direction, OperationKey = Guid.NewGuid().ToString("N") };

        [Fact]
        public async Task Name_only_optional_fields_salary_linking_edit_and_deactivation_persist_without_credentials()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var x = Command(); var id = await Run(f, "employee-save", x);
                Assert.Equal(id, await Run(f, "employee-save", x));
                var e = await Service(f).EmployeeAsync(id, Ct);
                Assert.Null(e.Salary); Assert.Null(e.Payday); Assert.Null(e.LinkedUserId); Assert.False(e.AttendanceEnabled); Assert.True(e.IsActive); Assert.Equal("EMP-000001", e.Code); Assert.NotNull(e.CreatedUtc);
                x.Id = id; x.Version = e.Version; x.Salary = "1234.56"; x.SalaryFrequency = "monthly"; x.LinkedUserId = f.UserId; x.IsActive = false; x.OperationKey = "edit";
                await Run(f, "employee-save", x); Migrator.Run(f.ConnectionString);
                e = await Service(f).EmployeeAsync(id, Ct); Assert.Equal("1234.56", e.Salary); Assert.Null(e.Payday); Assert.False(e.IsActive); Assert.Equal(f.UserId, e.LinkedUserId);
                var other = Command(); other.LinkedUserId = f.UserId; await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "employee-save", other));
                await Assert.ThrowsAsync<WorkforceException>(() => Service(f).ExecuteAsync("employee-save", Command(), f.UserId, false, Ct));
                using (var c = f.Open()) { Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM Users")); Assert.Equal(2, c.ExecuteScalar<long>("SELECT COUNT(*) FROM WorkforceAudit")); Assert.Throws<SQLiteException>(() => c.Execute("DELETE FROM Employees")); Assert.Empty(c.Query("PRAGMA foreign_key_check")); }
            }
        }
        [Fact]
        public async Task Concurrent_employee_codes_are_unique_retries_and_version_conflicts_are_safe()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() => Run(f, "employee-save", Command("موظف " + i)))));
                Assert.Equal(6, ids.Distinct().Count());
                var q = await Service(f).EmployeesAsync(new WorkforceQuery { PageSize = 2, Page = 2 }, Ct); Assert.Equal(6, q.Total); Assert.Equal(2, q.Items.Count);
                var x = Command(); x.Id = ids[0]; x.Version = 999;
                await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "employee-save", x));
                var same = Command(); var replay = await Task.WhenAll(Task.Run(() => Run(f, "employee-save", same)), Task.Run(() => Run(f, "employee-save", same))); Assert.Equal(replay[0], replay[1]);
            }
        }
        [Fact]
        public async Task Attendance_opt_out_duplicate_checkout_correction_cross_midnight_overlap_and_audit()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var x = Command(); var employee = await Run(f, "employee-save", x);
                var checkIn = Command(); checkIn.EmployeeId = employee;
                await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "attendance-in", checkIn));
                x.Id = employee; x.Version = 1; x.AttendanceEnabled = true; x.OperationKey = "enable"; await Run(f, "employee-save", x);
                var id = await Run(f, "attendance-in", checkIn); Assert.Equal(id, await Run(f, "attendance-in", checkIn));
                var duplicate = Command(); duplicate.EmployeeId = employee; await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "attendance-in", duplicate));
                var correct = Command(); correct.Id = id; correct.Version = 1; correct.CheckInUtc = "2026-10-01T23:00:00+02:00"; correct.CheckOutUtc = "2026-10-02T07:00:00+02:00"; correct.Reason = "تصحيح موثق";
                await Run(f, "attendance-correct", correct);
                var row = Assert.Single((await Service(f).AttendanceAsync(new WorkforceQuery(), Ct)).Items); Assert.Equal(28800, row.WorkedSeconds); Assert.Equal("2026-10-01T21:00:00.0000000Z", row.CheckInUtc);
                var next = Command(); next.EmployeeId = employee; var nextId = await Run(f, "attendance-in", next);
                correct.Id = nextId; correct.Version = 1; correct.OperationKey = "overlap"; await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "attendance-correct", correct));
                var checkout = Command(); checkout.Id = nextId; await Run(f, "attendance-out", checkout); Assert.Equal(nextId, await Run(f, "attendance-out", checkout));
                checkout.OperationKey = "duplicate-out"; await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "attendance-out", checkout));
                Migrator.Run(f.ConnectionString); Assert.Equal(2, (await Service(f).AttendanceAsync(new WorkforceQuery(), Ct)).Total);
                using (var c = f.Open()) { Assert.Contains("before=", c.ExecuteScalar<string>("SELECT Detail FROM WorkforceAudit WHERE Action='attendance-correct'")); Assert.Throws<SQLiteException>(() => c.Execute("UPDATE WorkforceAudit SET Detail='tamper'")); }
            }
        }
        [Theory]
        [InlineData("100", "0.00")]
        [InlineData("99", "-1.00")]
        [InlineData("101", "1.00")]
        public async Task Shift_open_close_exact_shortage_excess_restart_replay_and_closed_history(string counted, string difference)
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var open = Cash(); var id = await Run(f, "shift-open", open); Assert.Equal(id, await Run(f, "shift-open", open));
                Assert.Equal("100.00", (await Service(f).ShiftAsync(id, null, Ct)).Opening);
                await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "shift-open", Cash()));
                var close = Cash(id, counted); await Run(f, "shift-close", close); Assert.Equal(id, await Run(f, "shift-close", close));
                await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "shift-close", Cash(id, counted)));
                await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "shift-adjust", Cash(id, "1", "بعد الإغلاق", "in")));
                Migrator.Run(f.ConnectionString); var saved = await Service(f).ShiftAsync(id, null, Ct); Assert.Equal(difference, saved.Difference); Assert.NotNull(saved.ClosedUtc);
                var report = await Service(f).ShiftsAsync(new WorkforceQuery { DiscrepanciesOnly = true }, null, Ct); Assert.Equal(difference == "0.00" ? 0 : 1, report.Total);
                using (var c = f.Open()) { Assert.Throws<SQLiteException>(() => c.Execute("UPDATE CashierShifts SET OpeningScaled=0")); Assert.Throws<SQLiteException>(() => c.Execute("DELETE FROM CashierShifts")); }
            }
        }
        [Fact]
        public async Task Real_sales_collections_credit_separation_historical_exclusion_and_reconciliation_are_atomic()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); var old = await f.Draft(); Assert.True((await f.Post(old.Id, "historical")).Succeeded);
                var id = await Run(f.Db, "shift-open", Cash()); var cash = await f.Draft(); Assert.True((await f.Post(cash.Id, "new-cash")).Succeeded);
                long customer;
                using (var c = f.Db.Open()) customer = c.ExecuteScalar<long>("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('SHIFT-C','عميل','','',0,1000,'أساسي','نشط','2026-10-28','2026-10-28'); SELECT last_insert_rowid();");
                var credit = await f.Draft(customer: customer); Assert.True((await f.Post(credit.Id, "new-credit")).Succeeded);
                var payment = new SalePaymentCommand { Amount = "5", Method = "cash", OccurredOn = "2026-10-28", OperationKey = "collection" };
                Assert.True((await f.Sales.AddPaymentAsync(credit.Id, payment, f.Db.UserId, Ct)).Succeeded);
                Assert.True((await f.Sales.AddPaymentAsync(credit.Id, payment, f.Db.UserId, Ct)).Replayed);
                var adjustment = Cash(id, "10", "إضافة نقد للدرج", "in"); await Run(f.Db, "shift-adjust", adjustment); await Run(f.Db, "shift-adjust", adjustment);
                await Run(f.Db, "shift-adjust", Cash(id, "3", "سحب نقد موثق", "out"));
                var shift = await Service(f.Db).ShiftAsync(id, null, Ct); Assert.Equal(2, shift.SalesCount); Assert.Equal("12.34", shift.CashSales); Assert.Equal("12.34", shift.CreditSales); Assert.Equal("5.00", shift.Collections); Assert.Equal("124.34", shift.Expected);
                Assert.True((await f.Post(cash.Id, "new-cash")).Succeeded); Assert.Equal("124.34", (await Service(f.Db).ShiftAsync(id, null, Ct)).Expected);
                await Run(f.Db, "shift-close", Cash(id, "124.34"));
                var later = await f.Draft(); Assert.True((await f.Post(later.Id, "after-close")).Succeeded); Assert.Equal(2, (await Service(f.Db).ShiftAsync(id, null, Ct)).SalesCount);
                using (var c = f.Db.Open()) { Assert.Equal(5, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CashierShiftEvents")); Assert.Throws<SQLiteException>(() => c.Execute("UPDATE CashierShiftEvents SET AmountScaled=0")); Assert.Empty(c.Query("PRAGMA foreign_key_check")); }
            }
        }
        [Fact]
        public async Task Late_shift_event_failure_rolls_back_sale_inventory_payment_and_treasury()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); var id = await Run(f.Db, "shift-open", Cash()); var draft = await f.Draft();
                using (var c = f.Db.Open()) c.Execute("CREATE TRIGGER ShiftFailure BEFORE INSERT ON CashierShiftEvents BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.False((await f.Post(draft.Id)).Succeeded); Assert.Equal("draft", (await f.Sales.GetAsync(draft.Id, Ct)).Status);
                using (var c = f.Db.Open()) { Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments")); Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements")); Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments")); c.Execute("DROP TRIGGER ShiftFailure"); }
                Assert.True((await f.Post(draft.Id)).Succeeded); Assert.Equal("112.34", (await Service(f.Db).ShiftAsync(id, null, Ct)).Expected);
            }
        }
        [Fact]
        public async Task Cashier_ownership_conflicting_keys_adjustment_reason_and_audit_rollback_are_enforced()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var open = Cash(); var id = await Run(f, "shift-open", open);
                open.Amount = "101"; await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "shift-open", open));
                await Assert.ThrowsAsync<WorkforceException>(() => Service(f).ExecuteAsync("shift-close", Cash(id), f.UserId + 1, false, Ct));
                await Assert.ThrowsAsync<WorkforceException>(() => Service(f).ShiftAsync(id, f.UserId + 1, Ct));
                Assert.Empty((await Service(f).ShiftsAsync(new WorkforceQuery(), f.UserId + 1, Ct)).Items);
                await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "shift-adjust", Cash(id, "1", null, "in")));
                await Assert.ThrowsAsync<WorkforceException>(() => Run(f, "shift-adjust", Cash(id, "101", "تجاوز النقد", "out")));
                using (var c = f.Open()) c.Execute("CREATE TRIGGER AuditFailure BEFORE INSERT ON WorkforceAudit BEGIN SELECT RAISE(ABORT,'injected'); END;");
                var close = Cash(id); await Assert.ThrowsAsync<SQLiteException>(() => Run(f, "shift-close", close)); Assert.Null((await Service(f).ShiftAsync(id, null, Ct)).ClosedUtc);
                using (var c = f.Open()) c.Execute("DROP TRIGGER AuditFailure");
                await Run(f, "shift-close", close);
            }
        }
        [Fact]
        public async Task Concurrent_open_adjustment_retry_and_sale_close_never_assign_to_closed_shift()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare();
                Func<Task<long?>> open = async () => { try { return await Run(f.Db, "shift-open", Cash()); } catch (WorkforceException) { return null; } };
                var opened = await Task.WhenAll(Task.Run(open), Task.Run(open)); var id = Assert.Single(opened, x => x.HasValue).Value;
                var adjustment = Cash(id, "1", "نقد إضافي", "in");
                await Task.WhenAll(Task.Run(() => Run(f.Db, "shift-adjust", adjustment)), Task.Run(() => Run(f.Db, "shift-adjust", adjustment)));
                Assert.Equal("101.00", (await Service(f.Db).ShiftAsync(id, null, Ct)).Expected);
                var draft = await f.Draft();
                var posting = Task.Run(() => f.Post(draft.Id)); var closing = Task.Run(() => Run(f.Db, "shift-close", Cash(id, "101")));
                Assert.True((await posting).Succeeded); await closing;
                var saved = await Service(f.Db).ShiftAsync(id, null, Ct);
                Assert.Contains(saved.Expected, new[] { "101.00", "113.34" });
                using (var c = f.Db.Open())
                {
                    Assert.Equal(saved.ExpectedScaled, c.ExecuteScalar<long>("SELECT ClosingExpectedScaled FROM CashierShifts WHERE Id=@id", new { id }));
                    Assert.Throws<SQLiteException>(() => c.Execute("INSERT INTO CashierShiftEvents(ShiftId,Kind,AmountScaled,ActorUserId,Reason,CreatedUtc) VALUES(@id,'in',100,@actor,'late',@now)", new { id, actor = f.Db.UserId, now = DateTime.UtcNow.ToString("o") }));
                }
            }
        }
        [Fact]
        public async Task Card_collections_are_excluded_and_late_cash_collection_failure_rolls_back_source()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); long customer;
                using (var c = f.Db.Open()) customer = c.ExecuteScalar<long>("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('SHIFT-CARD','عميل','','',0,1000,'أساسي','نشط','2026-10-28','2026-10-28'); SELECT last_insert_rowid();");
                var id = await Run(f.Db, "shift-open", Cash()); var credit = await f.Draft(customer: customer); Assert.True((await f.Post(credit.Id)).Succeeded);
                Assert.True((await f.Sales.AddPaymentAsync(credit.Id, new SalePaymentCommand { Amount = "2", Method = "card", OccurredOn = "2026-10-28", OperationKey = "card" }, f.Db.UserId, Ct)).Succeeded);
                Assert.Equal("100.00", (await Service(f.Db).ShiftAsync(id, null, Ct)).Expected);
                using (var c = f.Db.Open()) c.Execute("CREATE TRIGGER CollectionFailure BEFORE INSERT ON CashierShiftEvents WHEN NEW.Kind='collection' BEGIN SELECT RAISE(ABORT,'injected'); END;");
                var command = new SalePaymentCommand { Amount = "3", Method = "cash", OccurredOn = "2026-10-28", OperationKey = "cash-failure" };
                Assert.False((await f.Sales.AddPaymentAsync(credit.Id, command, f.Db.UserId, Ct)).Succeeded);
                var saved = await f.Sales.GetAsync(credit.Id, Ct); Assert.Equal(20000, saved.PaidScaled);
                using (var c = f.Db.Open()) { Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE OperationKey='cash-failure'")); c.Execute("DROP TRIGGER CollectionFailure"); }
                Assert.True((await f.Sales.AddPaymentAsync(credit.Id, command, f.Db.UserId, Ct)).Succeeded); Assert.Equal("103.00", (await Service(f.Db).ShiftAsync(id, null, Ct)).Expected);
            }
        }
    }
}
