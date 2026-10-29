using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Globalization;
using System.Linq;
using AlKhayal.Application.Printing;

namespace AlKhayal.Printing
{
    public sealed class PrintCell
    {
        public string Text { get; set; }
        public float Xmm { get; set; }
        public float WidthMm { get; set; }
        public bool Numeric { get; set; }
        public float FontPoints { get; set; }
        public bool? Bold { get; set; }
        public string Alignment { get; set; }
    }
    public sealed class PrintRow
    {
        public List<PrintCell> Cells { get; } = new List<PrintCell>();
        public float HeightMm { get; set; }
        public float FontPoints { get; set; }
        public bool Bold { get; set; }
        public string Alignment { get; set; }
        public bool Divider { get; set; }
        public bool Logo { get; set; }
    }
    public interface IPrintRenderer
    {
        List<PrintRow> Layout(Graphics graphics, PrintDocumentData data, PrintTemplate template, float widthMm);
        void Draw(Graphics graphics, PrintRow row, float xMm, float yMm, PrintTemplate template);
    }
    // GDI+ shapes Arabic through the installed Windows font; no printer-resident text fonts are used.
    public sealed class NativePrintRenderer : IPrintRenderer
    {
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string> { ["reference"] = "رقم المستند", ["date"] = "التاريخ", ["cashier"] = "المستخدم", ["party"] = "الطرف", ["taxInfo"] = "الرقم الضريبي", ["subtotal"] = "قبل الخصم", ["discountTotal"] = "إجمالي الخصم", ["taxTotal"] = "الضريبة", ["grandTotal"] = "الإجمالي", ["paid"] = "المدفوع", ["remaining"] = "المتبقي / التسوية" };
        public List<PrintRow> Layout(Graphics g, PrintDocumentData d, PrintTemplate t, float width)
        {
            var rows = new List<PrintRow>();
            foreach (var s in t.Sections.Where(s => s.Visible))
            {
                var size = (float)(s.FontSize * t.Paper.Scale); var gap = (float)s.SpacingMm;
                if (s.Key == "logo")
                {
                    if (!string.IsNullOrEmpty(t.LogoPngBase64)) { using (var logo = DecodeLogo(t.LogoPngBase64)) { } rows.Add(new PrintRow { Logo = true, Alignment = s.Alignment, HeightMm = (float)t.LogoHeightMm + gap, Cells = { new PrintCell { WidthMm = width } } }); }
                    continue;
                }
                if (s.Key == "items")
                {
                    var nameWidth = width * (float)t.NameColumnPercent / 100;
                    var qtyWidth = width * (float)t.QuantityColumnPercent / 100;
                    var totalWidth = width - nameWidth - qtyWidth;
                    AddColumns(rows, g, new[] { "الصنف", Visible(t, "quantity") ? "كمية" : "", Visible(t, "lineTotal") ? "القيمة" : "" }, new[] { nameWidth, qtyWidth, totalWidth }, width, size, true, s.Alignment, gap);
                    foreach (var item in d.Items)
                    {
                        AddColumns(rows, g, new[] { item.Name, Visible(t, "quantity") ? item.Quantity : "", Visible(t, "lineTotal") ? item.Total : "" }, new[] { nameWidth, qtyWidth, totalWidth }, width, size, s.Bold, s.Alignment, gap, new[] { s, t.Sections.Single(x => x.Key == "quantity"), t.Sections.Single(x => x.Key == "lineTotal") }, (float)t.Paper.Scale);
                        foreach (var detail in t.Sections.Where(x => x.Visible && new[] { "variant", "unitPrice", "discount", "tax" }.Contains(x.Key)))
                        {
                            var detailValue = detail.Key == "variant" ? item.Variant : detail.Key == "unitPrice" ? item.UnitPrice : detail.Key == "discount" ? item.Discount : item.Tax;
                            if (string.IsNullOrWhiteSpace(detailValue)) continue;
                            var prefix = detail.Key == "unitPrice" ? "السعر: " : detail.Key == "discount" ? "خصم: " : detail.Key == "tax" ? "ضريبة: " : "";
                            var detailSize = (float)(detail.FontSize * t.Paper.Scale);
                            foreach (var line in Wrap(g, prefix + detailValue, nameWidth, detailSize, detail.Bold))
                                rows.Add(new PrintRow { FontPoints = detailSize, Bold = detail.Bold, Alignment = detail.Alignment, HeightMm = LineHeight(detailSize) + (float)detail.SpacingMm, Cells = { new PrintCell { Text = line, Xmm = width - nameWidth, WidthMm = nameWidth } } });
                        }
                        if (t.Dividers) rows.Add(new PrintRow { Divider = true, HeightMm = 1, Cells = { new PrintCell { WidthMm = width } } });
                    }
                    continue;
                }
                if (new[] { "variant", "unitPrice", "discount", "tax", "quantity", "lineTotal" }.Contains(s.Key)) continue;
                string value; d.Fields.TryGetValue(s.Key, out value);
                if (s.Key == "header") value = Join(value, t.HeaderText);
                if (s.Key == "footer") value = Join(value, t.FooterText);
                if (s.Key == "policy") value = t.ReturnPolicy;
                if (s.Key == "reference") value = d.Reference;
                if (s.Key == "title") value = d.Title;
                if (string.IsNullOrWhiteSpace(value)) continue;
                var label = Labels.ContainsKey(s.Key) ? Labels[s.Key] : null;
                if (d.Type.StartsWith("shift-", StringComparison.Ordinal))
                { if (s.Key == "grandTotal") label = "المتوقع"; if (s.Key == "paid") label = "المعدود"; if (s.Key == "remaining") label = "الفرق"; }
                if (label != null) value = label + ": " + value;
                var lines = Wrap(g, value, width, size, s.Bold);
                for (var i = 0; i < lines.Count; i++) rows.Add(new PrintRow { FontPoints = size, Bold = s.Bold, Alignment = s.Alignment, HeightMm = LineHeight(size) + (i == lines.Count - 1 ? gap : 0), Cells = { new PrintCell { Text = lines[i], WidthMm = width } } });
            }
            if (rows.Count == 0) throw new PrintException("empty_template", "القالب لا يحتوي محتوى ظاهرًا.");
            return rows;
        }
        public static bool Visible(PrintTemplate t, string key) => t.Sections.Any(s => s.Key == key && s.Visible);
        private static string Join(string a, string b) => string.IsNullOrWhiteSpace(a) ? b : string.IsNullOrWhiteSpace(b) ? a : a + "\n" + b;
        public static float LineHeight(float points) => points * 25.4f / 72 * 1.5f;
        private static Font Font(float size, bool bold) => new Font("Tahoma", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        private static StringFormat Format(string align, bool numeric = false)
        {
            var f = new StringFormat(StringFormat.GenericTypographic);
            f.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces | (numeric ? 0 : StringFormatFlags.DirectionRightToLeft);
            f.Alignment = align == "center" ? StringAlignment.Center : align == "left" ? StringAlignment.Far : StringAlignment.Near;
            // DirectionRightToLeft reverses Near/Far. Numeric cells explicitly align to their right edge.
            if (numeric) f.Alignment = align == "center" ? StringAlignment.Center : align == "left" ? StringAlignment.Near : StringAlignment.Far;
            f.LineAlignment = StringAlignment.Near; return f;
        }
        private static List<string> Wrap(Graphics g, string text, float widthMm, float size, bool bold)
        {
            var result = new List<string>();
            using (var font = Font(size, bold)) using (var fmt = Format("right"))
            {
                foreach (var paragraph in (text ?? "").Replace("\r", "").Split('\n'))
                {
                    var remaining = paragraph; if (remaining.Length == 0) { result.Add(""); continue; }
                    while (remaining.Length > 0)
                    {
                        // Binary-search a measured line. Graphics must use millimetres, as Draw does.
                        var lo = 1; var hi = remaining.Length; var fit = 0;
                        while (lo <= hi)
                        { var mid = (lo + hi) / 2; if (g.MeasureString(remaining.Substring(0, mid), font, new PointF(0, 0), fmt).Width <= widthMm - 1) { fit = mid; lo = mid + 1; } else hi = mid - 1; }
                        if (fit == 0) throw new PrintException("layout_too_narrow", "حجم الخط أكبر من عرض العمود. صغّر الخط أو وسّع العمود.");
                        if (fit < remaining.Length) { var space = remaining.LastIndexOf(' ', fit - 1, fit); if (space > 0) fit = space; }
                        // Do not split surrogate pairs or combining marks away from their base character.
                        while (fit > 1 && fit < remaining.Length && (char.IsLowSurrogate(remaining[fit]) || CharUnicodeInfo.GetUnicodeCategory(remaining, fit) == System.Globalization.UnicodeCategory.NonSpacingMark)) fit--;
                        result.Add(remaining.Substring(0, fit)); remaining = remaining.Substring(fit).TrimStart(' ');
                    }
                }
            }
            return result;
        }
        private static void AddColumns(List<PrintRow> rows, Graphics g, string[] values, float[] widths, float totalWidth, float size, bool bold, string alignment, float gap, PrintSection[] styles = null, float scale = 1)
        {
            var numeric = values.Select((v, i) => i > 0 && !string.IsNullOrEmpty(v) && v.All(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-')).ToArray();
            var bolds = values.Select((v, i) => styles == null ? bold : styles[i].Bold).ToArray();
            var sizes = values.Select((v, i) => styles == null ? size : (float)styles[i].FontSize * scale).ToArray();
            try { for (var i = 0; i < sizes.Length; i++) if (numeric[i]) sizes[i] = NumericSize(g, values[i], widths[i], sizes[i], bolds[i]); }
            catch (PrintException ex) when (ex.Code == "numeric_column_too_narrow" && styles != null)
            {
                // Preserve long exact quantities/amounts on narrow rolls by using stacked full-width fields.
                for (var i = 0; i < values.Length; i++)
                {
                    if (string.IsNullOrEmpty(values[i])) continue;
                    var fontSize = numeric[i] ? NumericSize(g, values[i], totalWidth, sizes[i], bolds[i]) : sizes[i];
                    if (i > 0) rows.Add(new PrintRow { FontPoints = fontSize, Alignment = "right", HeightMm = LineHeight(fontSize), Cells = { new PrintCell { Text = i == 1 ? "الكمية:" : "قيمة البند:", WidthMm = totalWidth } } });
                    foreach (var line in Wrap(g, values[i], totalWidth, fontSize, bolds[i])) rows.Add(new PrintRow { FontPoints = fontSize, Bold = bolds[i], Alignment = styles[i].Alignment, HeightMm = LineHeight(fontSize) + (float)styles[i].SpacingMm, Cells = { new PrintCell { Text = line, WidthMm = totalWidth, Numeric = numeric[i] } } });
                }
                return;
            }
            var lines = values.Select((v, i) => Wrap(g, v, widths[i], sizes[i], bolds[i])).ToArray(); var count = lines.Max(x => x.Count);
            for (var line = 0; line < count; line++)
            {
                var row = new PrintRow { FontPoints = size, Bold = bold, Alignment = alignment, HeightMm = LineHeight(sizes.Max()) + (line == count - 1 ? (styles == null ? gap : styles.Max(s => (float)s.SpacingMm)) : 0) }; var x = totalWidth;
                for (var i = 0; i < widths.Length; i++) { x -= widths[i]; row.Cells.Add(new PrintCell { Xmm = x, WidthMm = widths[i], Text = line < lines[i].Count ? lines[i][line] : "", Numeric = numeric[i], FontPoints = sizes[i], Bold = bolds[i], Alignment = styles == null ? alignment : styles[i].Alignment }); }
                rows.Add(row);
            }
        }
        private static float NumericSize(Graphics g, string text, float width, float size, bool bold)
        {
            using (var f = Format("right", true))
            for (var candidate = size; candidate >= Math.Min(6, size); candidate -= .25f)
            using (var font = Font(candidate, bold))
                if (g.MeasureString(text, font, new PointF(0, 0), f).Width <= width - 1) return candidate;
            throw new PrintException("numeric_column_too_narrow", "القيمة الرقمية أكبر من العمود. وسّع عمود القيمة أو اختر ورقًا أعرض.");
        }
        public void Draw(Graphics g, PrintRow row, float x, float y, PrintTemplate t)
        {
            g.PageUnit = GraphicsUnit.Millimeter; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (row.Divider) { using (var pen = new Pen(Color.Gray, .15f)) g.DrawLine(pen, x, y + .3f, x + row.Cells[0].WidthMm, y + .3f); return; }
            if (row.Logo)
            {
                var logoWidth = Math.Min((float)t.LogoWidthMm, row.Cells[0].WidthMm);
                var offset = row.Alignment == "left" ? 0 : row.Alignment == "center" ? (row.Cells[0].WidthMm - logoWidth) / 2 : row.Cells[0].WidthMm - logoWidth;
                using (var image = DecodeLogo(t.LogoPngBase64)) g.DrawImage(image, x + offset, y, logoWidth, (float)t.LogoHeightMm);
                return;
            }
            foreach (var cell in row.Cells)
            using (var font = Font(cell.FontPoints > 0 ? cell.FontPoints : row.FontPoints, cell.Bold ?? row.Bold))
            using (var format = Format(cell.Alignment ?? row.Alignment, cell.Numeric))
                g.DrawString(cell.Text ?? "", font, Brushes.Black, new RectangleF(x + cell.Xmm + .25f, y, cell.WidthMm - .5f, row.HeightMm), format);
        }
        public static Bitmap DecodeLogo(string encoded)
        {
            try
            {
                var bytes = Convert.FromBase64String(encoded);
                if (bytes.Length > 130000 || bytes.Length < 8 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71) throw new InvalidDataException();
                // Inspect IHDR dimensions before decoding to bound memory consumption.
                if (bytes.Length < 24) throw new InvalidDataException();
                uint w = (uint)(bytes[16] << 24 | bytes[17] << 16 | bytes[18] << 8 | bytes[19]); uint h = (uint)(bytes[20] << 24 | bytes[21] << 16 | bytes[22] << 8 | bytes[23]);
                if (w == 0 || h == 0 || w > 1200 || h > 1200) throw new InvalidDataException();
                using (var stream = new MemoryStream(bytes)) using (var img = Image.FromStream(stream, true, true)) return new Bitmap(img);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidDataException || ex is System.Runtime.InteropServices.ExternalException)
            { throw new PrintException("invalid_logo", "اختر شعار PNG صالحًا حتى 1200×1200 وبحجم لا يتجاوز 130 كيلوبايت."); }
        }
        public static List<List<PrintRow>> Paginate(List<PrintRow> rows, float availableHeightMm)
        {
            var pages = new List<List<PrintRow>>(); var page = new List<PrintRow>(); float used = 0;
            foreach (var row in rows)
            {
                if (row.HeightMm > availableHeightMm) throw new PrintException("layout_too_tall", "ارتفاع العنصر أكبر من المساحة القابلة للطباعة.");
                if (used + row.HeightMm > availableHeightMm && page.Count > 0) { pages.Add(page); page = new List<PrintRow>(); used = 0; }
                page.Add(row); used += row.HeightMm;
            }
            if (page.Count > 0) pages.Add(page); return pages;
        }
    }
}
