using System;
using System.Data.SQLite;
using System.Linq;
using AlKhayal.Application.Customers;
using AlKhayal.Application.Maintenance;
using AlKhayal.Core.Models;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Maintenance;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class MaintenancePersistenceTests
    {
        [Fact]
        public void Intake_advance_status_and_cash_or_credit_delivery_persist_atomically_and_project_to_statement()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var customer = new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer { Name = "عميل صيانة", Phone = "010", Balance = 5m, Status = "نشط" });
                var service = new MaintenanceService(new SqliteMaintenanceRepository(f.ConnectionString));
                var ticket = service.Create(new MaintenanceTicketCommand { CustomerId = customer.Id, Phone = customer.Phone, Device = "هاتف", Model = "موديل", Issue = "شاشة", EstimatedCost = 500m, AdvancePaid = 100m, ReceivedOn = "2026-10-29", ExpectedOn = "2026-11-05", OperationKey = "maintenance-create" });
                Assert.Equal(ticket.Id, service.Create(new MaintenanceTicketCommand { CustomerId = customer.Id, Phone = customer.Phone, Device = "هاتف", Model = "موديل", Issue = "شاشة", EstimatedCost = 500m, AdvancePaid = 100m, ReceivedOn = "2026-10-29", ExpectedOn = "2026-11-05", OperationKey = "maintenance-create" }).Id);
                Assert.Equal(100m, service.List(customer.Id).Single().AdvancePaid);
                var advanceStatement = new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString)).GetStatement(customer.Id, null, null);
                Assert.Equal(-95m, advanceStatement.CurrentBalance);
                Assert.Contains(advanceStatement.Movements, x => x.Description == "مقدم صيانة" && x.Credit == 100m);
                service.UpdateStatus(ticket.Id, "جاهز للتسليم", "maintenance-ready");
                service.Deliver(ticket.Id, 600m, "installment", "2026-11-05", "maintenance-credit-delivery");

                var statement = new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString)).GetStatement(customer.Id, null, null);
                Assert.Equal(505m, statement.CurrentBalance);
                Assert.Contains(statement.Movements, x => x.Description == "صيانة وخدمة" && x.Debit == 600m);
                Assert.Single(statement.Movements, x => x.Description == "مقدم صيانة" && x.Credit == 100m);
                Assert.Equal("تم التسليم", service.List(customer.Id).Single().Status);

                var cashTicket = service.Create(new MaintenanceTicketCommand { CustomerId = customer.Id, Phone = customer.Phone, Device = "لوحي", Model = "موديل 2", Issue = "بطارية", EstimatedCost = 80m, ReceivedOn = "2026-10-29", ExpectedOn = "2026-11-06", OperationKey = "maintenance-cash-create" });
                service.UpdateStatus(cashTicket.Id, "جاهز للتسليم", "maintenance-cash-ready");
                using (var c = f.Open()) c.Execute("CREATE TRIGGER fail_maintenance_ledger BEFORE INSERT ON CustomerTransactions WHEN NEW.Notes LIKE 'صيانة رقم%' BEGIN SELECT RAISE(ABORT,'injected maintenance failure'); END");
                Assert.Throws<SQLiteException>(() => service.Deliver(cashTicket.Id, 80m, "cash", "2026-11-06", "maintenance-cash-delivery"));
                using (var c = f.Open()) { Assert.Equal("جاهز للتسليم", c.ExecuteScalar<string>("SELECT Status FROM MaintenanceTickets WHERE Id=@Id", new { cashTicket.Id })); c.Execute("DROP TRIGGER fail_maintenance_ledger"); }
                service.Deliver(cashTicket.Id, 80m, "cash", "2026-11-06", "maintenance-cash-delivery");
                Assert.Equal(505m, new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString)).GetStatement(customer.Id, null, null).CurrentBalance);
                using (var c = f.Open())
                {
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts WHERE TicketId=@Id AND ReceiptType='advance'", new { ticket.Id }));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts WHERE TicketId=@Id AND ReceiptType='cash_delivery'", new { cashTicket.Id }));
                    Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketStatusHistory WHERE TicketId=@Id", new { cashTicket.Id }));
                    Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check")); Assert.Empty(c.Query("PRAGMA foreign_key_check"));
                }
            }
        }
    }
}
