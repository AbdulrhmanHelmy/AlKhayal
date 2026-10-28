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
        public async Task Pos_only_cashier_posts_cash_sale_and_replay_to_shift_revenue_without_manual_treasury_permission()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); var frontend = Path.Combine(Path.GetDirectoryName(f.Db.DatabasePath), "frontend"); Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), "{\"screens\":[{\"route\":\"pos\",\"title\":\"الكاشير اللمسي\",\"module\":\"المبيعات\"}]}");
                var password = Guid.NewGuid().ToString("N"); var users = new SqliteUserStore(f.Db.ConnectionString); var hasher = new PasswordHasher();
                await users.CreateAsync("revenue-owner", "مدير", hasher.Hash(password), UserRoles.Administrator, CancellationToken.None);
                var cashierId = await users.CreateAsync("revenue-cashier", "كاشير", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                using (var server = new AlKhayalServer())
                {
                    var url = new Uri("http://127.0.0.1:" + port); server.Start(new HostOptions { DatabasePath = f.Db.DatabasePath, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') });
                    using (var admin = Client(url)) using (var cashier = Client(url)) using (var anon = Client(url))
                    {
                        var csrf = await Login(admin, "revenue-owner", password); var cashierCsrf = await Login(cashier, "revenue-cashier", password);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + cashierId + "/screen-permissions", new { screenIds = new[] { "pos" } }, csrf)).StatusCode);
                        var draft = await Send(cashier, HttpMethod.Post, "api/sales/drafts", new { warehouseId = 1, invoiceDate = "2026-10-28", paymentMode = "cash", discount = "0.00", lines = new[] { new { variantId = f.Variant, productUnitId = f.Unit, quantity = "1", unitPrice = "12.34", discount = "0.00" } } }, cashierCsrf);
                        Assert.Equal(HttpStatusCode.Created, draft.StatusCode); var id = (long)JObject.Parse(await draft.Content.ReadAsStringAsync())["data"]["id"];
                        var route = "api/sales/" + id + "/post"; var command = new { operationKey = "pos-revenue-api" };
                        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anon, HttpMethod.Post, route, command, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, route, command, null)).StatusCode);
                        var posted = await Send(cashier, HttpMethod.Post, route, command, cashierCsrf); Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
                        var invoice = JObject.Parse(await posted.Content.ReadAsStringAsync())["data"]; Assert.Equal("إيراد الوردية", (string)invoice["treasuryAccountName"]); Assert.True((long)invoice["treasuryMovementId"] > 0);
                        var replay = await Send(cashier, HttpMethod.Post, route, command, cashierCsrf); Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(invoice["treasuryMovementId"], JObject.Parse(await replay.Content.ReadAsStringAsync())["data"]["treasuryMovementId"]);
                        Assert.Equal("12.34", Assert.Single(await f.Db.Service.ListAsync(CancellationToken.None)).Balance);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/treasury/accounts")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/treasury/deposit", new { accountId = (long)invoice["treasuryAccountId"], amount = "1", operationKey = "manual" }, cashierCsrf)).StatusCode);
                    }
                }
            }
        }
    }
}
