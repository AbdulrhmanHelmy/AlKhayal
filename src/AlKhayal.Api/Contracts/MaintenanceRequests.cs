namespace AlKhayal.Api.Contracts
{
    public sealed class MaintenanceTicketRequest
    {
        public long CustomerId { get; set; }
        public string Phone { get; set; }
        public string Device { get; set; }
        public string Model { get; set; }
        public string Serial { get; set; }
        public string Accessories { get; set; }
        public string Issue { get; set; }
        public string Technician { get; set; }
        public decimal EstimatedCost { get; set; }
        public decimal AdvancePaid { get; set; }
        public string Notes { get; set; }
        public string ReceivedOn { get; set; }
        public string ExpectedOn { get; set; }
        public string OperationKey { get; set; }
    }
    public sealed class MaintenanceStatusRequest { public string Status { get; set; } public string OperationKey { get; set; } }
    public sealed class MaintenanceDeliveryRequest { public decimal FinalCost { get; set; } public string SettlementType { get; set; } public string DeliveredOn { get; set; } public string OperationKey { get; set; } }
}
