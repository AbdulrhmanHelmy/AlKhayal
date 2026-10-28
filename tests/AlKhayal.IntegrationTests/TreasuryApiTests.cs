using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Authenticated_http_crud_csrf_screen_permissions_and_post_replay()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var frontend = Path.Combine(Path.GetDirectoryName(f.DatabasePath), "frontend");
                Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), JsonConvert.SerializeObject(new { screens = new[] {
                    new { route="accounts-list", title="قائمة الخزنة والبنك", module="الخزنة والبنك" },
                    new { route="account-deposit", title="إيداع رصيد", module="الخزنة والبنك" },
                    new { route="account-withdraw", title="سحب رصيد", module="الخزنة والبنك" },
                    new { route="purchase-management", title="المشتريات", module="المشتريات" }
                } }), Encoding.UTF8);
                var password = Guid.NewGuid().ToString("N");
                var users = new SqliteUserStore(f.ConnectionString); var hasher = new PasswordHasher();
                await users.CreateAsync("owner", "مسؤول", hasher.Hash(password), UserRoles.Administrator, CancellationToken.None);
                var cashierId = await users.CreateAsync("cashier", "كاشير", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                using (var server = new AlKhayalServer())
                {
                    server.Start(new HostOptions { DatabasePath = f.DatabasePath, FrontendPath = frontend, Url = "http://127.0.0.1:" + port });
                    var address = new Uri("http://127.0.0.1:" + port);
                    using (var anon = Client(address))
                    {
                        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/treasury/accounts")).StatusCode);
                        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anon, HttpMethod.Post, "api/treasury/deposit", new { }, null)).StatusCode);
                    }
                    using (var admin = Client(address))
                    using (var cashier = Client(address))
                    {
                        var csrf = await Login(admin, "owner", password); var cashierCsrf = await Login(cashier, "cashier", password);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/treasury/accounts")).StatusCode);
                        var body = new { code = "C1", name = "خزنة الشركة", type = "cash" };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Post, "api/treasury/accounts", body, null)).StatusCode);
                        var create = await Send(admin, HttpMethod.Post, "api/treasury/accounts", body, csrf);
                        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
                        var account = JObject.Parse(await create.Content.ReadAsStringAsync())["data"]; var id = (long)account["id"];
                        var movement = new { accountId = id, amount = "12.30", operationKey = "http-retry", notes = "إيداع فعلي" };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Post, "api/treasury/deposit", movement, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Created, (await Send(admin, HttpMethod.Post, "api/treasury/deposit", movement, csrf)).StatusCode);
                        var replay = await Send(admin, HttpMethod.Post, "api/treasury/deposit", movement, csrf);
                        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Contains("\"replayed\":true", await replay.Content.ReadAsStringAsync());
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, "api/treasury/deposit", new { accountId = id, amount = "1.001", operationKey = "invalid" }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, HttpMethod.Post, "api/treasury/withdraw", new { accountId = id, amount = "13", operationKey = "overdraw" }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, HttpMethod.Delete, "api/treasury/accounts/" + id + "?version=2", null, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/treasury/accounts/" + id, new { code = "C1", name = "الاسم المعدل", type = "cash", version = 2 }, csrf)).StatusCode);
                        var unused = await Send(admin, HttpMethod.Post, "api/treasury/accounts", new { code = "B1", name = "بنك", type = "bank" }, csrf);
                        var unusedId = (long)JObject.Parse(await unused.Content.ReadAsStringAsync())["data"]["id"];
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Delete, "api/treasury/accounts/" + unusedId + "?version=1", null, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + cashierId + "/screen-permissions", new { screenIds = new[] { "account-deposit" } }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("api/treasury/accounts")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/treasury/accounts", body, cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/treasury/withdraw", new { accountId = id, amount = "1", operationKey = "cashier-withdraw" }, cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Created, (await Send(cashier, HttpMethod.Post, "api/treasury/deposit", new { accountId = id, amount = "0.01", operationKey = "cashier-deposit" }, cashierCsrf)).StatusCode);
                        Assert.Contains("\"balance\":\"12.31\"", await admin.GetStringAsync("api/treasury/accounts"));
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + cashierId + "/screen-permissions", new { screenIds = new[] { "purchase-management" } }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("api/treasury/accounts")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/treasury/withdraw", new { accountId = id, amount = "1", operationKey = "manual-still-blocked" }, cashierCsrf)).StatusCode);
                        var catalog = new AlKhayal.Application.Catalog.CatalogService(new AlKhayal.Data.Catalog.SqliteCatalogStore(f.ConnectionString));
                        var product = await catalog.CreateProductAsync(new AlKhayal.Application.Catalog.CreateProductCommand { OperationKey="cash-api-product",Name="صنف شراء",BaseUnitId=1,ProductType="stock",SalePrice="15",IsActive=true }, CancellationToken.None);
                        Assert.True(product.Succeeded,product.ErrorMessage);
                        var detail = await catalog.GetProductDetailAsync(product.Value.Id,CancellationToken.None);
                        var supplierId = new AlKhayal.Data.Suppliers.SqliteSupplierRepository(f.ConnectionString).Add(new AlKhayal.Core.Models.Supplier { Code="CASH-API",Name="مورد",Phone="",Status="نشط" });
                        var cashInvoice = new { supplierId,warehouseId=1,invoiceNumber="CASH-API",invoiceDate="2026-10-28",paymentMode="cash",treasuryAccountId=id,
                            lines=new[]{new {variantId=detail.Value.Variants[0].Id,productUnitId=detail.Value.Units[0].Id,quantity="1",purchaseUnitPrice="10.10",saleUnitPrice="15"}} };
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Post,"api/purchases/drafts",cashInvoice,null)).StatusCode);
                        var cashResponse=await Send(cashier,HttpMethod.Post,"api/purchases/drafts",cashInvoice,cashierCsrf);
                        Assert.Equal(HttpStatusCode.Created,cashResponse.StatusCode);
                        var cashId=(long)JObject.Parse(await cashResponse.Content.ReadAsStringAsync())["data"]["id"];
                        var postCash=await Send(cashier,HttpMethod.Post,"api/purchases/"+cashId+"/post",new{operationKey="cash-api-post"},cashierCsrf);
                        Assert.Equal(HttpStatusCode.OK,postCash.StatusCode);
                        var postBody=JObject.Parse(await postCash.Content.ReadAsStringAsync())["data"];
                        Assert.True((bool)postBody["succeeded"]);Assert.Equal(id,(long)postBody["invoice"]["treasuryAccountId"]);Assert.NotNull(postBody["invoice"]["treasuryMovementId"]);
                        Assert.Contains("\"balance\":\"2.21\"",await cashier.GetStringAsync("api/treasury/accounts"));
                        var replayCash=await Send(cashier,HttpMethod.Post,"api/purchases/"+cashId+"/post",new{operationKey="cash-api-post"},cashierCsrf);
                        Assert.Equal(HttpStatusCode.OK,replayCash.StatusCode);Assert.Contains("\"replayed\":true",await replayCash.Content.ReadAsStringAsync());
                    }
                }
            }
        }
        private static HttpClient Client(Uri address) => new HttpClient(new HttpClientHandler { UseCookies = true, UseProxy = false }) { BaseAddress = address };
        private static async Task<string> Login(HttpClient client, string username, string password)
        {
            var result = await Send(client, HttpMethod.Post, "api/auth/login", new { username, password }, null);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            return (string)JObject.Parse(await result.Content.ReadAsStringAsync())["data"]["csrfToken"];
        }
        private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, string csrf)
        {
            var request = new HttpRequestMessage(method, path);
            if (body != null) request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            if (csrf != null) request.Headers.Add("X-CSRF-Token", csrf);
            return client.SendAsync(request);
        }
    }
}
