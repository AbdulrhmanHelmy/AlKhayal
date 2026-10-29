using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Printing
{
    public sealed class PrintException : Exception
    {
        public string Code { get; }
        public PrintException(string code, string message) : base(message) { Code = code; }
    }
    public static class PrintTypes
    {
        public static readonly string[] All = { "sale", "sales-return", "purchase", "purchase-return", "customer-payment", "supplier-payment", "shift-summary", "shift-closing" };
        public static readonly string[] Titles = { "فاتورة بيع", "إيصال مرتجع بيع", "فاتورة شراء", "مرتجع شراء", "سند قبض عميل", "سند دفع مورد", "ملخص وردية", "تقرير إغلاق وردية" };
    }
    public sealed class PrintPaper
    {
        public string Preset { get; set; } = "80mm";
        public decimal WidthMm { get; set; } = 80;
        public decimal HeightMm { get; set; } = 297;
        public bool Continuous { get; set; }
        public bool Landscape { get; set; }
        public decimal MarginLeftMm { get; set; } = 3;
        public decimal MarginRightMm { get; set; } = 3;
        public decimal MarginTopMm { get; set; } = 3;
        public decimal MarginBottomMm { get; set; } = 3;
        public decimal Scale { get; set; } = 1;
    }
    public sealed class PrintSection
    {
        public string Key { get; set; }
        public bool Visible { get; set; } = true;
        public bool Bold { get; set; }
        public decimal FontSize { get; set; } = 10;
        public decimal SpacingMm { get; set; } = 1;
        public string Alignment { get; set; } = "right";
    }
    public sealed class PrintTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public PrintPaper Paper { get; set; } = new PrintPaper();
        public List<PrintSection> Sections { get; set; } = new List<PrintSection>();
        public string HeaderText { get; set; }
        public string FooterText { get; set; } = "شكرًا لزيارتكم";
        public string ReturnPolicy { get; set; }
        public bool Dividers { get; set; } = true;
        public decimal NameColumnPercent { get; set; } = 48;
        public decimal QuantityColumnPercent { get; set; } = 20;
        public decimal LogoWidthMm { get; set; } = 25;
        public decimal LogoHeightMm { get; set; } = 15;
        // PNG bytes only; never a path, URL, script or executable template.
        public string LogoPngBase64 { get; set; }
    }
    public sealed class PrintProfile
    {
        public string DocumentType { get; set; }
        public string PrinterName { get; set; }
        public string TemplateId { get; set; }
        public int Copies { get; set; } = 1;
    }
    public sealed class PrintingSettings
    {
        public long Version { get; set; }
        public bool AutoPrintSale { get; set; }
        public List<PrintProfile> Profiles { get; set; } = new List<PrintProfile>();
        public List<PrintTemplate> Templates { get; set; } = new List<PrintTemplate>();
    }
    public sealed class PrinterInfo
    {
        public string Name { get; set; }
        public bool IsDefault { get; set; }
        public List<PrinterPaper> Papers { get; set; } = new List<PrinterPaper>();
        public bool CustomSizeSupported { get; set; }
        public string CapabilityNote { get; set; }
    }
    public sealed class PrinterPaper
    {
        public string Name { get; set; }
        public decimal WidthMm { get; set; }
        public decimal HeightMm { get; set; }
    }
    public sealed class PrintDocumentData
    {
        public string Type { get; set; }
        public long Id { get; set; }
        public string Reference { get; set; }
        public string Title { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
        public List<PrintItem> Items { get; set; } = new List<PrintItem>();
    }
    public sealed class PrintItem
    {
        public string Name { get; set; }
        public string Variant { get; set; }
        public string Quantity { get; set; }
        public string UnitPrice { get; set; }
        public string Discount { get; set; }
        public string Tax { get; set; }
        public string Total { get; set; }
    }
    public sealed class PrintRequest
    {
        public string DocumentType { get; set; }
        public long DocumentId { get; set; }
        public string OperationKey { get; set; }
        public bool Automatic { get; set; }
        public bool Reprint { get; set; }
    }
    public sealed class PrintJob
    {
        public long Id { get; set; }
        public string RequestKey { get; set; }
        public string DocumentType { get; set; }
        public long DocumentId { get; set; }
        public string Reference { get; set; }
        public long ActorId { get; set; }
        public string PrinterName { get; set; }
        public string TemplateId { get; set; }
        public int Copies { get; set; }
        public bool Reprint { get; set; }
        public string CreatedUtc { get; set; }
        public string Status { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public bool Replayed { get; set; }
        public string RequestHash { get; set; }
        public string BatchSummaryJson { get; set; }
    }
    public interface IPrintTemplateRepository
    {
        Task<PrintingSettings> ReadAsync(CancellationToken ct);
        Task<PrintingSettings> SaveAsync(PrintingSettings settings, long actorId, CancellationToken ct);
        Task<PrintJob> ClaimAsync(PrintJob job, CancellationToken ct);
        Task<PrintJob> FindAsync(string requestKey, CancellationToken ct);
        Task CompleteAsync(PrintJob job, CancellationToken ct);
        Task<IReadOnlyList<PrintJob>> JobsAsync(CancellationToken ct);
        Task ReviewAsync(long jobId, long actorId, CancellationToken ct);
    }
    public interface IPrintDocumentProvider
    {
        Task<PrintDocumentData> GetAsync(string type, long id, long actorId, bool administrator, CancellationToken ct);
    }
    public interface IPrinterBackend
    {
        Task<IReadOnlyList<PrinterInfo>> PrintersAsync(CancellationToken ct);
        Task ValidateAsync(PrintProfile profile, PrintTemplate template, CancellationToken ct);
        Task SubmitAsync(PrintDocumentData document, PrintTemplate template, PrintProfile profile, CancellationToken ct);
        Task<byte[]> PreviewAsync(PrintDocumentData document, PrintTemplate template, CancellationToken ct);
    }
}
