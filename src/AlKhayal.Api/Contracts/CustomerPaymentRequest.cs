namespace AlKhayal.Api.Contracts
{
    public sealed class CustomerPaymentRequest
    {
        public decimal Amount { get; set; }
        public string PaymentDate { get; set; }
        public string Method { get; set; }
        public string Account { get; set; }
        public string Notes { get; set; }
    }
}
