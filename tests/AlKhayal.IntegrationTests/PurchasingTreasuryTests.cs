using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Treasury;
using AlKhayal.Core.Models;
using AlKhayal.Data.Purchasing;
using AlKhayal.Data.Suppliers;
using AlKhayal.Data.Treasury;
using Dapper;
using Xunit;
using TreasuryAccountCommand = AlKhayal.Application.Treasury.TreasuryAccountCommand;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class InventoryPersistenceTests
    {
        private static async Task<long> FundPurchaseAccount(Fixture f, string amount = "100", string type = "cash")
        {
            var treasury = new TreasuryService(new SqliteTreasuryStore(f.Cs));
            var account = await treasury.SaveAsync(null, new TreasuryAccountCommand { Code = Guid.NewGuid().ToString("N"), Name = "حساب للمشتريات", Type = type }, 1, CancellationToken.None);
            await treasury.PostAsync(new TreasuryMovementCommand { AccountId = account.Id, Amount = amount, OperationKey = Guid.NewGuid().ToString("N") }, "deposit", 1, CancellationToken.None);
            return account.Id;
        }
        private static PurchaseDraftCommand CashPurchase(Fixture f, long supplier, long? account, string number = "CASH") => new PurchaseDraftCommand
        {
            SupplierId = supplier, WarehouseId = 1, InvoiceNumber = number, InvoiceDate = "2026-10-28", PaymentMode = "cash", TreasuryAccountId = account,
            Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "1", PurchaseUnitPrice = "10.13", SaleUnitPrice = "15" } }
        };
        private static long PurchaseSupplier(Fixture f) => new SqliteSupplierRepository(f.Cs).Add(new Supplier { Code = Guid.NewGuid().ToString("N"), Name = "مورد", Phone = "", Status = "نشط" });
        private static PurchaseService Purchases(Fixture f) => new PurchaseService(new SqlitePurchaseRepository(f.Cs), new SqliteSupplierRepository(f.Cs));

        [Theory]
        [InlineData("cash")]
        [InlineData("bank")]
        public async Task Cash_purchase_persists_selected_account_and_debits_once_atomically(string type)
        {
            using (var f = new Fixture())
            {
                var account = await FundPurchaseAccount(f, type: type); var untouched = await FundPurchaseAccount(f);
                var supplier = PurchaseSupplier(f); var service = Purchases(f);
                var draft = await service.CreateDraftAsync(CashPurchase(f, supplier, account), 1, CancellationToken.None);
                Assert.Equal(account, draft.TreasuryAccountId); Assert.Null(draft.TreasuryMovementId);
                using (var c = new SQLiteConnection(f.Cs)) { c.Open(); Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements WHERE Type='withdraw'")); }
                var results = await Task.WhenAll(Task.Run(() => service.PostAsync(draft.Id, "purchase-cash-retry", 1, CancellationToken.None)), Task.Run(() => service.PostAsync(draft.Id, "purchase-cash-retry", 1, CancellationToken.None)));
                Assert.All(results, x => Assert.True(x.Succeeded, x.ErrorMessage)); Assert.Single(results, x => x.Replayed);
                var saved = await Purchases(f).GetAsync(draft.Id, CancellationToken.None);
                Assert.Equal(account, saved.TreasuryAccountId); Assert.NotNull(saved.TreasuryMovementId); Assert.Equal("حساب للمشتريات", saved.TreasuryAccountName);
                Assert.True((await service.PostAsync(draft.Id, "purchase-cash-retry", 1, CancellationToken.None)).Replayed);
                var rows = await new TreasuryService(new SqliteTreasuryStore(f.Cs)).ListAsync(CancellationToken.None);
                Assert.Equal("89.87", rows.Single(x => x.Id == account).Balance); Assert.Equal("100.00", rows.Single(x => x.Id == untouched).Balance);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open(); Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements WHERE Type='withdraw'"));
                    Assert.Equal(101300, c.ExecuteScalar<long>("SELECT AmountScaled FROM TreasuryMovements WHERE Id=@Id", new { Id = saved.TreasuryMovementId }));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM SupplierLedgerEntries WHERE PurchaseInvoiceId=@Id", new { Id = draft.Id }));
                    Assert.Equal(1000000, c.ExecuteScalar<long>("SELECT SUM(QuantityBaseScaled) FROM InventoryBalances"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers")); Assert.Empty(c.Query("PRAGMA foreign_key_check"));
                }
            }
        }
        [Fact]
        public async Task Cash_purchase_missing_account_or_insufficient_funds_cannot_partially_post()
        {
            using (var f = new Fixture())
            {
                var supplier = PurchaseSupplier(f); var service = Purchases(f);
                await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDraftAsync(CashPurchase(f, supplier, null), 1, CancellationToken.None));
                await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDraftAsync(CashPurchase(f, supplier, 99999), 1, CancellationToken.None));
                var account = await FundPurchaseAccount(f, "1");
                var draft = await service.CreateDraftAsync(CashPurchase(f, supplier, account), 1, CancellationToken.None);
                Assert.Equal("treasury_insufficient_balance", (await service.PostAsync(draft.Id, "insufficient-cash", 1, CancellationToken.None)).ErrorCode);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open(); Assert.Equal("draft", c.ExecuteScalar<string>("SELECT Status FROM PurchaseInvoices"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE PurchaseInvoiceId IS NOT NULL"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements WHERE Type='withdraw'"));
                    c.Execute("UPDATE PurchaseInvoices SET TreasuryAccountId=NULL WHERE Id=@Id", new { draft.Id });
                }
                Assert.Equal("treasury_required", (await service.PostAsync(draft.Id, "old-cash", 1, CancellationToken.None)).ErrorCode);
            }
        }
        [Fact]
        public async Task Late_purchase_failure_rolls_back_treasury_supplier_stock_and_retry_posts_once()
        {
            using (var f = new Fixture())
            {
                var account = await FundPurchaseAccount(f); var service = Purchases(f);
                var draft = await service.CreateDraftAsync(CashPurchase(f, PurchaseSupplier(f), account), 1, CancellationToken.None);
                using (var c = new SQLiteConnection(f.Cs)) { c.Open(); c.Execute("CREATE TRIGGER fail_cash_purchase BEFORE INSERT ON PurchaseInvoiceAudit WHEN NEW.EventType='posted' BEGIN SELECT RAISE(ABORT,'injected failure'); END"); }
                Assert.False((await service.PostAsync(draft.Id, "cash-rollback", 1, CancellationToken.None)).Succeeded);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open(); Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(2, c.ExecuteScalar<long>("SELECT Version FROM TreasuryAccounts"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE PurchaseInvoiceId IS NOT NULL"));
                    Assert.Equal("draft", c.ExecuteScalar<string>("SELECT Status FROM PurchaseInvoices"));
                    Assert.Null(c.ExecuteScalar<long?>("SELECT TreasuryMovementId FROM PurchaseInvoices")); c.Execute("DROP TRIGGER fail_cash_purchase");
                }
                Assert.True((await service.PostAsync(draft.Id, "cash-rollback", 1, CancellationToken.None)).Succeeded);
                Assert.True((await service.PostAsync(draft.Id, "cash-rollback", 1, CancellationToken.None)).Replayed);
            }
        }
        [Fact]
        public async Task Draft_can_change_cash_account_or_switch_to_credit_without_debit()
        {
            using (var f = new Fixture())
            {
                var first = await FundPurchaseAccount(f); var second = await FundPurchaseAccount(f); var service = Purchases(f);
                var command = CashPurchase(f, PurchaseSupplier(f), first); var draft = await service.CreateDraftAsync(command, 1, CancellationToken.None);
                command.TreasuryAccountId = second;
                Assert.Equal(second, (await service.UpdateDraftAsync(draft.Id, command, 1, CancellationToken.None)).TreasuryAccountId);
                command.PaymentMode = "credit";
                await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateDraftAsync(draft.Id, command, 1, CancellationToken.None));
                command.TreasuryAccountId = null; await service.UpdateDraftAsync(draft.Id, command, 1, CancellationToken.None);
                var result = await service.PostAsync(draft.Id, "credit-from-cash", 1, CancellationToken.None);
                Assert.True(result.Succeeded); Assert.Null(result.Invoice.TreasuryMovementId);
                using (var c = new SQLiteConnection(f.Cs)) { c.Open(); Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements WHERE Type='withdraw'")); }
            }
        }
    }
}
