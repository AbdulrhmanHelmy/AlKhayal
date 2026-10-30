using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Reports
{
    public sealed class OverviewMetric
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string Route { get; set; }
        public bool Current { get; set; }
        public bool Complete { get; set; } = true;
    }
    public sealed class BusinessOverviewService
    {
        private readonly SalesReportService _sales;
        private readonly TransactionReportService _transactions;
        private readonly NetProfitService _profit;
        private readonly OperationalReportService _operations;
        public BusinessOverviewService(SalesReportService sales,TransactionReportService transactions,NetProfitService profit,OperationalReportService operations)
        { _sales=sales;_transactions=transactions;_profit=profit;_operations=operations; }
        public async Task<IReadOnlyList<OverviewMetric>> QueryAsync(ReportQuery q,ISet<string> allowed,CancellationToken ct)
        {
            ReportPeriod.Validate(q,TimeZoneInfo.Local);
            var metrics=new List<OverviewMetric>();
            if(allowed.Contains("sales")) {
                var d=await _sales.QueryAsync(new SalesReportQuery{From=q.From,To=q.To,PageSize=1},ct).ConfigureAwait(false);
                metrics.Add(new OverviewMetric{Key="Sales",Value=d.Total,Route="report-sales"});
            }
            if(allowed.Contains("returns")) {
                var d=await _transactions.ReturnsAsync(new TransactionReportQuery{From=q.From,To=q.To,PageSize=1},ct).ConfigureAwait(false);
                metrics.Add(new OverviewMetric{Key="Returns",Value=d.SalesReturns,Route="report-returns"});
            }
            if(allowed.Contains("net-profit")) {
                var d=await _profit.QueryAsync(new NetProfitQuery{From=q.From,To=q.To,PageSize=1},ct).ConfigureAwait(false);
                metrics.Add(new OverviewMetric{Key="NetProfit",Value=d.Summary.Complete?d.Summary.NetProfit:null,Complete=d.Summary.Complete,Route="report-net-profit"});
            }
            if(allowed.Contains("payment-methods")) {
                var d=await _transactions.PaymentsAsync(new TransactionReportQuery{From=q.From,To=q.To,PageSize=1},ct).ConfigureAwait(false);
                foreach(var x in new[]{new[]{"Cash",d.Cash},new[]{"Wallet",d.Wallet},new[]{"Card",d.Card},new[]{"Credit",d.Credit},new[]{"Unclassified",d.Unclassified}})
                    metrics.Add(new OverviewMetric{Key=x[0],Value=x[1],Route="report-payment-methods"});
            }
            foreach(var report in new[]{"customers","suppliers","maintenance","installments"}) if(allowed.Contains(report)) {
                var d=await _operations.QueryAsync(report,new OperationalReportQuery{From=q.From,To=q.To,PageSize=1},ct).ConfigureAwait(false);
                var key=report=="maintenance"?"openCount":report=="installments"?"overdueScaled":"balanceScaled";
                metrics.Add(new OverviewMetric{Key=report,Value=d.Summary[key],Current=true,Route="report-"+report});
            }
            return metrics;
        }
    }
}
