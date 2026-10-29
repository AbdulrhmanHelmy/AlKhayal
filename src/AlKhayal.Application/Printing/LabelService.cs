using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;

namespace AlKhayal.Application.Printing
{
    public sealed class LabelService
    {
        public static readonly string[] ElementKeys = { "productName", "company", "productCode", "sku", "barcode", "barcodeText", "price", "unit", "category", "size", "color", "static", "logo" };
        private readonly ILabelRepository _store;
        private readonly ILabelPrinterBackend _backend;
        private readonly PrintingService _printing;
        public LabelService(ILabelRepository store, ILabelPrinterBackend backend, PrintingService printing) { _store = store; _backend = backend; _printing = printing; }
        public Task<LabelSettings> SettingsAsync(CancellationToken ct) => Read(ct);
        private async Task<LabelSettings> Read(CancellationToken ct) => await _store.ReadAsync(ct).ConfigureAwait(false) ?? Defaults();
        public static LabelTemplate BuiltIn(string id = "label-basic", decimal width = 50, decimal height = 30)
        {
            var t = new LabelTemplate { Id = id, Name = "ملصق " + width + " × " + height, Stock = new LabelStock { LabelWidthMm = width, LabelHeightMm = height, PageWidthMm = width, PageHeightMm = height } };
            var ratio = height / 30m;
            t.Elements.Add(new LabelElement { Key = "productName", X = 1, Y = 1, Width = width - 2, Height = 6 * ratio, FontSize = height <= 20 ? 6 : 8, Bold = true });
            t.Elements.Add(new LabelElement { Key = "barcode", X = 1, Y = 8 * ratio, Width = width - 2, Height = 10 * ratio });
            t.Elements.Add(new LabelElement { Key = "barcodeText", X = 1, Y = 18 * ratio, Width = width - 2, Height = 4 * ratio, FontSize = height <= 20 ? 6 : 8, Alignment = "center" });
            t.Elements.Add(new LabelElement { Key = "price", X = 1, Y = 23 * ratio, Width = width - 2, Height = 5 * ratio, FontSize = height <= 20 ? 7 : 9, Bold = true, Alignment = "center" });
            foreach (var key in ElementKeys.Where(k => t.Elements.All(e => e.Key != k))) t.Elements.Add(new LabelElement { Key = key, Visible = false, X = 1, Y = 1, Width = width - 2, Height = Math.Min(5, height - 2) });
            return t;
        }
        public static LabelSettings Defaults()
        {
            var s = new LabelSettings(); s.Templates.Add(BuiltIn()); s.Profiles.Add(new LabelPrinterProfile { Id = "labels-default", Name = "طابعة الملصقات الافتراضية", TemplateId = "label-basic" }); return s;
        }
        public static void ValidateTemplate(LabelTemplate t)
        {
            if (t == null || !Id(t.Id) || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 100 || (t.LogoPngBase64 ?? "").Length > 180000) throw Invalid();
            var s = t.Stock;
            if (s == null || s.LabelWidthMm < 10 || s.LabelWidthMm > 420 || s.LabelHeightMm < 10 || s.LabelHeightMm > 1000 || s.PageWidthMm < 10 || s.PageWidthMm > 420 || s.PageHeightMm < 10 || s.PageHeightMm > 3000 || s.Columns < 1 || s.Columns > 20 || s.Rows < 1 || s.Rows > 100 || s.Columns * s.Rows > 500 || s.Continuous && (s.Columns != 1 || s.Rows != 1 || s.Landscape)) throw Invalid();
            if (new[] { s.LeftMm, s.RightMm, s.TopMm, s.BottomMm, s.HorizontalGapMm, s.VerticalGapMm }.Any(n => n < 0 || n > 100)) throw Invalid();
            var width = s.Landscape ? s.PageHeightMm : s.PageWidthMm; var height = s.Landscape ? s.PageWidthMm : s.PageHeightMm;
            if (s.Continuous && s.TopMm + s.LabelHeightMm + s.VerticalGapMm + s.BottomMm > height) throw Invalid("ارتفاع صفحة تعريف الرول يجب أن يشمل الملصق والفاصل الرأسي والهوامش؛ الصفحة هي خطوة تغذية الملصق.");
            if (s.LeftMm + s.RightMm + s.Columns * s.LabelWidthMm + (s.Columns - 1) * s.HorizontalGapMm > width || s.TopMm + s.BottomMm + s.Rows * s.LabelHeightMm + (s.Rows - 1) * s.VerticalGapMm > height) throw Invalid("خلايا الملصقات والهوامش والفواصل تتجاوز أبعاد الصفحة.");
            if (t.Elements == null || t.Elements.Count != ElementKeys.Length || t.Elements.Any(e => e == null) || t.Elements.Select(e => e.Key).Distinct().Count() != ElementKeys.Length) throw Invalid();
            foreach (var e in t.Elements)
                if (!ElementKeys.Contains(e.Key) || e.X < 0 || e.Y < 0 || e.Width <= 0 || e.Height <= 0 || e.X + e.Width > s.LabelWidthMm || e.Y + e.Height > s.LabelHeightMm || e.FontSize < 5 || e.FontSize > 36 || e.ModuleMm < .15m || e.ModuleMm > 1.2m || (e.Text ?? "").Length > 300 || !new[] { "right", "left", "center" }.Contains(e.Alignment)) throw Invalid("راجع موضع العنصر وأبعاده وخطه داخل الملصق.");
            // The barcode rectangle includes mandatory quiet zones; other elements cannot cover it.
            var bar = t.Elements.Single(e => e.Key == "barcode");
            if (bar.Visible && t.Elements.Any(e => e.Visible && e != bar && e.X < bar.X + bar.Width && e.X + e.Width > bar.X && e.Y < bar.Y + bar.Height && e.Y + e.Height > bar.Y)) throw Invalid("لا تضع نصًا أو شعارًا فوق الباركود أو مساحة الهدوء المحيطة به.");
            if (t.Elements.All(e => !e.Visible)) throw Invalid();
        }
        public async Task<LabelSettings> SaveAsync(LabelSettings s, long actor, CancellationToken ct)
        {
            if (s == null || s.Version < 0 || s.Templates == null || s.Templates.Count < 1 || s.Templates.Count > 40 || s.Templates.Any(t => t == null) || s.Templates.Select(t => t.Id).Distinct().Count() != s.Templates.Count || s.Profiles == null || s.Profiles.Count < 1 || s.Profiles.Count > 20 || s.Profiles.Any(p => p == null) || s.Profiles.Select(p => p.Id).Distinct().Count() != s.Profiles.Count || !s.Profiles.Any(p => p.Id == s.DefaultProfileId)) throw Invalid();
            foreach (var t in s.Templates) ValidateTemplate(t);
            foreach (var p in s.Profiles)
            {
                if (!Id(p.Id) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100 || (p.PrinterName ?? "").Length > 250 || p.Copies < 1 || p.Copies > 10 || !s.Templates.Any(t => t.Id == p.TemplateId)) throw Invalid();
                if (!string.IsNullOrWhiteSpace(p.PrinterName)) await _backend.ValidateLabelsAsync(p, s.Templates.Single(t => t.Id == p.TemplateId), ct).ConfigureAwait(false);
            }
            return await _store.SaveAsync(s, actor, ct).ConfigureAwait(false);
        }
        public Task<byte[]> PreviewAsync(LabelTemplate t, CancellationToken ct) { ValidateTemplate(t); return _backend.PreviewLabelsAsync(Sample(), t, ct); }
        public static LabelBatch Sample() => new LabelBatch { Items = new List<ProductLabel> { new ProductLabel { ProductId = 1, VariantId = 1, BarcodeId = 1, Barcode = "2912345678", Format = "code128", Quantity = 2, Fields = new Dictionary<string, string> { ["productName"] = "قميص عربي English", ["company"] = "الخيال", ["productCode"] = "P-000001", ["sku"] = "SHIRT-XL", ["price"] = "125.50 ج.م", ["unit"] = "قطعة", ["category"] = "ملابس", ["size"] = "XL", ["color"] = "أزرق" } } } };
        public async Task<PrintJob> SubmitAsync(LabelBatchRequest r, long actor, CancellationToken ct)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.OperationKey) || r.OperationKey.Length > 100 || r.Items == null || r.Items.Count < 1 || r.Items.Count > 200 || r.Items.Any(i => i == null || i.BarcodeId <= 0 || i.Quantity < 1 || i.Quantity > 1000 || i.Format != "code128" && i.Format != "ean13") || r.Items.Select(i => i.BarcodeId).Distinct().Count() != r.Items.Count || r.Items.Sum(i => i.Quantity) > 2000 || r.StartCell < 0 || (r.ProfileId ?? "").Length > 80 || (r.TemplateId ?? "").Length > 80) throw Invalid("اختر حتى 200 باركود وبحد أقصى 2000 ملصق، وكمية موجبة لكل بند.");
            var canonical = r.ProfileId + "|" + r.TemplateId + "|" + r.StartCell + "|" + r.Reprint + "|" + string.Join(";", r.Items.Select(i => i.BarcodeId + ":" + i.Quantity + ":" + i.Format));
            string hash; using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
            var requestKey = "labels:" + actor + ":" + r.OperationKey; var replay = await _printing.ReplayAsync(requestKey, hash, ct).ConfigureAwait(false); if (replay != null) return replay;
            var s = await Read(ct).ConfigureAwait(false); var p = s.Profiles.SingleOrDefault(x => x.Id == (string.IsNullOrEmpty(r.ProfileId) ? s.DefaultProfileId : r.ProfileId)); if (p == null) throw Invalid();
            var t = s.Templates.SingleOrDefault(x => x.Id == (string.IsNullOrEmpty(r.TemplateId) ? p.TemplateId : r.TemplateId)); if (t == null || r.StartCell >= t.Stock.Columns * t.Stock.Rows || r.Items.Sum(i => i.Quantity) * p.Copies > 2000) throw Invalid();
            var data = await _store.ProductsAsync(r.Items, ct).ConfigureAwait(false);
            var batch = new LabelBatch { Items = data, StartCell = r.StartCell };
            var summary = "{\"settingsVersion\":" + s.Version + ",\"startCell\":" + r.StartCell + ",\"items\":[" + string.Join(",", r.Items.Select(i => "{\"barcodeId\":" + i.BarcodeId + ",\"quantity\":" + i.Quantity + ",\"format\":\"" + i.Format + "\"}")) + "]}";
            var job = new PrintJob { RequestKey = requestKey, RequestHash = hash, BatchSummaryJson = summary, DocumentType = "labels", DocumentId = actor, ActorId = actor, Reference = "LABELS-" + r.Items.Sum(i => i.Quantity), PrinterName = p.PrinterName, TemplateId = t.Id, Copies = p.Copies, Reprint = r.Reprint, CreatedUtc = DateTime.UtcNow.ToString("o"), Status = "accepted", Message = "قُبل طلب دفعة الملصقات." };
            return await _printing.SubmitOutputAsync(job, async () => { await _backend.ValidateLabelsAsync(p, t, CancellationToken.None).ConfigureAwait(false); await _backend.SubmitLabelsAsync(batch, t, p, CancellationToken.None).ConfigureAwait(false); }, ct).ConfigureAwait(false);
        }
        public async Task<PrintJob> TestAsync(string profileId, string operationKey, long actor, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 100) throw Invalid();
            var s = await Read(ct).ConfigureAwait(false); var p = s.Profiles.SingleOrDefault(x => x.Id == profileId); if (p == null) throw Invalid(); var t = s.Templates.Single(x => x.Id == p.TemplateId);
            return await _printing.SubmitOutputAsync(new PrintJob { RequestKey = "label-test:" + actor + ":" + operationKey, RequestHash = "label-test:" + profileId, DocumentType = "label-test", DocumentId = actor, ActorId = actor, Reference = "LABEL-TEST", PrinterName = p.PrinterName, TemplateId = t.Id, Copies = p.Copies, CreatedUtc = DateTime.UtcNow.ToString("o"), Status = "accepted", Message = "اختبار ملصق غير تجاري." }, async () => { await _backend.ValidateLabelsAsync(p, t, CancellationToken.None).ConfigureAwait(false); await _backend.SubmitLabelsAsync(Sample(), t, p, CancellationToken.None).ConfigureAwait(false); }, ct).ConfigureAwait(false);
        }
        private static bool Id(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 80 && id.All(c => char.IsLetterOrDigit(c) || c == '-');
        private static PrintException Invalid(string message = "راجع قالب الملصق وأبعاد الورق وإعدادات الطابعة.") => new PrintException("validation", message);
    }
}
