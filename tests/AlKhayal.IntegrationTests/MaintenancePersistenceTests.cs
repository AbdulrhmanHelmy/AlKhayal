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
        private static MaintenanceService Service(TreasuryPersistenceTests.Fixture f) => new MaintenanceService(new SqliteMaintenanceRepository(f.ConnectionString));
        private static Customer Customer(TreasuryPersistenceTests.Fixture f, decimal debt = 0m) => new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer { Name="عميل مقدم صيانة",Phone="010",Balance=debt,Status="نشط" });
        private static MaintenanceTicketCommand Intake(long customerId,string key="advance-intake") => new MaintenanceTicketCommand { CustomerId=customerId,Phone="010",Device="هاتف",Model="موديل",Issue="عطل",EstimatedCost=500m,AdvancePaid=100m,ReceivedOn="2026-10-29",ExpectedOn="2026-11-05",OperationKey=key };

        [Theory]
        [InlineData(0,"cash")]
        [InlineData(5,"cash")]
        [InlineData(0,"installment")]
        [InlineData(5,"installment")]
        public void Advance_is_ticket_prepayment_and_is_applied_once_only_when_final_service_is_charged(int debt,string settlement)
        {
            using(var f=new TreasuryPersistenceTests.Fixture()){
                var customer=Customer(f,debt);var service=Service(f);var command=Intake(customer.Id);
                var ticket=service.Create(command);Assert.Equal(ticket.Id,service.Create(command).Id);
                var account=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                Assert.Equal(debt,account.GetStatement(customer.Id,null,null).CurrentBalance);
                Assert.Throws<ArgumentException>(()=>account.PostTransaction(customer.Id,"collection",debt+1,"2026-10-29",null,"cannot-collect-pending-advance"));
                command.AdvancePaid=101m;Assert.Throws<CustomerOperationKeyConflictException>(()=>service.Create(command));
                Assert.Throws<ArgumentException>(()=>service.Deliver(ticket.Id,600m,settlement,"2026-11-05","not-ready"));
                service.UpdateStatus(ticket.Id,"جاهز للتسليم","ready");
                Assert.Throws<ArgumentException>(()=>service.Deliver(ticket.Id,99m,settlement,"2026-11-05","below-advance"));
                service.Deliver(ticket.Id,600m,settlement,"2026-11-05","delivery");
                service.Deliver(ticket.Id,600m,settlement,"2026-11-05","delivery");
                Assert.Throws<CustomerOperationKeyConflictException>(()=>service.Deliver(ticket.Id,601m,settlement,"2026-11-05","delivery"));
                var expected=debt+(settlement=="cash"?0:500m);var statement=account.GetStatement(customer.Id,null,null);Assert.Equal(expected,statement.CurrentBalance);
                Assert.Single(statement.Movements,x=>x.Description=="مقدم صيانة"&&x.Credit==100m);
                using(var c=f.Open()){
                    Assert.Equal(expected,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTickets"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts WHERE ReceiptType='advance'"));
                    Assert.Equal(settlement=="cash"?1:0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts WHERE ReceiptType='cash_delivery'"));
                    Assert.Equal(settlement=="cash"?5000000:0,c.ExecuteScalar<long>("SELECT COALESCE(SUM(AmountScaled),0) FROM MaintenanceTicketReceipts WHERE ReceiptType='cash_delivery'"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey=@key",new{key="maintenance:"+ticket.Id+":advance"}));
                    Assert.Equal("2026-11-05",c.ExecuteScalar<string>("SELECT OccurredOn FROM CustomerTransactions WHERE OperationKey=@key",new{key="maintenance:"+ticket.Id+":advance"}));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries"));Assert.Empty(c.Query("PRAGMA foreign_key_check"));
                }
            }
        }

        [Theory]
        [InlineData("cash")]
        [InlineData("installment")]
        public void Full_prepayment_leaves_existing_debt_unchanged_without_an_extra_cash_receipt(string settlement)
        {
            using(var f=new TreasuryPersistenceTests.Fixture()){
                var customer=Customer(f,5);var service=Service(f);var ticket=service.Create(Intake(customer.Id));service.UpdateStatus(ticket.Id,"جاهز للتسليم","ready");
                service.Deliver(ticket.Id,100m,settlement,"2026-11-05","fully-prepaid");
                using(var c=f.Open()){Assert.Equal(5m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts WHERE ReceiptType='cash_delivery'"));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey LIKE 'maintenance:%'"));}
            }
        }

        [Theory]
        [InlineData("cash")]
        [InlineData("installment")]
        public void Legacy_advance_already_in_customer_ledger_is_preserved_without_a_second_deduction(string settlement)
        {
            using(var f=new TreasuryPersistenceTests.Fixture()){
                var customer=Customer(f,200m);var service=Service(f);var ticket=service.Create(Intake(customer.Id));
                using(var c=f.Open()){
                    // Persist the valid old-version intake state (200 debt minus a 100 advance).
                    c.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference) VALUES(@Id,@key,'legacy-hash','collection',1000000,'2026-10-29',@notes,'2026-10-29T00:00:00Z',@reference)",new{Id=customer.Id,key="maintenance:"+ticket.Id+":advance",notes="مقدم صيانة رقم "+ticket.TicketNumber,reference=ticket.TicketNumber});
                    c.Execute("UPDATE Customers SET Balance=100 WHERE Id=@Id",new{customer.Id});
                }
                service.UpdateStatus(ticket.Id,"جاهز للتسليم","ready");service.Deliver(ticket.Id,600m,settlement,"2026-11-05","delivery");service.Deliver(ticket.Id,600m,settlement,"2026-11-05","delivery");
                using(var c=f.Open()){
                    Assert.Equal(settlement=="cash"?200m:700m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey=@key",new{key="maintenance:"+ticket.Id+":advance"}));Assert.Equal("legacy-hash",c.ExecuteScalar<string>("SELECT RequestHash FROM CustomerTransactions WHERE OperationKey=@key",new{key="maintenance:"+ticket.Id+":advance"}));
                }
            }
        }

        [Fact]
        public void Mismatched_legacy_advance_is_rejected_without_charge_or_delivery_writes()
        {
            using(var f=new TreasuryPersistenceTests.Fixture()){
                var customer=Customer(f,200m);var service=Service(f);var ticket=service.Create(Intake(customer.Id));service.UpdateStatus(ticket.Id,"جاهز للتسليم","ready");
                using(var c=f.Open()){
                    c.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc) VALUES(@Id,@key,'bad-legacy','collection',500000,'2026-10-29','legacy','2026-10-29T00:00:00Z')",new{Id=customer.Id,key="maintenance:"+ticket.Id+":advance"});c.Execute("UPDATE Customers SET Balance=150 WHERE Id=@Id",new{customer.Id});
                }
                Assert.Throws<ArgumentException>(()=>service.Deliver(ticket.Id,600m,"cash","2026-11-05","delivery"));
                using(var c=f.Open()){
                    Assert.Equal(150m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts"));Assert.Equal("جاهز للتسليم",c.ExecuteScalar<string>("SELECT Status FROM MaintenanceTickets"));
                }
            }
        }

        [Fact]
        public void Intake_and_late_delivery_failures_roll_back_every_financial_and_ticket_effect_and_retry_once()
        {
            using(var f=new TreasuryPersistenceTests.Fixture()){
                var customer=Customer(f);var service=Service(f);var command=Intake(customer.Id);
                using(var c=f.Open())c.Execute("CREATE TRIGGER fail_maintenance_receipt BEFORE INSERT ON MaintenanceTicketReceipts BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.Throws<SQLiteException>(()=>service.Create(command));
                using(var c=f.Open()){
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTickets"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketStatusHistory"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts"));c.Execute("DROP TRIGGER fail_maintenance_receipt");
                }
                var ticket=service.Create(command);service.UpdateStatus(ticket.Id,"جاهز للتسليم","ready");
                using(var c=f.Open())c.Execute("CREATE TRIGGER fail_maintenance_delivery BEFORE INSERT ON MaintenanceTicketStatusHistory WHEN NEW.Status='تم التسليم' BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.Throws<SQLiteException>(()=>service.Deliver(ticket.Id,600m,"cash","2026-11-05","delivery"));
                using(var c=f.Open()){
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));Assert.Equal("جاهز للتسليم",c.ExecuteScalar<string>("SELECT Status FROM MaintenanceTickets"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts"));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketStatusHistory"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));c.Execute("DROP TRIGGER fail_maintenance_delivery");
                }
                service.Deliver(ticket.Id,600m,"cash","2026-11-05","delivery");service.Deliver(ticket.Id,600m,"cash","2026-11-05","delivery");
                using(var c=f.Open()){Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts"));Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketStatusHistory"));Assert.Equal("ok",c.ExecuteScalar<string>("PRAGMA integrity_check"));}
            }
        }

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
                // The receipt prepays an uncharged service, not the existing 5 EGP debt.
                Assert.Equal(5m, advanceStatement.CurrentBalance);
                Assert.DoesNotContain(advanceStatement.Movements, x => x.Description == "مقدم صيانة");
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
