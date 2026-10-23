namespace AlKhayal.Core.Models
{
    public sealed class SupplierPayment
    {
        public long Id { get; set; }
        public long SupplierId { get; set; }
        public long? InvoiceId { get; set; }
        public string Reference { get; set; }
        public decimal Amount { get; set; }
        public string PaymentDate { get; set; }
        public string Method { get; set; }
        public string Account { get; set; }
        public string Notes { get; set; }
        public string CreatedAt { get; set; }
        public string SupplierName { get; set; }
        public string InvoiceNumber { get; set; }
    }
}
