namespace AlKhayal.Core.Models
{
    public sealed class SupplierLedgerEntry
    {
        public long Id { get; set; }
        public long SupplierId { get; set; }
        public string EntryType { get; set; }
        public long AmountScaled { get; set; }
        public string OccurredOn { get; set; }
        public string Reference { get; set; }
        public string Notes { get; set; }
        public long? PurchaseInvoiceId { get; set; }
        public long? SupplierInvoiceId { get; set; }
        public string PurchaseInvoiceNumber { get; set; }
        public string PaymentMethod { get; set; }
        public string RequestHash { get; set; }
        public bool Replayed { get; set; }
    }

    public sealed class SupplierLedgerPaymentCommand
    {
        public long SupplierId { get; set; }
        public long? SupplierInvoiceId { get; set; }
        public long? PurchaseInvoiceId { get; set; }
        public long AmountScaled { get; set; }
        public string PaymentDate { get; set; }
        public string Method { get; set; }
        public string Account { get; set; }
        public string Notes { get; set; }
        public string OperationKey { get; set; }
        public string RequestHash { get; set; }
    }
}
