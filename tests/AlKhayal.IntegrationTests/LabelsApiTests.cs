using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Printing;
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
        public async Task Labels_HTTP_settings_permissions_module_CSFR_origin_barcode_persistence_POS_and_batch_replay()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var product = await LabelPersistenceTests.Product(f); var frontend = Path.Combine(Path.GetDirectoryName(f.DatabasePath), "frontend"); Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), "{\"screens\":[{\"route\":\"products-list\",\"title\":\"المنتجات\",\"module\":\"المخازن\"},{\"route\":\"settings-printers\",\"title\":\"الطباعة\",\"module\":\"الإعدادات\",\"administratorOnly\":true}]}");
                var password = Guid.NewGuid().ToString("N"); var store = new SqliteUserStore(f.ConnectionString); var hasher = new PasswordHasher();
                await store.CreateAsync("label-admin", "مدير", hasher.Hash(password), UserRoles.Administrator, CancellationToken.None);
                var cashierId = await store.CreateAsync("label-cashier", "كاشير", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var backend = new LabelPersistenceTests.Backend();
                using (var server = new AlKhayalServer(new TestLicenseGate()))
                {
                    var url = new Uri("http://127.0.0.1:" + port); server.Start(new HostOptions { DatabasePath = f.DatabasePath, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') }, backend);
                    using (var admin = Client(url)) using (var cashier = Client(url)) using (var anon = Client(url))
                    {
                        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/labels/settings")).StatusCode);
                        var csrf = await Login(admin, "label-admin", password); var cashCsrf = await Login(cashier, "label-cashier", password);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/labels/settings")).StatusCode);
                        var settings = LabelService.Defaults(); settings.Profiles[0].PrinterName = "Labels";
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Put, "api/labels/settings", settings, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/labels/settings", settings, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/labels/preview", settings.Templates[0], csrf)).StatusCode); Assert.Equal(0, backend.Labels);
                        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("api/labels/options")).StatusCode);
                        var barcode = new { productId = product.Id, variantId = product.Variants[0].Id, productUnitId = product.Units[0].Id, version = product.Version, generate = true, format = "code128", operationKey = "http-barcode" };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Post, "api/labels/barcodes", barcode, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/labels/barcodes", barcode, cashCsrf)).StatusCode);
                        var saved = await Send(admin, HttpMethod.Post, "api/labels/barcodes", barcode, csrf); Assert.Equal(HttpStatusCode.OK, saved.StatusCode); var data = JObject.Parse(await saved.Content.ReadAsStringAsync())["data"]; var id = (long)data["id"]; var code = (string)data["code"];
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/labels/barcodes", barcode, csrf)).StatusCode);
                        var lookup = JObject.Parse(await admin.GetStringAsync("api/catalog/barcodes/lookup?code=" + code))["data"]; Assert.Equal(product.Id, (long)lookup["productId"]); Assert.Equal(product.Variants[0].Id, (long)lookup["variantId"]);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/features/modules/barcode-label-printing", new { isEnabled = true }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/labels/options")).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + cashierId + "/screen-permissions", new { screenIds = new[] { "products-list" } }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("api/labels/options")).StatusCode);
                        var request = new { operationKey = "http-label-batch", items = new[] { new { barcodeId = id, quantity = 3, format = "code128" } } };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/labels/jobs", request, null)).StatusCode);
                        using (var cross = new HttpRequestMessage(HttpMethod.Post, "api/labels/jobs")) { cross.Headers.Add("Origin", "https://untrusted.invalid"); cross.Headers.Add("X-CSRF-Token", cashCsrf); cross.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"); Assert.Equal(HttpStatusCode.Forbidden, (await cashier.SendAsync(cross)).StatusCode); }
                        var printed = await Send(cashier, HttpMethod.Post, "api/labels/jobs", request, cashCsrf); Assert.Equal(HttpStatusCode.OK, printed.StatusCode); Assert.Equal("submitted_unknown", (string)JObject.Parse(await printed.Content.ReadAsStringAsync())["data"]["status"]);
                        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, HttpMethod.Post, "api/labels/jobs", request, cashCsrf)).StatusCode); Assert.Equal(1, backend.Labels); Assert.Equal(3, backend.LastBatch.Items[0].Quantity);
                        Assert.Equal(HttpStatusCode.Conflict, (await Send(cashier, HttpMethod.Post, "api/labels/jobs", new { operationKey = "http-label-batch", items = new[] { new { barcodeId = id, quantity = 4, format = "code128" } } }, cashCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/printing/jobs")).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, "api/labels/jobs", new { operationKey = "bad-quantity", items = new[] { new { barcodeId = id, quantity = 0, format = "code128" } } }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/labels/test/labels-default", new { operationKey = "http-label-test" }, csrf)).StatusCode); Assert.Equal(2, backend.Labels);
                    }
                }
            }
        }
    }
}
