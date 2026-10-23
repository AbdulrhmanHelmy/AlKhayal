namespace AlKhayal.Core.Models
{
    public sealed class Supplier
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Contact { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        // Current amount owed to the supplier. Changed only by invoices and payments after creation.
        public decimal Payable { get; set; }
        // Sum of all recorded payments (read-only, derived).
        public decimal Paid { get; set; }
        public string Status { get; set; }
    }
}
