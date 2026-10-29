using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Workforce;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class WorkforceServiceTests
    {
        [Theory]
        [InlineData("-1")]
        [InlineData("1.001")]
        [InlineData("922337203685477.59")]
        [InlineData("abc")]
        public async Task Invalid_money_is_rejected_before_storage(string value)
        {
            var store = new Store(); var service = new WorkforceService(store);
            await Assert.ThrowsAsync<WorkforceException>(() => service.ExecuteAsync("shift-open", new WorkforceCommand { Amount = value, OperationKey = "test" }, 1, true, CancellationToken.None)); Assert.Equal(0, store.Writes);
        }
        [Fact]
        public async Task Optional_employee_fields_and_canonical_money_are_preserved()
        {
            var store = new Store(); var service = new WorkforceService(store); var ct = CancellationToken.None;
            await service.ExecuteAsync("employee-save", new WorkforceCommand { Name = " موظف ", OperationKey = "name-only" }, 1, true, ct); Assert.Null(store.Amount);
            await service.ExecuteAsync("shift-open", new WorkforceCommand { Amount = "0.10", OperationKey = "one" }, 1, false, ct); var hash = store.Hash;
            await service.ExecuteAsync("shift-open", new WorkforceCommand { Amount = "0.1", OperationKey = "one" }, 1, false, ct); Assert.Equal(hash, store.Hash); Assert.Equal(1000, store.Amount);
            await Assert.ThrowsAsync<WorkforceException>(() => service.ExecuteAsync("employee-save", new WorkforceCommand { Name = "موظف", OperationKey = "two" }, 1, false, ct));
            await Assert.ThrowsAsync<WorkforceException>(() => service.ExecuteAsync("shift-open", new WorkforceCommand { Amount = "1", CashierId = 2, OperationKey = "three" }, 1, false, ct));
        }
        [Theory]
        [InlineData("2026-10-01T23:00:00", "2026-10-02T01:00:00Z")]
        [InlineData("2026-10-02T02:00:00Z", "2026-10-02T01:00:00Z")]
        [InlineData("2099-10-01T23:00:00Z", null)]
        public async Task Attendance_corrections_reject_ambiguous_reverse_and_future_timestamps(string start, string end)
        {
            var store = new Store(); var s = new WorkforceService(store);
            await Assert.ThrowsAsync<WorkforceException>(() => s.ExecuteAsync("attendance-correct", new WorkforceCommand { Id = 1, Version = 1, OperationKey = "time", Reason = "تصحيح", CheckInUtc = start, CheckOutUtc = end }, 1, true, CancellationToken.None)); Assert.Equal(0, store.Writes);
        }
        private sealed class Store : IWorkforceStore
        {
            public int Writes; public long? Amount; public string Hash;
            public Task<long> ExecuteAsync(string a, WorkforceCommand x, long? amount, string hash, long u, bool admin, CancellationToken ct) { Writes++; Amount = amount; Hash = hash; return Task.FromResult(1L); }
            public Task<WorkforcePage<Employee>> EmployeesAsync(WorkforceQuery q, CancellationToken ct) => throw new NotSupportedException();
            public Task<Employee> EmployeeAsync(long id, CancellationToken ct) => throw new NotSupportedException();
            public Task<WorkforcePage<AttendanceRecord>> AttendanceAsync(WorkforceQuery q, CancellationToken ct) => throw new NotSupportedException();
            public Task<WorkforcePage<CashierShift>> ShiftsAsync(WorkforceQuery q, long? owner, CancellationToken ct) => throw new NotSupportedException();
            public Task<CashierShift> ShiftAsync(long id, long? owner, CancellationToken ct) => throw new NotSupportedException();
            public Task<IReadOnlyList<ShiftEvent>> EventsAsync(long id, int page, long? owner, CancellationToken ct) => throw new NotSupportedException();
            public Task<IReadOnlyList<AttendanceAuditRecord>> AttendanceAuditAsync(long id, int page, CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
