using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Reports;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class NetProfitServiceTests
    {
        [Fact]
        public async Task Arabic_operator_culture_keeps_invariant_month_boundaries_and_partial_months()
        {
            var prior=CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture=new CultureInfo("ar-SA");
                var store=new Capture();var service=new NetProfitService(store,TimeZoneInfo.Utc);
                await service.QueryAsync(new NetProfitQuery{From="2026-01-31",To="2026-02-02",Breakdown="monthly"},CancellationToken.None);
                Assert.Equal(2,store.Query.Periods.Count);Assert.Equal("2026-01",store.Query.Periods[0].Period);
                Assert.Equal(store.Query.StartUtc,store.Query.Periods[0].StartUtc);
                Assert.Equal(store.Query.Periods[0].EndUtc,store.Query.Periods[1].StartUtc);
                Assert.Equal(store.Query.EndUtc,store.Query.Periods[1].EndUtc);
            }
            finally { CultureInfo.CurrentCulture=prior; }
        }

        [Fact]
        public async Task Final_supported_calendar_month_is_clipped_without_date_overflow()
        {
            var store = new Capture();
            await new NetProfitService(store, TimeZoneInfo.Utc).QueryAsync(
                new NetProfitQuery { From = "9999-12-01", To = "9999-12-30", Breakdown = "monthly" }, CancellationToken.None);
            var period = Assert.Single(store.Query.Periods);
            Assert.Equal("9999-12", period.Period);
            Assert.Equal(store.Query.StartUtc, period.StartUtc);
            Assert.Equal(store.Query.EndUtc, period.EndUtc);
        }

        [Fact]
        public async Task Unsupported_breakdown_and_unbounded_ranges_never_reach_store()
        {
            var store=new Capture();var service=new NetProfitService(store,TimeZoneInfo.Utc);
            await Assert.ThrowsAsync<ArgumentException>(()=>service.QueryAsync(new NetProfitQuery{From="2026-01-01",To="2026-01-02",Breakdown="weekly"},CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(()=>service.QueryAsync(new NetProfitQuery{From="2000-01-01",To="2020-01-01"},CancellationToken.None));
            Assert.Null(store.Query);
        }

        [Fact]
        public async Task Classification_requires_explicit_value_reason_revision_and_operation_identity()
        {
            var service=new NetProfitService(new Capture());
            foreach(var command in new[]{new ExpenseClassificationCommand{Classification="included",Reason="x",OperationKey="key"},
                new ExpenseClassificationCommand{Classification="guess",Reason="مراجعة",OperationKey="key"},
                new ExpenseClassificationCommand{Classification="included",Reason="مراجعة",OperationKey=""},
                new ExpenseClassificationCommand{Classification="included",Reason="مراجعة",OperationKey="key",ExpectedRevision=long.MaxValue}})
                await Assert.ThrowsAsync<ArgumentException>(()=>service.ClassifyAsync(1,command,1,CancellationToken.None));
        }

        private sealed class Capture : INetProfitStore
        {
            public NetProfitQuery Query;
            public Task<NetProfitResult> QueryAsync(NetProfitQuery q,CancellationToken ct){Query=q;return Task.FromResult(new NetProfitResult());}
            public Task ClassifyAsync(long id,ExpenseClassificationCommand cmd,long actor,CancellationToken ct)=>throw new InvalidOperationException("Invalid input reached store");
        }
    }
}
