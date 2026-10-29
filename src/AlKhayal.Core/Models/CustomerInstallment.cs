using System.Collections.Generic;

namespace AlKhayal.Core.Models
{
    public sealed class CustomerInstallmentContract
    {
        public long Id { get; set; }
        public long CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string ContractNumber { get; set; }
        public long ProductId { get; set; }
        public string ProductName { get; set; }
        public decimal ProductPrice { get; set; }
        public decimal DownPayment { get; set; }
        public decimal InterestRate { get; set; }
        public decimal TotalAmount { get; set; }
        public int InstallmentCount { get; set; }
        public int IntervalMonths { get; set; }
        public string FirstDueOn { get; set; }
        public string Status { get; set; }
        public List<CustomerInstallmentRow> Schedule { get; set; } = new List<CustomerInstallmentRow>();
    }

    public sealed class CustomerInstallmentRow
    {
        public int Number { get; set; }
        public string DueDate { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; }
        public string PaidOn { get; set; }
    }
}
