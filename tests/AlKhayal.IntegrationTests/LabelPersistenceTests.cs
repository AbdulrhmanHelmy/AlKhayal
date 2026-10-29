using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Printing;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Workforce;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Printing;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Sales;
using AlKhayal.Data.Workforce;
using Dapper;
using Xunit;
using Xunit.Abstractions;

namespace AlKhayal.IntegrationTests
{
    public sealed class LabelPersistenceTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private readonly ITestOutputHelper _output;
        public LabelPersistenceTests(ITestOutputHelper output) { _output = output; }
        internal sealed class Backend : IPrinterBackend, ILabelPrinterBackend
        {
            public readonly PrintingPersistenceTests.RecordingBackend Receipt = new PrintingPersistenceTests.RecordingBackend();
            public int Labels; public LabelBatch LastBatch; public LabelTemplate Template; public LabelPrinterProfile Profile; public string Error; public bool Unknown, Unsupported;
            public TaskCompletionSource<bool> Entered, Release;
            public Task<IReadOnlyList<PrinterInfo>> PrintersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<PrinterInfo>>(new[] { new PrinterInfo { Name = "Labels", CustomSizeSupported = true }, new PrinterInfo { Name = "Recording" } });
            public Task ValidateAsync(PrintProfile p, PrintTemplate t, CancellationToken ct) => Receipt.ValidateAsync(p, t, ct);
            public Task SubmitAsync(PrintDocumentData d, PrintTemplate t, PrintProfile p, CancellationToken ct) => Receipt.SubmitAsync(d, t, p, ct);
            public Task<byte[]> PreviewAsync(PrintDocumentData d, PrintTemplate t, CancellationToken ct) => Receipt.PreviewAsync(d, t, ct);
            public Task ValidateLabelsAsync(LabelPrinterProfile p, LabelTemplate t, CancellationToken ct)
            { if (p.PrinterName != "Labels") throw new PrintException("printer_not_found", "الطابعة غير موجودة."); if (Unsupported) throw new PrintException("unsupported_paper", "مقاس غير مدعوم."); return Task.CompletedTask; }
            public async Task SubmitLabelsAsync(LabelBatch batch, LabelTemplate t, LabelPrinterProfile p, CancellationToken ct)
            { Interlocked.Increment(ref Labels); LastBatch = batch; Template = t; Profile = p; if (Entered != null) { Entered.SetResult(true); await Release.Task; } if (Error != null) throw new PrintException(Error, "رفض الاختبار."); if (Unknown) throw new InvalidOperationException("Injected unknown submission."); }
            public Task<byte[]> PreviewLabelsAsync(LabelBatch batch, LabelTemplate t, CancellationToken ct) => Task.FromResult(new byte[] { 137, 80, 78, 71 });
        }
        internal static CatalogService Catalog(TreasuryPersistenceTests.Fixture f) => new CatalogService(new SqliteCatalogStore(f.ConnectionString));
        internal static async Task<CatalogProductDetail> Product(TreasuryPersistenceTests.Fixture f, string name = "منتج ملصق", bool variants = false)
        {
            var result = await Catalog(f).CreateProductAsync(new CreateProductCommand { OperationKey = Guid.NewGuid().ToString("N"), Name = name, BaseUnitId = 1, ProductType = "stock", SalePrice = "12.50", PurchasePrice = "8", IsActive = true, UserId = f.UserId, Configuration = variants ? new ProductConfigurationCommand { VariantMode = true, CapabilityKeys = new[] { "sizes", "colors" }, Attributes = new[] { new ProductAttributeAssignmentCommand { AttributeId = 1, Values = new[] { "M", "XL" } }, new ProductAttributeAssignmentCommand { AttributeId = 2, Values = new[] { "أزرق" } } } } : null }, Ct);
            Assert.True(result.Succeeded, result.ErrorMessage); return (await Catalog(f).GetProductDetailAsync(result.Value.Id, Ct)).Value;
        }
        internal static async Task<BarcodeChangeResult> Assign(TreasuryPersistenceTests.Fixture f, CatalogProductDetail d, string code = null, long? variantId = null, long? replaceId = null, string format = "code128", string key = null)
            => await new BarcodeService(new SqliteCatalogStore(f.ConnectionString)).SaveAsync(new BarcodeChange { ProductId = d.Id, VariantId = variantId ?? d.Variants.First().Id, ProductUnitId = d.Units.Single(u => u.IsBase).Id, Version = d.Version, Code = code, Generate = code == null, Format = format, ReplaceBarcodeId = replaceId, OperationKey = key ?? Guid.NewGuid().ToString("N") }, f.UserId, Ct);
        internal static LabelService Service(TreasuryPersistenceTests.Fixture f, Backend b, out PrintingService printing)
        {
            printing = new PrintingService(new SqlitePrintRepository(f.ConnectionString), PrintingPersistenceTests.Provider(f), b); return new LabelService(new SqliteLabelRepository(f.ConnectionString), b, printing);
        }
        internal static async Task<LabelSettings> Configure(LabelService service, long actor)
        { var s = await service.SettingsAsync(Ct); s.Profiles[0].PrinterName = "Labels"; return await service.SaveAsync(s, actor, Ct); }
        private static LabelBatchRequest Batch(params long[] ids) => new LabelBatchRequest { OperationKey = Guid.NewGuid().ToString("N"), Items = ids.Select(id => new LabelSelection { BarcodeId = id, Quantity = 2 }).ToList() };
        [Fact]
        public async Task Barcode_generation_edit_audit_restart_uniqueness_retirement_and_POS_identity_are_atomic()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var d = await Product(f); var operation = "generated-first"; var code = await Assign(f, d, key: operation); Assert.Equal(10, code.Code.Length);
                var invalid = await Catalog(f).CreateProductAsync(new CreateProductCommand { OperationKey = "invalid-label-content", Name = "رمز غير مدعوم", BaseUnitId = 1, ProductType = "stock", SalePrice = "1", IsActive = true, UserId = f.UserId, Configuration = new ProductConfigurationCommand { Variants = new[] { new ProductVariantCommand { Name = "افتراضي", IsDefault = true, IsActive = true, Barcodes = new[] { new ProductBarcodeCommand { Code = "عربي", Type = "internal", IsPrimary = true } } } } } }, Ct);
                Assert.False(invalid.Succeeded); Assert.Equal("identifier_invalid", invalid.ErrorCode);
                var replay = await Assign(f, d, key: operation); Assert.True(replay.Replayed); Assert.Equal(code.Id, replay.Id); Assert.Equal(code.Code, replay.Code);
                var lookup = await Catalog(f).LookupBarcodeAsync(code.Code, Ct); Assert.True(lookup.Succeeded); Assert.Equal(d.Id, lookup.Value.ProductId); Assert.Equal(d.Variants.Single().Id, lookup.Value.VariantId);
                d = (await Catalog(f).GetProductDetailAsync(d.Id, Ct)).Value; var changed = await Assign(f, d, "ABC-lower/1", replaceId: code.Id); Assert.NotEqual(code.Id, changed.Id);
                var old = await Catalog(f).LookupBarcodeAsync(code.Code, Ct); Assert.Equal("barcode_retired", old.ErrorCode);
                Assert.Equal(changed.Id, (await Catalog(f).LookupBarcodeAsync("abc-LOWER/1", Ct)).Value.BarcodeId);
                var other = await Product(f, "منتج ثانٍ"); Assert.Equal("duplicate_barcode", (await Assert.ThrowsAsync<CatalogStoreException>(() => Assign(f, other, "abc-LOWER/1"))).Code);
                Assert.Equal("duplicate_barcode", (await Assert.ThrowsAsync<CatalogStoreException>(() => Assign(f, other, code.Code))).Code);
                var ean = await Assign(f, other, "400638133393", format: "ean13"); Assert.Equal("4006381333931", ean.Code); Assert.True((await Catalog(f).LookupBarcodeAsync(ean.Code, Ct)).Succeeded);
                Assert.Equal("version_conflict", (await Assert.ThrowsAsync<CatalogStoreException>(() => Assign(f, other, "NEW-CODE"))).Code);
                var detail = (await Catalog(f).GetProductDetailAsync(other.Id, Ct)).Value; Assert.Equal("ean13", detail.Variants.Single().Barcodes.Single().Format);
                Migrator.Run(f.ConnectionString); Assert.Equal(changed.Id, (await Catalog(f).LookupBarcodeAsync(changed.Code, Ct)).Value.BarcodeId);
                using (var c = f.Open()) { Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductVariants WHERE ProductId=@id", new { id = d.Id })); Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CatalogAuditLog WHERE EntityType='barcode'")); Assert.Empty(c.Query("PRAGMA foreign_key_check")); }
                d = (await Catalog(f).GetProductDetailAsync(d.Id, Ct)).Value;
                using (var c = f.Open()) c.Execute("CREATE TRIGGER FailBarcodeAudit BEFORE INSERT ON CatalogAuditLog WHEN NEW.EntityType='barcode' BEGIN SELECT RAISE(ABORT,'injected'); END;");
                await Assert.ThrowsAsync<SQLiteException>(() => Assign(f, d, "ROLLBACK", replaceId: changed.Id));
                Assert.True((await Catalog(f).LookupBarcodeAsync(changed.Code, Ct)).Succeeded); Assert.Equal("barcode_not_found", (await Catalog(f).LookupBarcodeAsync("ROLLBACK", Ct)).ErrorCode);
            }
        }
        [Fact]
        public async Task Variant_codes_remain_distinct_and_bulk_labels_use_authoritative_names_units_attributes_prices()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var d = await Product(f, "قميص Arabic", true); var ids = new List<long>();
                foreach (var v in d.Variants.ToList()) { var result = await Assign(f, d, "VAR-" + v.Id, v.Id); ids.Add(result.Id); d = (await Catalog(f).GetProductDetailAsync(d.Id, Ct)).Value; var found = await Catalog(f).LookupBarcodeAsync(result.Code, Ct); Assert.Equal(v.Id, found.Value.VariantId); }
                var backend = new Backend(); PrintingService printing; var service = Service(f, backend, out printing); await Configure(service, f.UserId);
                var request = Batch(ids.ToArray()); var job = await service.SubmitAsync(request, f.UserId, Ct); Assert.Equal("submitted_unknown", job.Status); Assert.Equal(1, backend.Labels);
                Assert.Equal(ids.Count, backend.LastBatch.Items.Count); Assert.All(backend.LastBatch.Items, label => { Assert.Equal("قميص Arabic", label.Fields["productName"]); Assert.Contains("12.50", label.Fields["price"]); Assert.Equal("أزرق", label.Fields["color"]); Assert.Contains(label.Fields["size"], new[] { "M", "XL" }); });
                Assert.True((await service.SubmitAsync(request, f.UserId, Ct)).Replayed); Assert.Equal(1, backend.Labels);
                var restarted = Service(f, backend, out printing); Assert.True((await restarted.SubmitAsync(request, f.UserId, Ct)).Replayed);
                request.Items[0].Quantity++; Assert.Equal("key_conflict", (await Assert.ThrowsAsync<PrintException>(() => service.SubmitAsync(request, f.UserId, Ct))).Code);
                request.OperationKey = "explicit-reprint"; request.Reprint = true; Assert.True((await service.SubmitAsync(request, f.UserId, Ct)).Reprint); Assert.Equal(2, backend.Labels);
                var jobs = await printing.JobsAsync(Ct); Assert.Contains("barcodeId", jobs[0].BatchSummaryJson); Assert.NotNull(jobs[0].RequestHash);
            }
        }
        [Fact]
        public async Task Label_profiles_templates_restart_delete_constraints_custom_paper_and_receipt_independence()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var b = new Backend(); PrintingService printing; var service = Service(f, b, out printing); var receipt = await printing.SettingsAsync(Ct); receipt.Profiles[0].PrinterName = "Recording"; await printing.SaveAsync(receipt, f.UserId, Ct);
                var s = await Configure(service, f.UserId); var copy = LabelService.BuiltIn("label-copy", 73, 37); copy.Name = "مخصص عربي"; copy.Elements.Single(e => e.Key == "price").Bold = false; s.Templates.Add(copy); s.Profiles.Add(new LabelPrinterProfile { Id = "label-office", Name = "مكتب", PrinterName = "Labels", TemplateId = copy.Id, Copies = 2 }); s.DefaultProfileId = "label-office"; s = await service.SaveAsync(s, f.UserId, Ct);
                var saved = await new SqliteLabelRepository(f.ConnectionString).ReadAsync(Ct); Assert.Equal(2, saved.Templates.Count); Assert.Equal("label-office", saved.DefaultProfileId); Assert.Equal(73, saved.Templates.Last().Stock.LabelWidthMm); Assert.False(saved.Templates.Last().Elements.Single(e => e.Key == "price").Bold);
                Assert.Equal("Recording", (await printing.SettingsAsync(Ct)).Profiles[0].PrinterName); Assert.Equal(8, (await printing.SettingsAsync(Ct)).Templates.Count);
                Assert.Equal("submitted_unknown", (await service.TestAsync("labels-default", "test-profile-key", f.UserId, Ct)).Status);
                Assert.True((await service.TestAsync("labels-default", "test-profile-key", f.UserId, Ct)).Replayed);
                Assert.Equal("key_conflict", (await Assert.ThrowsAsync<PrintException>(() => service.TestAsync("label-office", "test-profile-key", f.UserId, Ct))).Code);
                saved.Templates.RemoveAll(t => t.Id == copy.Id); await Assert.ThrowsAsync<PrintException>(() => service.SaveAsync(saved, f.UserId, Ct));
                saved = await service.SettingsAsync(Ct); saved.Profiles[0].PrinterName = "gone"; Assert.Equal("printer_not_found", (await Assert.ThrowsAsync<PrintException>(() => service.SaveAsync(saved, f.UserId, Ct))).Code);
                saved = await service.SettingsAsync(Ct); b.Unsupported = true; Assert.Equal("unsupported_paper", (await Assert.ThrowsAsync<PrintException>(() => service.SaveAsync(saved, f.UserId, Ct))).Code); b.Unsupported = false;
                saved = await service.SettingsAsync(Ct); using (var c = f.Open()) c.Execute("CREATE TRIGGER FailLabelAudit BEFORE INSERT ON LabelConfigurationAudit BEGIN SELECT RAISE(ABORT,'test'); END;");
                await Assert.ThrowsAsync<SQLiteException>(() => service.SaveAsync(saved, f.UserId, Ct)); Assert.Equal(2, (await service.SettingsAsync(Ct)).Version);
            }
        }
        [Fact]
        public async Task Concurrent_duplicate_loss_failure_and_uncertain_submission_never_requeue_implicitly()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var d = await Product(f); var barcode = await Assign(f, d); var b = new Backend { Entered = new TaskCompletionSource<bool>(), Release = new TaskCompletionSource<bool>() }; PrintingService printing; var service = Service(f, b, out printing); await Configure(service, f.UserId); var request = Batch(barcode.Id);
                var running = service.SubmitAsync(request, f.UserId, Ct); await b.Entered.Task; var replay = await service.SubmitAsync(request, f.UserId, Ct); Assert.True(replay.Replayed); Assert.Equal("accepted", replay.Status); Assert.Equal(1, b.Labels);
                var concurrent = Batch(barcode.Id); Assert.Equal("print_busy", (await Assert.ThrowsAsync<PrintException>(() => service.SubmitAsync(concurrent, f.UserId, Ct))).Code);
                b.Release.SetResult(true); await running; b.Entered = null; b.Error = "rendering_failure";
                var failedRequest = Batch(barcode.Id); Assert.Equal("failed", (await service.SubmitAsync(failedRequest, f.UserId, Ct)).Status); b.Error = null; Assert.Equal("failed", (await service.SubmitAsync(failedRequest, f.UserId, Ct)).Status); Assert.Equal(2, b.Labels);
                b.Unknown = true; var unknown = Batch(barcode.Id); Assert.Equal("uncertain", (await service.SubmitAsync(unknown, f.UserId, Ct)).Status); Assert.True((await service.SubmitAsync(unknown, f.UserId, Ct)).Replayed); Assert.Equal(3, b.Labels);
                b.Unknown = false; var retry = Batch(barcode.Id); retry.Reprint = true; Assert.Equal("submitted_unknown", (await service.SubmitAsync(retry, f.UserId, Ct)).Status); Assert.Equal(4, b.Labels);
                retry = Batch(barcode.Id); retry.Items[0].Quantity = 2001; await Assert.ThrowsAsync<PrintException>(() => service.SubmitAsync(retry, f.UserId, Ct)); Assert.Equal(4, b.Labels);
            }
        }
        [Fact]
        public async Task Barcode_and_label_operations_leave_posted_financial_history_stock_FIFO_and_shift_reconciliation_unchanged()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var d = await Product(f); var variant = d.Variants.Single().Id; var unit = d.Units.Single().Id;
                var stock = await new SqliteInventoryStore(f.ConnectionString).PostAsync(new InventoryPostCommand { OperationKey = "label-stock", Type = "manual_receipt", WarehouseId = 1, UserId = f.UserId, Reason = "test", Lines = new[] { new InventoryLineInput { VariantId = variant, ProductUnitId = unit, Quantity = "10", UnitCost = "8", SalePrice = "12.50" } } }, "label-stock-hash", Ct); Assert.True(stock.Succeeded, stock.ErrorMessage);
                await new WorkforceService(new SqliteWorkforceStore(f.ConnectionString)).ExecuteAsync("shift-open", new WorkforceCommand { Amount = "10", OperationKey = "label-shift" }, f.UserId, true, Ct);
                var sales = new SalesService(new SqliteSalesStore(f.ConnectionString)); var draft = await sales.CreateDraftAsync(new SaleDraftCommand { WarehouseId = 1, PaymentMode = "cash", InvoiceDate = DateTime.Today.ToString("yyyy-MM-dd"), Discount = "0", Lines = new List<SaleLine> { new SaleLine { VariantId = variant, ProductUnitId = unit, Quantity = "1", UnitPrice = "12.50", Discount = "0" } } }, f.UserId, Ct); Assert.True((await sales.PostAsync(draft.Id, "label-sale", f.UserId, Ct)).Succeeded);
                string Snapshot() { using (var c = f.Open()) { var names = new[] { "SalesInvoices", "SalesPayments", "PurchaseInvoices", "StockMovements", "InventoryCostLayers", "CustomerTransactions", "SupplierLedgerEntries", "CashierShifts", "CashierShiftEvents", "TreasuryMovements" }; return string.Join("\n", names.Select(n => Newtonsoft.Json.JsonConvert.SerializeObject(c.Query("SELECT * FROM " + n).ToArray()))); } }
                var before = Snapshot(); d = (await Catalog(f).GetProductDetailAsync(d.Id, Ct)).Value; var barcode = await Assign(f, d); var b = new Backend(); PrintingService printing; var service = Service(f, b, out printing); await Configure(service, f.UserId);
                var watch = Stopwatch.StartNew(); var job = await service.SubmitAsync(Batch(barcode.Id), f.UserId, Ct); watch.Stop(); _output.WriteLine("Persisted-label fake-backend submission {0:F2}ms (no hardware).", watch.Elapsed.TotalMilliseconds); Assert.Equal("submitted_unknown", job.Status);
                b.Error = "driver_error"; Assert.Equal("failed", (await service.SubmitAsync(Batch(barcode.Id), f.UserId, Ct)).Status); Assert.Equal(before, Snapshot());
            }
        }
    }
}
