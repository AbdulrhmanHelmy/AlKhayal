using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Sales;
using AlKhayal.Core.Models;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Purchasing;
using AlKhayal.Data.Sales;
using AlKhayal.Data.Suppliers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class PricingFifoInvariantTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;

        [Fact]
        public async Task Edited_simple_price_sells_old_stock_at_new_price_with_original_fifo_costs_and_restart()
        {
            using (var f = await Fixture.Create())
            {
                await f.Receipt("batch-a", "10", "100");
                await f.Receipt("batch-b", "10", "120");
                await f.Edit("200", "999", true);
                var current = (await f.Catalog.GetProductDetailAsync(f.Product, Ct)).Value;
                Assert.Equal("200.00", current.Variants.Single().EffectiveSalePrice);
                var sale = await f.Draft("12", current.Variants.Single().EffectiveSalePrice);
                var posted = await f.Sales.PostAsync(sale.Id, "sell-twelve", 1, Ct);
                Assert.True(posted.Succeeded, posted.ErrorMessage);
                Assert.Equal(24000000, posted.Invoice.TotalScaled);
                Assert.Equal(12400000, f.Cogs(sale.Id));
                Assert.Equal(11600000, posted.Invoice.TotalScaled - f.Cogs(sale.Id));
                var before = f.Facts(sale.Id);
                await f.Edit("250", "1");
                Assert.Equal(before, f.Facts(sale.Id));
                var retry = await new SalesService(new SqliteSalesStore(f.Cs)).PostAsync(sale.Id, "sell-twelve", 1, Ct);
                Assert.True(retry.Succeeded, retry.ErrorMessage);
                Assert.Equal("200.00", retry.Invoice.Lines.Single().UnitPrice);
                Assert.Equal(before, f.Facts(sale.Id));
                Migrator.Run(f.Cs);
                var inventory = new SqliteInventoryStore(f.Cs);
                var balance = (await inventory.BalancesAsync(1, f.Product, Ct)).Single();
                Assert.Equal(8000000, balance.QuantityBaseScaled);
                Assert.Equal(9600000, balance.ValueScaled);
                await f.Edit("200", "777");
                var valuation = await inventory.ValuationAsync(1, f.Product, Ct);
                Assert.Equal("960.0000", valuation.CostValue);
                Assert.Equal("1600.0000", valuation.PotentialSellingValue);
                Assert.Equal("640.0000", valuation.EstimatedGrossMargin);
                Assert.NotEqual(1160m, decimal.Parse(valuation.EstimatedGrossMargin, System.Globalization.CultureInfo.InvariantCulture));
                using (var c = f.Open())
                {
                    Assert.Equal(new long[] {1000000, 1200000}, c.Query<long>("SELECT UnitCostScaled FROM InventoryCostLayers ORDER BY Id").ToArray());
                    Assert.Equal(new long[] {10000000, 2400000}, c.Query<long>("SELECT ValueScaled FROM InventoryCostAllocations ORDER BY Id").ToArray());
                    Assert.Equal(2, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostAllocations"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await inventory.VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task Saved_draft_keeps_its_price_when_posted_after_catalog_edit()
        {
            using (var f = await Fixture.Create())
            {
                await f.Receipt("draft-stock", "2", "100");
                var draft = await f.Draft("1", "150");
                await f.Edit("200", "500");
                Assert.Equal("150.00", (await f.Sales.GetAsync(draft.Id, Ct)).Lines.Single().UnitPrice);
                var posted = await f.Sales.PostAsync(draft.Id, "saved-draft-post", 1, Ct);
                Assert.True(posted.Succeeded, posted.ErrorMessage);
                Assert.Equal(1500000, posted.Invoice.TotalScaled);
                Assert.Equal(1000000, f.Cogs(draft.Id));
                // Existing policy: an explicit draft save validates the current price; posting does not reprice.
                await Assert.ThrowsAsync<ArgumentException>(() => f.Draft("1", "150"));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Discounts_tax_and_partial_return_keep_original_revenue_and_cost_after_price_edits(bool included)
        {
            using (var f = await Fixture.Create())
            {
                await f.Receipt("tax-stock", "10", "100");
                await f.Edit("200", "900");
                using (var c = f.Open()) c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=15,PricesIncludeTax=@Included WHERE Id=1", new {Included = included ? 1 : 0});
                var draft = await f.Sales.CreateDraftAsync(new SaleDraftCommand {CustomerId=f.Customer,WarehouseId=1,InvoiceDate="2026-10-29",PaymentMode="credit",Discount="10",Lines=new List<SaleLine>{new SaleLine{VariantId=f.Variant,ProductUnitId=f.Unit,Quantity="2",UnitPrice="200",Discount="10"}}}, 1, Ct);
                var posted = await f.Sales.PostAsync(draft.Id, "tax-post", 1, Ct);
                Assert.True(posted.Succeeded, posted.ErrorMessage);
                Assert.Equal(3900000, posted.Invoice.SubtotalScaled); // Line discount already deducted.
                Assert.Equal(100000, posted.Invoice.DiscountScaled);
                Assert.Equal(included ? 495700 : 570000, posted.Invoice.TaxScaled);
                Assert.Equal(included ? 3800000 : 4370000, posted.Invoice.TotalScaled);
                Assert.Equal(included ? 1304300 : 1800000, posted.Invoice.TotalScaled-posted.Invoice.TaxScaled-f.Cogs(draft.Id));
                var facts = f.Facts(draft.Id);
                await f.Edit("300", "1");
                using (var c = f.Open()) c.Execute("UPDATE ApplicationSettings SET TaxEnabled=0,DefaultTaxRate=0 WHERE Id=1");
                var command = new SaleReturnCommand{OperationKey="tax-half-return",Reason="إرجاع جزئي",Lines=new List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="1"}}};
                var returned = await f.Sales.ReturnAsync(draft.Id, command, 1, Ct);
                Assert.True(returned.Succeeded, returned.ErrorMessage);
                Assert.Equal(included ? 1900000 : 2185000, returned.TotalScaled);
                Assert.True((await f.Sales.ReturnAsync(draft.Id, command, 1, Ct)).Replayed);
                using (var c = f.Open()) Assert.Equal(1000000, c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations"));
                Assert.Equal(facts, f.Facts(draft.Id));
            }
        }

        [Fact]
        public async Task Cancellation_after_catalog_edit_restores_both_original_layers_once()
        {
            using (var f = await Fixture.Create())
            {
                await f.Receipt("cancel-a", "10", "100");
                await f.Receipt("cancel-b", "10", "120");
                var draft = await f.Draft("12", "150");
                Assert.True((await f.Sales.PostAsync(draft.Id, "cancel-post", 1, Ct)).Succeeded);
                var facts = f.Facts(draft.Id);
                await f.Edit("200", "999");
                var result = await f.Sales.CancelAsync(draft.Id, "cancel-key", "إلغاء آجل غير محصل", 1, Ct);
                Assert.True(result.Succeeded, result.ErrorMessage);
                Assert.True((await f.Sales.CancelAsync(draft.Id, "cancel-key", "إلغاء آجل غير محصل", 1, Ct)).Replayed);
                Assert.Equal(facts, f.Facts(draft.Id));
                var valuation = await f.Inventory.ValuationAsync(1, f.Product, Ct);
                Assert.Equal("2200.0000", valuation.CostValue);
                Assert.Equal("4000.0000", valuation.PotentialSellingValue);
                using (var c = f.Open()) Assert.Equal(12400000, c.ExecuteScalar<long>("SELECT SUM(m.ValueScaled) FROM SalesCancellations sc JOIN StockMovements m ON m.DocumentId=sc.InventoryDocumentId"));
                Assert.True(await f.Inventory.VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task New_purchase_receipt_has_its_own_cost_and_retry_never_reprices_catalog_or_old_layers()
        {
            using (var f = await Fixture.Create())
            {
                await f.Receipt("old-stock", "10", "100");
                await f.Edit("200", "999");
                var suppliers = new SqliteSupplierRepository(f.Cs);
                var supplier = suppliers.Add(new Supplier{Code="FIFO-SUP",Name="مورد اختبار",Phone="0100",Status="نشط"});
                var purchases = new PurchaseService(new SqlitePurchaseRepository(f.Cs), suppliers);
                var draft = await purchases.CreateDraftAsync(new PurchaseDraftCommand{SupplierId=supplier,WarehouseId=1,InvoiceNumber="FIFO-NEW",InvoiceDate="2026-10-29",PaymentMode="credit",Lines=new List<PurchaseInvoiceLine>{new PurchaseInvoiceLine{VariantId=f.Variant,ProductUnitId=f.Unit,Quantity="10",PurchaseUnitPrice="120",SaleUnitPrice="200"}}},1,Ct);
                Assert.True((await purchases.PostAsync(draft.Id,"new-purchase",1,Ct)).Succeeded);
                await f.Edit("250", "1");
                Assert.True((await purchases.PostAsync(draft.Id,"new-purchase",1,Ct)).Succeeded);
                Assert.Equal("250.00", (await f.Catalog.GetProductDetailAsync(f.Product,Ct)).Value.Variants.Single().EffectiveSalePrice);
                using (var c = f.Open()) Assert.Equal(new long[]{10000000,12000000},c.Query<long>("SELECT ValueScaled FROM InventoryCostLayers ORDER BY Id").ToArray());
                Assert.Equal("2200.0000",(await f.Inventory.ValuationAsync(1,f.Product,Ct)).CostValue);
            }
        }

        [Theory]
        [InlineData("150")]
        [InlineData("225")]
        public async Task Multi_variant_override_survives_base_price_edit_even_when_equal_to_old_base(string overridePrice)
        {
            using (var f = await Fixture.Create())
            {
                var detail=(await f.Catalog.GetProductDetailAsync(f.Product,Ct)).Value;
                var config=Configuration(detail);
                config.VariantMode=true;
                config.CapabilityKeys=new[]{"sizes"};
                config.Attributes=new[]{new ProductAttributeAssignmentCommand{AttributeId=1,Values=new[]{"S","L"}}};
                await f.Edit("150","100",config:config);
                var receipt=new InventoryPostCommand{OperationKey="variant-stock",Type="manual_receipt",WarehouseId=1,UserId=1,Lines=new[]{new InventoryLineInput{VariantId=f.Variant,ProductUnitId=f.Unit,Quantity="2",UnitCost="100",SalePrice=overridePrice}}};
                Assert.True((await f.Inventory.PostAsync(receipt,InventoryService.CanonicalHashPost(receipt),Ct)).Succeeded);
                await f.Edit("200","999",true);
                detail=(await f.Catalog.GetProductDetailAsync(f.Product,Ct)).Value;
                Assert.Equal(decimal.Parse(overridePrice).ToString("0.00"),detail.Variants.Single(v=>v.Id==f.Variant).EffectiveSalePrice);
                Assert.Equal("200.00",detail.Variants.Single(v=>v.Id!=f.Variant).EffectiveSalePrice);
                var sale=await f.Draft("1",overridePrice);
                Assert.True((await f.Sales.PostAsync(sale.Id,"variant-sale",1,Ct)).Succeeded);
                Assert.Equal(1000000,f.Cogs(sale.Id));
                Assert.Equal("100.0000",(await f.Inventory.ValuationAsync(1,f.Product,Ct)).CostValue);
            }
        }

        [Fact]
        public async Task Converted_sale_and_partial_return_in_second_warehouse_preserve_transferred_fifo_cost()
        {
            using (var f = await Fixture.Create())
            {
                var box=(await f.Catalog.SaveUnitAsync(new SaveUnitCommand{Code="FIFOBOX",Name="علبة",Symbol="ع",IsActive=true},Ct)).Value;
                var config=Configuration((await f.Catalog.GetProductDetailAsync(f.Product,Ct)).Value);
                config.CapabilityKeys=new[]{"multiple-units"};
                config.Units=config.Units.Concat(new[]{new ProductUnitCommand{UnitId=box.Id,Numerator=3,Denominator=1,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true}}).ToArray();
                await f.Edit("150","100",config:config);
                long boxId;using(var c=f.Open())boxId=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=0",new{Id=f.Product});
                var receipt=new InventoryPostCommand{OperationKey="box-stock",Type="manual_receipt",WarehouseId=1,UserId=1,Lines=new[]{new InventoryLineInput{VariantId=f.Variant,ProductUnitId=boxId,Quantity="10",UnitCost="300",SalePrice="450"}}};
                Assert.True((await f.Inventory.PostAsync(receipt,InventoryService.CanonicalHashPost(receipt),Ct)).Succeeded);
                await f.Edit("200","999",true);
                var second=(await f.Inventory.SaveWarehouseAsync(new WarehouseCommand{Code="FIFO2",Name="مخزن ثان"},true,Ct)).Value;
                var transfer=new InventoryPostCommand{OperationKey="box-transfer",Type="transfer",WarehouseId=1,DestinationWarehouseId=second.Id,UserId=1,Lines=new[]{new InventoryLineInput{VariantId=f.Variant,ProductUnitId=boxId,Quantity="4"}}};
                Assert.True((await f.Inventory.PostAsync(transfer,InventoryService.CanonicalHashPost(transfer),Ct)).Succeeded);
                var sale=await f.Draft("1","600",boxId,second.Id);
                var posted=await f.Sales.PostAsync(sale.Id,"box-sale",1,Ct);
                Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(6000000,posted.Invoice.TotalScaled);Assert.Equal(3000000,f.Cogs(sale.Id));
                await f.Edit("250","1");
                var returned=await f.Sales.ReturnAsync(sale.Id,new SaleReturnCommand{OperationKey="box-return",Reason="نصف علبة",Lines=new List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="0.5"}}},1,Ct);
                Assert.True(returned.Succeeded,returned.ErrorMessage);Assert.Equal(3000000,returned.TotalScaled);
                using(var c=f.Open())Assert.Equal(1500000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations"));
                Assert.Equal("1800.0000",(await f.Inventory.ValuationAsync(1,f.Product,Ct)).CostValue);
                Assert.Equal("1050.0000",(await f.Inventory.ValuationAsync(second.Id,f.Product,Ct)).CostValue);
                var reports=new AlKhayal.Application.Reports.StockReportService(new AlKhayal.Data.Reports.SqliteStockReportStore(f.Cs),TimeZoneInfo.Utc);
                var products=await reports.QueryAsync("product-profit",new AlKhayal.Application.Reports.StockReportQuery{From="2000-01-01",To="2099-12-31"},Ct);
                var productRow=Assert.Single(products.Items);Assert.Equal("3",productRow.Sold);Assert.Equal("1.5",productRow.Returned);Assert.Equal("1.5",productRow.Quantity);Assert.Equal("150.0000",productRow.Profit);
                var value=await reports.QueryAsync("inventory-valuation",new AlKhayal.Application.Reports.StockReportQuery{WarehouseId=second.Id},Ct);Assert.Equal("1050.0000",value.Summary.Cost);
                var movement=await reports.QueryAsync("inventory-movements",new AlKhayal.Application.Reports.StockReportQuery{From="2000-01-01",To="2099-12-31",Type="sales-return"},Ct);Assert.Single(movement.Items);Assert.Equal("1.5",movement.Items[0].QuantityIn);Assert.Equal("150.0000",movement.Items[0].Cost);
                Assert.True(await f.Inventory.VerifyProjectionAsync(Ct));
            }
        }

        private static ProductConfigurationCommand Configuration(CatalogProductDetail p)
        {
            return new ProductConfigurationCommand{
                VariantMode=p.VariantMode,CapabilityKeys=p.Capabilities.Where(x=>x.IsAssigned).Select(x=>x.Key).ToArray(),
                Attributes=p.Attributes.Select(a=>new ProductAttributeAssignmentCommand{AttributeId=a.AttributeId,ValueIds=a.Values.Select(v=>v.ValueId).ToArray()}).ToArray(),
                Units=p.Units.Select(u=>new ProductUnitCommand{Id=u.Id,UnitId=u.UnitId,Numerator=u.Numerator,Denominator=u.Denominator,IsBase=u.IsBase,IsSaleUnit=u.IsSaleUnit,IsPurchaseUnit=u.IsPurchaseUnit,IsActive=u.IsActive,Version=u.Version}).ToArray(),
                Variants=p.Variants.Select(v=>new ProductVariantCommand{Id=v.Id,Name=v.Name,AttributeValueIds=v.AttributeValueIds,IsDefault=v.IsDefault,IsActive=v.IsActive,Archive=v.IsArchived,SalePriceOverride=v.SalePriceOverride,PurchasePriceOverride=v.PurchasePriceOverride,Sku=v.Sku,Barcodes=v.Barcodes.Select(b=>new ProductBarcodeCommand{Id=b.Id,Code=b.Code,Type=b.Type,ProductUnitId=b.ProductUnitId,IsPrimary=b.IsPrimary,Retire=b.IsRetired}).ToArray()}).ToArray()
            };
        }

        [Fact]
        public async Task Partial_returns_cross_original_cost_layers_and_final_return_consumes_exact_residual()
        {
            using(var f=await Fixture.Create())
            {
                await f.Receipt("return-a","10","100");await f.Receipt("return-b","10","120");
                await f.Edit("200","999");var sale=await f.Draft("12","200");
                var posted=await f.Sales.PostAsync(sale.Id,"return-post",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var facts=f.Facts(sale.Id);await f.Edit("300","1");
                var command=new SaleReturnCommand{OperationKey="cross-return",Reason="إرجاع عبر دفعتين",Lines=new List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="11"}}};
                var returned=await f.Sales.ReturnAsync(sale.Id,command,1,Ct);Assert.True(returned.Succeeded,returned.ErrorMessage);Assert.Equal(22000000,returned.TotalScaled);
                Assert.True((await f.Sales.ReturnAsync(sale.Id,command,1,Ct)).Replayed);
                using(var c=f.Open())Assert.Equal(new long[]{10000000,1200000},c.Query<long>("SELECT ValueScaled FROM SalesReturnCostAllocations ORDER BY Id").ToArray());
                Assert.Equal("2080.0000",(await f.Inventory.ValuationAsync(1,f.Product,Ct)).CostValue);
                command.OperationKey="final-return";command.Lines[0].Quantity="1";
                Assert.True((await f.Sales.ReturnAsync(sale.Id,command,1,Ct)).Succeeded);
                using(var c=f.Open())Assert.Equal(12400000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations"));
                Assert.Equal(facts,f.Facts(sale.Id));Assert.Equal("2200.0000",(await f.Inventory.ValuationAsync(1,f.Product,Ct)).CostValue);
                Assert.True(await f.Inventory.VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task Configuration_round_trip_preserves_exact_fractional_variant_override()
        {
            using(var f=await Fixture.Create())
            {
                var box=(await f.Catalog.SaveUnitAsync(new SaveUnitCommand{Code="FRACTIONBOX",Name="علبة كسرية",Symbol="عك",IsActive=true},Ct)).Value;
                var config=Configuration((await f.Catalog.GetProductDetailAsync(f.Product,Ct)).Value);
                config.VariantMode=true;config.CapabilityKeys=new[]{"sizes","multiple-units"};
                config.Attributes=new[]{new ProductAttributeAssignmentCommand{AttributeId=1,Values=new[]{"S","L"}}};
                config.Units=config.Units.Concat(new[]{new ProductUnitCommand{UnitId=box.Id,Numerator=3,Denominator=1,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true}}).ToArray();
                await f.Edit("150","100",config:config);
                long boxId;using(var c=f.Open())boxId=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=0",new{Id=f.Product});
                var receipt=new InventoryPostCommand{OperationKey="fractional-price",Type="manual_receipt",WarehouseId=1,UserId=1,Lines=new[]{new InventoryLineInput{VariantId=f.Variant,ProductUnitId=boxId,Quantity="1",UnitCost="6",SalePrice="10"}}};
                Assert.True((await f.Inventory.PostAsync(receipt,InventoryService.CanonicalHashPost(receipt),Ct)).Succeeded);
                await f.Edit("200","999",true);
                var variant=(await f.Catalog.GetProductDetailAsync(f.Product,Ct)).Value.Variants.Single(v=>v.Id==f.Variant);
                Assert.Equal("3.3333",variant.EffectiveSalePriceExact);
                var sale=await f.Draft("1","10",boxId);var posted=await f.Sales.PostAsync(sale.Id,"fractional-sale",1,Ct);
                Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(100000,posted.Invoice.TotalScaled);Assert.Equal(60000,f.Cogs(sale.Id));
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string _path = Path.Combine(Path.GetTempPath(), "alkhayal-pricing-fifo-" + Guid.NewGuid().ToString("N") + ".db");
            public string Cs { get; private set; }
            public CatalogService Catalog { get; private set; }
            public SalesService Sales { get; private set; }
            public SqliteInventoryStore Inventory { get; private set; }
            public long Product, Variant, Unit, Customer;
            public static async Task<Fixture> Create()
            {
                var f = new Fixture();
                try
                {
                    Assert.False(f._path.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AlKhayal")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
                    f.Cs=Db.ConnectionStringFor(f._path); Migrator.Run(f.Cs);
                    using (var c = f.Open())
                    {
                        c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('fifo-test','test-only','2026-10-29')");
                        c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('FIFO-C','عميل اختبار','0100','',0,0,'أساسي','نشط','2026-10-29','2026-10-29')");
                        f.Customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='FIFO-C'");
                    }
                    f.Catalog=new CatalogService(new SqliteCatalogStore(f.Cs));f.Sales=new SalesService(new SqliteSalesStore(f.Cs));f.Inventory=new SqliteInventoryStore(f.Cs);
                    var product=await f.Catalog.CreateProductAsync(new CreateProductCommand{OperationKey="fifo-product",Name="صنف FIFO",BaseUnitId=1,ProductType="stock",SalePrice="150",PurchasePrice="100",IsActive=true},Ct);
                    Assert.True(product.Succeeded,product.ErrorMessage); f.Product=product.Value.Id;
                    using (var c=f.Open()){f.Variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=f.Product});f.Unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=f.Product});}
                    return f;
                }
                catch {f.Dispose();throw;}
            }
            public SQLiteConnection Open(){var c=new SQLiteConnection(Cs);c.Open();return c;}
            public async Task Receipt(string key,string quantity,string cost)
            {
                var command=new InventoryPostCommand{OperationKey=key,Type="manual_receipt",WarehouseId=1,UserId=1,Lines=new[]{new InventoryLineInput{VariantId=Variant,ProductUnitId=Unit,Quantity=quantity,UnitCost=cost,SalePrice="150"}}};
                var result=await Inventory.PostAsync(command,InventoryService.CanonicalHashPost(command),Ct);Assert.True(result.Succeeded,result.ErrorMessage);
            }
            public async Task Edit(string sale,string purchase,bool withConfiguration=false,ProductConfigurationCommand config=null)
            {
                var p=(await Catalog.GetProductDetailAsync(Product,Ct)).Value;
                var result=await Catalog.UpdateProductAsync(new UpdateProductCommand{ProductId=Product,Version=p.Version,Name=p.Name,BaseUnitId=p.BaseUnitId,ProductType=p.ProductType,IsActive=true,SalePrice=sale,PurchasePrice=purchase,UserId=1,Configuration=config??(withConfiguration?Configuration(p):null)},Ct);
                Assert.True(result.Succeeded,result.ErrorMessage);
            }
            public Task<SaleInvoice> Draft(string quantity,string price,long? unit=null,long warehouse=1)
            {return Sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=Customer,WarehouseId=warehouse,InvoiceDate="2026-10-29",PaymentMode="credit",Lines=new List<SaleLine>{new SaleLine{VariantId=Variant,ProductUnitId=unit??Unit,Quantity=quantity,UnitPrice=price}}},1,Ct);}
            public long Cogs(long invoice){using(var c=Open())return c.ExecuteScalar<long>("SELECT SUM(a.ValueScaled) FROM SalesInvoiceLines l JOIN StockMovements m ON m.DocumentLineId=l.InventoryDocumentLineId JOIN InventoryCostAllocations a ON a.MovementId=m.Id WHERE l.SalesInvoiceId=@Id",new{Id=invoice});}
            public string Facts(long invoice)
            {
                using(var c=Open())return string.Join("|",c.Query<string>(@"SELECT CAST(l.Id AS TEXT)||':'||l.UnitPriceScaled||':'||l.DiscountScaled||':'||l.InvoiceDiscountScaled||':'||l.TaxScaled||':'||l.LineTotalScaled||':'||a.Id||':'||a.LayerId||':'||a.ValueScaled FROM SalesInvoiceLines l JOIN StockMovements m ON m.DocumentLineId=l.InventoryDocumentLineId JOIN InventoryCostAllocations a ON a.MovementId=m.Id WHERE l.SalesInvoiceId=@Id ORDER BY l.Id,a.Id",new{Id=invoice}));
            }
            public void Dispose(){SQLiteConnection.ClearAllPools();if(File.Exists(_path))File.Delete(_path);}
        }
    }
}
