using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Printing;
using ZXing.OneD;

namespace AlKhayal.Printing
{
    public static class NativeBarcode
    {
        public static bool[] Modules(string value, string format)
        {
            BarcodeRules.Validate(value, format);
            var raw = format == "ean13" ? new EAN13Writer().encode(value) : new Code128Writer().encode(value);
            var quiet = format == "ean13" ? 11 : 10; var modules = new bool[raw.Length + quiet * 2]; Array.Copy(raw, 0, modules, quiet, raw.Length); return modules;
        }
        public static int ModuleDots(decimal moduleMm, float dpi) => Math.Max(2, (int)Math.Ceiling((float)moduleMm * dpi / 25.4f));
        public static void Validate(bool[] modules, LabelElement e, float dpi)
        {
            if (modules.Length * ModuleDots(e.ModuleMm, dpi) > Math.Floor((float)e.Width * dpi / 25.4f)) throw new PrintException("barcode_too_wide", "عرض الباركود لا يكفي للوحدات ومساحات الهدوء عند دقة الطابعة. وسّع العنصر/الملصق أو اختر كودًا أقصر.");
            if (e.Height < 4) throw new PrintException("barcode_too_short", "ارتفاع الباركود يجب ألا يقل عن 4 مم؛ تحقق من المسح على الجهاز الفعلي.");
        }
        public static void Draw(Graphics g, bool[] modules, LabelElement e, float x, float y)
        {
            Validate(modules, e, g.DpiX); var state = g.Save();
            try
            {
                g.PageUnit = GraphicsUnit.Pixel; g.SmoothingMode = SmoothingMode.None; g.PixelOffsetMode = PixelOffsetMode.None;
                var dots = ModuleDots(e.ModuleMm, g.DpiX); var available = (int)Math.Floor((float)e.Width * g.DpiX / 25.4f);
                var left = (int)Math.Round(x * g.DpiX / 25.4f) + (available - modules.Length * dots) / 2;
                var top = (int)Math.Round(y * g.DpiY / 25.4f); var height = (int)Math.Round((float)e.Height * g.DpiY / 25.4f);
                g.FillRectangle(Brushes.White, left, top, modules.Length * dots, height);
                for (var i = 0; i < modules.Length;)
                { if (!modules[i]) { i++; continue; } var end = i + 1; while (end < modules.Length && modules[end]) end++; g.FillRectangle(Brushes.Black, left + i * dots, top, (end - i) * dots, height); i = end; }
            }
            finally { g.Restore(state); }
        }
    }
    public sealed class LabelPlacement
    {
        public ProductLabel Label { get; set; }
        public int Page { get; set; }
        public int Cell { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }
    public interface ILabelRenderer
    {
        List<LabelPlacement> Layout(LabelBatch batch, LabelTemplate template);
        void Validate(Graphics g, LabelBatch batch, LabelTemplate template, RectangleF printableMm);
        void Draw(Graphics g, ProductLabel label, LabelTemplate template, float x, float y);
    }
    public sealed class NativeLabelRenderer : ILabelRenderer
    {
        public List<LabelPlacement> Layout(LabelBatch batch, LabelTemplate t)
        {
            LabelService.ValidateTemplate(t); var result = new List<LabelPlacement>(); var s = t.Stock; var index = batch.StartCell; var capacity = s.Columns * s.Rows;
            if (index < 0 || index >= capacity || batch.Items == null || batch.Items.Count == 0 || batch.Items.Any(x => x.Quantity <= 0) || batch.Items.Sum(x => x.Quantity) > 2000) throw new PrintException("validation", "دفعة الملصقات أو خلية البداية غير صالحة.");
            foreach (var label in batch.Items)
            for (var copy = 0; copy < label.Quantity; copy++, index++)
            { var cell = index % capacity; result.Add(new LabelPlacement { Label = label, Page = index / capacity, Cell = cell, X = (float)(s.LeftMm + cell % s.Columns * (s.LabelWidthMm + s.HorizontalGapMm)), Y = (float)(s.TopMm + cell / s.Columns * (s.LabelHeightMm + s.VerticalGapMm)) }); }
            return result;
        }
        public void Validate(Graphics g, LabelBatch batch, LabelTemplate t, RectangleF printable)
        {
            g.PageUnit = GraphicsUnit.Millimeter;
            var checkedLabels = new HashSet<ProductLabel>();
            foreach (var placement in Layout(batch, t))
            {
            foreach (var e in t.Elements.Where(x => x.Visible))
            {
                var rect = new RectangleF(placement.X + (float)e.X, placement.Y + (float)e.Y, (float)e.Width, (float)e.Height);
                const float tolerance = .05f;
                if (rect.Left < printable.Left - tolerance || rect.Top < printable.Top - tolerance || rect.Right > printable.Right + tolerance || rect.Bottom > printable.Bottom + tolerance) throw new PrintException("unprintable_label", "عنصر الملصق يتجاوز المساحة القابلة للطباعة في التعريف. راجع الهوامش والخلايا والمقاس.");
                if (checkedLabels.Contains(placement.Label)) continue;
                if (e.Key == "barcode") NativeBarcode.Validate(NativeBarcode.Modules(placement.Label.Barcode, placement.Label.Format), e, g.DpiX);
                else if (e.Key == "logo") { if (!string.IsNullOrEmpty(t.LogoPngBase64)) using (var image = NativePrintRenderer.DecodeLogo(t.LogoPngBase64)) { } }
                else { using (var font = Fit(g, Text(placement.Label, e), e)) { } }
            }
                checkedLabels.Add(placement.Label);
            }
        }
        public void Draw(Graphics g, ProductLabel label, LabelTemplate t, float x, float y)
        {
            g.PageUnit = GraphicsUnit.Millimeter;
            foreach (var e in t.Elements.Where(e => e.Visible))
            {
                var rect = new RectangleF(x + (float)e.X, y + (float)e.Y, (float)e.Width, (float)e.Height);
                if (e.Key == "barcode") { NativeBarcode.Draw(g, NativeBarcode.Modules(label.Barcode, label.Format), e, rect.X, rect.Y); continue; }
                if (e.Key == "logo") { if (!string.IsNullOrEmpty(t.LogoPngBase64)) using (var image = NativePrintRenderer.DecodeLogo(t.LogoPngBase64)) g.DrawImage(image, rect); continue; }
                var text = Text(label, e); using (var font = Fit(g, text, e)) using (var format = Format(e))
                { g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.DrawString(text, font, Brushes.Black, rect, format); }
            }
        }
        private static string Text(ProductLabel label, LabelElement e)
        { if (e.Key == "static") return e.Text ?? ""; if (e.Key == "barcodeText") return label.Barcode; string value; return label.Fields.TryGetValue(e.Key, out value) ? value ?? "" : ""; }
        private static StringFormat Format(LabelElement e)
        {
            var rtl = e.Key != "barcodeText" && e.Key != "sku" && e.Key != "productCode";
            var f = new StringFormat(StringFormat.GenericDefault) { FormatFlags = rtl ? StringFormatFlags.DirectionRightToLeft : 0, Trimming = StringTrimming.None };
            f.Alignment = e.Alignment == "center" ? StringAlignment.Center : e.Alignment == "right" ? (rtl ? StringAlignment.Near : StringAlignment.Far) : (rtl ? StringAlignment.Far : StringAlignment.Near); return f;
        }
        private static Font Fit(Graphics g, string text, LabelElement e)
        {
            using (var format = Format(e))
            for (var size = (float)e.FontSize; size >= 5; size -= .25f)
            {
                var font = new Font("Tahoma", size, e.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                // Measure using the exact rectangle and format drawn; no hidden ellipsis or clipped label text.
                int chars, lines; g.MeasureString(text, font, new SizeF((float)e.Width, (float)e.Height), format, out chars, out lines);
                var measured = g.MeasureString(text, font, new SizeF((float)e.Width, 10000), format);
                if (chars == text.Length && measured.Height <= (float)e.Height + .02f) return font;
                font.Dispose();
            }
            throw new PrintException("label_text_overflow", "النص لا يلائم العنصر حتى بخط 5 نقاط. وسّع العنصر أو عدّل القالب قبل الطباعة.");
        }
    }
}
