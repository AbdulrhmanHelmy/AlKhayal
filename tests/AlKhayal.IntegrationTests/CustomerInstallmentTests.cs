using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Customers;
using AlKhayal.Core.Models;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class CustomerInstallmentTests
    {
        [Fact]
        public async Task Contract_and_installment_payments_are_atomic_idempotent_and_project_to_customer_statement()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var catalog = new CatalogService(new SqliteCatalogStore(f.ConnectionString));
                var unit = (await catalog.SaveUnitAsync(new SaveUnitCommand { Code = "PCS-INST", Name = "قطعة", Symbol = "قطعة", IsActive = true }, CancellationToken.None)).Value;
                var product = (await catalog.CreateProductAsync(new CreateProductCommand { OperationKey = "installment-product", Name = "جهاز بالتقسيط", BaseUnitId = unit.Id, ProductType = ProductTypes.Stock, SalePrice = "1000.00", IsActive = true }, CancellationToken.None)).Value;
                var customer = new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer { Name = "عميل تقسيط", Phone = "010", Balance = 20m, Status = "نشط" });
                var service = new InstallmentService(new SqliteCustomerInstallmentRepository(f.ConnectionString));
                using (var c = f.Open()) c.Execute("CREATE TRIGGER fail_installment_ledger BEFORE INSERT ON CustomerTransactions WHEN NEW.Notes LIKE 'بيع بالتقسيط%' BEGIN SELECT RAISE(ABORT,'injected installment failure'); END");
                Assert.Throws<SQLiteException>(() => service.Create(customer.Id, product.Id, 1000m, 100m, 10m, 3, 2, "2026-11-30", "installment-create-rollback"));
                using (var c = f.Open()) { Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentContracts")); c.Execute("DROP TRIGGER fail_installment_ledger"); }

                var contract = service.Create(customer.Id, product.Id, 1000m, 100m, 10m, 3, 2, "2026-11-30", "installment-create-1");
                Assert.Equal(1090m, contract.TotalAmount); Assert.Equal(990m, contract.Schedule.Sum(x => x.Amount));
                Assert.Equal(new[] { "2026-11-30", "2027-01-30", "2027-03-30" }, contract.Schedule.Select(x => x.DueDate).ToArray());
                Assert.Equal(contract.Id, service.Create(customer.Id, product.Id, 1000m, 100m, 10m, 3, 2, "2026-11-30", "installment-create-1").Id);
                Assert.Throws<CustomerOperationKeyConflictException>(() => service.Create(customer.Id, product.Id, 1000m, 90m, 10m, 3, 2, "2026-11-30", "installment-create-1"));
                var changedProduct = await catalog.UpdateProductAsync(new UpdateProductCommand { ProductId = product.Id, Name = product.Name, BaseUnitId = unit.Id, ProductType = ProductTypes.Stock, SalePrice = "1100.00", IsActive = true, Version = product.Version }, CancellationToken.None);
                Assert.True(changedProduct.Succeeded);
                Assert.Throws<ArgumentException>(() => service.Create(customer.Id, product.Id, 1000m, 100m, 10m, 3, 2, "2026-11-30", "installment-stale-product-price"));

                var paid = service.Pay(customer.Id, contract.Id, 1, "2026-12-01", "installment-payment-1");
                Assert.Equal("مسدد", paid.Schedule[0].Status); Assert.Equal("مستحق", paid.Schedule[1].Status);
                Assert.Equal(contract.Id, service.Pay(customer.Id, contract.Id, 1, "2026-12-01", "installment-payment-1").Id);
                Assert.Throws<CustomerOperationKeyConflictException>(() => service.Pay(customer.Id, contract.Id, 1, "2026-12-02", "installment-payment-1"));
                Assert.Throws<ArgumentException>(() => service.Pay(customer.Id, contract.Id, 1, "2026-12-01", "installment-payment-duplicate"));

                var statement = new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString)).GetStatement(customer.Id, null, null);
                Assert.Equal(680m, statement.CurrentBalance); Assert.Equal(680m, statement.ClosingBalance);
                Assert.Contains(statement.Movements, x => x.Description == "بيع بالتقسيط" && x.Debit == 1090m);
                Assert.Contains(statement.Movements, x => x.Description == "مقدم بيع بالتقسيط" && x.Credit == 100m);
                Assert.Contains(statement.Movements, x => x.Description == "سداد القسط رقم 1" && x.Credit == 330m);
                using (var c = f.Open()) { Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentPayments")); Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentContracts WHERE OperationKey='installment-create-1'")); Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check")); Assert.Empty(c.Query("PRAGMA foreign_key_check")); }
            }
        }
    }
}
