using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Printing;

namespace AlKhayal.Printing
{
    public sealed partial class WindowsPrinterBackend
    {
        private readonly ILabelRenderer _labels = new NativeLabelRenderer();
        private static PrintPaper LabelPaper(LabelTemplate t) => new PrintPaper { Preset = "custom", WidthMm = t.Stock.PageWidthMm, HeightMm = t.Stock.PageHeightMm, Landscape = t.Stock.Landscape, MarginLeftMm = t.Stock.LeftMm, MarginRightMm = t.Stock.RightMm, MarginTopMm = t.Stock.TopMm, MarginBottomMm = t.Stock.BottomMm };
        public Task ValidateLabelsAsync(LabelPrinterProfile p, LabelTemplate t, CancellationToken ct) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested(); LabelService.ValidateTemplate(t);
            using (var doc = Create(p.PrinterName, p.Copies, LabelPaper(t), (float)t.Stock.PageHeightMm)) { CheckStatus(p.PrinterName); }
            if (!string.IsNullOrEmpty(t.LogoPngBase64)) using (var image = NativePrintRenderer.DecodeLogo(t.LogoPngBase64)) { }
        }, ct);
        public Task SubmitLabelsAsync(LabelBatch batch, LabelTemplate t, LabelPrinterProfile p, CancellationToken ct) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using (var doc = Create(p.PrinterName, p.Copies, LabelPaper(t), (float)t.Stock.PageHeightMm))
            {
                CheckStatus(p.PrinterName); var placements = _labels.Layout(batch, t); var pages = placements.Max(x => x.Page) + 1;
                using (var measure = doc.PrinterSettings.CreateMeasurementGraphics(doc.DefaultPageSettings))
                { var a = doc.DefaultPageSettings.PrintableArea; _labels.Validate(measure, batch, t, new RectangleF(Mm(a.X), Mm(a.Y), Mm(a.Width), Mm(a.Height))); }
                var page = 0; doc.DocumentName = "AlKhayal Product Labels"; doc.PrintController = new StandardPrintController();
                doc.PrintPage += (sender, e) =>
                {
                    foreach (var cell in placements.Where(x => x.Page == page)) _labels.Draw(e.Graphics, cell.Label, t, cell.X - Mm(e.PageSettings.HardMarginX), cell.Y - Mm(e.PageSettings.HardMarginY));
                    page++; e.HasMorePages = page < pages;
                };
                try { doc.Print(); }
                catch (Exception ex) { throw new InvalidOperationException("Label submission may have partially reached the spooler.", ex); }
            }
        }, ct);
        public Task<byte[]> PreviewLabelsAsync(LabelBatch batch, LabelTemplate t, CancellationToken ct) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested(); var s = t.Stock; var width = (float)(s.Landscape ? s.PageHeightMm : s.PageWidthMm); var height = (float)(s.Landscape ? s.PageWidthMm : s.PageHeightMm);
            const float dpi = 300; var pixelWidth = (int)Math.Ceiling(width / 25.4f * dpi); var pixelHeight = (int)Math.Ceiling(height / 25.4f * dpi);
            if ((long)pixelWidth * pixelHeight > 16000000) throw new PrintException("preview_too_large", "الصفحة أكبر من حد المعاينة الآمن. استخدم صفحة أقصر للمعاينة؛ الطباعة تعتمد إمكانات التعريف.");
            using (var image = new Bitmap(pixelWidth, pixelHeight))
            {
                image.SetResolution(dpi, dpi);
                using (var g = Graphics.FromImage(image))
                {
                    g.Clear(Color.White); _labels.Validate(g, batch, t, new RectangleF(0, 0, width, height));
                    foreach (var placement in _labels.Layout(batch, t).Where(x => x.Page == 0)) _labels.Draw(g, placement.Label, t, placement.X, placement.Y);
                }
                using (var stream = new MemoryStream()) { image.Save(stream, ImageFormat.Png); return stream.ToArray(); }
            }
        }, ct);
    }
}
