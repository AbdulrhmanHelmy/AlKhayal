using System.Collections.Generic;
namespace AlKhayal.Core.Models
{
    public sealed class CustomerStatement
    {
        public long CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal ClosingBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal CreditLimit { get; set; }
        public List<CustomerStatementMovement> Movements { get; set; } = new List<CustomerStatementMovement>();
    }
    public sealed class CustomerStatementMovement
    {
        public long? TransactionId { get; set; }
        public string Date { get; set; }
        public string Reference { get; set; }
        public string Description { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
        public string Kind { get; set; }
        public string Notes { get; set; }
    }
}
