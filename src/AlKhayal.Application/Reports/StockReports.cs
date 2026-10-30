using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Reports
{
    public sealed class StockReportQuery : ReportQuery
    {
        public long? CategoryId { get; set; }
        public long? WarehouseId { get; set; }
        public string Status { get; set; }
        public string Type { get; set; }
        public string Reference { get; set; }
        public string Sort { get; set; } = "quantity";
    }
    public sealed class StockReportRow
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public long VariantId { get; set; }
        public string Product { get; set; }
        public string Variant { get; set; }
        public string Sku { get; set; }
        public string Unit { get; set; }
        public long WarehouseId { get; set; }
        public string Warehouse { get; set; }
        public string Quantity { get; set; }
        public string Sold { get; set; }
        public string Returned { get; set; }
        public string Cancelled { get; set; }
        public string Revenue { get; set; }
        public string RecordedAmount { get; set; }
        public string Cost { get; set; }
        public string Profit { get; set; }
        public string Margin { get; set; }
        public string SalePrice { get; set; }
        public string SellingValue { get; set; }
        public bool Complete { get; set; }
        public string Status { get; set; }
        public string EventUtc { get; set; }
        public string Type { get; set; }
        public string QuantityIn { get; set; }
        public string QuantityOut { get; set; }
        public string Reference { get; set; }
        public string DocumentType { get; set; }
        public string Route { get; set; }
        public long DocumentId { get; set; }
    }
    public sealed class StockReportSummary
    {
        public string Revenue { get; set; }
        public string Cost { get; set; }
        public string Profit { get; set; }
        public string SellingValue { get; set; }
        public long IncompleteCount { get; set; }
        public long ZeroCount { get; set; }
        public bool Complete { get; set; }
    }
    public sealed class StockReportResult
    {
        public long Count { get; set; }
        public StockReportSummary Summary { get; set; }
        public IReadOnlyList<StockReportRow> Items { get; set; }
        public string Scope { get; set; }
    }
    public interface IStockReportStore
    {
        Task<StockReportResult> QueryAsync(string report, StockReportQuery query, CancellationToken ct);
    }
    public sealed class StockReportService
    {
        private readonly IStockReportStore _store;
        private readonly TimeZoneInfo _zone;
        public StockReportService(IStockReportStore store, TimeZoneInfo zone = null) { _store=store; _zone=zone ?? TimeZoneInfo.Local; }
        public async Task<StockReportResult> QueryAsync(string report, StockReportQuery q, CancellationToken ct)
        {
            if (q == null) throw new ArgumentException("مرشحات التقرير غير صحيحة.");
            bool current=report=="inventory-valuation" || report=="low-stock";
            if (report!="product-profit" && report!="top-products" && report!="inventory-movements" && !current)
                throw new ArgumentException("التقرير غير موجود.");
            if (current && (q.From!=null || q.To!=null)) throw new ArgumentException("هذا التقرير يعرض المخزون الحالي فقط.");
            ReportPeriod.Validate(q,_zone);
            if (q.CategoryId<=0 || q.WarehouseId<=0 || (q.Reference!=null && q.Reference.Length>120) ||
                (q.Sort!="quantity" && q.Sort!="amount") ||
                (q.Status!=null && q.Status!="zero" && q.Status!="available" && q.Status!="incomplete") ||
                (q.Type!=null && Array.IndexOf(MovementTypes,q.Type)<0) ||
                ((report=="product-profit" || report=="top-products") && q.WarehouseId.HasValue))
                throw new ArgumentException("مرشحات التقرير غير صحيحة.");
            var result=await _store.QueryAsync(report,q,ct).ConfigureAwait(false);
            // A top-sellers or availability grant must not disclose the separate profit or
            // valuation report, including fields that its own table does not render.
            if (report=="top-products" || report=="low-stock")
            {
                if(result.Summary!=null) {result.Summary.Revenue=null;result.Summary.Cost=null;result.Summary.Profit=null;result.Summary.SellingValue=null;}
                if(result.Items!=null)foreach(var row in result.Items) {row.Revenue=null;row.Cost=null;row.Profit=null;row.Margin=null;row.SalePrice=null;row.SellingValue=null;}
            }
            if(report=="top-products") result.Scope="صافي الكمية بالوحدة الأساسية لكل منتج؛ قيمة البيع الأصلية شاملة الضريبة بعد مرتجعات وإلغاءات الفترة. الأسماء والتصنيفات الحالية للبحث فقط.";
            if((report=="product-profit" || report=="top-products") && (q.CategoryId.HasValue || !string.IsNullOrWhiteSpace(q.Search)))
                result.Scope+=" نطاق مفلتر يستبعد مبيعات المنتجات الأخرى؛ إجمالي صافي الربح العام يشملها ولا يقارن بهذا النطاق مباشرةً.";
            return result;
        }
        private static readonly string[] MovementTypes={"purchase","sale","sales-return","purchase-return","cancellation","opening","manual_receipt","manual_issue","adjustment","count","transfer","reversal","projection_rebuild"};
    }
}
