using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Sales;
using Dapper;
using Xunit;
using Xunit.Abstractions;

namespace AlKhayal.IntegrationTests
{
    public sealed class SalesCancellationValuationTests
    {
        private readonly ITestOutputHelper _output;
        public SalesCancellationValuationTests(ITestOutputHelper output){_output=output;}
        private static string DisposablePath()
        {
            var path=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"alkhayal-stabilization-"+Guid.NewGuid().ToString("N")+".db"));
            var real=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AlKhayal","alkhayal.db"));
            Assert.False(string.Equals(path,real,StringComparison.OrdinalIgnoreCase));
            Assert.False(path.StartsWith(Path.GetDirectoryName(real)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(path));
            return path;
        }
        private static async Task<(long product,long variant,long unit,long customer)> Seed(string cs)
        {
            Migrator.Run(cs);
            using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('stabilization-admin','test-only','2026-01-01T00:00:00Z')");c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('C-STAB','عميل اختبار','0100','',0,1000,'أساسي','نشط','2026-01-01','2026-01-01')");}
            var catalog=new CatalogService(new SqliteCatalogStore(cs));var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="stabilization-product",Name="صنف تقييم",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(product.Succeeded,product.ErrorMessage);
            using(var c=new SQLiteConnection(cs)){c.Open();return(product.Value.Id,c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id}),c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id}),c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='C-STAB'"));}
        }
        private static async Task Receipt(SqliteInventoryStore inventory,long variant,long unit,string key,string quantity,string cost,long warehouse=1)
        {var result=await inventory.PostAsync(new InventoryPostCommand{OperationKey=key,Type="manual_receipt",WarehouseId=warehouse,UserId=1,Reason="توريد اختبار",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity=quantity,UnitCost=cost,SalePrice="12"}}},"v2:"+key,CancellationToken.None);Assert.True(result.Succeeded,result.ErrorMessage);}
        private static Task<SaleInvoice> Draft(SalesService sales,long variant,long unit,long? customer,string mode,string quantity="1")
        {return sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode=mode,Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity=quantity,UnitPrice="12"}}},1,CancellationToken.None);}
        [Fact]
        public async Task Pos_browse_paginates_net_sales_categories_search_and_warehouse_stock()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                var ids=await Seed(cs);var catalog=new CatalogService(new SqliteCatalogStore(cs));
                var category=(await catalog.SaveCategoryAsync(new SaveCategoryCommand{Name="تصنيف POS",IsActive=true},CancellationToken.None)).Value;
                var second=(await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="pos-second",Name="صنف ب",CategoryId=category.Id,BaseUnitId=1,ProductType="stock",SalePrice="14",IsActive=true},CancellationToken.None)).Value;
                var zero=(await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="pos-zero",Name="صنف ج",CategoryId=category.Id,BaseUnitId=1,ProductType="stock",SalePrice="8",IsActive=true},CancellationToken.None)).Value;
                long secondVariant,secondUnit;using(var c=new SQLiteConnection(cs)){c.Open();secondVariant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=second.Id});secondUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=second.Id});}
                var inventory=new SqliteInventoryStore(cs);await Receipt(inventory,ids.variant,ids.unit,"pos-first-stock","10","9");await Receipt(inventory,secondVariant,secondUnit,"pos-second-stock","10","10");
                var sales=new SalesService(new SqliteSalesStore(cs));var today=DateTime.UtcNow.ToString("yyyy-MM-dd");
                var firstDraft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=ids.customer,WarehouseId=1,InvoiceDate=today,PaymentMode="credit",Lines=new(){new SaleLine{VariantId=ids.variant,ProductUnitId=ids.unit,Quantity="4",UnitPrice="12"}}},1,CancellationToken.None);
                var firstPost=await sales.PostAsync(firstDraft.Id,"pos-first-post",1,CancellationToken.None);Assert.True(firstPost.Succeeded,firstPost.ErrorMessage);
                var secondDraft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=ids.customer,WarehouseId=1,InvoiceDate=today,PaymentMode="credit",Lines=new(){new SaleLine{VariantId=secondVariant,ProductUnitId=secondUnit,Quantity="2",UnitPrice="12"}}},1,CancellationToken.None);
                Assert.True((await sales.PostAsync(secondDraft.Id,"pos-second-post",1,CancellationToken.None)).Succeeded);
                var returned=await sales.ReturnAsync(firstDraft.Id,new SaleReturnCommand{OperationKey="pos-return",Reason="مرتجع اختبار",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=firstPost.Invoice.Lines.Single().Id,Quantity="3"}}},1,CancellationToken.None);Assert.True(returned.Succeeded,returned.ErrorMessage);
                var oldDraft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=ids.customer,WarehouseId=1,InvoiceDate=DateTime.UtcNow.AddDays(-45).ToString("yyyy-MM-dd"),PaymentMode="credit",Lines=new(){new SaleLine{VariantId=ids.variant,ProductUnitId=ids.unit,Quantity="5",UnitPrice="12"}}},1,CancellationToken.None);
                Assert.True((await sales.PostAsync(oldDraft.Id,"pos-old-post",1,CancellationToken.None)).Succeeded);
                var watch=Stopwatch.StartNew();var first=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,IsActive=true,Page=1,PageSize=1},CancellationToken.None);watch.Stop();_output.WriteLine("POS browse, three products/two invoices/one return: {0} ms",watch.Elapsed.TotalMilliseconds.ToString("0.000"));
                Assert.Equal(3,first.Value.TotalCount);Assert.Equal(second.Id,first.Value.Items.Single().Id);Assert.Equal(8000000,first.Value.Items.Single().AvailableQuantityScaled);
                var next=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,IsActive=true,Page=2,PageSize=1},CancellationToken.None);Assert.Equal(ids.product,next.Value.Items.Single().Id);
                var last=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,IsActive=true,Page=3,PageSize=1},CancellationToken.None);Assert.Equal(zero.Id,last.Value.Items.Single().Id);
                var categoryPage=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,CategoryId=category.Id,IsActive=true,Page=1,PageSize=10},CancellationToken.None);Assert.Equal(2,categoryPage.Value.TotalCount);Assert.Equal(second.Id,categoryPage.Value.Items[0].Id);
                var searched=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,CategoryId=category.Id,Search="صنف ج",IsActive=true,Page=1,PageSize=10},CancellationToken.None);Assert.Single(searched.Value.Items);Assert.Equal(zero.Id,searched.Value.Items[0].Id);
                var warehouse=await inventory.SaveWarehouseAsync(new WarehouseCommand{Code="POS-W2",Name="مخزن POS ثان",IsActive=true},true,CancellationToken.None);Assert.True(warehouse.Succeeded);
                var inOtherWarehouse=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=warehouse.Value.Id,IsActive=true,Page=1,PageSize=10},CancellationToken.None);Assert.All(inOtherWarehouse.Value.Items,x=>Assert.Equal(0,x.AvailableQuantityScaled));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Pos_browse_keeps_fifteen_thousand_products_server_side()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();using(var tx=c.BeginTransaction())
                    {
                        c.Execute(@"WITH RECURSIVE numbers(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM numbers WHERE n<15000)
                            INSERT INTO Products(Name,Price) SELECT 'POS-'||printf('%05d',n),1 FROM numbers",transaction:tx);
                        c.Execute(@"INSERT INTO ProductDetails(ProductId,BaseUnitId,ProductType,SellPriceScaled,IsActive,CreatedUtc,UpdatedUtc)
                            SELECT Id,1,'stock',10000,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z' FROM Products",transaction:tx);
                        c.Execute(@"INSERT INTO ProductVariants(ProductId,Name,NormalizedName,IsDefault,IsActive,CreatedUtc,UpdatedUtc)
                            SELECT Id,Name,Name,1,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z' FROM Products",transaction:tx);
                        c.Execute(@"INSERT INTO ProductUnits(ProductId,UnitId,ToBaseNumerator,ToBaseDenominator,IsBase,IsSaleUnit,IsPurchaseUnit,IsActive,Version,CreatedUtc,UpdatedUtc)
                            SELECT Id,1,1,1,1,1,1,1,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z' FROM Products",transaction:tx);
                        tx.Commit();
                    }
                }
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var watch=Stopwatch.StartNew();var first=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,IsActive=true,Page=1,PageSize=40},CancellationToken.None);watch.Stop();_output.WriteLine("POS browse of 15,000 sellable products, first 40 results: {0} ms",watch.Elapsed.TotalMilliseconds.ToString("0.000"));
                Assert.Equal(15000,first.Value.TotalCount);Assert.Equal(40,first.Value.Items.Count);Assert.Equal("POS-00001",first.Value.Items[0].Name);
                var last=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,IsActive=true,Page=375,PageSize=40},CancellationToken.None);Assert.Equal(40,last.Value.Items.Count);Assert.Equal("POS-15000",last.Value.Items[39].Name);
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO InventoryBalances(WarehouseId,VariantId,QuantityBaseScaled,ValueScaled,Version,UpdatedUtc) SELECT 1,Id,0,0,1,'2026-01-01T00:00:00Z' FROM ProductVariants");}
                var inventory=new SqliteInventoryStore(cs);watch.Restart();var balances=await inventory.BalancePageAsync(1,"POS-15000",1,25,CancellationToken.None);watch.Stop();
                _output.WriteLine("Inventory search of 15,000 balance rows: {0} ms",watch.Elapsed.TotalMilliseconds.ToString("0.000"));
                Assert.Equal(1,balances.TotalCount);Assert.Equal("POS-15000",balances.Items.Single().ProductName);
                var firstBalances=await inventory.BalancePageAsync(1,"",1,25,CancellationToken.None);Assert.Equal(15000,firstBalances.TotalCount);Assert.Equal(25,firstBalances.Items.Count);
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Converted_percentage_discount_posts_once_with_tax_and_customer_balance()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                var ids=await Seed(cs);var inventory=new SqliteInventoryStore(cs);await Receipt(inventory,ids.variant,ids.unit,"discount-stock","10","9");
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=15,PricesIncludeTax=0 WHERE Id=1");}
                var sales=new SalesService(new SqliteSalesStore(cs));var draft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=ids.customer,WarehouseId=1,InvoiceDate=DateTime.UtcNow.ToString("yyyy-MM-dd"),PaymentMode="credit",Discount="15.00",Lines=new(){new SaleLine{VariantId=ids.variant,ProductUnitId=ids.unit,Quantity="10",UnitPrice="12"}}},1,CancellationToken.None);
                var posted=await sales.PostAsync(draft.Id,"percentage-discount-post",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.Equal(150000,posted.Invoice.DiscountScaled);Assert.Equal(157500,posted.Invoice.TaxScaled);Assert.Equal(1207500,posted.Invoice.TotalScaled);
                var retry=await sales.PostAsync(draft.Id,"percentage-discount-post",1,CancellationToken.None);Assert.True(retry.Succeeded);Assert.Equal(draft.Id,retry.Invoice.Id);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(120.75m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=ids.customer}));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE SalesInvoiceId=@Id AND TransactionType='increase'",new{Id=draft.Id}));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Draft_deletion_is_idempotent_and_never_changes_stock()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try{var ids=await Seed(cs);var inventory=new SqliteInventoryStore(cs);await Receipt(inventory,ids.variant,ids.unit,"draft-stock","3","9");var sales=new SalesService(new SqliteSalesStore(cs));var draft=await Draft(sales,ids.variant,ids.unit,null,"cash");
                var deleted=await sales.DeleteDraftAsync(draft.Id,"delete-draft",1,CancellationToken.None);Assert.True(deleted.Succeeded);Assert.True((await sales.DeleteDraftAsync(draft.Id,"delete-draft",1,CancellationToken.None)).Replayed);Assert.Equal("operation_key_conflict",(await sales.DeleteDraftAsync(draft.Id,"delete-draft",2,CancellationToken.None)).ErrorCode);Assert.Null(await sales.GetAsync(draft.Id,CancellationToken.None));Assert.DoesNotContain(await sales.ListAsync(null,null,null,CancellationToken.None),x=>x.Id==draft.Id);Assert.Null(await sales.PostAsync(draft.Id,"post-deleted",1,CancellationToken.None));
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(3000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Id",new{Id=ids.variant}));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements m JOIN InventoryDocuments d ON d.Id=m.DocumentId WHERE d.OperationKey LIKE 'sale:%'"));}}
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Unpaid_credit_cancellation_restores_multiple_fifo_layers_and_customer_ledger_atomically()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try{var ids=await Seed(cs);var inventory=new SqliteInventoryStore(cs);await Receipt(inventory,ids.variant,ids.unit,"layer-one","1","9");await Receipt(inventory,ids.variant,ids.unit,"layer-two","2","11");var sales=new SalesService(new SqliteSalesStore(cs));var draft=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit","2");Assert.True((await sales.PostAsync(draft.Id,"post-credit",1,CancellationToken.None)).Succeeded);
                var before=await inventory.ValuationAsync(1,null,CancellationToken.None);Assert.Equal("11.0000",before.CostValue);var canceled=await sales.CancelAsync(draft.Id,"cancel-credit","إلغاء قبل التحصيل",1,CancellationToken.None);Assert.True(canceled.Succeeded,canceled.ErrorMessage);Assert.Equal("canceled",canceled.Invoice.Status);Assert.True((await sales.CancelAsync(draft.Id,"cancel-credit","إلغاء قبل التحصيل",1,CancellationToken.None)).Replayed);Assert.Equal("operation_key_conflict",(await sales.CancelAsync(draft.Id,"cancel-credit","سبب مختلف",1,CancellationToken.None)).ErrorCode);Assert.Equal("already_canceled",(await sales.CancelAsync(draft.Id,"other-key","إلغاء آخر",1,CancellationToken.None)).ErrorCode);
                var after=await inventory.ValuationAsync(1,null,CancellationToken.None);Assert.Equal("31.0000",after.CostValue);Assert.Equal("36.0000",after.PotentialSellingValue);Assert.Equal("5.0000",after.EstimatedGrossMargin);Assert.True(await inventory.VerifyProjectionAsync(CancellationToken.None));var statement=new SqliteCustomerAccountRepository(cs).GetStatement(ids.customer,null,null);Assert.Contains(statement.Movements,x=>x.Reference==draft.InvoiceNumber&&x.Description=="إلغاء فاتورة بيع");
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(3000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Id",new{Id=ids.variant}));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=ids.customer}));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE SalesInvoiceId=@Id",new{Id=draft.Id}));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesCancellations WHERE SalesInvoiceId=@Id",new{Id=draft.Id}));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers WHERE SourceMovementId IN (SELECT Id FROM StockMovements WHERE DocumentId=(SELECT InventoryDocumentId FROM SalesCancellations WHERE SalesInvoiceId=@Id))",new{Id=draft.Id}));}}
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Cash_and_partially_paid_invoices_require_payment_policy_and_failed_cancellation_rolls_back()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try{var ids=await Seed(cs);var inventory=new SqliteInventoryStore(cs);await Receipt(inventory,ids.variant,ids.unit,"stock","5","9");var sales=new SalesService(new SqliteSalesStore(cs));var cash=await Draft(sales,ids.variant,ids.unit,null,"cash");Assert.True((await sales.PostAsync(cash.Id,"post-cash",1,CancellationToken.None)).Succeeded);Assert.Equal("cash_refund_policy",(await sales.CancelAsync(cash.Id,"cancel-cash","رد نقدي",1,CancellationToken.None)).ErrorCode);
                var clean=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit");Assert.True((await sales.PostAsync(clean.Id,"post-clean",1,CancellationToken.None)).Succeeded);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("CREATE TRIGGER test_cancel_failure BEFORE INSERT ON SalesCancellations BEGIN SELECT RAISE(ABORT,'test_failure'); END;");}var quantityBefore=(await inventory.BalancesAsync(1,null,CancellationToken.None)).Single().QuantityBaseScaled;var failed=await sales.CancelAsync(clean.Id,"cancel-clean","اختبار رجوع",1,CancellationToken.None);Assert.Equal("sales_conflict",failed.ErrorCode);using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(quantityBefore,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Id",new{Id=ids.variant}));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesCancellations"));c.Execute("DROP TRIGGER test_cancel_failure");}
                new SqliteCustomerAccountRepository(cs).PostTransaction(ids.customer,"collection",1m,"2026-10-25","تحصيل عام","outside-collection");Assert.Equal("possible_collection",(await sales.CancelAsync(clean.Id,"cancel-after-general","تحصيل غير مخصص",1,CancellationToken.None)).ErrorCode);
                var credit=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit");Assert.True((await sales.PostAsync(credit.Id,"post-credit",1,CancellationToken.None)).Succeeded);Assert.True((await sales.AddPaymentAsync(credit.Id,new SalePaymentCommand{Amount="5.00",Method="cash",OperationKey="collect",OccurredOn="2026-10-25"},1,CancellationToken.None)).Succeeded);Assert.Equal("linked_payments",(await sales.CancelAsync(credit.Id,"cancel-paid","إلغاء مدفوع",1,CancellationToken.None)).ErrorCode);}
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Linked_return_blocks_cancellation_and_concurrent_retry_creates_one_reversal()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try{var ids=await Seed(cs);var inventory=new SqliteInventoryStore(cs);await Receipt(inventory,ids.variant,ids.unit,"return-stock","4","9");var sales=new SalesService(new SqliteSalesStore(cs));var returnedInvoice=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit");var posted=await sales.PostAsync(returnedInvoice.Id,"return-post",1,CancellationToken.None);Assert.True(posted.Succeeded);var line=posted.Invoice.Lines.Single();var returned=await sales.ReturnAsync(returnedInvoice.Id,new SaleReturnCommand{OperationKey="return-once",Reason="مرتجع",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},1,CancellationToken.None);Assert.True(returned.Succeeded,returned.ErrorMessage);Assert.Equal("linked_returns",(await sales.CancelAsync(returnedInvoice.Id,"cancel-returned","إلغاء مرتجع",1,CancellationToken.None)).ErrorCode);
                var clean=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit");Assert.True((await sales.PostAsync(clean.Id,"clean-post",1,CancellationToken.None)).Succeeded);var attempts=await Task.WhenAll(sales.CancelAsync(clean.Id,"same-cancel","إلغاء متزامن",1,CancellationToken.None),sales.CancelAsync(clean.Id,"same-cancel","إلغاء متزامن",1,CancellationToken.None));Assert.Contains(attempts,x=>x.Succeeded);Assert.All(attempts,x=>Assert.True(x.Succeeded||x.ErrorCode=="database_busy"));using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesCancellations WHERE SalesInvoiceId=@Id",new{Id=clean.Id}));}Assert.True(await inventory.VerifyProjectionAsync(CancellationToken.None));}
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Valuation_respects_warehouse_product_and_variant_without_double_counting()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try{var ids=await Seed(cs);long secondVariant;using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO ProductVariants(ProductId,Name,NormalizedName,IsDefault,IsActive,CreatedUtc,UpdatedUtc,SellPriceOverrideScaled) VALUES(@ProductId,'لون آخر','لون آخر',0,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',200000)",new{ProductId=ids.product});secondVariant=c.ExecuteScalar<long>("SELECT last_insert_rowid()");}var inventory=new SqliteInventoryStore(cs);var warehouse=await inventory.SaveWarehouseAsync(new WarehouseCommand{Code="W2",Name="مخزن ثان",IsActive=true},true,CancellationToken.None);Assert.True(warehouse.Succeeded,warehouse.ErrorMessage);await Receipt(inventory,ids.variant,ids.unit,"w1-v1","2","9");await Receipt(inventory,secondVariant,ids.unit,"w1-v2","1","7");await Receipt(inventory,ids.variant,ids.unit,"w2-v1","1","5",warehouse.Value.Id);
                var extra=await new CatalogService(new SqliteCatalogStore(cs)).CreateProductAsync(new CreateProductCommand{OperationKey="other-product",Name="صنف آخر",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="3",IsActive=true},CancellationToken.None);Assert.True(extra.Succeeded,extra.ErrorMessage);long extraVariant,extraUnit;using(var c=new SQLiteConnection(cs)){c.Open();extraVariant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=extra.Value.Id});extraUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=extra.Value.Id});c.Execute("UPDATE ProductVariants SET SellPriceOverrideScaled=200000 WHERE Id=@Id",new{Id=secondVariant});}await Receipt(inventory,extraVariant,extraUnit,"other-stock","1","3");var watch=Stopwatch.StartNew();var first=await inventory.ValuationAsync(1,ids.product,CancellationToken.None);watch.Stop();_output.WriteLine("Valuation query on four balance rows (product filter): {0} ms",watch.Elapsed.TotalMilliseconds.ToString("0.000"));Assert.Equal(2,first.VariantCount);Assert.Equal("25.0000",first.CostValue);Assert.Equal("44.0000",first.PotentialSellingValue);Assert.Equal("19.0000",first.EstimatedGrossMargin);var all=await inventory.ValuationAsync(1,null,CancellationToken.None);Assert.Equal("28.0000",all.CostValue);Assert.Equal("56.0000",all.PotentialSellingValue);var second=await inventory.ValuationAsync(warehouse.Value.Id,ids.product,CancellationToken.None);Assert.Equal(1,second.VariantCount);Assert.Equal("5.0000",second.CostValue);Assert.Equal("12.0000",second.PotentialSellingValue);Assert.True(await inventory.VerifyProjectionAsync(CancellationToken.None));}
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
    }
}
