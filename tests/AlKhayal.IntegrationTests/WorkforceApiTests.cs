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
        public async Task Workforce_http_module_flags_admin_payroll_privacy_csrf_cashier_ownership_and_replay()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var frontend = Path.Combine(Path.GetDirectoryName(f.DatabasePath), "frontend"); Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), "{\"screens\":[{\"route\":\"pos\",\"title\":\"الكاشير\",\"module\":\"المبيعات\"},{\"route\":\"shifts-list\",\"title\":\"الورديات\",\"module\":\"الموظفين\",\"featureKey\":\"advanced-shifts\"},{\"route\":\"employees-list\",\"title\":\"الموظفين\",\"module\":\"الموظفين\",\"administratorOnly\":true,\"featureKey\":\"employee-management\"}]}");
                var password = Guid.NewGuid().ToString("N"); var users = new SqliteUserStore(f.ConnectionString); var hasher = new PasswordHasher();
                await users.CreateAsync("workforce-owner", "مدير", hasher.Hash(password), UserRoles.Administrator, CancellationToken.None);
                var cashierId = await users.CreateAsync("workforce-cashier", "كاشير", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var otherId = await users.CreateAsync("workforce-other", "كاشير آخر", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                using (var server = new AlKhayalServer(new TestLicenseGate()))
                {
                    var url = new Uri("http://127.0.0.1:" + port); server.Start(new HostOptions { DatabasePath = f.DatabasePath, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') });
                    using (var admin = Client(url)) using (var cashier = Client(url)) using (var other = Client(url)) using (var anon = Client(url))
                    {
                        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/workforce/employees")).StatusCode);
                        var csrf = await Login(admin, "workforce-owner", password); var cashierCsrf = await Login(cashier, "workforce-cashier", password); var otherCsrf = await Login(other, "workforce-other", password);
                        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("api/workforce/employees")).StatusCode);
                        foreach (var feature in new[] { "employee-management", "employee-attendance", "advanced-shifts" }) Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/features/modules/" + feature, new { isEnabled = true }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Post, "api/workforce/employees/save", new { name = "موظف", operationKey = "employee-http" }, null)).StatusCode);
                        var result = await Send(admin, HttpMethod.Post, "api/workforce/employees/save", new { name = "موظف", operationKey = "employee-http" }, csrf); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
                        var employee = (long)JObject.Parse(await result.Content.ReadAsStringAsync())["data"]["id"];
                        var saved = JObject.Parse(await admin.GetStringAsync("api/workforce/employees/" + employee))["data"]; Assert.Equal("موظف", (string)saved["name"]); Assert.Null(saved["salary"]); Assert.Null(saved["payday"]);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/workforce/employees/" + employee)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/workforce/attendance")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/workforce/attendance/1/audit")).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/workforce/employees/save", new { id = employee, version = 1, name = "موظف", attendanceEnabled = true, operationKey = "enable-attendance" }, csrf)).StatusCode);
                        var checkIn = await Send(admin, HttpMethod.Post, "api/workforce/attendance/in", new { employeeId = employee, operationKey = "attendance-in-http" }, csrf); Assert.Equal(HttpStatusCode.OK, checkIn.StatusCode);
                        var attendanceId = (long)JObject.Parse(await checkIn.Content.ReadAsStringAsync())["data"]["id"];
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Put, "api/features/modules/employee-attendance", new { isEnabled = false }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/workforce/attendance/out", new { id = attendanceId, operationKey = "attendance-out-http" }, csrf)).StatusCode);
                        Assert.Equal(2, ((JArray)JObject.Parse(await admin.GetStringAsync("api/workforce/attendance/" + attendanceId + "/audit"))["data"]).Count);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/workforce/shifts")).StatusCode);
                        foreach (var id in new[] { cashierId, otherId }) Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + id + "/screen-permissions", new { screenIds = new[] { "pos" } }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/workforce/shifts/open", new { amount = "5", operationKey = "open-http" }, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/workforce/shifts/open", new { amount = "5", cashierId = otherId, operationKey = "forged-owner" }, cashierCsrf)).StatusCode);
                        var command = new { amount = "5.10", operationKey = "open-http" };
                        result = await Send(cashier, HttpMethod.Post, "api/workforce/shifts/open", command, cashierCsrf); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
                        var shiftId = (long)JObject.Parse(await result.Content.ReadAsStringAsync())["data"]["id"];
                        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, HttpMethod.Post, "api/workforce/shifts/open", command, cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Conflict, (await Send(cashier, HttpMethod.Post, "api/workforce/shifts/open", new { amount = "0", operationKey = "duplicate" }, cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("api/workforce/shifts/" + shiftId)).StatusCode);
                        Assert.Empty(JObject.Parse(await other.GetStringAsync("api/workforce/shifts"))["data"]["items"]);
                        Assert.Equal(HttpStatusCode.NotFound, (await Send(other, HttpMethod.Post, "api/workforce/shifts/close", new { id = shiftId, amount = "0", operationKey = "other-close" }, otherCsrf)).StatusCode);
                        Assert.Equal("5.10", (string)JObject.Parse(await cashier.GetStringAsync("api/workforce/shifts/current"))["data"]["expected"]);
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(cashier, HttpMethod.Post, "api/workforce/shifts/adjust", new { id = shiftId, amount = "1", direction = "in", operationKey = "no-reason" }, cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/workforce/shifts/close", new { id = shiftId, amount = "5", operationKey = "admin-close" }, csrf)).StatusCode);
                        Assert.Equal("-0.10", (string)JObject.Parse(await cashier.GetStringAsync("api/workforce/shifts/" + shiftId))["data"]["difference"]);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/workforce/shifts/open", new { cashierId = otherId, amount = "0", operationKey = "admin-open-other" }, csrf)).StatusCode);
                        Assert.Equal(otherId, (long)JObject.Parse(await other.GetStringAsync("api/workforce/shifts/current"))["data"]["cashierId"]);
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Put, "api/features/modules/advanced-shifts", new { isEnabled = false }, csrf)).StatusCode);
                        var current = (long)JObject.Parse(await other.GetStringAsync("api/workforce/shifts/current"))["data"]["id"];
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/workforce/shifts/close", new { id = current, amount = "0", operationKey = "close-before-disable" }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/features/modules/advanced-shifts", new { isEnabled = false }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/workforce/shifts")).StatusCode);
                    }
                }
            }
        }
    }
}
