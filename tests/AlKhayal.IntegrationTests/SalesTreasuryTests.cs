using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Treasury;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Sales;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SalesTreasuryTests
    {
        internal sealed class Fixture : IDisposable
        {
            public TreasuryPersistenceTests.Fixture Db { get; } = new TreasuryPersistenceTests.Fixture();
            public SalesService Sales => new SalesService(new SqliteSalesStore(Db.ConnectionString));
            public long Variant { get; private set; }
            public long Unit { get; private set; }
            public async Task Prepare()
            {
                var catalog = new CatalogService(new SqliteCatalogStore(Db.ConnectionString));
                var product = await catalog.CreateProductAsync(new CreateProductCommand { OperationKey = "revenue-product", Name = "صنف الوردية", BaseUnitId = 1, ProductType = "stock", SalePrice = "12.34", PurchasePrice = "9", IsActive = true }, CancellationToken.None);
                Assert.True(product.Succeeded, product.ErrorMessage);
                using (var c = Db.Open()) { Variant = c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id", new { product.Value.Id }); Unit = c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1", new { product.Value.Id }); }
                var stock = await new SqliteInventoryStore(Db.ConnectionString).PostAsync(new InventoryPostCommand { OperationKey = "revenue-stock", Type = "manual_receipt", WarehouseId = 1, UserId = Db.UserId, Reason = "توريد", Lines = new[] { new InventoryLineInput { VariantId = Variant, ProductUnitId = Unit, Quantity = "20", UnitCost = "9", SalePrice = "12.34" } } }, "revenue-stock-hash", CancellationToken.None);
                Assert.True(stock.Succeeded, stock.ErrorMessage);
            }
            public Task<SaleInvoice> Draft(string price = "12.34", string discount = "0.00", long? customer = null, string quantity = "1") => Sales.CreateDraftAsync(new SaleDraftCommand {
                WarehouseId = 1, InvoiceDate = "2026-10-28", PaymentMode = customer.HasValue ? "credit" : "cash", CustomerId = customer, Discount = discount,
                Lines = new System.Collections.Generic.List<SaleLine> { new SaleLine { VariantId = Variant, ProductUnitId = Unit, Quantity = quantity, UnitPrice = price } }
            }, Db.UserId, CancellationToken.None);
            public Task<SalePostResult> Post(long id, string key = "revenue-post") => Sales.PostAsync(id, key, Db.UserId, CancellationToken.None);
            public async Task FundRefund(string amount = "12.34")
            {
                var account = await Db.Service.SaveAsync(null, new TreasuryAccountCommand { Code = "SALES-RETURNS", Name = "مرتجعات البيع", Type = "cash" }, Db.UserId, CancellationToken.None);
                await Db.Post(account.Id, amount);
            }
            public void Dispose() { Db.Dispose(); }
        }
        [Fact]
        public async Task Cash_sale_deposits_exact_final_total_creates_shift_account_once_and_replays_after_restart()
        {
            using (var f = new Fixture())
            {
                await f.Prepare(); using (var c = f.Db.Open()) c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=10,PricesIncludeTax=0");
                var draft = await f.Draft(discount: "0.68", quantity: "2"); var sale = await f.Post(draft.Id);
                Assert.True(sale.Succeeded, sale.ErrorMessage); Assert.Equal(264000, sale.Invoice.TotalScaled);
                Assert.Equal(sale.Invoice.TotalScaled, sale.Invoice.PaidScaled); Assert.True(sale.Invoice.TreasuryMovementId > 0);
                var account = Assert.Single(await f.Db.Service.ListAsync(CancellationToken.None)); Assert.Equal(ShiftRevenuePolicy.AccountName, account.Name); Assert.Equal("cash", account.Type); Assert.Equal("26.40", account.Balance);
                Migrator.Run(f.Db.ConnectionString); var retry = await f.Post(draft.Id); Assert.True(retry.Succeeded, retry.ErrorMessage); Assert.Equal(sale.Invoice.TreasuryMovementId, retry.Invoice.TreasuryMovementId);
                var second = await f.Draft(); Assert.True((await f.Post(second.Id, "next-sale")).Succeeded);
                Assert.Equal("39.97", Assert.Single(await f.Db.Service.ListAsync(CancellationToken.None)).Balance);
                using (var c = f.Db.Open())
                {
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM ShiftRevenueTreasury")); Assert.Equal(2, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesTreasuryReceipts"));
                    Assert.Throws<System.Data.SQLite.SQLiteException>(() => c.Execute("DELETE FROM SalesTreasuryReceipts"));
                    Assert.Throws<System.Data.SQLite.SQLiteException>(() => c.Execute("UPDATE ShiftRevenueTreasury SET AccountId=AccountId"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
        }
        [Fact]
        public async Task Existing_cashbox_with_user_spelling_is_reused_but_credit_and_zero_sales_do_not_deposit()
        {
            using (var f = new Fixture())
            {
                await f.Prepare(); var account = await f.Db.Service.SaveAsync(null, new TreasuryAccountCommand { Code = "CUSTOM", Name = "ايراد الورديه", Type = "cash" }, f.Db.UserId, CancellationToken.None); await f.Db.Post(account.Id, "5");
                await Assert.ThrowsAsync<ArgumentException>(() => f.Draft("1", "1"));
                long customerId;
                using (var c = f.Db.Open()) customerId = c.ExecuteScalar<long>("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('REV-C','عميل','','',0,1000,'أساسي','نشط','2026-10-28','2026-10-28'); SELECT last_insert_rowid();");
                var credit = await f.Draft(customer: customerId); Assert.True((await f.Post(credit.Id, "credit")).Succeeded);
                using (var c = f.Db.Open()) Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM ShiftRevenueTreasury"));
                var cash = await f.Draft(); var result = await f.Post(cash.Id); Assert.True(result.Succeeded, result.ErrorMessage); Assert.Equal(account.Id, result.Invoice.TreasuryAccountId);
                Assert.Equal("17.34", Assert.Single(await f.Db.Service.ListAsync(CancellationToken.None)).Balance);
            }
        }
        [Fact]
        public async Task Late_failure_rolls_back_inventory_sale_payment_new_cashbox_source_and_audit_then_retry_succeeds()
        {
            using (var f = new Fixture())
            {
                await f.Prepare(); var draft = await f.Draft();
                using (var c = f.Db.Open()) c.Execute("CREATE TRIGGER RevenueFailure BEFORE INSERT ON SalesInvoiceAudit WHEN NEW.EventType='posted' BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.False((await f.Post(draft.Id)).Succeeded);
                using (var c = f.Db.Open())
                {
                    foreach (var table in new[] { "TreasuryAccounts", "TreasuryMovements", "TreasuryAudit", "ShiftRevenueTreasury", "SalesTreasuryReceipts", "SalesPayments" }) Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM " + table));
                    Assert.Equal("draft", c.ExecuteScalar<string>("SELECT Status FROM SalesInvoices")); Assert.Equal(20000000, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances")); c.Execute("DROP TRIGGER RevenueFailure");
                }
                var retry = await f.Post(draft.Id); Assert.True(retry.Succeeded, retry.ErrorMessage); Assert.Equal("12.34", Assert.Single(await f.Db.Service.ListAsync(CancellationToken.None)).Balance);
            }
        }
        [Fact]
        public async Task Overflow_and_ambiguous_accounts_rollback_while_concurrent_retries_deposit_once()
        {
            using (var f = new Fixture())
            {
                await f.Prepare(); var account = await f.Db.Service.SaveAsync(null, new TreasuryAccountCommand { Code = "EXIST", Name = ShiftRevenuePolicy.AccountName, Type = "cash" }, f.Db.UserId, CancellationToken.None);
                var bank = await f.Db.Service.SaveAsync(null, new TreasuryAccountCommand { Code = "BANK", Name = "ايراد الورديه", Type = "bank" }, f.Db.UserId, CancellationToken.None);
                var draft = await f.Draft(); var conflict = await f.Post(draft.Id); Assert.False(conflict.Succeeded); Assert.Equal("treasury_shift_account_conflict", conflict.ErrorCode);
                await f.Db.Service.DeleteAsync(bank.Id, bank.Version, f.Db.UserId, CancellationToken.None);
                var results = await Task.WhenAll(Task.Run(() => f.Post(draft.Id)), Task.Run(() => f.Post(draft.Id))); Assert.All(results, x => Assert.True(x.Succeeded, x.ErrorMessage));
                Assert.Equal("12.34", Assert.Single(await f.Db.Service.ListAsync(CancellationToken.None)).Balance);
                await f.Db.Post(account.Id, "922337203685465.24"); var overflow = await f.Draft(); var failed = await f.Post(overflow.Id, "overflow"); Assert.False(failed.Succeeded); Assert.Equal("treasury_balance_overflow", failed.ErrorCode);
                using (var c = f.Db.Open()) { Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesTreasuryReceipts")); Assert.Equal(19000000, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances")); }
            }
        }
    }
}
