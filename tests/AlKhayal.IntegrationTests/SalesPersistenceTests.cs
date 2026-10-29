using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Features;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Data.BusinessActivities;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Database;
using AlKhayal.Data.Features;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Sales;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SalesPersistenceTests
    {
        private const string NoCancellation = "";
        [Fact]
        public async Task Saved_draft_can_be_resubmitted_with_a_changed_quantity_before_posting()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('draft-admin','test-only','2026-01-01T00:00:00Z')");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="draft-product",Name="مسودة قابلة للتعديل",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(product.Succeeded,product.ErrorMessage);
                long variant,unit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});}
                var inventory=new SqliteInventoryStore(cs);var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="draft-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="5",UnitCost="9",SalePrice="12"}}},"draft-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var service=new SalesService(new SqliteSalesStore(cs));var draft=await service.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Discount="0.00",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12",Discount="0.00"}}},1,CancellationToken.None);
                draft.Lines.Single().Quantity="2";
                var updated=await service.UpdateDraftAsync(draft.Id,new SaleDraftCommand{WarehouseId=draft.WarehouseId,InvoiceDate=draft.InvoiceDate,PaymentMode=draft.PaymentMode,Discount="0.00",Lines=draft.Lines},1,CancellationToken.None);
                Assert.Equal("2",updated.Lines.Single().Quantity);Assert.Equal(240000,updated.TotalScaled);
                var posted=await service.PostAsync(updated.Id,"updated-draft-post",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal("2",posted.Invoice.Lines.Single().Quantity);
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Credit_sale_payment_and_fifo_return_update_customer_and_stock_atomically()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('return-admin','test-only','2026-01-01T00:00:00Z')");c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('C-RET','عميل مرتجعات','0100','',0,1000,'أساسي','نشط','2026-01-01','2026-01-01')");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var created=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="return-product",Name="صنف إرجاع",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(created.Succeeded,created.ErrorMessage);
                long variant,unit,customer;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=created.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=created.Value.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='C-RET'");}
                var inventory=new SqliteInventoryStore(cs);var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="return-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="5",UnitCost="9",SalePrice="12"}}},"return-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var service=new SalesService(new SqliteSalesStore(cs));var invoice=await service.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12"}}},1,CancellationToken.None);
                var posted=await service.PostAsync(invoice.Id,"credit-sale-key",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(120000,posted.Invoice.TotalScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(120000,c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM CustomerTransactions WHERE SalesInvoiceId=@Id AND TransactionType='increase'",new{Id=invoice.Id}));Assert.Equal(12m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));}
                var paid=await service.AddPaymentAsync(invoice.Id,new SalePaymentCommand{Amount="5.00",Method="cash",OperationKey="collection-1",OccurredOn="2026-10-25"},1,CancellationToken.None);Assert.True(paid.Succeeded,paid.ErrorMessage);Assert.Equal(50000,paid.Invoice.PaidScaled);
                var paymentReplay=await service.AddPaymentAsync(invoice.Id,new SalePaymentCommand{Amount="5.00",Method="cash",OperationKey="collection-1",OccurredOn="2026-10-25"},1,CancellationToken.None);Assert.True(paymentReplay.Succeeded);Assert.True(paymentReplay.Replayed);
                // This legacy invoice-collection path records customer money only; fund the sales-returns cashbox explicitly.
                var treasury=new AlKhayal.Application.Treasury.TreasuryService(new AlKhayal.Data.Treasury.SqliteTreasuryStore(cs));
                var refundSource=await treasury.SaveAsync(null,new AlKhayal.Application.Treasury.TreasuryAccountCommand{Code="REFUND-SOURCE",Name="مرتجعات البيع",Type="cash"},1,CancellationToken.None);
                await treasury.PostAsync(new AlKhayal.Application.Treasury.TreasuryMovementCommand{AccountId=refundSource.Id,Amount="5.00",OperationKey="legacy-collection-funding"},"deposit",1,CancellationToken.None);
                var line=paid.Invoice.Lines.Single();var returned=await service.ReturnAsync(invoice.Id,new SaleReturnCommand{OperationKey="return-1",Reason="نصف الكمية",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="0.5"}}},1,CancellationToken.None);Assert.True(returned.Succeeded,returned.ErrorMessage);Assert.Equal(60000,returned.TotalScaled);Assert.Equal(0,returned.CashRefundScaled);Assert.Equal(60000,returned.CreditReductionScaled);
                var returnReplay=await service.ReturnAsync(invoice.Id,new SaleReturnCommand{OperationKey="return-1",Reason="نصف الكمية",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="0.5"}}},1,CancellationToken.None);Assert.True(returnReplay.Succeeded);Assert.True(returnReplay.Replayed);
                var over=await service.ReturnAsync(invoice.Id,new SaleReturnCommand{OperationKey="return-over",Reason="زيادة",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="0.6"}}},1,CancellationToken.None);Assert.False(over.Succeeded);Assert.Equal("return_quantity_exceeded",over.ErrorCode);
                var finalCollection=await service.AddPaymentAsync(invoice.Id,new SalePaymentCommand{Amount="1.00",Method="transfer",OperationKey="collection-final",OccurredOn="2026-10-25"},1,CancellationToken.None);Assert.True(finalCollection.Succeeded,finalCollection.ErrorMessage);Assert.Equal(60000,finalCollection.Invoice.PaidScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(4500000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant AND WarehouseId=1",new{Variant=variant}));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));Assert.Equal(60000,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=invoice.Id}));Assert.Equal(45000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));}
                var statement=new SqliteCustomerAccountRepository(cs).GetStatement(customer,null,null);Assert.Contains(statement.Movements,x=>x.Reference==invoice.InvoiceNumber&&x.Description=="فاتورة بيع");Assert.Contains(statement.Movements,x=>x.Reference==returned.ReturnNumber&&x.Description=="إرجاع فاتورة بيع");
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(34,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Concurrent_sales_cannot_consume_the_same_last_unit()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('concurrent-sales','test-only','2026-01-01T00:00:00Z')");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var created=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="concurrent-product",Name="آخر وحدة",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(created.Succeeded,created.ErrorMessage);
                long variant,unit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=created.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=created.Value.Id});}
                var inventory=new SqliteInventoryStore(cs);var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="concurrent-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد وحدة",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitCost="9",SalePrice="12"}}},"concurrent-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var service=new SalesService(new SqliteSalesStore(cs));var first=await service.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12"}}},1,CancellationToken.None);var second=await service.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12"}}},1,CancellationToken.None);
                var results=await Task.WhenAll(service.PostAsync(first.Id,"concurrent-sale-a",1,CancellationToken.None),service.PostAsync(second.Id,"concurrent-sale-b",1,CancellationToken.None));Assert.Single(results,x=>x.Succeeded);Assert.Single(results,x=>!x.Succeeded);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant AND WarehouseId=1",new{Variant=variant}));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices WHERE Status='posted'"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE EventType='cash_sale'"));}
                await FundRefundCashbox(cs);
                var posted=results.Single(x=>x.Succeeded).Invoice;var line=posted.Lines.Single();var returns=await Task.WhenAll(
                    service.ReturnAsync(posted.Id,new SaleReturnCommand{OperationKey="concurrent-return-a",Reason="إرجاع متزامن",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},1,CancellationToken.None),
                    service.ReturnAsync(posted.Id,new SaleReturnCommand{OperationKey="concurrent-return-b",Reason="إرجاع متزامن",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},1,CancellationToken.None));Assert.Single(returns,x=>x.Succeeded);Assert.Single(returns,x=>!x.Succeeded);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(1000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant AND WarehouseId=1",new{Variant=variant}));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE EventType='refund'"));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Serial_tracked_sale_and_return_restore_the_original_physical_unit()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('serial-admin','test-only','2026-01-01T00:00:00Z')");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var created=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="serial-return-product",Name="جهاز متسلسل",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(created.Succeeded,created.ErrorMessage);
                long variant,unit,productId;using(var c=new SQLiteConnection(cs)){c.Open();productId=created.Value.Id;variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=productId});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=productId});var now="2026-10-25T00:00:00Z";c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@Id,'serial-numbers',1,@Now,@Now,1),(@Id,'imei-tracking',1,@Now,@Now,1),(@Id,'batch-tracking',1,@Now,@Now,1),(@Id,'expiry-dates',1,@Now,@Now,1)",new{Id=productId,Now=now});}
                var expiry=DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");var inventory=new SqliteInventoryStore(cs);var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="serial-return-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد أجهزة",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitCost="9",SalePrice="12",LotNumber="LOT-SER-RET",ExpirationDate=expiry,PhysicalUnits=new[]{new InventoryUnitInput{Serial="SER-RET-001",Imeis=new[]{"490154203237518","356938035643809"}}}}}},"serial-return-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var service=new SalesService(new SqliteSalesStore(cs));var invoice=await service.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12",BatchLotNumber="LOT-SER-RET",ExpirationDate=expiry,PhysicalUnits=new(){new SaleTrackingUnit{Serial="SER-RET-001",Imeis=new(){"490154203237518","356938035643809"}}}}}},1,CancellationToken.None);
                var posted=await service.PostAsync(invoice.Id,"serial-sale-key",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Single(posted.Invoice.Lines.Single().AvailableTrackingUnits);
                await FundRefundCashbox(cs);
                var line=posted.Invoice.Lines.Single();var returned=await service.ReturnAsync(invoice.Id,new SaleReturnCommand{OperationKey="serial-return-key",Reason="استبدال جهاز",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1",Serials=new(){"SER-RET-001"}}}},1,CancellationToken.None);Assert.True(returned.Succeeded,returned.ErrorMessage);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryPhysicalUnits WHERE WarehouseId=1 AND Id=(SELECT PhysicalUnitId FROM InventoryIdentifiers WHERE NormalizedValue='SER-RET-001')"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturnPhysicalUnits WHERE Serial='SER-RET-001'"));Assert.Equal(1000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Sale_draft_posting_is_atomic_fifo_costed_and_idempotent()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('sales-admin','test-only','2026-01-01T00:00:00Z')");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="sales-product",Name="اختبار",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(product.Succeeded,product.ErrorMessage);
                long variant,unit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});}
                var inv=new SqliteInventoryStore(cs);var receipt=await inv.PostAsync(new InventoryPostCommand{OperationKey="sales-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد اختبار",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="10",UnitCost="9",SalePrice="12"}}},"v2:sales-receipt",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=15,PricesIncludeTax=0 WHERE Id=1");}
                var svc=new SalesService(new SqliteSalesStore(cs));var draft=await svc.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Discount="1",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="2.5",UnitPrice="12",Discount="0"}}},1,CancellationToken.None);
                Assert.Equal(333500,draft.TotalScaled);Assert.Equal(43500,draft.TaxScaled);Assert.Equal("draft",draft.Status);
                var posted=await svc.PostAsync(draft.Id,"sales-op-1",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal("posted",posted.Invoice.Status);Assert.Equal(333500,posted.Invoice.PaidScaled);Assert.Equal(333500,posted.Invoice.Lines.Single().LineTotalScaled);Assert.Equal(43500,posted.Invoice.Lines.Single().TaxScaled);
                var replay=await svc.PostAsync(draft.Id,"sales-op-1",1,CancellationToken.None);Assert.True(replay.Succeeded);Assert.Equal(draft.InvoiceNumber,replay.Invoice.InvoiceNumber);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(7500000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(225000,c.ExecuteScalar<long>("SELECT SUM(a.ValueScaled) FROM InventoryCostAllocations a JOIN StockMovements m ON m.Id=a.MovementId JOIN InventoryDocumentLines dl ON dl.Id=m.DocumentLineId JOIN InventoryDocuments d ON d.Id=dl.DocumentId WHERE d.DocumentType='manual_issue'"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices WHERE Status='posted'"));}
                var oversell=await svc.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="100",UnitPrice="12",Discount="0"}}},1,CancellationToken.None);
                var failed=await svc.PostAsync(oversell.Id,"sales-op-oversell",1,CancellationToken.None);Assert.False(failed.Succeeded);Assert.Equal("draft",(await svc.GetAsync(oversell.Id,CancellationToken.None)).Status);
                var rollback=await svc.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12",Discount="0"}}},1,CancellationToken.None);long before;
                using(var c=new SQLiteConnection(cs)){c.Open();before=c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant});c.Execute("CREATE TRIGGER test_fail_sale_audit BEFORE INSERT ON SalesInvoiceAudit WHEN NEW.EventType='posted' BEGIN SELECT RAISE(ABORT,'test_failure'); END;");}
                var rolledBack=await svc.PostAsync(rollback.Id,"sales-op-rollback",1,CancellationToken.None);Assert.False(rolledBack.Succeeded);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(before,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Null(c.ExecuteScalar<long?>("SELECT InventoryDocumentId FROM SalesInvoices WHERE Id=@Id",new{Id=rollback.Id}));c.Execute("DROP TRIGGER test_fail_sale_audit;");}
                var recovered=await svc.PostAsync(rollback.Id,"sales-op-rollback",1,CancellationToken.None);Assert.True(recovered.Succeeded,recovered.ErrorMessage);
                var conflict=await svc.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12",Discount="0"}}},1,CancellationToken.None);
                var conflictingReplay=await svc.PostAsync(conflict.Id,"sales-op-rollback",1,CancellationToken.None);Assert.False(conflictingReplay.Succeeded);Assert.Equal("operation_key_conflict",conflictingReplay.ErrorCode);
                Migrator.Run(cs);var reopened=new SalesService(new SqliteSalesStore(cs));var history=await reopened.ListAsync(null,null,null,CancellationToken.None);Assert.Equal(4,history.Count);Assert.Contains(history,x=>x.InvoiceNumber==draft.InvoiceNumber&&x.Status=="posted");
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        private static async Task FundRefundCashbox(string cs)
        {
            var treasury = new AlKhayal.Application.Treasury.TreasuryService(new AlKhayal.Data.Treasury.SqliteTreasuryStore(cs));
            var account = await treasury.SaveAsync(null,new AlKhayal.Application.Treasury.TreasuryAccountCommand{Code="SALES-RETURNS",Name="مرتجعات البيع",Type="cash"},1,CancellationToken.None);
            await treasury.PostAsync(new AlKhayal.Application.Treasury.TreasuryMovementCommand{AccountId=account.Id,Amount="12.00",OperationKey="refund-funding"},"deposit",1,CancellationToken.None);
        }
    }
}
