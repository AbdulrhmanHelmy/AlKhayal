using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Printing;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Returns;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Settings;
using AlKhayal.Application.Customers;
using AlKhayal.Application.Suppliers;
using AlKhayal.Core.Models;
using AlKhayal.Application.Workforce;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Printing;
using AlKhayal.Data.Purchasing;
using AlKhayal.Data.Returns;
using AlKhayal.Data.Sales;
using AlKhayal.Data.Settings;
using AlKhayal.Data.Suppliers;
using AlKhayal.Data.Workforce;
using Dapper;
using Xunit;
using Xunit.Abstractions;

namespace AlKhayal.IntegrationTests
{
    public sealed class PrintingPersistenceTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private readonly ITestOutputHelper _output;
        public PrintingPersistenceTests(ITestOutputHelper output) { _output = output; }
        internal sealed class RecordingBackend : IPrinterBackend
        {
            public int Submissions; public string Error; public bool UnknownFailure; public PrintDocumentData Document;
            public TaskCompletionSource<bool> Entered, Release;
            public Task<IReadOnlyList<PrinterInfo>> PrintersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<PrinterInfo>>(new[] { new PrinterInfo { Name = "Recording", IsDefault = true, Papers = new List<PrinterPaper> { new PrinterPaper { Name = "A4", WidthMm = 210, HeightMm = 297 } } } });
            public Task ValidateAsync(PrintProfile p, PrintTemplate t, CancellationToken ct)
            {
                if (p.PrinterName != "Recording") throw new PrintException("printer_not_found", "الطابعة غير موجودة.");
                if (t.Paper.WidthMm > 210) throw new PrintException("unsupported_paper", "الورق غير مدعوم.");
                return Task.CompletedTask;
            }
            public async Task SubmitAsync(PrintDocumentData d, PrintTemplate t, PrintProfile p, CancellationToken ct)
            {
                Interlocked.Increment(ref Submissions); Document = d;
                if (Entered != null) { Entered.TrySetResult(true); await Release.Task; }
                if (UnknownFailure) throw new InvalidOperationException("injected failure after possible acceptance");
                if (Error != null) throw new PrintException(Error, "فشل الطباعة للاختبار.");
            }
            public Task<byte[]> PreviewAsync(PrintDocumentData d, PrintTemplate t, CancellationToken ct) => Task.FromResult(new byte[] { 137, 80, 78, 71 });
        }
        private sealed class Documents : IPrintDocumentProvider
        {
            public Task<PrintDocumentData> GetAsync(string type, long id, long actor, bool admin, CancellationToken ct) => Task.FromResult(new PrintDocumentData { Id = id, Type = type, Reference = type + "-" + id, Title = "اختبار" });
        }
        internal static PrintDocumentProvider Provider(TreasuryPersistenceTests.Fixture f)
        {
            var sales = new SalesService(new SqliteSalesStore(f.ConnectionString)); var purchases = new PurchaseService(new SqlitePurchaseRepository(f.ConnectionString), new SqliteSupplierRepository(f.ConnectionString));
            var settingsStore = new SqliteSettingsStore(f.ConnectionString, f.DatabasePath);
            return new PrintDocumentProvider(sales, purchases, new ReturnService(new SqliteReturnReadStore(f.ConnectionString), sales, purchases), new WorkforceService(new SqliteWorkforceStore(f.ConnectionString)), new SettingsService(settingsStore, settingsStore), new SqlitePrintPaymentReader(f.ConnectionString));
        }
        private static async Task<PrintingSettings> Configure(PrintingService service, long user, bool auto = false)
        {
            var s = await service.SettingsAsync(Ct); s.AutoPrintSale = auto; foreach (var p in s.Profiles) p.PrinterName = "Recording";
            return await service.SaveAsync(s, user, Ct);
        }
        private static PrintRequest Request(string key, bool auto = false, string type = "sale", long id = 1) => new PrintRequest { DocumentId = id, DocumentType = type, OperationKey = key, Automatic = auto, Reprint = !auto };
        [Fact]
        public async Task Persisted_purchases_returns_customer_and_supplier_receipts_resolve_only_supported_financial_events()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                long supplierId, customerId;
                using (var c = f.Open())
                {
                    supplierId = c.ExecuteScalar<long>("INSERT INTO Suppliers(Code,Name,Phone,CreatedAt,UpdatedAt) VALUES('PRINT-S','مورد الطباعة','1','2026-01-01','2026-01-01'); SELECT last_insert_rowid();");
                    customerId = c.ExecuteScalar<long>("INSERT INTO Customers(Code,Name,Phone,CreatedAt,UpdatedAt) VALUES('PRINT-C','عميل الطباعة','2','2026-01-01','2026-01-01'); SELECT last_insert_rowid();");
                }
                var catalog = new CatalogService(new SqliteCatalogStore(f.ConnectionString)); var product = await catalog.CreateProductAsync(new CreateProductCommand { OperationKey = "purchase-print-product", Name = "صنف شراء", BaseUnitId = 1, ProductType = "stock", SalePrice = "12", PurchasePrice = "10", IsActive = true }, Ct); Assert.True(product.Succeeded, product.ErrorMessage);
                long variant, unit; using (var c = f.Open()) { variant = c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id", new { product.Value.Id }); unit = c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1", new { product.Value.Id }); }
                var purchases = new PurchaseService(new SqlitePurchaseRepository(f.ConnectionString), new SqliteSupplierRepository(f.ConnectionString));
                var draft = await purchases.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplierId, WarehouseId = 1, InvoiceNumber = "PRINT-P-1", InvoiceDate = "2026-10-29", PaymentMode = "credit", Lines = new List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = variant, ProductUnitId = unit, Quantity = "4", PurchaseUnitPrice = "10", SaleUnitPrice = "12" } } }, f.UserId, Ct);
                var provider = Provider(f); Assert.Null(await provider.GetAsync("purchase", draft.Id, f.UserId, true, Ct));
                var posted = await purchases.PostAsync(draft.Id, "print-purchase-post", f.UserId, Ct); Assert.True(posted.Succeeded, posted.ErrorMessage);
                Assert.Equal("40.00", (await provider.GetAsync("purchase", draft.Id, f.UserId, true, Ct)).Fields["grandTotal"]);
                var returned = await purchases.PostReturnAsync(draft.Id, new PurchaseReturnCommand { WarehouseId = 1, ReturnMode = "supplier_credit", Lines = new List<PurchaseReturnLine> { new PurchaseReturnLine { PurchaseInvoiceLineId = posted.Invoice.Lines[0].Id, Quantity = "1" } } }, "print-purchase-return", f.UserId, Ct); Assert.True(returned.Succeeded, returned.ErrorMessage);
                Assert.Equal("10.00", (await provider.GetAsync("purchase-return", returned.Id, f.UserId, true, Ct)).Fields["grandTotal"]);
                var ledger = new SupplierLedgerService(new SqliteSupplierLedgerRepository(f.ConnectionString)); var account = await f.Create("PRINT-PAY"); await f.Post(account.Id, "100");
                var payment = await ledger.AddTreasuryPaymentAsync(supplierId, null, draft.Id, "5", "2026-10-29", "نقدي", "", "", "print-supplier-payment", account.Id, f.UserId, Ct);
                Assert.Equal("5.00", (await provider.GetAsync("supplier-payment", payment.Id, f.UserId, true, Ct)).Fields["grandTotal"]);
                var customer = new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString)); customer.PostTransaction(customerId, "increase", 50, "2026-10-29", "", "print-customer-debt");
                var collection = customer.PostTransaction(customerId, "collection", 12.34m, "2026-10-29", "", "print-customer-payment");
                var receipt = await provider.GetAsync("customer-payment", collection.Id, f.UserId, true, Ct); Assert.Equal("12.34", receipt.Fields["grandTotal"]); Assert.Equal("عميل الطباعة", receipt.Fields["party"]);
                var reduction = customer.PostTransaction(customerId, "reduction", 1, "2026-10-29", "", "print-customer-reduction"); Assert.Null(await provider.GetAsync("customer-payment", reduction.Id, f.UserId, true, Ct));
                var backend = new RecordingBackend(); var service = new PrintingService(new SqlitePrintRepository(f.ConnectionString), provider, backend); await Configure(service, f.UserId);
                foreach (var pair in new[] { Tuple.Create("purchase", draft.Id), Tuple.Create("purchase-return", returned.Id), Tuple.Create("customer-payment", collection.Id), Tuple.Create("supplier-payment", payment.Id) })
                    Assert.Equal("submitted_unknown", (await service.SubmitAsync(Request("doc-" + pair.Item1, type: pair.Item1, id: pair.Item2), f.UserId, true, Ct)).Status);
                Assert.Equal(4, backend.Submissions);
            }
        }
        [Fact]
        public async Task Configuration_templates_profiles_restart_conflict_invalid_paper_and_audit_rollback()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var backend = new RecordingBackend(); var repo = new SqlitePrintRepository(f.ConnectionString); var service = new PrintingService(repo, new Documents(), backend);
                Assert.Equal(8, (await service.SettingsAsync(Ct)).Templates.Count); Assert.Single(await service.PrintersAsync(Ct));
                var s = await Configure(service, f.UserId, true); var copy = PrintingService.BuiltIn("copy", "قالب نسخ"); copy.Paper.Preset = "custom"; copy.Paper.WidthMm = 100; copy.Paper.HeightMm = 150; copy.HeaderText = "مرحبا English"; copy.Sections[0].FontSize = 14;
                s.Templates.Add(copy); s.Profiles[0].TemplateId = copy.Id; s.Profiles[0].Copies = 2; s = await service.SaveAsync(s, f.UserId, Ct);
                var restarted = new PrintingService(new SqlitePrintRepository(f.ConnectionString), new Documents(), backend); var saved = await restarted.SettingsAsync(Ct);
                Assert.True(saved.AutoPrintSale); Assert.Equal("copy", saved.Profiles[0].TemplateId); Assert.Equal(2, saved.Profiles[0].Copies); Assert.Equal("مرحبا English", saved.Templates.Last().HeaderText); Assert.Equal(100, saved.Templates.Last().Paper.WidthMm);
                s.Version--; Assert.Equal("conflict", (await Assert.ThrowsAsync<PrintException>(() => service.SaveAsync(s, f.UserId, Ct))).Code);
                saved.Templates.RemoveAll(t => t.Id == "copy"); await Assert.ThrowsAsync<PrintException>(() => service.SaveAsync(saved, f.UserId, Ct));
                saved = await restarted.SettingsAsync(Ct); saved.Templates.Last().Paper.WidthMm = 220;
                Assert.Equal("unsupported_paper", (await Assert.ThrowsAsync<PrintException>(() => service.SaveAsync(saved, f.UserId, Ct))).Code);
                saved = await restarted.SettingsAsync(Ct); saved.Profiles[0].PrinterName = "gone";
                Assert.Equal("printer_not_found", (await Assert.ThrowsAsync<PrintException>(() => service.SaveAsync(saved, f.UserId, Ct))).Code);
                saved = await restarted.SettingsAsync(Ct); using (var c = f.Open()) c.Execute("CREATE TRIGGER FailPrintingAudit BEFORE INSERT ON PrintingAudit BEGIN SELECT RAISE(ABORT,'test rollback'); END;");
                await Assert.ThrowsAsync<System.Data.SQLite.SQLiteException>(() => service.SaveAsync(saved, f.UserId, Ct));
                Assert.Equal(2, (await restarted.SettingsAsync(Ct)).Version);
            }
        }
        [Fact]
        public async Task Direct_auto_and_intentional_reprints_are_durable_idempotent_and_concurrent_clicks_are_blocked()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var backend = new RecordingBackend(); var repo = new SqlitePrintRepository(f.ConnectionString); var service = new PrintingService(repo, new Documents(), backend);
                Assert.Equal("disabled", (await service.SubmitAsync(Request("off", true), f.UserId, true, Ct)).Status); Assert.Equal(0, backend.Submissions);
                await Configure(service, f.UserId, true);
                var first = await service.SubmitAsync(Request("auto", true), f.UserId, true, Ct); Assert.Equal("submitted_unknown", first.Status);
                var replay = await new PrintingService(repo, new Documents(), backend).SubmitAsync(Request("repeated-callback", true), f.UserId, true, Ct); Assert.True(replay.Replayed); Assert.Equal(first.Id, replay.Id); Assert.Equal(1, backend.Submissions);
                var reprint = await service.SubmitAsync(Request("intentional"), f.UserId, true, Ct); Assert.True(reprint.Reprint); Assert.Equal(2, backend.Submissions);
                Assert.True((await service.SubmitAsync(Request("intentional"), f.UserId, true, Ct)).Replayed);
                Assert.Equal("key_conflict", (await Assert.ThrowsAsync<PrintException>(() => service.SubmitAsync(Request("intentional", id: 2), f.UserId, true, Ct))).Code);
                backend.Entered = new TaskCompletionSource<bool>(); backend.Release = new TaskCompletionSource<bool>();
                var inProgress = service.SubmitAsync(Request("concurrent-1"), f.UserId, true, Ct); await backend.Entered.Task;
                Assert.Equal("print_busy", (await Assert.ThrowsAsync<PrintException>(() => service.SubmitAsync(Request("concurrent-2"), f.UserId, true, Ct))).Code);
                Assert.Equal("print_busy", (await Assert.ThrowsAsync<PrintException>(() => service.ReviewAsync(reprint.Id + 1, f.UserId, Ct))).Code);
                backend.Release.SetResult(true); await inProgress;
                Assert.Equal(3, backend.Submissions);
                var jobs = await repo.JobsAsync(Ct); Assert.Equal(3, jobs.Count); Assert.All(jobs, j => Assert.Equal("submitted_unknown", j.Status));
            }
        }
        [Fact]
        public async Task Failed_auto_print_never_retries_implicitly_and_orphaned_jobs_require_explicit_review()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var backend = new RecordingBackend { Error = "printer_offline" }; var repo = new SqlitePrintRepository(f.ConnectionString); var service = new PrintingService(repo, new Documents(), backend); await Configure(service, f.UserId, true);
                var failed = await service.SubmitAsync(Request("auto", true), f.UserId, true, Ct); Assert.Equal("failed", failed.Status); Assert.Equal("printer_offline", failed.ErrorCode);
                backend.Error = null; Assert.Equal("failed", (await service.SubmitAsync(Request("callback", true), f.UserId, true, Ct)).Status); Assert.Equal(1, backend.Submissions);
                Assert.Equal("submitted_unknown", (await service.SubmitAsync(Request("explicit-retry"), f.UserId, true, Ct)).Status);
                backend.UnknownFailure = true; Assert.Equal("uncertain", (await service.SubmitAsync(Request("unknown"), f.UserId, true, Ct)).Status);
                var orphan = await repo.ClaimAsync(new PrintJob { ActorId = f.UserId, RequestKey = "orphan", DocumentType = "sale", DocumentId = 2, Reference = "S-2", Copies = 1, TemplateId = "builtin-sale", CreatedUtc = DateTime.UtcNow.AddHours(-1).ToString("o"), Status = "accepted", Message = "test" }, Ct);
                await service.ReviewAsync(orphan.Id, f.UserId, Ct); Assert.Equal("uncertain", (await repo.JobsAsync(Ct)).First().Status);
                Assert.Equal("review_not_allowed", (await Assert.ThrowsAsync<PrintException>(() => service.ReviewAsync(orphan.Id, f.UserId, Ct))).Code);
            }
        }
        [Fact]
        public async Task Posted_sale_authoritative_amounts_print_failure_reprint_and_shift_reports_never_change_financial_history()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var catalog = new CatalogService(new SqliteCatalogStore(f.ConnectionString));
                var product = await catalog.CreateProductAsync(new CreateProductCommand { OperationKey = "print-product", Name = "قميص Arabic XL", BaseUnitId = 1, ProductType = "stock", SalePrice = "12.50", PurchasePrice = "9", IsActive = true }, Ct); Assert.True(product.Succeeded, product.ErrorMessage);
                long variant, unit; using (var c = f.Open()) { variant = c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id", new { product.Value.Id }); unit = c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1", new { product.Value.Id }); }
                var stock = await new SqliteInventoryStore(f.ConnectionString).PostAsync(new InventoryPostCommand { OperationKey = "print-stock", Type = "manual_receipt", WarehouseId = 1, UserId = f.UserId, Reason = "test", Lines = new[] { new InventoryLineInput { VariantId = variant, ProductUnitId = unit, Quantity = "5", UnitCost = "9", SalePrice = "12.50" } } }, "print-stock", Ct); Assert.True(stock.Succeeded, stock.ErrorMessage);
                var sales = new SalesService(new SqliteSalesStore(f.ConnectionString)); var draft = await sales.CreateDraftAsync(new SaleDraftCommand { WarehouseId = 1, InvoiceDate = DateTime.Today.ToString("yyyy-MM-dd"), PaymentMode = "cash", Discount = "0.00", Lines = new List<SaleLine> { new SaleLine { VariantId = variant, ProductUnitId = unit, Quantity = "2", UnitPrice = "12.50", Discount = "0.00" } } }, f.UserId, Ct);
                var provider = Provider(f); Assert.Null(await provider.GetAsync("sale", draft.Id, f.UserId, true, Ct));
                var posted = await sales.PostAsync(draft.Id, "print-post", f.UserId, Ct); Assert.True(posted.Succeeded, posted.ErrorMessage); Assert.NotNull(posted.Invoice.PostedUtc);
                var backend = new RecordingBackend { Error = "printer_offline" }; var service = new PrintingService(new SqlitePrintRepository(f.ConnectionString), provider, backend); await Configure(service, f.UserId, true);
                string Snapshot() { using (var c = f.Open()) return string.Join("/", new[] { "SalesInvoices", "SalesPayments", "SalesReturns", "StockMovements", "TreasuryMovements", "CustomerTransactions", "CashierShiftEvents" }.Select(table => c.ExecuteScalar<long>("SELECT COUNT(*) FROM " + table))) + "/" + c.ExecuteScalar<long>("SELECT SUM(TotalScaled) FROM SalesInvoices"); }
                var before = Snapshot(); Assert.Equal("failed", (await service.SubmitAsync(Request("post-auto", true, id: draft.Id), f.UserId, true, Ct)).Status); Assert.Equal("25.00", backend.Document.Fields["grandTotal"]); Assert.Equal("قميص Arabic XL", backend.Document.Items[0].Name); Assert.Contains("ترحيل:", backend.Document.Fields["date"]); Assert.Contains("EGP", backend.Document.Fields["header"]);
                backend.Error = null; var watch = Stopwatch.StartNew();
                for (var i = 0; i < 20; i++) await service.SubmitAsync(Request("measure-" + i, id: draft.Id), f.UserId, true, Ct);
                watch.Stop(); _output.WriteLine("20 persisted-document submissions through recording backend: {0:F2} ms total; {1:F2} ms mean (no Windows/hardware timing).", watch.Elapsed.TotalMilliseconds, watch.Elapsed.TotalMilliseconds / 20);
                Assert.Equal(before, Snapshot()); Assert.Equal("posted", (await sales.GetAsync(draft.Id, Ct)).Status);
                var workforce = WorkforcePersistenceTests.Service(f); var shift = await workforce.ExecuteAsync("shift-open", new WorkforceCommand { Amount = "10", OperationKey = "print-shift" }, f.UserId, true, Ct);
                Assert.NotNull(await provider.GetAsync("shift-summary", shift, f.UserId, false, Ct)); Assert.Null(await provider.GetAsync("shift-summary", shift, f.UserId + 1, false, Ct)); Assert.Null(await provider.GetAsync("shift-closing", shift, f.UserId, true, Ct));
                await workforce.ExecuteAsync("shift-close", new WorkforceCommand { Id = shift, Amount = "9", OperationKey = "close-print-shift" }, f.UserId, true, Ct);
                var report = await provider.GetAsync("shift-closing", shift, f.UserId, true, Ct); Assert.Equal("10.00", report.Fields["grandTotal"]); Assert.Equal("-1.00", report.Fields["remaining"]);
                Assert.Equal("submitted_unknown", (await service.SubmitAsync(Request("shift", type: "shift-closing", id: shift), f.UserId, true, Ct)).Status);
                var returned = await sales.ReturnAsync(draft.Id, new SaleReturnCommand { OperationKey = "print-return", Reason = "اختبار مرتجع", Lines = new List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = posted.Invoice.Lines[0].Id, Quantity = "1" } } }, f.UserId, Ct); Assert.True(returned.Succeeded, returned.ErrorMessage);
                Assert.Equal("12.50", (await provider.GetAsync("sales-return", returned.ReturnId, f.UserId, true, Ct)).Fields["grandTotal"]);
            }
        }
    }
}
