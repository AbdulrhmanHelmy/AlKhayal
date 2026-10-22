namespace AlKhayal.Core.Models
{
    public sealed class CustomerPayment
    {
        public long Id { get; set; }
        public long CustomerId { get; set; }
        public string Reference { get; set; }
        public decimal Amount { get; set; }
        public string PaymentDate { get; set; }
        public string Method { get; set; }
        public string Account { get; set; }
        public string Notes { get; set; }
        public string CreatedAt { get; set; }
        public string CustomerName { get; set; }
    }
}
