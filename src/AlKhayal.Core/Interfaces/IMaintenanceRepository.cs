using System.Collections.Generic;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface IMaintenanceRepository
    {
        MaintenanceTicket Create(MaintenanceTicket ticket, string operationKey, string requestHash);
        List<MaintenanceTicket> List(long? customerId);
        MaintenanceTicket UpdateStatus(long ticketId, string status, string operationKey);
        MaintenanceTicket Deliver(long ticketId, decimal finalCost, string settlementType, string deliveredOn, string operationKey, string requestHash);
    }
}
