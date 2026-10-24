using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Purchasing;
using AlKhayal.Core.Models;
using AlKhayal.Data.Purchasing;
using AlKhayal.Data.Suppliers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class InventoryPersistenceTests
    {
        [Fact]
        public async Task Purchase_draft_persists_then_posts_once_with_fifo_and_supplier_ledger_atomically()
        {
            using (var f = new Fixture())
            {
                var suppliers = new SqliteSupplierRepository(f.Cs);
                var supplierId = suppliers.Add(new Supplier { Code = "PUR-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = "مورد شراء", Phone = "01000000000", Status = "نشط" });
                var service = new PurchaseService(new SqlitePurchaseRepository(f.Cs), suppliers);
                var draft = await service.CreateDraftAsync(new PurchaseDraftCommand
                {
                    SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "INV-PH4-1", InvoiceDate = "2026-10-24", PaymentMode = "credit",
                    Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "2.5", PurchaseUnitPrice = "3.3333", SaleUnitPrice = "5.2500" } }
                }, 1, CancellationToken.None);
                Assert.Equal("draft", draft.Status); Assert.Equal(0, suppliers.GetById(supplierId).Payable);
                var reopened = await service.GetAsync(draft.Id, CancellationToken.None);
                Assert.Equal("2.5", reopened.Lines.Single().Quantity); Assert.Equal("3.3333", reopened.Lines.Single().PurchaseUnitPrice);
                var posted = await service.PostAsync(draft.Id, "purch-test-1", 1, CancellationToken.None);
                Assert.True(posted.Succeeded, posted.ErrorCode + ": " + posted.ErrorMessage);
                Assert.Equal("posted", posted.Invoice.Status); Assert.False(posted.Replayed);
                Assert.Equal(8.3333m, suppliers.GetById(supplierId).Payable);
                var replay = await service.PostAsync(draft.Id, "purch-test-1", 1, CancellationToken.None);
                Assert.True(replay.Succeeded); Assert.True(replay.Replayed);
                Assert.Equal(8.3333m, suppliers.GetById(supplierId).Payable);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments WHERE DocumentType='manual_receipt' AND Status='posted' AND Notes LIKE @Note", new { Note = "%purchaseInvoiceId=" + draft.Id + "%" }));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers WHERE Basis='receipt'"));
                    Assert.Equal(83333, c.ExecuteScalar<long>("SELECT ValueScaled FROM InventoryCostLayers WHERE Basis='receipt'"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE PurchaseInvoiceId=@Id AND EntryType='purchase'", new { Id = draft.Id }));
                    Assert.Equal(18, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task Cash_purchase_creates_equal_payable_and_payment_entries_without_treasury_rows()
        {
            using (var f = new Fixture())
            {
                var suppliers = new SqliteSupplierRepository(f.Cs);
                var supplierId = suppliers.Add(new Supplier { Code = "CASH-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = "مورد نقدي", Phone = "01000000000", Status = "نشط" });
                var service = new PurchaseService(new SqlitePurchaseRepository(f.Cs), suppliers);
                var draft = await service.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "CASH-1", InvoiceDate = "2026-10-24", PaymentMode = "cash", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "1", PurchaseUnitPrice = "10.1250", SaleUnitPrice = "15" } } }, 1, CancellationToken.None);
                var result = await service.PostAsync(draft.Id, "cash-post-1", 1, CancellationToken.None);
                Assert.True(result.Succeeded, result.ErrorMessage);
                Assert.Equal(0m, suppliers.GetById(supplierId).Payable);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(new long[] { -101250, 101250 }, c.Query<long>("SELECT AmountScaled FROM SupplierLedgerEntries WHERE PurchaseInvoiceId=@Id ORDER BY AmountScaled", new { Id = draft.Id }).ToArray());
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));
                }
            }
        }

        [Fact]
        public async Task Inventory_failure_rolls_back_receipt_and_financial_posting_but_keeps_draft()
        {
            using (var f = new Fixture())
            {
                var suppliers = new SqliteSupplierRepository(f.Cs);
                var supplierId = suppliers.Add(new Supplier { Code = "ROLL-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = "مورد تراجع", Phone = "01000000000", Status = "نشط" });
                var repo = new SqlitePurchaseRepository(f.Cs); var service = new PurchaseService(repo, suppliers);
                var draft = await service.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "ROLL-1", InvoiceDate = "2026-10-24", PaymentMode = "credit", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "1", PurchaseUnitPrice = "10", SaleUnitPrice = "15" }, new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "1", PurchaseUnitPrice = "10", SaleUnitPrice = "15" } } }, 1, CancellationToken.None);
                using (var c = new SQLiteConnection(f.Cs)) { c.Open(); c.Execute("CREATE TRIGGER test_fail_second_purchase_line BEFORE INSERT ON StockMovements WHEN (SELECT LineNumber FROM InventoryDocumentLines WHERE Id=NEW.DocumentLineId)=2 BEGIN SELECT RAISE(ABORT,'test failure'); END;"); }
                var result = await service.PostAsync(draft.Id, "rollback-post-1", 1, CancellationToken.None);
                Assert.False(result.Succeeded); Assert.Equal("purchase_conflict", result.ErrorCode);
                Assert.Equal("draft", (await service.GetAsync(draft.Id, CancellationToken.None)).Status);
                Assert.Equal(0m, suppliers.GetById(supplierId).Payable);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments WHERE OperationKey='purchase:rollback-post-1'"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE PurchaseInvoiceId=@Id AND EntryType IN ('purchase','payment')", new { Id = draft.Id }));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers WHERE Basis='receipt'"));
                }
            }
        }

        [Fact]
        public async Task Purchase_return_uses_original_account_value_and_actual_fifo_cost_and_replays_once()
        {
            using (var f = new Fixture())
            {
                var suppliers = new SqliteSupplierRepository(f.Cs);
                var supplierId = suppliers.Add(new Supplier { Code = "RET-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = "مورد مرتجع", Phone = "01000000000", Status = "نشط" });
                var service = new PurchaseService(new SqlitePurchaseRepository(f.Cs), suppliers);
                var draft = await service.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "RET-INV-1", InvoiceDate = "2026-10-24", PaymentMode = "credit", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "2", PurchaseUnitPrice = "10.1250", SaleUnitPrice = "15" } } }, 1, CancellationToken.None);
                Assert.True((await service.PostAsync(draft.Id, "ret-purchase-post", 1, CancellationToken.None)).Succeeded);
                var eligible = (await service.ReturnableLinesAsync(draft.Id, 1, CancellationToken.None)).Single();
                Assert.Equal("2", eligible.AvailableQuantity);
                var command = new PurchaseReturnCommand { WarehouseId = 1, ReturnMode = "cash_refund", Lines = new System.Collections.Generic.List<PurchaseReturnLine> { new PurchaseReturnLine { PurchaseInvoiceLineId = eligible.PurchaseInvoiceLineId, Quantity = "1" } } };
                var returned = await service.PostReturnAsync(draft.Id, command, "ret-op-1", 1, CancellationToken.None);
                Assert.True(returned.Succeeded, returned.ErrorCode + ": " + returned.ErrorMessage);
                Assert.Equal("cash_refund", returned.ReturnMode);
                Assert.Equal(101250, returned.Lines.Single().AccountAmountScaled);
                Assert.Equal(returned.Lines.Single().AccountAmountScaled, returned.Lines.Single().InventoryValueScaled);
                Assert.Equal(0, returned.Lines.Single().VarianceScaled);
                var replay = await service.PostReturnAsync(draft.Id, command, "ret-op-1", 1, CancellationToken.None);
                Assert.True(replay.Succeeded); Assert.True(replay.Replayed);
                Assert.Single(await service.ReturnsAsync(draft.Id, CancellationToken.None));
                Assert.Equal(10.125m, suppliers.GetById(supplierId).Payable);
                Assert.Equal("1", (await service.ReturnableLinesAsync(draft.Id, 1, CancellationToken.None)).Single().AvailableQuantity);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE EntryType='purchase_return_refund' AND PurchaseInvoiceId=@Id", new { Id = draft.Id }));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));
                }
            }
        }

        [Fact]
        public async Task Purchase_return_can_follow_the_original_fifo_layer_after_stock_transfer()
        {
            using (var f = new Fixture())
            {
                var suppliers = new SqliteSupplierRepository(f.Cs);
                var supplierId = suppliers.Add(new Supplier { Code = "MOVE-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = "مورد نقل", Phone = "01000000000", Status = "نشط" });
                var service = new PurchaseService(new SqlitePurchaseRepository(f.Cs), suppliers);
                var draft = await service.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "MOVE-INV-1", InvoiceDate = "2026-10-24", PaymentMode = "credit", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "2", PurchaseUnitPrice = "10.1250", SaleUnitPrice = "15" } } }, 1, CancellationToken.None);
                Assert.True((await service.PostAsync(draft.Id, "move-purchase-post", 1, CancellationToken.None)).Succeeded);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    c.Execute("INSERT INTO Warehouses(Code,NormalizedCode,Name,IsActive,IsDefault,CreatedUtc,UpdatedUtc) VALUES('RETURN','RETURN','مخزن الإرجاع',1,0,'2026-10-24','2026-10-24')");
                }
                var transfer = f.Command("transfer", "purchase-transfer-1", "1", 1, 2);
                Assert.True((await f.Store.PostAsync(transfer, AlKhayal.Application.Inventory.InventoryService.CanonicalHashPost(transfer), CancellationToken.None)).Succeeded);
                var eligible = (await service.ReturnableLinesAsync(draft.Id, 2, CancellationToken.None)).Single();
                Assert.Equal("1", eligible.AvailableQuantity);
                var returned = await service.PostReturnAsync(draft.Id, new PurchaseReturnCommand { WarehouseId = 2, ReturnMode = "supplier_credit", Lines = new System.Collections.Generic.List<PurchaseReturnLine> { new PurchaseReturnLine { PurchaseInvoiceLineId = eligible.PurchaseInvoiceLineId, Quantity = "1" } } }, "move-return-1", 1, CancellationToken.None);
                Assert.True(returned.Succeeded, returned.ErrorCode + ": " + returned.ErrorMessage);
                Assert.Equal(101250, returned.Lines.Single().AccountAmountScaled);
                Assert.Equal(101250, returned.Lines.Single().InventoryValueScaled);
                Assert.Equal("0", (await service.ReturnableLinesAsync(draft.Id, 2, CancellationToken.None)).Single().AvailableQuantity);
                using (var c = new SQLiteConnection(f.Cs)) { c.Open(); Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check")); }
            }
        }

        [Fact]
        public async Task Serial_tracked_purchase_return_requires_and_retires_an_original_available_serial()
        {
            using (var f = new Fixture())
            {
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@Id,'serial-numbers',1,'2026-10-24','2026-10-24',1)", new { Id = f.ProductId });
                }
                var suppliers = new SqliteSupplierRepository(f.Cs);
                var supplierId = suppliers.Add(new Supplier { Code = "SN-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = "مورد أجهزة", Phone = "01000000000", Status = "نشط" });
                var service = new PurchaseService(new SqlitePurchaseRepository(f.Cs), suppliers);
                var draft = await service.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "SN-INV-1", InvoiceDate = "2026-10-24", PaymentMode = "credit", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "2", PurchaseUnitPrice = "10", SaleUnitPrice = "15", PhysicalUnits = new System.Collections.Generic.List<PurchasePhysicalUnit> { new PurchasePhysicalUnit { Serial = "DEVICE-A" }, new PurchasePhysicalUnit { Serial = "DEVICE-B" } } } } }, 1, CancellationToken.None);
                Assert.True((await service.PostAsync(draft.Id, "sn-purchase-post", 1, CancellationToken.None)).Succeeded);
                var eligible = (await service.ReturnableLinesAsync(draft.Id, 1, CancellationToken.None)).Single();
                Assert.Equal(new[] { "DEVICE-A", "DEVICE-B" }, eligible.PhysicalUnits.Select(x => x.Serial).OrderBy(x => x).ToArray());
                var invalid = await service.PostReturnAsync(draft.Id, new PurchaseReturnCommand { WarehouseId = 1, ReturnMode = "supplier_credit", Lines = new System.Collections.Generic.List<PurchaseReturnLine> { new PurchaseReturnLine { PurchaseInvoiceLineId = eligible.PurchaseInvoiceLineId, Quantity = "1", PhysicalUnits = new System.Collections.Generic.List<PurchasePhysicalUnit> { new PurchasePhysicalUnit { Serial = "UNRELATED" } } } } }, "sn-return-bad", 1, CancellationToken.None);
                Assert.False(invalid.Succeeded); Assert.Equal("return_serial_mismatch", invalid.ErrorCode);
                var valid = await service.PostReturnAsync(draft.Id, new PurchaseReturnCommand { WarehouseId = 1, ReturnMode = "supplier_credit", Lines = new System.Collections.Generic.List<PurchaseReturnLine> { new PurchaseReturnLine { PurchaseInvoiceLineId = eligible.PurchaseInvoiceLineId, Quantity = "1", PhysicalUnits = new System.Collections.Generic.List<PurchasePhysicalUnit> { eligible.PhysicalUnits[1] } } } }, "sn-return-good", 1, CancellationToken.None);
                Assert.True(valid.Succeeded, valid.ErrorCode + ": " + valid.ErrorMessage);
                Assert.Equal(new[] { "DEVICE-A" }, (await service.ReturnableLinesAsync(draft.Id, 1, CancellationToken.None)).Single().PhysicalUnits.Select(x => x.Serial).ToArray());
            }
        }
    }
}
