using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Reports;
using AlKhayal.Data.Inventory;
using Dapper;

namespace AlKhayal.Data.Reports
{
    public sealed class SqliteStockReportStore : IStockReportStore
    {
        private readonly string _cs;
        public SqliteStockReportStore(string cs) { _cs=cs; }
        public Task<StockReportResult> QueryAsync(string report,StockReportQuery q,CancellationToken ct)
        {
            if(report=="product-profit" || report=="top-products") return new SqliteNetProfitStore(_cs).QueryProductsAsync(q,ct);
            return report=="inventory-movements"?Movements(q,ct):Current(report,q,ct);
        }
        private static string Money(decimal n)=>(n/10000m).ToString("0.0000",CultureInfo.InvariantCulture);
        private static string Qty(decimal n)=>(n/1000000m).ToString("0.######",CultureInfo.InvariantCulture);
        private const string Labels=@" JOIN ProductVariants v ON v.Id=b.VariantId JOIN Products p ON p.Id=v.ProductId
            JOIN ProductDetails d ON d.ProductId=p.Id JOIN Units u ON u.Id=d.BaseUnitId JOIN Warehouses w ON w.Id=b.WarehouseId ";
        private const string Filters=@" (@WarehouseId IS NULL OR b.WarehouseId=@WarehouseId) AND (@CategoryId IS NULL OR d.CategoryId=@CategoryId)
            AND (@Search IS NULL OR p.Name LIKE @Search OR v.Name LIKE @Search OR EXISTS(SELECT 1 FROM ProductSkus s WHERE s.VariantId=v.Id AND s.Sku LIKE @Search)) ";
        private static DynamicParameters Args(StockReportQuery q)=>new DynamicParameters(new {q.StartUtc,q.EndUtc,q.WarehouseId,q.CategoryId,q.Status,q.Type,
            Search=string.IsNullOrWhiteSpace(q.Search)?null:"%"+q.Search.Trim()+"%",Reference=string.IsNullOrWhiteSpace(q.Reference)?null:"%"+q.Reference.Trim()+"%",Limit=q.PageSize,Offset=(q.Page-1)*q.PageSize});
        private async Task<StockReportResult> Current(string report,StockReportQuery q,CancellationToken ct)
        {
            var args=Args(q);bool low=report=="low-stock";
            // Shared evidence query and exact decimal selling calculation with the original
            // valuation endpoint. Batch integrity is checked before warehouse/variant grouping.
            string sql="WITH evidence AS ("+SqliteInventoryStore.ValuationEvidence+@"), facts AS (
                SELECT b.*,CASE WHEN b.UnresolvedCount=0 AND b.LayerQuantity=b.QuantityBaseScaled AND b.LayerValue=b.ValueScaled THEN 1 ELSE 0 END Complete
                FROM evidence b
            ), grouped AS (
                SELECT b.VariantId,b.WarehouseId,SUM(b.QuantityBaseScaled) Quantity,MIN(b.Complete) Complete,p.Id ProductId,p.Name Product,v.Name Variant,w.Name Warehouse,u.Name Unit,
                    COALESCE(v.SellPriceOverrideScaled,d.SellPriceScaled) Price
                FROM facts b "+Labels+" WHERE "+Filters+(low?" AND d.IsActive=1 AND d.ArchivedUtc IS NULL AND v.IsActive=1 AND v.ArchivedUtc IS NULL AND w.IsActive=1 AND d.ProductType='stock' ":" ")+@"
                GROUP BY b.WarehouseId,b.VariantId
            ), matching AS (
                SELECT * FROM grouped WHERE (@Status IS NULL OR (@Status='zero' AND Quantity=0) OR (@Status='available' AND Quantity>0) OR (@Status='incomplete' AND Complete=0))"+
                (low?"":" AND Quantity>0")+") ";
            using(var c=new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using(var tx=c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    var summary=await c.QuerySingleAsync<CurrentSummary>(new CommandDefinition(sql+" SELECT COUNT(*) Count,COALESCE(SUM(CASE WHEN Complete=0 THEN 1 ELSE 0 END),0) Incomplete,COALESCE(SUM(CASE WHEN Quantity=0 THEN 1 ELSE 0 END),0) ZeroCount FROM matching",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    var page=(await c.QueryAsync<CurrentRow>(new CommandDefinition(sql+" SELECT * FROM matching ORDER BY ProductId,VariantId,WarehouseId LIMIT @Limit OFFSET @Offset",args,tx,cancellationToken:ct)).ConfigureAwait(false)).ToList();
                    var lookup=page.ToDictionary(x=>Tuple.Create(x.WarehouseId,x.VariantId));decimal cost=0,selling=0;bool sellingComplete=true;
                    // Stream matching batches: bounded memory, no catalog-wide materialization.
                    using(var reader=await c.ExecuteReaderAsync(new CommandDefinition(sql+@" SELECT b.* FROM facts b JOIN matching m ON m.VariantId=b.VariantId AND m.WarehouseId=b.WarehouseId",args,tx,cancellationToken:ct)).ConfigureAwait(false))
                    {
                        var parse=reader.GetRowParser<BatchRow>();while(reader.Read())
                        {
                            ct.ThrowIfCancellationRequested();var b=parse(reader);bool valid=SqliteInventoryStore.CostComplete(b);
                            decimal value=0;bool price=b.SalePriceScaled.HasValue && b.SalePriceScaled>=0;
                            if(price)value=SqliteInventoryStore.SellingScaled(b);else sellingComplete=false;
                            if(valid)cost+=b.LayerValue;selling+=value;
                            CurrentRow row;if(lookup.TryGetValue(Tuple.Create(b.WarehouseId,b.VariantId),out row)) {row.Cost+=b.LayerValue;row.Selling+=value;row.SellingComplete&=price;}
                        }
                    }
                    tx.Commit();bool complete=summary.Incomplete==0;
                    return new StockReportResult {Count=summary.Count,Scope=low?"توافر حالي فقط؛ لا يوجد حد أدنى مخزني مضبوط. النطاق: أصناف ومتغيرات ومخازن نشطة ذات أرصدة مسجلة سابقًا؛ الأصناف التي لم تخزن غير مشمولة.":"لقطة المخزون الحالي؛ تكلفة طبقات FIFO المتبقية الفعلية. الهامش المتوقع بسعر البيع الحالي ليس ربحًا محققًا. الكميات بوحداتها ولا تجمع بين المنتجات.",
                        Summary=new StockReportSummary{Complete=complete && sellingComplete,IncompleteCount=summary.Incomplete,ZeroCount=summary.ZeroCount,Cost=complete?Money(cost):null,SellingValue=sellingComplete?Money(selling):null,Profit=complete && sellingComplete?Money(selling-cost):null},
                        Items=page.Select(x=>new StockReportRow{ProductId=x.ProductId,VariantId=x.VariantId,WarehouseId=x.WarehouseId,Product=x.Product,Variant=x.Variant,Warehouse=x.Warehouse,Unit=x.Unit,Quantity=Qty(x.Quantity),SalePrice=Money(x.Price),
                            Cost=x.Complete==1?Money(x.Cost):null,SellingValue=x.SellingComplete?Money(x.Selling):null,Profit=x.Complete==1 && x.SellingComplete?Money(x.Selling-x.Cost):null,Complete=x.Complete==1 && x.SellingComplete,Status=x.Quantity==0?"zero":"available"}).ToArray()};
                }
            }
        }
        private const string MovementFacts=@"WITH movements AS (
            SELECT b.Id,b.WarehouseId,b.VariantId,b.QuantityBaseScaled Quantity,b.ValueScaled Cost,b.UnitCostScaled,b.CreatedUtc EventUtc,
                CASE WHEN b.UnitCostScaled IS NOT NULL OR sr.Id IS NOT NULL OR sc.SalesInvoiceId IS NOT NULL
                    OR EXISTS(SELECT 1 FROM InventoryCostAllocations a WHERE a.MovementId=b.Id)
                    OR EXISTS(SELECT 1 FROM InventoryCostLayers cl WHERE cl.SourceMovementId=b.Id AND cl.Basis<>'unresolved' AND cl.UnitCostScaled IS NOT NULL)
                    THEN 1 ELSE 0 END CostComplete,
                p.Id ProductId,p.Name Product,v.Name Variant,w.Name Warehouse,u.Name Unit,
                CASE WHEN pi.Id IS NOT NULL THEN 'purchase' WHEN si.Id IS NOT NULL THEN 'sale' WHEN sr.Id IS NOT NULL THEN 'sales-return'
                     WHEN pr.Id IS NOT NULL THEN 'purchase-return' WHEN sc.SalesInvoiceId IS NOT NULL THEN 'cancellation' ELSE doc.DocumentType END Type,
                COALESCE(pi.InvoiceNumber,si.InvoiceNumber,sr.ReturnNumber,CAST(pr.Id AS TEXT),'حركة '||doc.Id) Reference,
                CASE WHEN pi.Id IS NOT NULL OR pr.Id IS NOT NULL THEN 'purchase-management' WHEN si.Id IS NOT NULL OR sr.Id IS NOT NULL OR sc.SalesInvoiceId IS NOT NULL THEN 'sales-management' END Route,
                COALESCE(pi.Id,si.Id,sr.SalesInvoiceId,pr.PurchaseInvoiceId,sc.SalesInvoiceId,doc.Id) DocumentId,doc.DocumentType
            FROM StockMovements b "+Labels+@" JOIN InventoryDocuments doc ON doc.Id=b.DocumentId
            LEFT JOIN PurchaseInvoices pi ON pi.InventoryDocumentId=doc.Id LEFT JOIN SalesInvoices si ON si.InventoryDocumentId=doc.Id
            LEFT JOIN SalesReturns sr ON sr.InventoryDocumentId=doc.Id LEFT JOIN PurchaseReturns pr ON pr.InventoryDocumentId=doc.Id
            LEFT JOIN SalesCancellations sc ON sc.InventoryDocumentId=doc.Id
            WHERE b.CreatedUtc>=@StartUtc AND b.CreatedUtc<@EndUtc AND "+Filters+@"
        ), matching AS (SELECT * FROM movements WHERE (@Type IS NULL OR Type=@Type) AND (@Reference IS NULL OR Reference LIKE @Reference)) ";
        private async Task<StockReportResult> Movements(StockReportQuery q,CancellationToken ct)
        {
            using(var c=new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);var args=Args(q);
                using(var tx=c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    var summary=await c.QuerySingleAsync<MovementSummary>(new CommandDefinition(MovementFacts+@" SELECT COUNT(*) Count,COALESCE(SUM(Cost),0) Cost,
                        COALESCE(SUM(CASE WHEN CostComplete=0 THEN 1 ELSE 0 END),0) Incomplete FROM matching",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    var rows=await c.QueryAsync<MovementRow>(new CommandDefinition(MovementFacts+" SELECT * FROM matching ORDER BY EventUtc DESC,Id DESC LIMIT @Limit OFFSET @Offset",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    var items=rows.Select(x=>new StockReportRow{Id=x.Id,ProductId=x.ProductId,VariantId=x.VariantId,WarehouseId=x.WarehouseId,Product=x.Product,Variant=x.Variant,Warehouse=x.Warehouse,Unit=x.Unit,EventUtc=x.EventUtc,Type=x.Type,DocumentType=x.DocumentType,DocumentId=x.DocumentId,Reference=x.Reference,Route=x.Route,
                        QuantityIn=Qty(Math.Max(0,x.Quantity)),QuantityOut=Qty(Math.Max(0,-x.Quantity)),Cost=x.CostComplete==1?Money(x.Cost):null,Complete=x.CostComplete==1}).ToArray();
                    tx.Commit();return new StockReportResult{Count=summary.Count,Items=items,Scope="حركات محفوظة حسب وقت الحدث؛ قيم تاريخية موقعة وليست مشتريات أو مبيعات جديدة. التحويل يظهر خروجًا ودخولًا. لا تجمع كميات الوحدات المختلفة.",Summary=new StockReportSummary{Cost=summary.Incomplete==0?Money(summary.Cost):null,Complete=summary.Incomplete==0,IncompleteCount=summary.Incomplete}};
                }
            }
        }
        private sealed class CurrentSummary {public long Count{get;set;}public long Incomplete{get;set;}public long ZeroCount{get;set;}}
        private sealed class CurrentRow
        {
            public long ProductId{get;set;}public long VariantId{get;set;}public long WarehouseId{get;set;}public string Product{get;set;}public string Variant{get;set;}public string Warehouse{get;set;}public string Unit{get;set;}
            public decimal Quantity{get;set;}public long Price{get;set;}public int Complete{get;set;}public decimal Cost{get;set;}public decimal Selling{get;set;}public bool SellingComplete{get;set;}=true;
        }
        private sealed class BatchRow : SqliteInventoryStore.ValuationRow {public long WarehouseId{get;set;}}
        private sealed class MovementSummary {public long Count{get;set;}public decimal Cost{get;set;}public long Incomplete{get;set;}}
        private sealed class MovementRow
        {
            public long Id{get;set;}public long ProductId{get;set;}public long VariantId{get;set;}public long WarehouseId{get;set;}public string Product{get;set;}public string Variant{get;set;}public string Warehouse{get;set;}public string Unit{get;set;}
            public string EventUtc{get;set;}public string Type{get;set;}public string DocumentType{get;set;}public string Reference{get;set;}public string Route{get;set;}public long DocumentId{get;set;}public decimal Quantity{get;set;}public decimal Cost{get;set;}public long? UnitCostScaled{get;set;}public int CostComplete{get;set;}
        }
    }
}
