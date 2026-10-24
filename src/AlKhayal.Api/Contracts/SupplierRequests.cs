namespace AlKhayal.Api.Contracts
{
    public sealed class SupplierInvoiceRequest
    {
        public string InvoiceNumber { get; set; }
        public string InvoiceDate { get; set; }
        public string DueDate { get; set; }
        public decimal Total { get; set; }
        public string Notes { get; set; }
    }

    public sealed class SupplierPaymentRequest
    {
        public long? InvoiceId { get; set; }
        public long? PurchaseInvoiceId { get; set; }
        public string OperationKey { get; set; }
        public decimal Amount { get; set; }
        public string PaymentDate { get; set; }
        public string Method { get; set; }
        public string Account { get; set; }
        public string Notes { get; set; }
    }
}
