namespace AlKhayal.Api.Contracts
{
    public sealed class CustomerTransactionRequest
    {
        public string Type { get; set; }
        public decimal Amount { get; set; }
        public string OccurredOn { get; set; }
        public string Notes { get; set; }
        public string OperationKey { get; set; }
    }
}
