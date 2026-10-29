using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Printing;

namespace AlKhayal.Printing
{
    public sealed partial class WindowsPrinterBackend : IPrinterBackend, ILabelPrinterBackend
    {
        private readonly IPrintRenderer _renderer;
        public WindowsPrinterBackend(IPrintRenderer renderer) { _renderer = renderer; }
        public Task<IReadOnlyList<PrinterInfo>> PrintersAsync(CancellationToken ct) => Task.Run<IReadOnlyList<PrinterInfo>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = new List<PrinterInfo>();
                foreach (string name in PrinterSettings.InstalledPrinters)
                {
                    var p = new PrinterSettings { PrinterName = name }; var bounds = Bounds(name);
                    result.Add(new PrinterInfo { Name = name, IsDefault = p.IsDefaultPrinter, CustomSizeSupported = bounds != null,
                        CapabilityNote = bounds == null ? "التعريف لا يعلن حدودًا للمقاسات المخصصة؛ تُقبل مقاساته المسجلة فقط." : "المقاسات المخصصة ضمن حدود التعريف وتحقق DEVMODE فقط؛ يلزم اختبار الجهاز.",
                        Papers = p.PaperSizes.Cast<PaperSize>().Select(s => new PrinterPaper { Name = s.PaperName, WidthMm = (decimal)Mm(s.Width), HeightMm = (decimal)Mm(s.Height) }).ToList() });
                }
                return result;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidPrinterException)
            { throw new PrintException("spooler_unavailable", "تعذر قراءة طابعات Windows. تحقق من خدمة Print Spooler والتعريفات."); }
        }, ct);
        public Task ValidateAsync(PrintProfile profile, PrintTemplate template, CancellationToken ct) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested(); PrintingService.ValidateTemplate(template);
            var height = (float)template.Paper.HeightMm;
            if (template.Paper.Continuous)
            {
                var bounds = Bounds(profile.PrinterName);
                if (bounds == null) throw new PrintException("unsupported_paper", "التعريف لا يعلن دعم طول إيصال متغير. اختر مقاسًا ثابتًا مسجلًا.");
                height = Math.Min(height, bounds[3]);
            }
            using (var doc = Create(profile, template, height)) { CheckStatus(profile.PrinterName); }
            if (!string.IsNullOrEmpty(template.LogoPngBase64)) using (var logo = NativePrintRenderer.DecodeLogo(template.LogoPngBase64)) { }
        }, ct);
        public Task SubmitAsync(PrintDocumentData data, PrintTemplate template, PrintProfile profile, CancellationToken ct) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            // Measure in physical mm. Printing is graphics through the selected Windows driver.
            using (var measureBitmap = new Bitmap(8, 8)) using (var measure = Graphics.FromImage(measureBitmap))
            {
                measure.PageUnit = GraphicsUnit.Millimeter;
                var p = template.Paper; var width = (float)(p.Landscape ? p.HeightMm : p.WidthMm);
                var rows = _renderer.Layout(measure, data, template, width - (float)(p.MarginLeftMm + p.MarginRightMm));
                var height = (float)p.HeightMm;
                if (p.Continuous)
                {
                    height = Math.Max(30, rows.Sum(r => r.HeightMm) + (float)(p.MarginTopMm + p.MarginBottomMm) + 2);
                    // Driver's upper bound determines pagination for long rolls.
                    var bounds = Bounds(profile.PrinterName);
                    if (bounds == null) throw new PrintException("unsupported_paper", "التعريف لا يعلن دعم طول إيصال متغير. اختر مقاسًا ثابتًا مسجلًا.");
                    height = Math.Max(bounds[1], Math.Min(height, Math.Min((float)p.HeightMm, bounds[3])));
                }
                using (var doc = Create(profile, template, height))
                {
                    CheckStatus(profile.PrinterName);
                    var pageIndex = 0; List<List<PrintRow>> pages = null; float left = 0, top = 0;
                    doc.DocumentName = "AlKhayal " + data.Type + " " + data.Id; doc.PrintController = new StandardPrintController();
                    doc.PrintPage += (sender, e) =>
                    {
                        if (pages == null)
                        {
                            // PrintableArea accounts for hardware margins. No clipping into nonprintable edges.
                            var area = e.PageSettings.PrintableArea;
                            left = Math.Max((float)p.MarginLeftMm, Mm(area.Left)); top = Math.Max((float)p.MarginTopMm, Mm(area.Top));
                            var right = Math.Min(Mm(e.PageBounds.Width) - (float)p.MarginRightMm, Mm(area.Right));
                            var bottom = Math.Min(Mm(e.PageBounds.Height) - (float)p.MarginBottomMm, Mm(area.Bottom));
                            e.Graphics.PageUnit = GraphicsUnit.Millimeter;
                            rows = _renderer.Layout(e.Graphics, data, template, right - left);
                            pages = NativePrintRenderer.Paginate(rows, bottom - top);
                        }
                        e.Graphics.PageUnit = GraphicsUnit.Millimeter;
                        // PrintDocument's graphics origin is the device printable origin when OriginAtMargins=false.
                        var x = left - Mm(e.PageSettings.HardMarginX); var y = top - Mm(e.PageSettings.HardMarginY);
                        foreach (var row in pages[pageIndex]) { _renderer.Draw(e.Graphics, row, x, y, template); y += row.HeightMm; }
                        pageIndex++; e.HasMorePages = pageIndex < pages.Count;
                    };
                    // Do not automatically retry: a failure can occur after Windows accepted some pages/copies.
                    doc.Print();
                }
            }
        }, ct);
        public Task<byte[]> PreviewAsync(PrintDocumentData data, PrintTemplate t, CancellationToken ct) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested(); PrintingService.ValidateTemplate(t);
            var p = t.Paper; var width = (float)(p.Landscape ? p.HeightMm : p.WidthMm); var height = (float)(p.Landscape ? p.WidthMm : p.HeightMm);
            using (var tiny = new Bitmap(8, 8)) using (var g = Graphics.FromImage(tiny))
            {
                g.PageUnit = GraphicsUnit.Millimeter; var rows = _renderer.Layout(g, data, t, width - (float)(p.MarginLeftMm + p.MarginRightMm));
                if (p.Continuous) height = Math.Min(height, Math.Max(30, rows.Sum(x => x.HeightMm) + (float)(p.MarginTopMm + p.MarginBottomMm)));
                var pages = NativePrintRenderer.Paginate(rows, height - (float)(p.MarginTopMm + p.MarginBottomMm));
                // Editor sample only. Bound raster memory even with a 3m roll; first sample page is sufficient.
                var dpi = Math.Min(144f, 2500f * 25.4f / Math.Max(width, height));
                using (var image = new Bitmap(Math.Max(1, (int)Math.Ceiling(width / 25.4f * dpi)), Math.Max(1, (int)Math.Ceiling(height / 25.4f * dpi))))
                {
                    image.SetResolution(dpi, dpi);
                    using (var canvas = Graphics.FromImage(image))
                    {
                        canvas.Clear(Color.White); var y = (float)p.MarginTopMm;
                        foreach (var row in pages[0]) { _renderer.Draw(canvas, row, (float)p.MarginLeftMm, y, t); y += row.HeightMm; }
                    }
                    using (var stream = new MemoryStream()) { image.Save(stream, ImageFormat.Png); return stream.ToArray(); }
                }
            }
        }, ct);
        private static PrintDocument Create(PrintProfile profile, PrintTemplate template, float height)
            => Create(profile.PrinterName, profile.Copies, template.Paper, height);
        private static PrintDocument Create(string printerName, int copies, PrintPaper paperSettings, float height)
        {
            if (string.IsNullOrWhiteSpace(printerName)) throw new PrintException("printer_not_configured", "اختر الطابعة من الإعدادات أولًا.");
            var doc = new PrintDocument();
            try
            {
                doc.PrinterSettings.PrinterName = printerName;
                if (!doc.PrinterSettings.IsValid) throw new PrintException("printer_not_found", "الطابعة المحددة غير موجودة. راجع إعدادات الطابعات.");
                RejectInteractiveOutput(printerName);
                if (doc.PrinterSettings.IsPlotter || copies > doc.PrinterSettings.MaximumCopies) throw new PrintException("driver_error", "التعريف لا يدعم إعدادات الطباعة المطلوبة.");
                doc.PrinterSettings.Copies = (short)copies;
                var p = paperSettings; var paper = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(s => Math.Abs(Mm(s.Width) - (float)p.WidthMm) < 1 && Math.Abs(Mm(s.Height) - height) < 1);
                var custom = paper == null;
                if (custom)
                {
                    var bounds = Bounds(printerName);
                    if (bounds == null || (float)p.WidthMm < bounds[0] || height < bounds[1] || (float)p.WidthMm > bounds[2] || height > bounds[3]) throw new PrintException("unsupported_paper", "مقاس الورق خارج إمكانات التعريف. سجّل مقاسًا في Windows أو اختر مقاسًا مدعومًا.");
                    paper = new PaperSize("AlKhayal Custom", Hundredths((float)p.WidthMm), Hundredths(height));
                }
                doc.DefaultPageSettings.PaperSize = paper; doc.DefaultPageSettings.Landscape = p.Landscape;
                if (p.Landscape && doc.PrinterSettings.LandscapeAngle == 0) throw new PrintException("unsupported_orientation", "التعريف لا يدعم الاتجاه الأفقي.");
                doc.DefaultPageSettings.Margins = new Margins(Hundredths((float)p.MarginLeftMm), Hundredths((float)p.MarginRightMm), Hundredths((float)p.MarginTopMm), Hundredths((float)p.MarginBottomMm));
                doc.OriginAtMargins = false;
                if (custom) ValidateDevMode(doc.PrinterSettings, doc.DefaultPageSettings, (float)p.WidthMm, height);
                return doc;
            }
            catch { doc.Dispose(); throw; }
        }
        private static void CheckStatus(string name)
        {
            IntPtr handle;
            if (!OpenPrinter(name, out handle, IntPtr.Zero)) throw new PrintException("spooler_unavailable", "تعذر الاتصال بالطابعة أو خدمة Print Spooler.");
            try
            {
                int needed; GetPrinter(handle, 6, IntPtr.Zero, 0, out needed);
                if (needed <= 0) throw new PrintException("driver_error", "تعذر قراءة حالة الطابعة من التعريف.");
                var buffer = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!GetPrinter(handle, 6, buffer, needed, out needed)) throw new PrintException("driver_error", "تعذر قراءة حالة الطابعة.");
                    ValidateReportedStatus(Marshal.ReadInt32(buffer));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { ClosePrinter(handle); }
        }
        public static void ValidateReportedStatus(int status)
        {
            const int offlineOrUnavailable = 0x80 | 0x1000;
            // PAUSED, ERROR, PENDING_DELETION, PAPER_JAM/OUT/MANUAL_FEED/PROBLEM,
            // OUTPUT_BIN_FULL, NO_TONER, USER_INTERVENTION, OUT_OF_MEMORY, DOOR_OPEN, SERVER_UNKNOWN.
            const int knownError = 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x20 | 0x40 | 0x800 | 0x40000 | 0x100000 | 0x200000 | 0x400000 | 0x800000;
            if ((status & offlineOrUnavailable) != 0) throw new PrintException("printer_offline", "الطابعة غير متصلة أو غير متاحة.");
            if ((status & knownError) != 0) throw new PrintException("printer_error", "الطابعة متوقفة أو تحتاج ورقًا/تدخل المستخدم. راجع قائمة Windows.");
            // BUSY/PRINTING/PROCESSING are ordinary queue activity, not rejection conditions.
        }
        private static void RejectInteractiveOutput(string name)
        {
            IntPtr handle;
            if (!OpenPrinter(name, out handle, IntPtr.Zero)) throw new PrintException("spooler_unavailable", "تعذر الوصول إلى تعريف الطابعة.");
            try
            {
                int needed; GetPrinter(handle, 2, IntPtr.Zero, 0, out needed);
                if (needed <= 0) throw new PrintException("driver_error", "تعذر التحقق من وجهة الطابعة.");
                var buffer = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!GetPrinter(handle, 2, buffer, needed, out needed)) throw new PrintException("driver_error", "تعذر التحقق من وجهة الطابعة.");
                    var port = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, IntPtr.Size * 3)) ?? "";
                    var driver = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, IntPtr.Size * 4)) ?? "";
                    if (port.IndexOf("PORTPROMPT:", StringComparison.OrdinalIgnoreCase) >= 0 || port.IndexOf("FILE:", StringComparison.OrdinalIgnoreCase) >= 0 || driver.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0 || driver.IndexOf("XPS", StringComparison.OrdinalIgnoreCase) >= 0 || driver.IndexOf("OneNote", StringComparison.OrdinalIgnoreCase) >= 0 || driver.IndexOf("Fax", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new PrintException("interactive_printer", "اختر طابعة فعلية تدعم الإرسال المباشر؛ طابعات الملفات وPDF والوجهات التفاعلية غير مدعومة.");
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { ClosePrinter(handle); }
        }
        private static float[] Bounds(string name)
        {
            var min = DeviceCapabilities(name, null, 4, IntPtr.Zero, IntPtr.Zero); var max = DeviceCapabilities(name, null, 5, IntPtr.Zero, IntPtr.Zero);
            if (min <= 0 || max <= 0) return null;
            return new[] { (min & 65535) / 10f, ((uint)min >> 16) / 10f, (max & 65535) / 10f, ((uint)max >> 16) / 10f };
        }
        private static void ValidateDevMode(PrinterSettings printer, PageSettings page, float width, float height)
        {
            IntPtr handle;
            if (!OpenPrinter(printer.PrinterName, out handle, IntPtr.Zero)) throw new PrintException("spooler_unavailable", "تعذر فتح تعريف الطابعة.");
            var mode = IntPtr.Zero; var pointer = IntPtr.Zero;
            try
            {
                mode = printer.GetHdevmode(page);
                pointer = GlobalLock(mode);
                if (pointer == IntPtr.Zero || GlobalSize(mode).ToUInt64() < (ulong)Math.Max(84, DocumentProperties(IntPtr.Zero, handle, printer.PrinterName, IntPtr.Zero, IntPtr.Zero, 0))) throw new PrintException("driver_error", "تعذر قراءة إعدادات تعريف الطابعة بأمان.");
                // DEVMODEW: dmFields=72, dmPaperLength=80, dmPaperWidth=82 (WORD dimensions in 0.1mm).
                if (DocumentProperties(IntPtr.Zero, handle, printer.PrinterName, pointer, pointer, 10) != 1 || (Marshal.ReadInt32(pointer, 72) & 12) != 12 || Math.Abs(Marshal.ReadInt16(pointer, 82) / 10f - width) > 1 || Math.Abs(Marshal.ReadInt16(pointer, 80) / 10f - height) > 1)
                    throw new PrintException("unsupported_paper", "تعريف الطابعة رفض المقاس المخصص أو غيّره. اختر مقاسًا مسجلًا.");
                GlobalUnlock(mode); pointer = IntPtr.Zero;
                page.SetHdevmode(mode);
            }
            finally { if (pointer != IntPtr.Zero) GlobalUnlock(mode); if (mode != IntPtr.Zero) GlobalFree(mode); ClosePrinter(handle); }
        }
        public static float Mm(float hundredths) => hundredths * 25.4f / 100;
        public static int Hundredths(float mm) => (int)Math.Round(mm * 100 / 25.4f, MidpointRounding.AwayFromZero);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr printer);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetPrinter(IntPtr printer, int level, IntPtr buffer, int size, out int needed);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)] private static extern int DeviceCapabilities(string device, string port, short capability, IntPtr output, IntPtr mode);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)] private static extern int DocumentProperties(IntPtr hwnd, IntPtr printer, string name, IntPtr output, IntPtr input, int flags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr GlobalSize(IntPtr memory);
    }
}
