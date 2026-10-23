namespace AlKhayal.Core.Models
{
    public sealed class CustomerStatementTransaction
    {
        public long Id { get; set; }
        public long CustomerId { get; set; }
        public string Type { get; set; }
        public decimal Amount { get; set; }
        public string OccurredOn { get; set; }
        public string Notes { get; set; }
        public bool Replayed { get; set; }
    }
}
