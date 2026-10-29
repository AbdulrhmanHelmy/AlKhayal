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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Printing_http_auth_admin_csrf_origin_permissions_shift_owner_fake_backend_and_no_webview()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var frontend = Path.Combine(Path.GetDirectoryName(f.DatabasePath), "frontend"); Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), "{\"screens\":[{\"route\":\"pos\",\"title\":\"الكاشير\",\"module\":\"المبيعات\"},{\"route\":\"shifts-list\",\"title\":\"الورديات\",\"module\":\"الموظفين\",\"featureKey\":\"advanced-shifts\"},{\"route\":\"settings-printers\",\"title\":\"الطباعة\",\"module\":\"الإعدادات\",\"administratorOnly\":true}]}");
                var password = Guid.NewGuid().ToString("N"); var users = new SqliteUserStore(f.ConnectionString); var hasher = new PasswordHasher();
                await users.CreateAsync("print-admin", "مدير", hasher.Hash(password), UserRoles.Administrator, CancellationToken.None);
                var cashierId = await users.CreateAsync("print-cashier", "كاشير", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var otherId = await users.CreateAsync("print-other", "آخر", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                var backend = new PrintingPersistenceTests.RecordingBackend();
                using (var server = new AlKhayalServer())
                {
                    var url = new Uri("http://127.0.0.1:" + port); server.Start(new HostOptions { DatabasePath = f.DatabasePath, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') }, backend);
                    using (var admin = Client(url)) using (var cashier = Client(url)) using (var other = Client(url)) using (var anon = Client(url))
                    {
                        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/printing/settings")).StatusCode);
                        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anon, HttpMethod.Post, "api/printing/jobs", new { documentType = "sale", documentId = 1, operationKey = "anon" }, null)).StatusCode);
                        var csrf = await Login(admin, "print-admin", password); var cashCsrf = await Login(cashier, "print-cashier", password); var otherCsrf = await Login(other, "print-other", password);
                        foreach (var path in new[] { "settings", "defaults", "printers", "jobs" }) Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/printing/" + path)).StatusCode);
                        var config = PrintingService.Defaults(); foreach (var p in config.Profiles) p.PrinterName = "Recording"; config.AutoPrintSale = true;
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Put, "api/printing/settings", config, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/printing/settings", config, csrf)).StatusCode);
                        Assert.Contains("Recording", await admin.GetStringAsync("api/printing/printers"));
                        Assert.Equal(1, (long)JObject.Parse(await admin.GetStringAsync("api/printing/settings"))["data"]["version"]);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/printing/preview", config.Templates[0], csrf)).StatusCode); Assert.Equal(0, backend.Submissions);
                        using (var req = new HttpRequestMessage(HttpMethod.Post, "api/printing/test/sale"))
                        {
                            req.Headers.Add("Origin", "https://untrusted.invalid"); req.Headers.Add("X-CSRF-Token", csrf); req.Content = new StringContent("{\"operationKey\":\"bad-origin\"}", Encoding.UTF8, "application/json");
                            Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(req)).StatusCode);
                        }
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/printing/jobs", new { documentType = "sale", documentId = 1, operationKey = "no-grant" }, cashCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/features/modules/advanced-shifts", new { isEnabled = true }, csrf)).StatusCode);
                        foreach (var id in new[] { cashierId, otherId }) Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + id + "/screen-permissions", new { screenIds = new[] { "pos" } }, csrf)).StatusCode);
                        var shiftResponse = await Send(cashier, HttpMethod.Post, "api/workforce/shifts/open", new { amount = "0", operationKey = "print-api-open" }, cashCsrf); Assert.Equal(HttpStatusCode.OK, shiftResponse.StatusCode);
                        var shiftId = (long)JObject.Parse(await shiftResponse.Content.ReadAsStringAsync())["data"]["id"];
                        var command = new { documentType = "shift-summary", documentId = shiftId, operationKey = "shift-print", reprint = false };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/printing/jobs", command, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.NotFound, (await Send(other, HttpMethod.Post, "api/printing/jobs", command, otherCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, HttpMethod.Post, "api/printing/jobs", command, cashCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, HttpMethod.Post, "api/printing/jobs", command, cashCsrf)).StatusCode); Assert.Equal(1, backend.Submissions);
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, "api/printing/jobs", new { documentType = "executable", documentId = 1, operationKey = "invalid" }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/printing/test/sale", new { operationKey = "test-print" }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/printing/test/sale", new { operationKey = "test-print" }, csrf)).StatusCode); Assert.Equal(2, backend.Submissions);
                        Assert.Equal(2, ((JArray)JObject.Parse(await admin.GetStringAsync("api/printing/jobs"))["data"]).Count);
                    }
                }
            }
        }
    }
}
