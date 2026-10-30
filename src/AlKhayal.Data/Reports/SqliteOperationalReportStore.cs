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
    // Read models only: no balance projections or posting writes.
    public sealed class SqliteOperationalReportStore : IOperationalReportStore
    {
        private readonly string _connectionString;
        public SqliteOperationalReportStore(string connectionString) { _connectionString=connectionString; }
        private static string Key(string value) => char.ToLowerInvariant(value[0])+value.Substring(1);
        private static string Money(object value) { return (Convert.ToDecimal(value,CultureInfo.InvariantCulture)/10000m).ToString("0.0000",CultureInfo.InvariantCulture); }
        public async Task<OperationalReportResult> QueryAsync(string report, OperationalReportQuery q, CancellationToken ct)
        {
            string source, summary;
            switch(report)
            {
                case "customers":
                    source=@"WITH ledger AS (SELECT CustomerId,SUM(CASE WHEN TransactionType IN ('opening','increase') THEN AmountScaled ELSE -AmountScaled END) BalanceScaled,
                        SUM(CASE WHEN TransactionType IN ('collection','legacy_payment') AND CreatedUtc>=@StartUtc AND CreatedUtc<@EndUtc THEN AmountScaled ELSE 0 END) PeriodPaidScaled,MAX(CreatedUtc) EventUtc
                        FROM CustomerTransactions GROUP BY CustomerId), credits AS (SELECT CustomerId,SUM(TotalScaled) CreditScaled FROM SalesInvoices WHERE Status='posted' AND PaymentMode='credit' AND PostedUtc>=@StartUtc AND PostedUtc<@EndUtc GROUP BY CustomerId),
                        rows AS (SELECT c.Id,c.Id PersonId,c.Name Party,c.Phone Contact,c.Status,COALESCE(l.BalanceScaled,0) BalanceScaled,COALESCE(l.PeriodPaidScaled,0) PeriodPaidScaled,COALESCE(v.CreditScaled,0) CreditScaled,l.EventUtc
                        FROM Customers c LEFT JOIN ledger l ON l.CustomerId=c.Id LEFT JOIN credits v ON v.CustomerId=c.Id
                        WHERE (@Search IS NULL OR c.Name LIKE @Like OR c.Phone LIKE @Like) AND (@PersonId IS NULL OR c.Id=@PersonId) AND (@Status IS NULL OR COALESCE(l.BalanceScaled,0)>0)) ";
                    summary="COUNT(*) Count,COALESCE(SUM(CASE WHEN BalanceScaled>0 THEN BalanceScaled ELSE 0 END),0) BalanceScaled,COALESCE(SUM(CASE WHEN BalanceScaled>0 THEN 1 ELSE 0 END),0) DebtCount,COALESCE(SUM(PeriodPaidScaled),0) PeriodPaidScaled,COALESCE(SUM(CreditScaled),0) CreditScaled";
                    break;
                case "suppliers":
                    source=@"WITH ledger AS (SELECT SupplierId,SUM(AmountScaled) BalanceScaled,SUM(CASE WHEN EntryType IN ('payment','legacy_payment') AND CreatedUtc>=@StartUtc AND CreatedUtc<@EndUtc THEN -AmountScaled ELSE 0 END) PeriodPaidScaled,MAX(CreatedUtc) EventUtc FROM SupplierLedgerEntries GROUP BY SupplierId),
                        credits AS (SELECT SupplierId,SUM(TotalScaled) CreditScaled FROM PurchaseInvoices WHERE Status='posted' AND PaymentMode='credit' AND PostedUtc>=@StartUtc AND PostedUtc<@EndUtc GROUP BY SupplierId),
                        rows AS (SELECT c.Id,c.Id PersonId,c.Name Party,c.Phone Contact,c.Status,COALESCE(l.BalanceScaled,0) BalanceScaled,COALESCE(l.PeriodPaidScaled,0) PeriodPaidScaled,COALESCE(v.CreditScaled,0) CreditScaled,l.EventUtc
                        FROM Suppliers c LEFT JOIN ledger l ON l.SupplierId=c.Id LEFT JOIN credits v ON v.SupplierId=c.Id WHERE (@Search IS NULL OR c.Name LIKE @Like OR c.Phone LIKE @Like OR c.Code LIKE @Like) AND (@PersonId IS NULL OR c.Id=@PersonId) AND (@Status IS NULL OR COALESCE(l.BalanceScaled,0)>0)) ";
                    summary="COUNT(*) Count,COALESCE(SUM(CASE WHEN BalanceScaled>0 THEN BalanceScaled ELSE 0 END),0) BalanceScaled,COALESCE(SUM(CASE WHEN BalanceScaled>0 THEN 1 ELSE 0 END),0) DebtCount,COALESCE(SUM(PeriodPaidScaled),0) PeriodPaidScaled,COALESCE(SUM(CreditScaled),0) CreditScaled";
                    break;
                case "shifts":
                    // Reuse the workforce source, including its legacy event definitions.
                    source="WITH original AS ("+Workforce.SqliteWorkforceStore.ShiftSql+@"), rows AS (SELECT Id,CashierId PersonId,CashierName Party,OpenedUtc EventUtc,ClosedUtc EndUtc,OpeningScaled,CountedScaled,
                        COALESCE(ClosingExpectedScaled,OpeningScaled+CashSalesScaled+CollectionsScaled+CashInScaled-CashOutScaled) ExpectedScaled,
                        SalesCount LegacySalesCount,CashSalesScaled,CreditSalesScaled,
                        CASE WHEN ClosedUtc IS NULL THEN 'open' ELSE 'closed' END Status,
                        CASE WHEN CountedScaled<ClosingExpectedScaled THEN ClosingExpectedScaled-CountedScaled ELSE 0 END ShortageScaled,
                        CASE WHEN CountedScaled>ClosingExpectedScaled THEN CountedScaled-ClosingExpectedScaled ELSE 0 END OverageScaled
                        FROM original WHERE OpenedUtc>=@StartUtc AND OpenedUtc<@EndUtc AND (@PersonId IS NULL OR CashierId=@PersonId) AND (@Search IS NULL OR CashierName LIKE @Like) AND (@Status IS NULL OR (@Status='open' AND ClosedUtc IS NULL) OR (@Status='closed' AND ClosedUtc IS NOT NULL))) ";
                    summary="COUNT(*) Count,COALESCE(SUM(CASE WHEN Status='open' THEN 1 ELSE 0 END),0) OpenCount,COALESCE(SUM(CASE WHEN Status='closed' THEN 1 ELSE 0 END),0) ClosedCount,COALESCE(SUM(CASE WHEN Status='closed' THEN ExpectedScaled ELSE 0 END),0) ExpectedScaled,COALESCE(SUM(CountedScaled),0) CountedScaled,COALESCE(SUM(ShortageScaled),0) ShortageScaled,COALESCE(SUM(OverageScaled),0) OverageScaled,COALESCE(SUM(CashSalesScaled),0) CashSalesScaled,COALESCE(SUM(CreditSalesScaled),0) CreditSalesScaled,COALESCE(SUM(LegacySalesCount),0) LegacySalesCount";
                    break;
                case "attendance":
                    source=@"WITH rows AS (SELECT a.Id,e.Id PersonId,e.Name Party,a.CheckInUtc EventUtc,a.CheckOutUtc EndUtc,CASE WHEN a.CheckOutUtc IS NULL THEN 'open' ELSE 'closed' END Status,
                        CASE WHEN a.CheckOutUtc IS NULL THEN NULL ELSE CAST(ROUND((julianday(a.CheckOutUtc)-julianday(a.CheckInUtc))*86400) AS INTEGER) END WorkedSeconds
                        FROM EmployeeAttendance a JOIN Employees e ON e.Id=a.EmployeeId WHERE a.CheckInUtc>=@StartUtc AND a.CheckInUtc<@EndUtc AND (@PersonId IS NULL OR e.Id=@PersonId) AND (@Search IS NULL OR e.Name LIKE @Like) AND (@Status IS NULL OR (@Status='open' AND a.CheckOutUtc IS NULL) OR (@Status='closed' AND a.CheckOutUtc IS NOT NULL))) ";
                    summary=@"COUNT(*) Count,COALESCE(SUM(WorkedSeconds),0) WorkedSeconds,COALESCE(SUM(CASE WHEN Status='open' THEN 1 ELSE 0 END),0) OpenCount,(SELECT COUNT(*) FROM Employees WHERE IsActive=1 AND (@PersonId IS NULL OR Id=@PersonId) AND (@Search IS NULL OR Name LIKE @Like)) ActiveEmployees";
                    break;
                case "maintenance":
                    source=@"WITH receipts AS (SELECT TicketId,SUM(AmountScaled) ReceivedScaled,SUM(CASE WHEN ReceiptType='advance' AND CreatedUtc>=@StartUtc AND CreatedUtc<@EndUtc THEN AmountScaled ELSE 0 END) PeriodPaidScaled FROM MaintenanceTicketReceipts GROUP BY TicketId),
                        deliveries AS (SELECT TicketId,MAX(ChangedUtc) DeliveryUtc FROM MaintenanceTicketStatusHistory WHERE Status='تم التسليم' GROUP BY TicketId),
                        rows AS (SELECT t.Id,t.CustomerId PersonId,t.TicketNumber Reference,c.Name Party,t.Device||' '||t.Model Device,t.CreatedUtc EventUtc,t.ReceivedOn IntakeDate,t.DeliveredOn DeliveryDate,t.Status,t.AdvancePaidScaled AdvanceScaled,t.FinalCostScaled ChargeScaled,
                        CASE WHEN t.FinalCostScaled IS NULL THEN NULL ELSE MAX(t.FinalCostScaled-COALESCE(r.ReceivedScaled,0),0) END BalanceScaled,
                        COALESCE(r.PeriodPaidScaled,0) PeriodPaidScaled,
                        CASE WHEN t.CreatedUtc>=@StartUtc AND t.CreatedUtc<@EndUtc THEN 1 ELSE 0 END IntakeCount,
                        CASE WHEN d.DeliveryUtc>=@StartUtc AND d.DeliveryUtc<@EndUtc THEN 1 ELSE 0 END DeliveredCount,
                        CASE WHEN d.DeliveryUtc>=@StartUtc AND d.DeliveryUtc<@EndUtc THEN t.FinalCostScaled ELSE 0 END PeriodChargeScaled
                        FROM MaintenanceTickets t JOIN Customers c ON c.Id=t.CustomerId LEFT JOIN receipts r ON r.TicketId=t.Id LEFT JOIN deliveries d ON d.TicketId=t.Id
                        WHERE (@Search IS NULL OR c.Name LIKE @Like OR t.TicketNumber LIKE @Like) AND (@PersonId IS NULL OR t.CustomerId=@PersonId) AND (@Status IS NULL OR t.Status=@Status)) ";
                    summary=@"COUNT(*) Count,COALESCE(SUM(IntakeCount),0) IntakeCount,COALESCE(SUM(DeliveredCount),0) DeliveredCount,COALESCE(SUM(CASE WHEN Status='جاهز للتسليم' THEN 1 ELSE 0 END),0) ReadyCount,COALESCE(SUM(CASE WHEN Status<>'تم التسليم' THEN 1 ELSE 0 END),0) OpenCount,COALESCE(SUM(PeriodPaidScaled),0) PeriodPaidScaled,COALESCE(SUM(PeriodChargeScaled),0) PeriodChargeScaled,COALESCE(SUM(BalanceScaled),0) BalanceScaled";
                    break;
                case "installments":
                    source=@"WITH schedule AS (SELECT s.ContractId,SUM(MAX(s.AmountScaled-COALESCE(p.AmountScaled,0),0)) BalanceScaled,
                        SUM(CASE WHEN s.DueOn<@Today THEN MAX(s.AmountScaled-COALESCE(p.AmountScaled,0),0) ELSE 0 END) OverdueScaled,
                        SUM(CASE WHEN s.DueOn>=@From AND s.DueOn<=@To THEN s.AmountScaled ELSE 0 END) DueScaled,
                        MIN(CASE WHEN s.AmountScaled>COALESCE(p.AmountScaled,0) THEN s.DueOn ELSE NULL END) NextDue,
                        SUM(COALESCE(p.AmountScaled,0)) PaidScaled,
                        SUM(CASE WHEN p.CreatedUtc>=@StartUtc AND p.CreatedUtc<@EndUtc THEN p.AmountScaled ELSE 0 END) PeriodPaidScaled,
                        SUM(CASE WHEN p.AmountScaled IS NOT NULL AND p.AmountScaled<>s.AmountScaled THEN 1 ELSE 0 END) CoverageCount
                        FROM CustomerInstallmentSchedule s LEFT JOIN CustomerInstallmentPayments p ON p.ContractId=s.ContractId AND p.InstallmentNumber=s.InstallmentNumber GROUP BY s.ContractId),
                        rows AS (SELECT t.Id,t.CustomerId PersonId,t.ContractNumber Reference,c.Name Party,t.CreatedUtc EventUtc,s.*,CASE WHEN s.BalanceScaled>0 THEN 'active' ELSE 'completed' END Status
                        FROM CustomerInstallmentContracts t JOIN Customers c ON c.Id=t.CustomerId JOIN schedule s ON s.ContractId=t.Id WHERE (@Search IS NULL OR c.Name LIKE @Like OR t.ContractNumber LIKE @Like) AND (@PersonId IS NULL OR t.CustomerId=@PersonId)
                        AND (@Status IS NULL OR (@Status='active' AND s.BalanceScaled>0) OR (@Status='completed' AND s.BalanceScaled=0) OR (@Status='overdue' AND s.OverdueScaled>0) OR (@Status='due' AND s.DueScaled>0))) ";
                    summary="COUNT(*) Count,COALESCE(SUM(CASE WHEN Status='active' THEN 1 ELSE 0 END),0) ActiveCount,COALESCE(SUM(BalanceScaled),0) BalanceScaled,COALESCE(SUM(OverdueScaled),0) OverdueScaled,COALESCE(SUM(DueScaled),0) DueScaled,COALESCE(SUM(PeriodPaidScaled),0) PeriodPaidScaled,COALESCE(SUM(CoverageCount),0) CoverageCount";
                    break;
                default: throw new ArgumentException("التقرير غير معروف.");
            }
            var args=new {q.From,q.To,q.Today,q.StartUtc,q.EndUtc,q.PersonId,q.Status,Search=string.IsNullOrWhiteSpace(q.Search)?null:q.Search.Trim(),Like="%"+(q.Search??"").Trim()+"%",Limit=q.PageSize,Offset=(q.Page-1)*q.PageSize};
            using(var c=new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using(var tx=c.BeginTransaction())
                {
                    var totals=(IDictionary<string,object>)await c.QuerySingleAsync<object>(new CommandDefinition(source+"SELECT "+summary+" FROM rows",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    var rows=await c.QueryAsync<object>(new CommandDefinition(source+"SELECT * FROM rows ORDER BY EventUtc DESC,Id DESC LIMIT @Limit OFFSET @Offset",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    var result=new OperationalReportResult {Count=Convert.ToInt64(totals["Count"]),Summary=totals.ToDictionary(x=>Key(x.Key),x=>x.Key.EndsWith("Scaled",StringComparison.Ordinal)?Money(x.Value):Convert.ToString(x.Value,CultureInfo.InvariantCulture)),Items=rows.Select(x=>(IDictionary<string,object>)x).Select(x=>(IDictionary<string,object>)x.ToDictionary(v=>Key(v.Key),v=>v.Value!=null && v.Key.EndsWith("Scaled",StringComparison.Ordinal)?(object)Money(v.Value):v.Value)).ToList()};
                    tx.Commit();return result;
                }
            }
        }
    }
}
