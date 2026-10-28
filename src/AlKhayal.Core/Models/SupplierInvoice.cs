namespace AlKhayal.Core.Models
{
    public sealed class SupplierInvoice
    {
        public long Id { get; set; }
        public long SupplierId { get; set; }
        public string InvoiceNumber { get; set; }
        public string InvoiceDate { get; set; }
        public string DueDate { get; set; }
        public decimal Total { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal Remaining { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
        public string CreatedAt { get; set; }
        public string InvoiceType { get; set; }
        public long? PurchaseInvoiceId { get; set; }
        public string DocumentStatus { get; set; }
        public decimal ReturnedAmount { get; set; }
        public bool Replayed { get; set; }
    }
}
