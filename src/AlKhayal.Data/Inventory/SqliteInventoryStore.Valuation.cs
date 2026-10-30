using System;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Inventory;
using Dapper;

namespace AlKhayal.Data.Inventory
{
    public sealed partial class SqliteInventoryStore
    {
        internal class ValuationRow
        {
            public long VariantId { get; set; }
            public long QuantityBaseScaled { get; set; }
            public long ValueScaled { get; set; }
            public long LayerQuantity { get; set; }
            public long LayerValue { get; set; }
            public long UnresolvedCount { get; set; }
            public long? SalePriceScaled { get; set; }
        }

        internal const string ValuationEvidence = @"SELECT b.Id,b.WarehouseId,b.BatchId,b.VariantId,b.QuantityBaseScaled,b.ValueScaled,
                      COALESCE((SELECT SUM(l.RemainingQuantityScaled) FROM InventoryCostLayers l
                        WHERE l.WarehouseId=b.WarehouseId AND l.VariantId=b.VariantId AND l.BatchId IS b.BatchId AND l.RemainingQuantityScaled>0),0) LayerQuantity,
                      COALESCE((SELECT SUM(l.RemainingValueScaled) FROM InventoryCostLayers l
                        WHERE l.WarehouseId=b.WarehouseId AND l.VariantId=b.VariantId AND l.BatchId IS b.BatchId AND l.RemainingQuantityScaled>0),0) LayerValue,
                      (SELECT COUNT(*) FROM InventoryCostLayers l WHERE l.WarehouseId=b.WarehouseId
                        AND l.VariantId=b.VariantId AND l.BatchId IS b.BatchId AND (l.Basis='unresolved' OR l.UnitCostScaled IS NULL)
                        AND l.RemainingQuantityScaled>0) UnresolvedCount,
                      COALESCE(v.SellPriceOverrideScaled,d.SellPriceScaled) SalePriceScaled
                    FROM InventoryBalances b JOIN ProductVariants v ON v.Id=b.VariantId
                    JOIN ProductDetails d ON d.ProductId=v.ProductId";
        internal static bool CostComplete(ValuationRow row) => row.UnresolvedCount==0 && row.LayerQuantity==row.QuantityBaseScaled && row.LayerValue==row.ValueScaled;
        internal static decimal SellingScaled(ValuationRow row) => decimal.Round((decimal)row.QuantityBaseScaled*row.SalePriceScaled.Value/1000000m/100m,0,MidpointRounding.AwayFromZero)*100m;

        public async Task<InventoryValuation> ValuationAsync(long warehouseId,long? productId,CancellationToken ct)
        {
            using(var c=Open())
            {
                if(await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Warehouses WHERE Id=@Id",new{Id=warehouseId},cancellationToken:ct)).ConfigureAwait(false)==0)
                    throw new ArgumentException("المخزن غير موجود.");
                var rows=(await c.QueryAsync<ValuationRow>(new CommandDefinition(ValuationEvidence+@"
                    WHERE b.WarehouseId=@WarehouseId AND b.QuantityBaseScaled>0
                      AND (@ProductId IS NULL OR v.ProductId=@ProductId)",
                    new{WarehouseId=warehouseId,ProductId=productId},cancellationToken:ct)).ConfigureAwait(false)).ToList();
                decimal cost=0,selling=0,quantity=0;
                var result=new InventoryValuation{WarehouseId=warehouseId,ProductId=productId,CostComplete=true,SellingComplete=true,VariantCount=rows.Select(x=>x.VariantId).Distinct().LongCount()};
                foreach(var row in rows)
                {
                    quantity+=row.QuantityBaseScaled;
                    if(!CostComplete(row))result.CostComplete=false;
                    else cost+=row.LayerValue;
                    if(!row.SalePriceScaled.HasValue||row.SalePriceScaled.Value<0)result.SellingComplete=false;
                    else selling+=SellingScaled(row);
                }
                result.QuantityBaseScaled=checked((long)quantity);
                result.CostValue=result.CostComplete?((long)cost/10000m).ToString("0.0000",CultureInfo.InvariantCulture):null;
                result.PotentialSellingValue=result.SellingComplete?((long)selling/10000m).ToString("0.0000",CultureInfo.InvariantCulture):null;
                result.EstimatedGrossMargin=result.CostComplete&&result.SellingComplete?((selling-cost)/10000m).ToString("0.0000",CultureInfo.InvariantCulture):null;
                return result;
            }
        }
    }
}
