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
        public async Task Returns_only_cashier_can_lookup_post_replay_and_view_details_with_csrf_without_manual_treasury_access()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); var draft = await f.Draft(); var sale = (await f.Post(draft.Id)).Invoice;
                var frontend = Path.Combine(Path.GetDirectoryName(f.Db.DatabasePath), "frontend"); Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), "{\"screens\":[{\"route\":\"returns-overview\",\"title\":\"إدارة المرتجعات\",\"module\":\"المرتجعات\"},{\"route\":\"return-details\",\"title\":\"تفاصيل المرتجع\",\"module\":\"المرتجعات\",\"navigationHidden\":true,\"permissionRoute\":\"returns-overview\"}]}");
                var password = Guid.NewGuid().ToString("N"); var users = new SqliteUserStore(f.Db.ConnectionString); var hasher = new PasswordHasher();
                await users.CreateAsync("returns-owner", "مدير", hasher.Hash(password), UserRoles.Administrator, CancellationToken.None);
                var cashierId = await users.CreateAsync("returns-cashier", "كاشير", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                using (var server = new AlKhayalServer(new TestLicenseGate()))
                {
                    var url = new Uri("http://127.0.0.1:" + port); server.Start(new HostOptions { DatabasePath = f.Db.DatabasePath, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') });
                    using (var admin = Client(url)) using (var cashier = Client(url)) using (var anon = Client(url))
                    {
                        var csrf = await Login(admin, "returns-owner", password); var cashierCsrf = await Login(cashier, "returns-cashier", password);
                        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/returns")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/returns")).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + cashierId + "/screen-permissions", new { screenIds = new[] { "returns-overview" } }, csrf)).StatusCode);
                        foreach (var listRoute in new[] { "api/returns", "api/returns?search=", "api/returns?search=%20%20" })
                        {
                            var empty = await cashier.GetAsync(listRoute);
                            Assert.True(empty.StatusCode == HttpStatusCode.OK, listRoute + ": " + await empty.Content.ReadAsStringAsync());
                            Assert.Empty((JArray)JObject.Parse(await empty.Content.ReadAsStringAsync())["data"]);
                        }
                        var lookup = JObject.Parse(await cashier.GetStringAsync("api/returns/invoice?type=sales&number=" + Uri.EscapeDataString(sale.InvoiceNumber)))["data"]; Assert.Equal(sale.Id, (long)lookup["id"]);
                        var command = new { type = "sales", invoiceId = sale.Id, reason = "مرتجع موثق", operationKey = "managed-api-return", lines = new[] { new { invoiceLineId = sale.Lines[0].Id, quantity = "1", units = new object[0] } } };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/returns", command, null)).StatusCode);
                        var created = await Send(cashier, HttpMethod.Post, "api/returns", command, cashierCsrf); Assert.Equal(HttpStatusCode.Created, created.StatusCode); var id = (long)JObject.Parse(await created.Content.ReadAsStringAsync())["data"]["id"];
                        var replay = await Send(cashier, HttpMethod.Post, "api/returns", command, cashierCsrf); Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal(id, (long)JObject.Parse(await replay.Content.ReadAsStringAsync())["data"]["id"]);
                        var detail = JObject.Parse(await cashier.GetStringAsync("api/returns/sales/" + id))["data"]; Assert.Equal("12.34", (string)detail["total"]); Assert.Single((JArray)detail["movements"]); Assert.Equal("مرتجعات البيع", (string)detail["movements"][0]["accountName"]); Assert.Single((JArray)detail["lines"]);
                        Assert.Single((JArray)JObject.Parse(await cashier.GetStringAsync("api/returns?search=" + Uri.EscapeDataString(sale.InvoiceNumber)))["data"]);
                        Assert.Single((JArray)JObject.Parse(await cashier.GetStringAsync("api/returns?search="))["data"]);
                        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("api/returns/sales/99999")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/treasury/accounts")).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync("api/returns/invoice?type=invalid&number=x")).StatusCode);
                    }
                }
            }
        }
    }
}
