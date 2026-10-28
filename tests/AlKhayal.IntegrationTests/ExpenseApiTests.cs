using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Expenses_http_permissions_csrf_exact_money_replay_and_selected_treasury_are_enforced()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var frontend = Path.Combine(Path.GetDirectoryName(f.DatabasePath), "frontend"); Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), "{\"screens\":[{\"route\":\"expenses-list\",\"title\":\"إدارة المصروفات\",\"module\":\"المصروفات والسندات\"},{\"route\":\"expense-add\",\"title\":\"سند صرف\",\"module\":\"المصروفات والسندات\"}]}");
                var password = Guid.NewGuid().ToString("N"); var users = new SqliteUserStore(f.ConnectionString); var hasher = new PasswordHasher();
                await users.CreateAsync("expense-owner", "مدير", hasher.Hash(password), UserRoles.Administrator, CancellationToken.None);
                var cashierId = await users.CreateAsync("expense-cashier", "كاشير", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var account = await f.Create(); await f.Post(account.Id, "100.20");
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                using (var server = new AlKhayalServer())
                {
                    var url = new Uri("http://127.0.0.1:" + port); server.Start(new HostOptions { DatabasePath = f.DatabasePath, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') });
                    using (var admin = Client(url)) using (var cashier = Client(url)) using (var anon = Client(url))
                    {
                        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/expenses/categories")).StatusCode);
                        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anon, HttpMethod.Post, "api/expenses/vouchers", new { }, null)).StatusCode);
                        var csrf = await Login(admin, "expense-owner", password); var cashierCsrf = await Login(cashier, "expense-cashier", password);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/expenses/categories")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Post, "api/expenses/categories", new { name = "إيجار" }, null)).StatusCode);
                        var create = await Send(admin, HttpMethod.Post, "api/expenses/categories", new { name = "إيجار" }, csrf); Assert.Equal(HttpStatusCode.Created, create.StatusCode);
                        var id = (long)JObject.Parse(await create.Content.ReadAsStringAsync())["data"]["id"];
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + cashierId + "/screen-permissions", new { screenIds = new[] { "expenses-list" } }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("api/expenses/categories")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/expenses/vouchers")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/treasury/accounts")).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + cashierId + "/screen-permissions", new { screenIds = new[] { "expense-add" } }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("api/treasury/accounts")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/expenses/categories", new { name = "أخرى" }, cashierCsrf)).StatusCode);
                        var command = new { categoryId = id, treasuryAccountId = account.Id, amount = "10.10", expenseDate = "2026-10-28", payee = "مالك العقار", notes = "إيجار موثق", operationKey = "expense-api" };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/expenses/vouchers", command, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Created, (await Send(cashier, HttpMethod.Post, "api/expenses/vouchers", command, cashierCsrf)).StatusCode);
                        var replay = await Send(cashier, HttpMethod.Post, "api/expenses/vouchers", command, cashierCsrf); Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Contains("\"replayed\":true", await replay.Content.ReadAsStringAsync());
                        var rows = JObject.Parse(await cashier.GetStringAsync("api/expenses/vouchers"))["data"]; Assert.Single(rows); Assert.Equal("10.10", (string)rows[0]["amount"]); Assert.Null(rows[0]["requestHash"]);
                        Assert.Equal("90.10", (string)JObject.Parse(await cashier.GetStringAsync("api/treasury/accounts"))["data"][0]["balance"]);
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(cashier, HttpMethod.Post, "api/expenses/vouchers", new { categoryId = id, treasuryAccountId = account.Id, amount = "1.001", expenseDate = "2026-10-28", operationKey = "invalid" }, cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Conflict, (await Send(cashier, HttpMethod.Post, "api/expenses/vouchers", new { categoryId = id, treasuryAccountId = account.Id, amount = "91", expenseDate = "2026-10-28", operationKey = "overdraw" }, cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/treasury/withdraw", new { accountId = account.Id, amount = "1", operationKey = "manual" }, cashierCsrf)).StatusCode);
                    }
                }
            }
        }
    }
}
