using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Reports;
using Dapper;

namespace AlKhayal.Data.Reports
{
    public sealed partial class SqliteNetProfitStore
    {
        private const string ProductEvents = @",
            product_events AS (
                SELECT l.VariantId, -dl.BaseQuantityScaled Sold,0 Returned,0 Cancelled,l.LineTotalScaled Gross,
                    l.LineTotalScaled-l.TaxScaled Revenue,l.Cost,l.CostComplete,f.RevenueComplete
                FROM line_facts l JOIN invoice_facts f ON f.Id=l.SalesInvoiceId
                LEFT JOIN InventoryDocumentLines dl ON dl.Id=l.InventoryDocumentLineId
                WHERE f.PostedUtc>=@StartUtc AND f.PostedUtc<@EndUtc
                UNION ALL
                SELECT l.VariantId,0,0,-dl.BaseQuantityScaled,-l.LineTotalScaled,-(l.LineTotalScaled-l.TaxScaled),-l.Cost,
                    CASE WHEN NOT EXISTS(SELECT 1 FROM SalesReturns WHERE SalesInvoiceId=f.Id)
                        AND (SELECT COALESCE(SUM(ValueScaled),0) FROM StockMovements WHERE DocumentId=sc.InventoryDocumentId)=f.Cost THEN l.CostComplete ELSE 0 END,
                    CASE WHEN NOT EXISTS(SELECT 1 FROM SalesReturns WHERE SalesInvoiceId=f.Id) THEN f.RevenueComplete ELSE 0 END
                FROM line_facts l JOIN invoice_facts f ON f.Id=l.SalesInvoiceId
                JOIN SalesCancellations sc ON sc.SalesInvoiceId=f.Id
                LEFT JOIN InventoryDocumentLines dl ON dl.Id=l.InventoryDocumentLineId
                WHERE sc.CreatedUtc>=@StartUtc AND sc.CreatedUtc<@EndUtc
                UNION ALL
                SELECT l.VariantId,0,rl.QuantityScaled*l.ToBaseNumerator/l.ToBaseDenominator,0,-rl.AmountScaled,0,0,1,1
                FROM SalesReturnLines rl JOIN SalesReturns r ON r.Id=rl.SalesReturnId
                JOIN line_facts l ON l.Id=rl.SalesInvoiceLineId LEFT JOIN InventoryDocumentLines dl ON dl.Id=l.InventoryDocumentLineId
                WHERE r.CreatedUtc>=@StartUtc AND r.CreatedUtc<@EndUtc
            ), selected AS (
                SELECT e.*,p.Id ProductId,p.Name Product,v.Name Variant,d.BaseUnitId,u.Name Unit,
                    (SELECT Sku FROM ProductSkus WHERE VariantId=v.Id AND RetiredUtc IS NULL LIMIT 1) Sku
                FROM product_events e JOIN ProductVariants v ON v.Id=e.VariantId JOIN Products p ON p.Id=v.ProductId
                JOIN ProductDetails d ON d.ProductId=p.Id JOIN Units u ON u.Id=d.BaseUnitId
                WHERE (@CategoryId IS NULL OR d.CategoryId=@CategoryId)
                AND (@Search IS NULL OR p.Name LIKE @Search OR v.Name LIKE @Search OR EXISTS(
                    SELECT 1 FROM ProductSkus s WHERE s.VariantId=v.Id AND s.Sku LIKE @Search))
            ), grouped AS (
                SELECT VariantId,ProductId,Product,Variant,Unit,Sku,SUM(Sold) Sold,SUM(Returned) Returned,SUM(Cancelled) Cancelled,
                    CASE WHEN SUM(CASE WHEN Sold IS NULL OR Returned IS NULL OR Cancelled IS NULL THEN 1 ELSE 0 END)=0 THEN SUM(Sold-Returned-Cancelled) END Quantity,SUM(Gross) Gross,
                    SUM(CASE WHEN Sold IS NULL OR Returned IS NULL OR Cancelled IS NULL THEN 1 ELSE 0 END) MissingQuantity,
                    SUM(CASE WHEN RevenueComplete=1 THEN Revenue ELSE 0 END) Revenue,
                    SUM(CASE WHEN CostComplete=1 THEN Cost ELSE 0 END) Cost,
                    SUM(CASE WHEN RevenueComplete=0 THEN 1 ELSE 0 END) MissingRevenue,
                    SUM(CASE WHEN CostComplete=0 THEN 1 ELSE 0 END) MissingCost
                FROM selected GROUP BY VariantId
            )";

        internal async Task<StockReportResult> QueryProductsAsync(StockReportQuery q, CancellationToken ct)
        {
            var args=new DynamicParameters(new {q.StartUtc,q.EndUtc,q.CategoryId,Search=string.IsNullOrWhiteSpace(q.Search)?null:"%"+q.Search.Trim()+"%",Limit=q.PageSize,Offset=(q.Page-1)*q.PageSize});
            using(var c=new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using(var tx=c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    var sql="WITH "+Evidence+ProductEvents;
                    var count=await c.ExecuteScalarAsync<long>(new CommandDefinition(sql+" SELECT COUNT(*) FROM grouped",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    var total=await c.QuerySingleAsync<ProductAggregate>(new CommandDefinition(sql+@" SELECT COALESCE(SUM(Revenue),0) Revenue,COALESCE(SUM(Cost),0) Cost,
                        COALESCE(SUM(MissingRevenue),0) MissingRevenue,COALESCE(SUM(MissingCost),0) MissingCost,COALESCE(SUM(MissingQuantity),0) MissingQuantity FROM grouped",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    var pageRows=(await c.QueryAsync<ProductAggregate>(new CommandDefinition(sql+" SELECT * FROM grouped ORDER BY "+(q.Sort=="amount"?"Gross":"Quantity")+" DESC,VariantId LIMIT @Limit OFFSET @Offset",args,tx,cancellationToken:ct)).ConfigureAwait(false)).ToList();
                    var page=pageRows.ToDictionary(x=>x.VariantId);
                    // The very same tax allocation, original-cost lineage and cumulative-restoration
                    // checks as Net Profit. Earlier partials are streamed but never recognized again.
                    var period=new NetProfitQuery {StartUtc=q.StartUtc,EndUtc=q.EndUtc,Periods=new[]{new ProfitPeriod{StartUtc=q.StartUtc,EndUtc=q.EndUtc}}};
                    await AddReturns(c,tx,period,args,null,ct,(r,revenue,value,cost)=> {
                        Action<ProductAggregate> add=x=> { if(revenue)x.Revenue-=value;else x.MissingRevenue++; if(cost)x.Cost-=r.RestoredCost.Value;else x.MissingCost++; };
                        add(total); ProductAggregate item;if(page.TryGetValue(r.VariantId,out item))add(item);
                    },@" AND EXISTS(SELECT 1 FROM SalesInvoiceLines original JOIN ProductVariants v ON v.Id=original.VariantId
                        JOIN Products p ON p.Id=v.ProductId JOIN ProductDetails d ON d.ProductId=p.Id WHERE original.Id=rl.SalesInvoiceLineId
                        AND (@CategoryId IS NULL OR d.CategoryId=@CategoryId) AND (@Search IS NULL OR p.Name LIKE @Search OR v.Name LIKE @Search
                        OR EXISTS(SELECT 1 FROM ProductSkus s WHERE s.VariantId=v.Id AND s.Sku LIKE @Search)))").ConfigureAwait(false);
                    // Unattributable headers must not disappear behind a complete product total.
                    var unassigned=await c.ExecuteScalarAsync<long>(new CommandDefinition("WITH "+Evidence+@" SELECT
                        (SELECT COUNT(*) FROM invoice_facts f WHERE NOT EXISTS(SELECT 1 FROM line_facts WHERE SalesInvoiceId=f.Id))+
                        (SELECT COUNT(*) FROM SalesReturns r WHERE r.CreatedUtc>=@StartUtc AND r.CreatedUtc<@EndUtc AND NOT EXISTS(SELECT 1 FROM SalesReturnLines WHERE SalesReturnId=r.Id))",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    tx.Commit();
                    bool complete=total.MissingRevenue==0 && total.MissingCost==0 && total.MissingQuantity==0 && unassigned==0;
                    return new StockReportResult {Count=count,Scope="ربح إجمالي محقق قبل المصروفات؛ الكميات بالوحدة الأساسية لكل منتج. الأسماء والتصنيفات الحالية للبحث فقط. أحداث غير قابلة لنسبتها إلى منتج: "+unassigned,
                        Summary=new StockReportSummary {Revenue=total.MissingRevenue==0 && unassigned==0?Money(total.Revenue):null,Cost=total.MissingCost==0 && unassigned==0?Money(total.Cost):null,Profit=complete?Money(total.Revenue-total.Cost):null,Complete=complete,IncompleteCount=total.MissingRevenue+total.MissingCost+total.MissingQuantity+unassigned},
                        Items=pageRows.Select(x=>new StockReportRow {ProductId=x.ProductId,VariantId=x.VariantId,Product=x.Product,Variant=x.Variant,Unit=x.Unit,Sku=x.Sku,
                            Sold=x.MissingQuantity==0?Qty(x.Sold):null,Returned=x.MissingQuantity==0?Qty(x.Returned):null,Cancelled=x.MissingQuantity==0?Qty(x.Cancelled):null,Quantity=x.MissingQuantity==0?Qty(x.Quantity):null,RecordedAmount=Money(x.Gross),
                            Revenue=x.MissingRevenue==0?Money(x.Revenue):null,Cost=x.MissingCost==0?Money(x.Cost):null,
                            Profit=x.MissingRevenue==0 && x.MissingCost==0?Money(x.Revenue-x.Cost):null,Complete=x.MissingRevenue==0 && x.MissingCost==0,
                            Margin=x.MissingRevenue==0 && x.MissingCost==0 && x.Revenue>0?((x.Revenue-x.Cost)/x.Revenue*100m).ToString("0.00",CultureInfo.InvariantCulture):null}).ToArray()};
                }
            }
        }
        private static string Qty(decimal value)=>(value/1000000m).ToString("0.######",CultureInfo.InvariantCulture);
        private sealed class ProductAggregate
        {
            public long VariantId{get;set;} public long ProductId{get;set;} public string Product{get;set;} public string Variant{get;set;} public string Unit{get;set;} public string Sku{get;set;}
            public decimal Sold{get;set;} public decimal Returned{get;set;} public decimal Cancelled{get;set;} public decimal Quantity{get;set;} public decimal Gross{get;set;}
            public decimal Revenue{get;set;} public decimal Cost{get;set;} public long MissingRevenue{get;set;} public long MissingCost{get;set;} public long MissingQuantity{get;set;}
        }
    }
}
