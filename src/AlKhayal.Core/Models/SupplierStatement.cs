using System.Collections.Generic;
namespace AlKhayal.Core.Models
{
    public sealed class SupplierStatement
    {
        public long SupplierId { get; set; }
        public string SupplierName { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal OpeningBalance { get; set; }
        // Invoices in the period increase what we owe (credit to the supplier).
        public decimal TotalInvoices { get; set; }
        // Payments in the period decrease what we owe (debit to the supplier).
        public decimal TotalPaid { get; set; }
        public decimal ClosingBalance { get; set; }
        public List<SupplierStatementMovement> Movements { get; set; } = new List<SupplierStatementMovement>();
    }
    public sealed class SupplierStatementMovement
    {
        public string Date { get; set; }
        public string Reference { get; set; }
        public string Description { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
    }
}
