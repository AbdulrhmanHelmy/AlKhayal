using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Server.Host;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class BusinessActivityApiTests
    {
        [Fact]
        public async Task Api_enforces_auth_admin_validation_and_persists_after_restart()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            AlKhayalServer server = null;
            try
            {
                var cs = Db.ConnectionStringFor(path); Migrator.Run(cs);
                var users = new SqliteUserStore(cs); var hasher = new PasswordHasher();
                await users.CreateAsync("activity-api-admin", "مسؤول API", hasher.Hash("secret"), "administrator", CancellationToken.None);
                await users.CreateAsync("activity-api-cashier", "كاشير API", hasher.Hash("secret"), "operator", CancellationToken.None);
                var url = "http://127.0.0.1:" + FreePort();
                server = Start(path, url);

                using (var anonymous = Client(url))
                    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/business-activities")).StatusCode);

                using (var cashier = Client(url))
                {
                    var csrf = await Login(cashier, "activity-api-cashier");
                    Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("api/business-activities")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await Put(cashier, csrf, "{\"activityKeys\":[\"clothing\"],\"primaryActivityKey\":\"clothing\"}")).StatusCode);
                }

                using (var admin = Client(url))
                {
                    var csrf = await Login(admin, "activity-api-admin");
                    Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync("api/business-activities/selected", new StringContent("{\"activityKeys\":[\"clothing\"],\"primaryActivityKey\":\"clothing\"}", Encoding.UTF8, "application/json"))).StatusCode);
                    Assert.Equal(HttpStatusCode.BadRequest, (await Put(admin, csrf, "{\"activityKeys\":[\"invalid\"],\"primaryActivityKey\":\"invalid\"}")).StatusCode);
                    var saved = await Put(admin, csrf, "{\"activityKeys\":[\"clothing\",\"footwear\"],\"primaryActivityKey\":\"clothing\",\"applyRecommendations\":true}");
                    Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
                    var preview = await admin.GetStringAsync("api/business-activities/recommendations");
                    Assert.Contains("product-variants", preview); Assert.Contains("clothing", preview); Assert.Contains("footwear", preview);
                }

                server.Dispose(); server = null;
                url = "http://127.0.0.1:" + FreePort(); server = Start(path, url);
                using (var restarted = Client(url))
                {
                    await Login(restarted, "activity-api-admin");
                    var selected = await restarted.GetStringAsync("api/business-activities/selected");
                    Assert.Contains("\"isConfigured\":true", selected); Assert.Contains("\"primaryActivityKey\":\"clothing\"", selected);
                }
            }
            finally { if (server != null) server.Dispose(); System.Data.SQLite.SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }

        private static AlKhayalServer Start(string path, string url) { var server = new AlKhayalServer(); server.Start(new HostOptions { DatabasePath = path, Url = url, FrontendPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "missing-frontend") }); return server; }
        private static HttpClient Client(string url) { return new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(url) }; }
        private static async Task<string> Login(HttpClient client, string username)
        {
            var response = await client.PostAsync("api/auth/login", new StringContent("{\"username\":\"" + username + "\",\"password\":\"secret\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Regex.Match(await response.Content.ReadAsStringAsync(), "\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value;
        }
        private static Task<HttpResponseMessage> Put(HttpClient client, string csrf, string json) { var request = new HttpRequestMessage(HttpMethod.Put, "api/business-activities/selected") { Content = new StringContent(json, Encoding.UTF8, "application/json") }; request.Headers.Add("X-CSRF-Token", csrf); return client.SendAsync(request); }
        private static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    }
}
