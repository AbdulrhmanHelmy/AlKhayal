using System;
using System.Data.SQLite;
using System.Linq;
using AlKhayal.Application.Customers;
using AlKhayal.Core.Models;
using AlKhayal.Data.Customers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class CustomerStatementTests
    {
        [Fact]
        public void All_time_and_filtered_statements_reconcile_once_and_retain_current_balance_notes_and_restart()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customers=new CustomerService(new SqliteCustomerRepository(f.ConnectionString));
                var customer=customers.Create(new Customer{Name="عميل كشف",Phone="010",Balance=100,Status="نشط"});
                var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                service.PostTransaction(customer.Id,"increase",50.50m,"2026-10-28","مديونية جديدة","statement-increase");
                service.PostTransaction(customer.Id,"collection",20.25m,"2026-10-28","تحصيل جزئي","statement-collection");
                var statement=service.GetStatement(customer.Id,null,null);
                Assert.Equal(0m,statement.OpeningBalance);Assert.Equal(150.50m,statement.TotalDebit);Assert.Equal(20.25m,statement.TotalCredit);
                Assert.Equal(130.25m,statement.ClosingBalance);Assert.Equal(130.25m,statement.CurrentBalance);Assert.Equal(statement.ClosingBalance,statement.Movements.Last().Balance);
                Assert.Contains(statement.Movements,x=>x.Notes=="مديونية جديدة");Assert.Contains(statement.Movements,x=>x.Notes=="تحصيل جزئي");
                // Creation records an opening entry on the actual UTC day; derive filter boundaries from the fixture.
                string afterDate, beforeDate; using(var c=f.Open()){afterDate=DateTime.Parse(c.ExecuteScalar<string>("SELECT MAX(OccurredOn) FROM CustomerTransactions WHERE CustomerId=@Id",new{customer.Id}),System.Globalization.CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd");beforeDate=DateTime.Parse(c.ExecuteScalar<string>("SELECT MIN(OccurredOn) FROM CustomerTransactions WHERE CustomerId=@Id",new{customer.Id}),System.Globalization.CultureInfo.InvariantCulture).AddDays(-1).ToString("yyyy-MM-dd");}
                var later=service.GetStatement(customer.Id,afterDate,afterDate);Assert.Equal(130.25m,later.OpeningBalance);Assert.Equal(130.25m,later.ClosingBalance);Assert.Equal(130.25m,later.CurrentBalance);Assert.Equal(0m,later.TotalDebit);
                var before=service.GetStatement(customer.Id,null,beforeDate);Assert.Equal(0m,before.ClosingBalance);Assert.Equal(130.25m,before.CurrentBalance);
                Assert.True(service.PostTransaction(customer.Id,"collection",20.25m,"2026-10-28","تحصيل جزئي","statement-collection").Replayed);
                Assert.Equal(130.25m,new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString)).GetStatement(customer.Id,null,null).ClosingBalance);
                Assert.Throws<ArgumentException>(()=>service.PostTransaction(customer.Id,"collection",130.26m,"2026-10-28",null,"over-collection"));
            }
        }
        [Fact]
        public void Failed_balance_update_rolls_back_entry_and_retry_identity_then_increase_can_exceed_existing_debt()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customer=new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer{Name="عميل تراجع",Phone="010",Status="نشط"});
                var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                using(var c=f.Open())c.Execute("CREATE TRIGGER fail_customer_statement BEFORE UPDATE OF Balance ON Customers BEGIN SELECT RAISE(ABORT,'injected failure'); END");
                Assert.Throws<SQLiteException>(()=>service.PostTransaction(customer.Id,"increase",200.10m,"2026-10-28",null,"late-customer-failure"));
                using(var c=f.Open()){Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey='late-customer-failure'"));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));c.Execute("DROP TRIGGER fail_customer_statement");}
                Assert.False(service.PostTransaction(customer.Id,"increase",200.10m,"2026-10-28",null,"late-customer-failure").Replayed);
                Assert.True(service.PostTransaction(customer.Id,"increase",200.10m,"2026-10-28",null,"late-customer-failure").Replayed);
                Assert.Equal(200.10m,service.GetStatement(customer.Id,null,null).CurrentBalance);
            }
        }
        [Fact]
        public void Overflow_is_rejected_before_an_entry_is_inserted()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customer=new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer{Name="عميل الحدود",Phone="010",Status="نشط"});
                var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                service.PostTransaction(customer.Id,"increase",922337203685477.58m,"2026-10-28",null,"max-debt");
                Assert.Throws<OverflowException>(()=>service.PostTransaction(customer.Id,"increase",0.01m,"2026-10-28",null,"too-much-debt"));
                using(var c=f.Open())Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey='too-much-debt'"));
                Assert.Equal(922337203685477.58m,service.GetStatement(customer.Id,null,null).CurrentBalance);
            }
        }
    }
}
