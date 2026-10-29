using System.Collections.Generic;

namespace AlKhayal.Core.Models
{
    public sealed class MaintenanceTicket
    {
        public long Id { get; set; }
        public long CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string TicketNumber { get; set; }
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
        public string Status { get; set; }
        public string DeliveredOn { get; set; }
        public decimal? FinalCost { get; set; }
        public string SettlementType { get; set; }
        public List<MaintenanceStatusEvent> History { get; set; } = new List<MaintenanceStatusEvent>();
    }

    public sealed class MaintenanceStatusEvent
    {
        public string Status { get; set; }
        public string ChangedUtc { get; set; }
    }
}
