using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Expenses;
using AlKhayal.Application.Reports;
using AlKhayal.Data.Expenses;
using AlKhayal.Data.Reports;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class ExpenseReportTests
    {
        [Fact]
        public async Task Posted_vouchers_are_counted_once_filtered_and_paged_without_writes()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var ct = CancellationToken.None;
                var expenses = new ExpenseService(new SqliteExpenseStore(f.ConnectionString));
                var rent = await expenses.AddCategoryAsync(new ExpenseCategoryCommand { Name="إيجار" },f.UserId,ct);
                var power = await expenses.AddCategoryAsync(new ExpenseCategoryCommand { Name="كهرباء" },f.UserId,ct);
                var account = await f.Create(); await f.Post(account.Id,"100");
                foreach (var pair in new[] { new { Id=rent.Id, Amount="10.10", Key="one" }, new { Id=power.Id, Amount="20.20", Key="two" } })
                {
                    var cmd = new ExpenseCommand { CategoryId=pair.Id, TreasuryAccountId=account.Id, Amount=pair.Amount, ExpenseDate="2020-01-01", Payee="جهة", Notes="وصف", OperationKey=pair.Key };
                    await expenses.PostAsync(cmd,f.UserId,ct); await expenses.PostAsync(cmd,f.UserId,ct);
                }
                var service = new TransactionReportService(new SqliteTransactionReportStore(f.ConnectionString),TimeZoneInfo.Utc);
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var query = new TransactionReportQuery { From=today,To=today,PageSize=1 };
                var before = System.IO.File.ReadAllBytes(f.DatabasePath);
                var report = await service.ExpensesAsync(query,ct);
                Assert.Equal(2,report.Count); Assert.Equal("30.3000",report.Total); Assert.Single(report.Items); Assert.Equal(2,report.Categories.Count);
                Assert.Equal("2020-01-01",report.Items[0].ExpenseDate);
                query.Page=2; Assert.Single((await service.ExpensesAsync(query,ct)).Items);
                query.Page=1; query.CategoryId=rent.Id; report=await service.ExpensesAsync(query,ct);
                Assert.Equal(1,report.Count); Assert.Equal("10.1000",report.Total); Assert.Single(report.Categories);
                query.Search="not present"; Assert.Equal(0,(await service.ExpensesAsync(query,ct)).Count);
                query.Search=null; query.CategoryId=0; await Assert.ThrowsAsync<ArgumentException>(()=>service.ExpensesAsync(query,ct));
                Assert.Equal(before,System.IO.File.ReadAllBytes(f.DatabasePath));
            }
        }
    }
}
