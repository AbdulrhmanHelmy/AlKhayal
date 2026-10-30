using System;
using AlKhayal.Application.Reports;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class ReportPeriodTests
    {
        [Theory]
        [InlineData("2024-02-29", "2024-03-01")]
        [InlineData("2026-01-31", "2026-02-01")]
        [InlineData("2026-12-31", "2027-01-01")]
        public void Inclusive_calendar_day_has_exclusive_next_day_boundary(string day, string next)
        {
            var q = new ReportQuery { From=day, To=day };
            ReportPeriod.Validate(q, TimeZoneInfo.Utc);
            Assert.Equal(day+"T00:00:00.0000000Z", q.StartUtc);
            Assert.Equal(next+"T00:00:00.0000000Z", q.EndUtc);
        }

        [Theory]
        [InlineData(330, "2026-02-28T18:30:00.0000000Z")]
        [InlineData(-420, "2026-03-01T07:00:00.0000000Z")]
        [InlineData(0, "2026-03-01T00:00:00.0000000Z")]
        public void Uses_timezone_instead_of_fixed_offset(int minutes, string expected)
        {
            var zone=TimeZoneInfo.CreateCustomTimeZone("fixture", TimeSpan.FromMinutes(minutes), "fixture", "fixture");
            var q=new ReportQuery { From="2026-03-01", To="2026-03-01" };
            ReportPeriod.Validate(q,zone); Assert.Equal(expected,q.StartUtc);
        }

        [Theory]
        [InlineData("2026-03-08",23)]
        [InlineData("2026-11-01",25)]
        public void Daylight_saving_converts_both_boundaries_independently(string day, int hours)
        {
            var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var q=new ReportQuery{From=day,To=day};ReportPeriod.Validate(q,zone);
            Assert.Equal(hours,(DateTime.Parse(q.EndUtc).ToUniversalTime()-DateTime.Parse(q.StartUtc).ToUniversalTime()).TotalHours);
        }

        [Theory]
        [InlineData("2026-02-29","2026-03-01",1,20)]
        [InlineData("2026-03-02","2026-03-01",1,20)]
        [InlineData("2026-03-01",null,1,20)]
        [InlineData("2026-03-01","2026-03-01",0,20)]
        [InlineData("2026-03-01","2026-03-01",1,0)]
        [InlineData("2026-03-01","2026-03-01",1,101)]
        [InlineData("2026-03-01","2026-03-01",1000001,20)]
        [InlineData("9999-12-31","9999-12-31",1,20)]
        public void Rejects_invalid_dates_and_page_bounds(string from,string to,int page,int size)
        { Assert.Throws<ArgumentException>(()=>ReportPeriod.Validate(new ReportQuery{From=from,To=to,Page=page,PageSize=size},TimeZoneInfo.Utc)); }

        [Fact]
        public void Missing_dates_default_to_current_server_local_month()
        {
            var q=new ReportQuery(); ReportPeriod.Validate(q,TimeZoneInfo.Utc);
            Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM")+"-01",q.From);
            Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"),q.To);
        }

        [Theory]
        [InlineData("2026-04-01","2026-03-31T22:00:00.0000000Z",23)]
        [InlineData("2026-10-01","2026-09-30T21:00:00.0000000Z",25)]
        public void Skipped_and_repeated_midnights_cover_the_whole_business_date(string day,string start,int hours)
        {
            var midnight=new DateTime(1,1,1,0,0,0);
            var rule=TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026,1,1),new DateTime(2026,12,31),TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(midnight,4,1),TimeZoneInfo.TransitionTime.CreateFixedDateRule(midnight.AddHours(1),10,1));
            var zone=TimeZoneInfo.CreateCustomTimeZone("midnight-fixture",TimeSpan.FromHours(2),"fixture","standard","summer",new[]{rule});
            var q=new ReportQuery{From=day,To=day};ReportPeriod.Validate(q,zone);Assert.Equal(start,q.StartUtc);
            Assert.Equal(hours,(DateTime.Parse(q.EndUtc).ToUniversalTime()-DateTime.Parse(q.StartUtc).ToUniversalTime()).TotalHours);
        }
    }
}
