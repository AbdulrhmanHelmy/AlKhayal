using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Purchasing;
using AlKhayal.Core.Models;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Purchasing;
using AlKhayal.Data.Suppliers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    // QA Batch 4B (phase 4) focused edge coverage. Purchase draft validation
    // (payment mode, money precision, quantity precision, unknown supplier) and the
    // auto invoice-number fallback had no direct test anywhere in the suite
    // (see docs/qa/phase-4-coverage.md). This single test exercises exactly that gap
    // against a disposable explicit SQLite path and never touches the operator's
    // LocalApplicationData database.
    public sealed class QaPhase4EdgeTests
    {
        [Fact]
        public async Task Purchase_draft_validation_rejections_leave_no_rows_and_the_null_invoice_number_gets_an_auto_reference()
        {
            var path = Path.Combine(Path.GetTempPath(), "qa-phase4-edge-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var connectionString = Db.ConnectionStringFor(path);
                Migrator.Run(connectionString);

                var suppliers = new SqliteSupplierRepository(connectionString);
                var supplierId = suppliers.Add(new Supplier { Code = "QA4-S", Name = "مورد مرحلة أربعة", Phone = "01000000000", Status = "نشط" });
                // PurchaseInvoices.CreatedByUserId references Users; the draft actor must exist.
                using (var c = new SQLiteConnection(connectionString))
                {
                    c.Open(); c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('qa4-admin','test-only','2026-01-01T00:00:00Z')");
                }
                var catalog = new CatalogService(new SqliteCatalogStore(connectionString));
                var product = await catalog.CreateProductAsync(new CreateProductCommand { OperationKey = "qa4-edge-product", Name = "صنف مسودة", BaseUnitId = 1, ProductType = "stock", SalePrice = "12", PurchasePrice = "9", IsActive = true }, CancellationToken.None);
                Assert.True(product.Succeeded, product.ErrorMessage);
                long variantId, unitId;
                using (var c = new SQLiteConnection(connectionString))
                {
                    c.Open();
                    variantId = c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id", new { Id = product.Value.Id });
                    unitId = c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1", new { Id = product.Value.Id });
                }

                var service = new PurchaseService(new SqlitePurchaseRepository(connectionString), suppliers);
                PurchaseDraftCommand ValidDraft(long supplier, string quantity = "2.5", string cost = "3.33", string sale = "5.25", string mode = "credit")
                {
                    return new PurchaseDraftCommand
                    {
                        SupplierId = supplier, WarehouseId = 1, InvoiceNumber = "QA4-INV-1", InvoiceDate = "2026-10-24", PaymentMode = mode,
                        Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = variantId, ProductUnitId = unitId, Quantity = quantity, PurchaseUnitPrice = cost, SaleUnitPrice = sale } }
                    };
                }
                Task<PurchaseInvoice> Create(PurchaseDraftCommand command) { return service.CreateDraftAsync(command, 1, CancellationToken.None); }

                // Validation must reject the payload before any row is written:
                // bad payment mode, sub-piastre prices, sub-micro quantity, no lines,
                // and an unknown supplier.
                Assert.Throws<ArgumentException>(() => Create(ValidDraft(supplierId, mode: "deferred")).GetAwaiter().GetResult());
                Assert.Throws<ArgumentException>(() => Create(ValidDraft(supplierId, cost: "3.335")).GetAwaiter().GetResult());
                Assert.Throws<ArgumentException>(() => Create(ValidDraft(supplierId, sale: "5.555")).GetAwaiter().GetResult());
                Assert.Throws<ArgumentException>(() => Create(ValidDraft(supplierId, quantity: "0.0000005")).GetAwaiter().GetResult());
                Assert.Throws<ArgumentException>(() => Create(ValidDraft(0)).GetAwaiter().GetResult());
                Assert.Throws<ArgumentException>(() => Create(ValidDraft(999999)).GetAwaiter().GetResult());
                Assert.Throws<ArgumentException>(() => Create(ValidDraft(supplierId, quantity: "0")).GetAwaiter().GetResult());
                Assert.Throws<ArgumentException>(() => Create(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceDate = "2026-10-24", PaymentMode = "credit", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine>() }).GetAwaiter().GetResult());

                using (var check = new SQLiteConnection(connectionString))
                {
                    check.Open();
                    Assert.Equal(0L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseInvoices"));
                    Assert.Equal(0L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseInvoiceLines"));
                    Assert.Equal(0L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseInvoiceAudit"));
                    Assert.Equal(0L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries"));
                }

                // A null invoice number is saved with a generated PUR- reference and the
                // draft total is the piastre-rounded 2.5 × 3.33 = 8.33 (83300 at scale 4).
                var draft = await service.CreateDraftAsync(
                    new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceDate = "2026-10-24", PaymentMode = "credit", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = variantId, ProductUnitId = unitId, Quantity = "2.5", PurchaseUnitPrice = "3.33", SaleUnitPrice = "5.25" } } },
                    1, CancellationToken.None);
                Assert.Equal("draft", draft.Status);
                Assert.StartsWith("PUR-", draft.InvoiceNumber);
                Assert.Equal(83300, draft.TotalScaled);
                using (var check = new SQLiteConnection(connectionString))
                {
                    check.Open();
                    Assert.Equal(1L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseInvoices"));
                    Assert.Equal(1L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseInvoiceLines"));
                }
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
