using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Printing;
using AlKhayal.Printing;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class PrintingLayoutTests
    {
        [Theory]
        [InlineData("58mm", 58, 297)]
        [InlineData("80mm", 80, 297)]
        [InlineData("A4", 210, 297)]
        [InlineData("A5", 148, 210)]
        [InlineData("custom", 100, 150)]
        public async Task Arabic_mixed_text_exact_amounts_and_long_receipts_layout_and_rasterize_without_browser(string preset, int width, int height)
        {
            var template = PrintingService.BuiltIn("layout-test", "قالب عربي"); template.Paper.Preset = preset; template.Paper.WidthMm = width; template.Paper.HeightMm = height;
            PrintingService.ValidateTemplate(template);
            var doc = PrintingService.Sample(); doc.Fields["grandTotal"] = "123456789.12";
            for (var i = 0; i < 100; i++) doc.Items.Add(new PrintItem { Name = "وصف صنف طويل Arabic English SKU-123 مع ألوان ومقاسات متعددة " + new string('س', 100), Variant = "XL / أزرق", Quantity = "1.25", UnitPrice = "25.50", Total = "31.88" });
            var renderer = new NativePrintRenderer();
            using (var bitmap = new Bitmap(10, 10)) using (var g = Graphics.FromImage(bitmap))
            {
                g.PageUnit = GraphicsUnit.Millimeter;
                var availableWidth = width - 6; var rows = renderer.Layout(g, doc, template, availableWidth);
                Assert.Contains(rows.SelectMany(r => r.Cells), c => c.Text == "كمية");
                Assert.Contains(rows.SelectMany(r => r.Cells), c => c.Text.Contains("English"));
                Assert.Contains(rows.SelectMany(r => r.Cells), c => c.Text.Contains("125.50"));
                Assert.All(rows.SelectMany(r => r.Cells), c => { Assert.True(c.Xmm >= -.01); Assert.True(c.Xmm + c.WidthMm <= availableWidth + .01); });
                var pages = NativePrintRenderer.Paginate(rows, height - 6); Assert.True(pages.Count > 1);
                Assert.Equal(rows.Count, pages.Sum(p => p.Count)); Assert.All(pages, p => Assert.True(p.Sum(r => r.HeightMm) <= height - 6 + .01));
                Assert.All(rows.Where(r => !r.Logo && !r.Divider).SelectMany(r => r.Cells), c => Assert.NotNull(c.Text));
            }
            var bytes = await new WindowsPrinterBackend(renderer).PreviewAsync(PrintingService.Sample(), template, CancellationToken.None);
            Assert.Equal(137, bytes[0]); Assert.Equal(80, bytes[1]);
            using (var stream = new MemoryStream(bytes)) using (var image = Image.FromStream(stream)) { Assert.True(image.Width > 100); Assert.True(image.Height > image.Width); }
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "printing-preview-" + preset + ".png"), bytes);
        }
        [Fact]
        public void Invalid_templates_and_unrenderable_logo_are_rejected()
        {
            var t = PrintingService.BuiltIn("safe", "آمن"); t.Paper.WidthMm = 1;
            Assert.Throws<PrintException>(() => PrintingService.ValidateTemplate(t));
            t = PrintingService.BuiltIn("safe", "آمن"); t.Sections.RemoveAt(0); Assert.Throws<PrintException>(() => PrintingService.ValidateTemplate(t));
            t = PrintingService.BuiltIn("safe", "آمن"); t.Sections[0].Key = "<script>"; Assert.Throws<PrintException>(() => PrintingService.ValidateTemplate(t));
            Assert.Throws<PrintException>(() => NativePrintRenderer.DecodeLogo("not-a-png"));
            Assert.Throws<PrintException>(() => NativePrintRenderer.Paginate(new System.Collections.Generic.List<PrintRow> { new PrintRow { HeightMm = 100 } }, 20));
        }
        [Fact]
        public void Narrow_receipts_preserve_long_exact_quantities_and_amounts_in_stacked_fields()
        {
            var t = PrintingService.BuiltIn("numeric", "قيم دقيقة"); t.Paper.Preset = "58mm"; t.Paper.WidthMm = 58;
            var d = PrintingService.Sample(); d.Items[0].Quantity = "123456.123456"; d.Items[0].Total = "922337203685477.58";
            using (var b = new Bitmap(10, 10)) using (var g = Graphics.FromImage(b))
            {
                g.PageUnit = GraphicsUnit.Millimeter; var cells = new NativePrintRenderer().Layout(g, d, t, 52).SelectMany(r => r.Cells).ToList();
                Assert.Contains(cells, c => c.Text == "123456.123456"); Assert.Contains(cells, c => c.Text == "922337203685477.58");
            }
        }
        [Fact]
        public void Driver_status_distinguishes_normal_queue_activity_from_device_errors()
        {
            WindowsPrinterBackend.ValidateReportedStatus(0);
            WindowsPrinterBackend.ValidateReportedStatus(0x100 | 0x200 | 0x400 | 0x4000);
            Assert.Equal("printer_offline", Assert.Throws<PrintException>(() => WindowsPrinterBackend.ValidateReportedStatus(0x80)).Code);
            foreach (var status in new[] { 1, 2, 8, 16, 0x800, 0x100000, 0x400000 }) Assert.Equal("printer_error", Assert.Throws<PrintException>(() => WindowsPrinterBackend.ValidateReportedStatus(status)).Code);
        }
        [Fact]
        public void Physical_units_round_trip_within_driver_resolution()
        {
            foreach (var mm in new[] { 58, 80, 148, 210, 297 }) Assert.InRange(Math.Abs(WindowsPrinterBackend.Mm(WindowsPrinterBackend.Hundredths(mm)) - mm), 0, .13);
        }
    }
}
