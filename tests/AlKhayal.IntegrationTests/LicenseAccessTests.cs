using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;
using AlKhayal.Server.Host;
using Xunit;
namespace AlKhayal.IntegrationTests
{
    public sealed class LicenseAccessTests
    {
        [Fact]
        public async Task Signed_activation_allows_login_and_live_file_removal_blocks_existing_session_every_route_and_static_files()
        {
            var root = Path.Combine(Path.GetTempPath(), "alk-license-http-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var database = Path.Combine(root, "test.db"); var file = Path.Combine(root, "license.alklic");
            using (var rsa = RsaLicenseSignatures.CreateRsa())
            {
                var hardware = new FakeHardware(); var storage = new FileLicenseStorage(file);
                var service = new LicenseService(storage, new RsaLicenseSignatures(new Dictionary<string,string>{{"test-key",rsa.ToXmlString(false)}}), hardware);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                var options = new HostOptions { Url = "http://127.0.0.1:" + port, DatabasePath = database, FrontendPath = root };
                File.WriteAllText(Path.Combine(root,"login.html"), "license static smoke");
                using (var server = new AlKhayalServer(service))
                {
                    server.Start(options);
                    using(var client = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(options.Url) })
                    {
                        await AssertLocked(client);
                        var payload = LicenseFormat.CreatePayload(hardware.ReadClaims(), "test-key", Guid.NewGuid(), DateTime.UtcNow);
                        var license = payload + "|" + Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(payload), "SHA256"));
                        Assert.True(service.Activate(license).IsValid);
                        var bootstrap = await client.PostAsync("api/auth/bootstrap", Json("{\"username\":\"license-admin\",\"password\":\"isolated-only\",\"confirmPassword\":\"isolated-only\"}"));
                        Assert.True(bootstrap.IsSuccessStatusCode, await bootstrap.Content.ReadAsStringAsync());
                        var login = await client.PostAsync("api/auth/login", Json("{\"username\":\"license-admin\",\"password\":\"isolated-only\"}"));
                        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("api/auth/me")).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("login.html")).StatusCode);
                        File.Delete(file); await AssertLocked(client);
                        File.WriteAllText(file,"corrupt"); await AssertLocked(client);
                        Assert.True(service.Activate(license).IsValid);
                        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("api/auth/me")).StatusCode);
                    }
                }
            }
            System.Data.SQLite.SQLiteConnection.ClearAllPools(); Directory.Delete(root,true);
        }
        [Fact]
        public async Task Direct_WebApi_configuration_fails_closed_without_OWIN()
        {
            var gate = new TestLicenseGate { IsValid = false };
            using(var config = AlKhayal.Api.ApiBootstrap.CreateConfiguration(t => null, gate))
            using(var server = new System.Web.Http.HttpServer(config))
            using(var client = new HttpClient(server))
            {
                var result = await client.PostAsync("http://localhost/api/auth/login",Json("{}"));
                Assert.Equal(HttpStatusCode.Forbidden,result.StatusCode);
                Assert.Contains("activation_required",await result.Content.ReadAsStringAsync());
            }
        }
        private static StringContent Json(string value) => new StringContent(value,System.Text.Encoding.UTF8,"application/json");
        private static async Task AssertLocked(HttpClient client)
        {
            foreach(var route in new[]{"api/auth/me","api/auth/bootstrap-status","api/system/health","api/sales","api/inventory","api/reports/sales","api/settings/company","login.html","index.html","/","api/not-registered"})
            {
                var response = await client.GetAsync(route); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
                Assert.Contains("activation_required",await response.Content.ReadAsStringAsync());
            }
            foreach(var route in new[]{"api/auth/login","api/auth/bootstrap","api/sales","api/settings/company"})
                Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync(route,Json("{}"))).StatusCode);
        }
        private sealed class FakeHardware : IHardwareIdentity
        {
            public IDictionary<string,string> ReadClaims() => LicenseFormat.HashClaims(new Dictionary<string,string>{{"uuid","http-test-uuid"},{"board","http-test-board"},{"bios","http-test-bios"}});
        }
    }
}
