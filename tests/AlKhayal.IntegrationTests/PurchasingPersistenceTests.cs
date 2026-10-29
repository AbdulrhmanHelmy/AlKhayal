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
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Customers;
using AlKhayal.Application.Features;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Suppliers;
using AlKhayal.Data.BusinessActivities;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Features;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class InventoryPersistenceTests
    {
        [Fact]
        public async Task Realistic_disposable_financial_dataset_reconciles_account_boundaries_and_preserves_fifo_residuals()
        {
            using (var f = new Fixture())
            {
                var customers = new CustomerService(new SqliteCustomerRepository(f.Cs));
                var customerAccounts = new CustomerAccountService(new SqliteCustomerAccountRepository(f.Cs));
                var firstCustomer = customers.Create(new Customer { Code="C-REAL-1", Name="عميل القاهرة", Phone="01000000001", Balance=100.50m, CreditLimit=1000m, Status="نشط" });
                var secondCustomer = customers.Create(new Customer { Code="C-REAL-2", Name="عميل الجيزة", Phone="01000000002", Balance=0m, CreditLimit=500m, Status="نشط" });
                customerAccounts.PostTransaction(firstCustomer.Id,"collection",0.10m,"2026-10-24","تحصيل","real-customer-1");
                customerAccounts.PostTransaction(secondCustomer.Id,"increase",0.99m,"2026-10-24","تسوية مدينة","real-customer-2");

                var supplierService = new SupplierService(new SqliteSupplierRepository(f.Cs));
                var firstSupplier = supplierService.Create(new Supplier { Code="S-REAL-1", Name="مورد الإسكندرية", Phone="01000000003", Payable=0m, Status="نشط" });
                var secondSupplier = supplierService.Create(new Supplier { Code="S-REAL-2", Name="مورد الصعيد", Phone="01000000004", Payable=0.01m, Status="نشط" });

                var secondProduct = await new CatalogService(new SqliteCatalogStore(f.Cs)).CreateProductAsync(new CreateProductCommand { OperationKey="real-product-2", Name="بن موزون", BaseUnitId=1, ProductType="stock", SalePrice="15.75", PurchasePrice="10.10", IsActive=true }, CancellationToken.None);
                Assert.True(secondProduct.Succeeded, secondProduct.ErrorMessage);
                long secondVariant, secondUnit;
                using (var c = new SQLiteConnection(f.Cs)) { c.Open(); secondVariant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=secondProduct.Value.Id}); secondUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=secondProduct.Value.Id}); }

                var purchaseService = new PurchaseService(new SqlitePurchaseRepository(f.Cs), new SqliteSupplierRepository(f.Cs));
                var draft = await purchaseService.CreateDraftAsync(new PurchaseDraftCommand { SupplierId=firstSupplier.Id, WarehouseId=1, InvoiceNumber="REAL-INV-1", InvoiceDate="2026-10-24", PaymentMode="credit", Lines=new System.Collections.Generic.List<PurchaseInvoiceLine> {
                    new PurchaseInvoiceLine { VariantId=f.VariantId, ProductUnitId=f.UnitId, Quantity="0.5", PurchaseUnitPrice="0.01", SaleUnitPrice="0.10" },
                    new PurchaseInvoiceLine { VariantId=secondVariant, ProductUnitId=secondUnit, Quantity="0.125", PurchaseUnitPrice="10.10", SaleUnitPrice="15.75" }
                } }, 1, CancellationToken.None);
                Assert.Equal(12700, draft.TotalScaled);
                var posted = await purchaseService.PostAsync(draft.Id,"real-post-1",1,CancellationToken.None); Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.True((await purchaseService.PostAsync(draft.Id,"real-post-1",1,CancellationToken.None)).Replayed);
                var cashDraft = await purchaseService.CreateDraftAsync(new PurchaseDraftCommand { SupplierId=secondSupplier.Id, WarehouseId=1, InvoiceNumber="REAL-CASH-1", InvoiceDate="2026-10-24", PaymentMode="cash", TreasuryAccountId=await FundPurchaseAccount(f), Lines=new System.Collections.Generic.List<PurchaseInvoiceLine> {
                    new PurchaseInvoiceLine { VariantId=f.VariantId, ProductUnitId=f.UnitId, Quantity="1", PurchaseUnitPrice="0.99", SaleUnitPrice="1.25" }
                } }, 1, CancellationToken.None);
                Assert.Equal(9900, cashDraft.TotalScaled);
                Assert.True((await purchaseService.PostAsync(cashDraft.Id,"real-cash-post-1",1,CancellationToken.None)).Succeeded);

                var ledger = new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.Cs));
                await ledger.AddPaymentAsync(firstSupplier.Id,null,draft.Id,"0.10","2026-10-24","نقدي",null,"سداد جزئي","real-pay-1",CancellationToken.None);
                var eligible=(await purchaseService.ReturnableLinesAsync(draft.Id,1,CancellationToken.None)).Single(x=>x.PurchaseInvoiceLineId==draft.Lines[0].Id);
                var returned=await purchaseService.PostReturnAsync(draft.Id,new PurchaseReturnCommand { WarehouseId=1,ReturnMode="cash_refund",Lines=new System.Collections.Generic.List<PurchaseReturnLine>{new PurchaseReturnLine{PurchaseInvoiceLineId=eligible.PurchaseInvoiceLineId,Quantity="0.5"}}},"real-return-1",1,CancellationToken.None);
                Assert.True(returned.Succeeded,returned.ErrorMessage); Assert.Equal(100,returned.Lines.Single().AccountAmountScaled); Assert.Equal(50,returned.Lines.Single().InventoryValueScaled); Assert.Equal(50,returned.Lines.Single().VarianceScaled);

                var features=new FeatureService(new SqliteFeatureStore(f.Cs),new SqliteBusinessActivityStore(f.Cs));
                Assert.True((await features.SetAsync(new SetFeatureCommand{Key="multiple-warehouses",Kind=FeatureKinds.Module,IsEnabled=true,ChangedByUserId=1},CancellationToken.None)).Succeeded);
                var secondWarehouse=await f.Store.SaveWarehouseAsync(new WarehouseCommand{Name="مخزن الاختبار الثاني"},true,CancellationToken.None); Assert.True(secondWarehouse.Succeeded);
                Assert.True((await f.InventoryService.PostAsync(f.Command("manual_receipt","real-layer-1","2",cost:"20"),CancellationToken.None)).Succeeded);
                Assert.True((await f.InventoryService.PostAsync(f.Command("manual_receipt","real-layer-2","2",cost:"30"),CancellationToken.None)).Succeeded);
                long valueBeforeTransfer;
                using (var c = new SQLiteConnection(f.Cs)) { c.Open(); valueBeforeTransfer=c.ExecuteScalar<long>("SELECT SUM(RemainingValueScaled) FROM InventoryCostLayers"); }
                Assert.Equal(1022525, valueBeforeTransfer);
                Assert.True((await f.InventoryService.PostAsync(f.Command("transfer","real-transfer","1",1,secondWarehouse.Value.Id,null),CancellationToken.None)).Succeeded);
                using (var c = new SQLiteConnection(f.Cs)) { c.Open(); Assert.Equal(valueBeforeTransfer,c.ExecuteScalar<long>("SELECT SUM(RemainingValueScaled) FROM InventoryCostLayers")); }
                var count=await f.Store.CreateCountAsync(1,1,null,CancellationToken.None); Assert.True(count.Succeeded);
                var countLines=count.Value.Lines.Select(x=>new InventoryCountLine{VariantId=x.VariantId,BatchId=x.BatchId,RecordedQuantityScaled=x.RecordedQuantityScaled,CountedQuantity=(x.RecordedQuantityScaled/1000000m).ToString(System.Globalization.CultureInfo.InvariantCulture),BalanceVersion=x.BalanceVersion}).ToArray();
                Assert.True((await f.InventoryService.PostCountAsync(count.Value.Id,new InventoryCountPostCommand{OperationKey="real-count",UserId=1,Lines=countLines},CancellationToken.None)).Succeeded);

                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(12675, c.ExecuteScalar<long>("SELECT SUM(l.ValueScaled) FROM InventoryCostLayers l JOIN StockMovements m ON m.Id=l.OriginMovementId JOIN InventoryDocuments d ON d.Id=m.DocumentId WHERE l.Basis='receipt' AND d.Notes LIKE @Note",new{Note="%purchaseInvoiceId="+draft.Id+"%"}));
                    Assert.Equal(11600, c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM SupplierLedgerEntries WHERE SupplierId=@Id",new{Id=firstSupplier.Id}));
                    Assert.Equal(1.16m, c.ExecuteScalar<decimal>("SELECT Payable FROM Suppliers WHERE Id=@Id",new{Id=firstSupplier.Id}));
                    Assert.Equal(100.40m, c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=firstCustomer.Id}));
                    Assert.Equal(0.99m, c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=secondCustomer.Id}));
                    Assert.Equal(0L, c.ExecuteScalar<long>("SELECT SUM(AmountScaled)-11600 FROM SupplierLedgerEntries WHERE SupplierId=@Id",new{Id=firstSupplier.Id}));
                    Assert.Equal(0L, c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM SupplierLedgerEntries WHERE PurchaseInvoiceId=@Id",new{Id=cashDraft.Id}));
                    Assert.Equal(valueBeforeTransfer,c.ExecuteScalar<long>("SELECT SUM(RemainingValueScaled) FROM InventoryCostLayers"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
                AlKhayal.Data.Database.Migrator.Run(f.Cs);
            }
        }

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
                    Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "2.5", PurchaseUnitPrice = "3.33", SaleUnitPrice = "5.25" } }
                }, 1, CancellationToken.None);
                Assert.Equal("draft", draft.Status); Assert.Equal(0, suppliers.GetById(supplierId).Payable);
                var reopened = await service.GetAsync(draft.Id, CancellationToken.None);
                Assert.Equal("2.5", reopened.Lines.Single().Quantity); Assert.Equal("3.33", reopened.Lines.Single().PurchaseUnitPrice);
                var posted = await service.PostAsync(draft.Id, "purch-test-1", 1, CancellationToken.None);
                Assert.True(posted.Succeeded, posted.ErrorCode + ": " + posted.ErrorMessage);
                Assert.Equal("posted", posted.Invoice.Status); Assert.False(posted.Replayed);
                Assert.Equal(8.33m, suppliers.GetById(supplierId).Payable);
                var replay = await service.PostAsync(draft.Id, "purch-test-1", 1, CancellationToken.None);
                Assert.True(replay.Succeeded); Assert.True(replay.Replayed);
                Assert.Equal(8.33m, suppliers.GetById(supplierId).Payable);
                var another = await service.CreateDraftAsync(new PurchaseDraftCommand
                {
                    SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "INV-PH4-OTHER", InvoiceDate = "2026-10-24", PaymentMode = "credit",
                    Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "1", PurchaseUnitPrice = "3.33", SaleUnitPrice = "5.25" } }
                }, 1, CancellationToken.None);
                Assert.Equal("operation_key_conflict", (await service.PostAsync(another.Id, "purch-test-1", 1, CancellationToken.None)).ErrorCode);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments WHERE DocumentType='manual_receipt' AND Status='posted' AND Notes LIKE @Note", new { Note = "%purchaseInvoiceId=" + draft.Id + "%" }));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers WHERE Basis='receipt'"));
                    Assert.Equal(83250, c.ExecuteScalar<long>("SELECT ValueScaled FROM InventoryCostLayers WHERE Basis='receipt'"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE PurchaseInvoiceId=@Id AND EntryType='purchase'", new { Id = draft.Id }));
                    Assert.Equal(34,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await f.Store.VerifyProjectionAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task Cash_purchase_creates_equal_payable_and_payment_entries_with_treasury_withdrawal()
        {
            using (var f = new Fixture())
            {
                var suppliers = new SqliteSupplierRepository(f.Cs);
                var supplierId = suppliers.Add(new Supplier { Code = "CASH-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = "مورد نقدي", Phone = "01000000000", Status = "نشط" });
                var service = new PurchaseService(new SqlitePurchaseRepository(f.Cs), suppliers);
                var draft = await service.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "CASH-1", InvoiceDate = "2026-10-24", PaymentMode = "cash", TreasuryAccountId = await FundPurchaseAccount(f), Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "1", PurchaseUnitPrice = "10.13", SaleUnitPrice = "15" } } }, 1, CancellationToken.None);
                var result = await service.PostAsync(draft.Id, "cash-post-1", 1, CancellationToken.None);
                Assert.True(result.Succeeded, result.ErrorMessage);
                Assert.Equal(0m, suppliers.GetById(supplierId).Payable);
                using (var c = new SQLiteConnection(f.Cs))
                {
                    c.Open();
                    Assert.Equal(new long[] { -101300, 101300 }, c.Query<long>("SELECT AmountScaled FROM SupplierLedgerEntries WHERE PurchaseInvoiceId=@Id ORDER BY AmountScaled", new { Id = draft.Id }).ToArray());
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
                var draft = await service.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "RET-INV-1", InvoiceDate = "2026-10-24", PaymentMode = "credit", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "2", PurchaseUnitPrice = "10.13", SaleUnitPrice = "15" } } }, 1, CancellationToken.None);
                Assert.True((await service.PostAsync(draft.Id, "ret-purchase-post", 1, CancellationToken.None)).Succeeded);
                var eligible = (await service.ReturnableLinesAsync(draft.Id, 1, CancellationToken.None)).Single();
                Assert.Equal("2", eligible.AvailableQuantity);
                var command = new PurchaseReturnCommand { WarehouseId = 1, ReturnMode = "cash_refund", Lines = new System.Collections.Generic.List<PurchaseReturnLine> { new PurchaseReturnLine { PurchaseInvoiceLineId = eligible.PurchaseInvoiceLineId, Quantity = "1" } } };
                var returned = await service.PostReturnAsync(draft.Id, command, "ret-op-1", 1, CancellationToken.None);
                Assert.True(returned.Succeeded, returned.ErrorCode + ": " + returned.ErrorMessage);
                Assert.Equal("cash_refund", returned.ReturnMode);
                Assert.Equal(101300, returned.Lines.Single().AccountAmountScaled);
                Assert.Equal(returned.Lines.Single().AccountAmountScaled, returned.Lines.Single().InventoryValueScaled);
                Assert.Equal(0, returned.Lines.Single().VarianceScaled);
                var replay = await service.PostReturnAsync(draft.Id, command, "ret-op-1", 1, CancellationToken.None);
                Assert.True(replay.Succeeded); Assert.True(replay.Replayed);
                Assert.Single(await service.ReturnsAsync(draft.Id, CancellationToken.None));
                Assert.Equal(10.13m, suppliers.GetById(supplierId).Payable);
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
                var draft = await service.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "MOVE-INV-1", InvoiceDate = "2026-10-24", PaymentMode = "credit", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.VariantId, ProductUnitId = f.UnitId, Quantity = "2", PurchaseUnitPrice = "10.13", SaleUnitPrice = "15" } } }, 1, CancellationToken.None);
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
                Assert.Equal(101300, returned.Lines.Single().AccountAmountScaled);
                Assert.Equal(101300, returned.Lines.Single().InventoryValueScaled);
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
