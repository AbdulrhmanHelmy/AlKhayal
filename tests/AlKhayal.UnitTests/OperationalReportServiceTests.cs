using System;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Reports;
using Xunit;
namespace AlKhayal.UnitTests
{
    public sealed class OperationalReportServiceTests
    {
        private sealed class Store : IOperationalReportStore
        {
            public OperationalReportQuery Query;
            public Task<OperationalReportResult> QueryAsync(string report,OperationalReportQuery query,CancellationToken ct) { Query=query;return Task.FromResult(new OperationalReportResult()); }
        }
        [Theory][InlineData("customers")][InlineData("suppliers")][InlineData("shifts")][InlineData("attendance")][InlineData("maintenance")][InlineData("installments")]
        public async Task Each_report_validates_before_read_port_and_computes_local_calendar_bounds(string report)
        {
            var store=new Store();var service=new OperationalReportService(store,TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
            await Assert.ThrowsAsync<ArgumentException>(()=>service.QueryAsync(report,new OperationalReportQuery{Status="invented"},CancellationToken.None));Assert.Null(store.Query);
            foreach(var day in new[]{"2026-03-08","2026-11-01"}) {
                await service.QueryAsync(report,new OperationalReportQuery{From=day,To=day},CancellationToken.None);
                Assert.Equal(day.EndsWith("03-08")?23:25,(DateTime.Parse(store.Query.EndUtc).ToUniversalTime()-DateTime.Parse(store.Query.StartUtc).ToUniversalTime()).TotalHours);Assert.Matches(@"^\d{4}-\d{2}-\d{2}$",store.Query.Today);
            }
        }
        [Fact]
        public async Task Unsupported_report_never_reaches_store()
        {
            var store=new Store();await Assert.ThrowsAsync<ArgumentException>(()=>new OperationalReportService(store).QueryAsync("unknown",new OperationalReportQuery(),CancellationToken.None));Assert.Null(store.Query);
        }
    }
}
