using System;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Reports;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class StockReportServiceTests
    {
        private sealed class Store : IStockReportStore
        {
            public StockReportQuery Query;
            public Task<StockReportResult> QueryAsync(string report,StockReportQuery query,CancellationToken ct) {Query=query;return Task.FromResult(new StockReportResult());}
        }
        [Theory]
        [InlineData("inventory-valuation")] [InlineData("low-stock")]
        public async Task Current_snapshots_reject_historical_dates(string report)
        {
            var store=new Store();var service=new StockReportService(store,TimeZoneInfo.Utc);
            await Assert.ThrowsAsync<ArgumentException>(async()=>await service.QueryAsync(report,new StockReportQuery{From="2026-01-01",To="2026-01-31"},CancellationToken.None));Assert.Null(store.Query);
        }
        [Theory]
        [InlineData("product-profit")] [InlineData("top-products")]
        public async Task Product_scopes_reject_unsupported_warehouse_attribution(string report)
        {
            var store=new Store();await Assert.ThrowsAsync<ArgumentException>(async()=>await new StockReportService(store).QueryAsync(report,new StockReportQuery{WarehouseId=1},CancellationToken.None));Assert.Null(store.Query);
        }
        [Theory]
        [InlineData("2026-03-08",23)] [InlineData("2026-11-01",25)]
        public async Task Report_calendar_boundaries_retain_DST_for_historical_product_events(string day,int hours)
        {
            var store=new Store();var service=new StockReportService(store,TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
            await service.QueryAsync("product-profit",new StockReportQuery{From=day,To=day},CancellationToken.None);
            Assert.Equal(hours,(DateTime.Parse(store.Query.EndUtc).ToUniversalTime()-DateTime.Parse(store.Query.StartUtc).ToUniversalTime()).TotalHours);
        }
    }
}
