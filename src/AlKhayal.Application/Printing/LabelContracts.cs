using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Printing
{
    public sealed class LabelElement
    {
        public string Key { get; set; }
        public bool Visible { get; set; } = true;
        public decimal X { get; set; }
        public decimal Y { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal FontSize { get; set; } = 8;
        public bool Bold { get; set; }
        public string Alignment { get; set; } = "right";
        public string Text { get; set; }
        public decimal ModuleMm { get; set; } = .2m;
    }
    public sealed class LabelStock
    {
        public decimal LabelWidthMm { get; set; } = 50;
        public decimal LabelHeightMm { get; set; } = 30;
        public decimal PageWidthMm { get; set; } = 50;
        public decimal PageHeightMm { get; set; } = 30;
        public decimal LeftMm { get; set; }
        public decimal RightMm { get; set; }
        public decimal TopMm { get; set; }
        public decimal BottomMm { get; set; }
        public decimal HorizontalGapMm { get; set; }
        public decimal VerticalGapMm { get; set; }
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public bool Landscape { get; set; }
        public bool Continuous { get; set; }
    }
    public sealed class LabelTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public LabelStock Stock { get; set; } = new LabelStock();
        public List<LabelElement> Elements { get; set; } = new List<LabelElement>();
        public string LogoPngBase64 { get; set; }
    }
    public sealed class LabelPrinterProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string PrinterName { get; set; }
        public string TemplateId { get; set; }
        public int Copies { get; set; } = 1;
    }
    public sealed class LabelSettings
    {
        public long Version { get; set; }
        public string DefaultProfileId { get; set; } = "labels-default";
        public List<LabelPrinterProfile> Profiles { get; set; } = new List<LabelPrinterProfile>();
        public List<LabelTemplate> Templates { get; set; } = new List<LabelTemplate>();
    }
    public sealed class LabelSelection
    {
        public long BarcodeId { get; set; }
        public int Quantity { get; set; } = 1;
        public string Format { get; set; } = "code128";
    }
    public sealed class LabelBatchRequest
    {
        public string OperationKey { get; set; }
        public string ProfileId { get; set; }
        public string TemplateId { get; set; }
        public int StartCell { get; set; }
        public bool Reprint { get; set; }
        public List<LabelSelection> Items { get; set; } = new List<LabelSelection>();
    }
    // A label is a catalog projection, never a financial document DTO.
    public sealed class ProductLabel
    {
        public long ProductId { get; set; }
        public long VariantId { get; set; }
        public long BarcodeId { get; set; }
        public string Barcode { get; set; }
        public string Format { get; set; }
        public int Quantity { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
    }
    public sealed class LabelBatch
    {
        public List<ProductLabel> Items { get; set; } = new List<ProductLabel>();
        public int StartCell { get; set; }
    }
    public interface ILabelRepository
    {
        Task<LabelSettings> ReadAsync(CancellationToken ct);
        Task<LabelSettings> SaveAsync(LabelSettings settings, long actorId, CancellationToken ct);
        Task<List<ProductLabel>> ProductsAsync(IReadOnlyList<LabelSelection> selected, CancellationToken ct);
    }
    public interface ILabelPrinterBackend
    {
        Task ValidateLabelsAsync(LabelPrinterProfile profile, LabelTemplate template, CancellationToken ct);
        Task SubmitLabelsAsync(LabelBatch batch, LabelTemplate template, LabelPrinterProfile profile, CancellationToken ct);
        Task<byte[]> PreviewLabelsAsync(LabelBatch batch, LabelTemplate template, CancellationToken ct);
    }
}
