namespace AlKhayal.Api.Contracts
{
    public sealed class InstallmentSaleRequest
    {
        public long ProductId { get; set; }
        public decimal ProductPrice { get; set; }
        public decimal DownPayment { get; set; }
        public decimal InterestRate { get; set; }
        public int InstallmentCount { get; set; }
        public int IntervalMonths { get; set; }
        public string FirstDueOn { get; set; }
        public string OperationKey { get; set; }
    }

    public sealed class InstallmentPaymentRequest
    {
        public string PaidOn { get; set; }
        public string OperationKey { get; set; }
    }
}
