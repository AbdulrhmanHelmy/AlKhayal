using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;

namespace AlKhayal.Application.Workforce
{
    public sealed class WorkforceException : Exception
    {
        public string Code { get; }
        public WorkforceException(string code, string message) : base(message) { Code = code; }
    }
    public sealed class WorkforceCommand
    {
        public string OperationKey { get; set; }
        public long Id { get; set; }
        public long Version { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string JobTitle { get; set; }
        public string HireDate { get; set; }
        public string Address { get; set; }
        public string Notes { get; set; }
        public long? LinkedUserId { get; set; }
        public bool IsActive { get; set; } = true;
        public bool AttendanceEnabled { get; set; }
        public string Salary { get; set; }
        public string SalaryFrequency { get; set; }
        public int? Payday { get; set; }
        public string CompensationNotes { get; set; }
        public long EmployeeId { get; set; }
        public long? CashierId { get; set; }
        public string Amount { get; set; }
        public string Direction { get; set; }
        public string Reason { get; set; }
        public string CheckInUtc { get; set; }
        public string CheckOutUtc { get; set; }
    }
    public sealed class Employee
    {
        public long Id { get; set; }
        public string Code => "EMP-" + Id.ToString("D6", CultureInfo.InvariantCulture);
        public long Version { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string JobTitle { get; set; }
        public string HireDate { get; set; }
        public string Address { get; set; }
        public string Notes { get; set; }
        public long? LinkedUserId { get; set; }
        public bool IsActive { get; set; }
        public bool AttendanceEnabled { get; set; }
        public long? SalaryScaled { get; set; }
        public string Salary => SalaryScaled.HasValue ? MoneyPolicy.FormatForUser(SalaryScaled.Value) : null;
        public string SalaryFrequency { get; set; }
        public int? Payday { get; set; }
        public string CompensationNotes { get; set; }
        public string CreatedUtc { get; set; }
    }
    public sealed class AttendanceRecord
    {
        public long Id { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public long Version { get; set; }
        public string CheckInUtc { get; set; }
        public string CheckOutUtc { get; set; }
        public long WorkedSeconds => (long)((CheckOutUtc == null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(CheckOutUtc, CultureInfo.InvariantCulture)) - DateTimeOffset.Parse(CheckInUtc, CultureInfo.InvariantCulture)).TotalSeconds;
    }
    public sealed class CashierShift
    {
        public long Id { get; set; }
        public long CashierId { get; set; }
        public string CashierName { get; set; }
        public string RegisterId { get; set; }
        public string OpenedUtc { get; set; }
        public string ClosedUtc { get; set; }
        public string OpeningNotes { get; set; }
        public string ClosingNotes { get; set; }
        public long OpeningScaled { get; set; }
        public long CashSalesScaled { get; set; }
        public long CreditSalesScaled { get; set; }
        public long CollectionsScaled { get; set; }
        public long CashInScaled { get; set; }
        public long CashOutScaled { get; set; }
        public long SalesCount { get; set; }
        public long? CountedScaled { get; set; }
        public long ExpectedScaled => checked(OpeningScaled + CashSalesScaled + CollectionsScaled + CashInScaled - CashOutScaled);
        public long? DifferenceScaled => CountedScaled.HasValue ? checked(CountedScaled.Value - ExpectedScaled) : (long?)null;
        public string Opening => MoneyPolicy.FormatForUser(OpeningScaled);
        public string CashSales => MoneyPolicy.FormatForUser(CashSalesScaled);
        public string CreditSales => MoneyPolicy.FormatForUser(CreditSalesScaled);
        public string Collections => MoneyPolicy.FormatForUser(CollectionsScaled);
        public string CashIn => MoneyPolicy.FormatForUser(CashInScaled);
        public string CashOut => MoneyPolicy.FormatForUser(CashOutScaled);
        public string Expected => MoneyPolicy.FormatForUser(ExpectedScaled);
        public string Counted => CountedScaled.HasValue ? MoneyPolicy.FormatForUser(CountedScaled.Value) : null;
        public string Difference => DifferenceScaled.HasValue ? MoneyPolicy.FormatForUser(DifferenceScaled.Value) : null;
    }
    public sealed class ShiftEvent
    {
        public long Id { get; set; }
        public string Kind { get; set; }
        public long AmountScaled { get; set; }
        public string Amount => MoneyPolicy.FormatForUser(AmountScaled);
        public long ActorUserId { get; set; }
        public long? SalesInvoiceId { get; set; }
        public long? SalesPaymentId { get; set; }
        public string Reason { get; set; }
        public string CreatedUtc { get; set; }
    }
    public sealed class AttendanceAuditRecord
    {
        public string Action { get; set; }
        public long ActorUserId { get; set; }
        public string Detail { get; set; }
        public string CreatedUtc { get; set; }
    }
    public sealed class WorkforceQuery
    {
        public string Search { get; set; }
        public string Status { get; set; }
        public long? EmployeeId { get; set; }
        public long? CashierId { get; set; }
        public string FromUtc { get; set; }
        public string ToUtc { get; set; }
        public bool DiscrepanciesOnly { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }
    public sealed class WorkforcePage<T>
    {
        public IReadOnlyList<T> Items { get; set; }
        public long Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
    public interface IWorkforceStore
    {
        Task<long> ExecuteAsync(string action, WorkforceCommand command, long? amount, string hash, long actorId, bool administrator, CancellationToken ct);
        Task<WorkforcePage<Employee>> EmployeesAsync(WorkforceQuery query, CancellationToken ct);
        Task<Employee> EmployeeAsync(long id, CancellationToken ct);
        Task<WorkforcePage<AttendanceRecord>> AttendanceAsync(WorkforceQuery query, CancellationToken ct);
        Task<WorkforcePage<CashierShift>> ShiftsAsync(WorkforceQuery query, long? ownerId, CancellationToken ct);
        Task<CashierShift> ShiftAsync(long id, long? ownerId, CancellationToken ct);
        Task<IReadOnlyList<ShiftEvent>> EventsAsync(long id, int page, long? ownerId, CancellationToken ct);
        Task<IReadOnlyList<AttendanceAuditRecord>> AttendanceAuditAsync(long id, int page, CancellationToken ct);
    }
}
