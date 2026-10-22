namespace AlKhayal.Core.Models
{
    public sealed class Customer
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public decimal Balance { get; set; }
        public decimal CreditLimit { get; set; }
        public string Subscription { get; set; }
        public string Status { get; set; }
    }
}
