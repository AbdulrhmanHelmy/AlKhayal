using System;
using System.Globalization;
using System.Linq;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Reports;
using AlKhayal.Data.Reports;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SalesReportTests
    {
        [Fact]
        public async Task Returns_are_counted_on_their_event_day_and_empty_periods_remain_empty()
        {
            using(var f=new SalesTreasuryTests.Fixture()) {
                await f.Prepare(); var invoice=await f.Draft();Assert.True((await f.Post(invoice.Id,"report-event")).Succeeded);
                await f.FundRefund(); var saved=await f.Sales.GetAsync(invoice.Id,CancellationToken.None);
                var ret=await f.Sales.ReturnAsync(invoice.Id,new AlKhayal.Application.Sales.SaleReturnCommand { OperationKey="report-return",Reason="مرتجع",Lines=new System.Collections.Generic.List<AlKhayal.Application.Sales.SaleReturnLineCommand> { new AlKhayal.Application.Sales.SaleReturnLineCommand { InvoiceLineId=saved.Lines.Single().Id,Quantity="1" } } },f.Db.UserId,CancellationToken.None);
                Assert.True(ret.Succeeded,ret.ErrorMessage);
                var service=new SalesReportService(new SqliteSalesReportStore(f.Db.ConnectionString),TimeZoneInfo.Utc);
                var today=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var r=await service.QueryAsync(new SalesReportQuery { From=today,To=today },CancellationToken.None);
                Assert.Equal("12.3400",r.Total); Assert.Equal("12.3400",r.Returns);Assert.Equal("0.0000",r.NetEvents);
                var tomorrow=DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                r=await service.QueryAsync(new SalesReportQuery { From=tomorrow,To=tomorrow },CancellationToken.None);
                Assert.Equal(0,r.Count);Assert.Equal("0.0000",r.Returns);
            }
        }
        [Fact]
        public async Task Posted_totals_exclude_drafts_are_exact_filtered_paginated_and_read_only()
        {
            using(var f=new SalesTreasuryTests.Fixture()) {
                await f.Prepare();
                var first=await f.Draft(); Assert.True((await f.Post(first.Id,"report-first")).Succeeded);
                var second=await f.Draft(); Assert.True((await f.Post(second.Id,"report-second")).Succeeded);
                await f.Draft();
                var day=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var service=new SalesReportService(new SqliteSalesReportStore(f.Db.ConnectionString),TimeZoneInfo.Utc);
                var q=new SalesReportQuery { From=day,To=day,PageSize=1 };
                long before; using(var c=f.Db.Open()) before=c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments");
                var result=await service.QueryAsync(q,CancellationToken.None);
                Assert.Equal(2,result.Count); Assert.Equal("24.6800",result.Total); Assert.Equal("24.6800",result.Cash); Assert.Equal("0.0000",result.Returns);
                Assert.Equal(second.Id,Assert.Single(result.Items).Id);
                q.Page=2; Assert.Equal(first.Id,Assert.Single((await service.QueryAsync(q,CancellationToken.None)).Items).Id);
                q.Page=1;q.Search=first.InvoiceNumber;result=await service.QueryAsync(q,CancellationToken.None);Assert.Equal(1,result.Count);Assert.Equal(first.Id,Assert.Single(result.Items).Id);
                q.Search="missing";Assert.Empty((await service.QueryAsync(q,CancellationToken.None)).Items);
                q.Search=null;q.Method="wallet";Assert.Equal(0,(await service.QueryAsync(q,CancellationToken.None)).Count);
                using(var c=f.Db.Open()) Assert.Equal(before,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));
                await Assert.ThrowsAsync<ArgumentException>(()=>service.QueryAsync(new SalesReportQuery { From="2026-01-02",To="2026-01-01" },CancellationToken.None));
            }
        }
    }
}
