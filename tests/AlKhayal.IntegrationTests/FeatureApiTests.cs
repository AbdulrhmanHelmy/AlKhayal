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
    public sealed class FeatureApiTests
    {
        [Fact]
        public async Task Feature_api_reads_validates_authorizes_and_persists_after_restart()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db"); AlKhayalServer server = null;
            try
            {
                var cs = Db.ConnectionStringFor(path); Migrator.Run(cs); var users = new SqliteUserStore(cs); var hasher = new PasswordHasher();
                await users.CreateAsync("feature-api-admin", "مسؤول", hasher.Hash("secret"), "administrator", CancellationToken.None);
                await users.CreateAsync("feature-api-cashier", "كاشير", hasher.Hash("secret"), "operator", CancellationToken.None);
                var url = "http://127.0.0.1:" + FreePort(); server = Start(path, url);
                using (var anonymous = Client(url)) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/features")).StatusCode);
                using (var cashier = Client(url)) { var csrf = await Login(cashier, "feature-api-cashier"); Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("api/features/effective")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Put(cashier, csrf, "modules/installments", "{\"isEnabled\":true}")).StatusCode); }
                using (var admin = Client(url))
                {
                    var csrf = await Login(admin, "feature-api-admin");
                    for (var index = 0; index < 20; index++)
                    {
                        using (var response = await admin.GetAsync("api/features/effective")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    }
                    Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync("api/features/modules/installments", Json("{\"isEnabled\":true}"))).StatusCode);
                    Assert.Equal(HttpStatusCode.BadRequest, (await Put(admin, csrf, "modules/installments", "{}")).StatusCode);
                    Assert.Equal(HttpStatusCode.BadRequest, (await Put(admin, csrf, "modules/sales", "{\"isEnabled\":false}")).StatusCode);
                    Assert.Equal(HttpStatusCode.Conflict, (await Put(admin, csrf, "capabilities/imei-tracking", "{\"isEnabled\":true}")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await Put(admin, csrf, "modules/installments", "{\"isEnabled\":true}")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await Put(admin, csrf, "modules/installments", "{\"isEnabled\":false}")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await Put(admin, csrf, "modules/installments", "{\"isEnabled\":true}")).StatusCode);
                    Assert.Equal(HttpStatusCode.Conflict, (await Put(admin, csrf, "capabilities/expiry-dates", "{\"isEnabled\":true}")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await Put(admin, csrf, "capabilities/batch-tracking", "{\"isEnabled\":true}")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await Put(admin, csrf, "capabilities/expiry-dates", "{\"isEnabled\":true}")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await Put(admin, csrf, "capabilities/expiry-dates", "{\"isEnabled\":false}")).StatusCode);
                    var preview = await Post(admin, csrf, "recommendations/preview", "{\"activityKeys\":[\"mobile-phones\"]}"); Assert.Equal(HttpStatusCode.OK, preview.StatusCode); Assert.Contains("maintenance-management", await preview.Content.ReadAsStringAsync());
                    var applied = await Post(admin, csrf, "recommendations/apply", "{\"activityKeys\":[\"grocery\"]}");
                    Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
                    Assert.Matches("\\\"key\\\":\\\"expiry-dates\\\".*?\\\"isEnabled\\\":false.*?\\\"source\\\":\\\"administrator\\\"", await applied.Content.ReadAsStringAsync());
                }
                server.Dispose(); server = null; url = "http://127.0.0.1:" + FreePort(); server = Start(path, url);
                using (var restarted = Client(url)) { await Login(restarted, "feature-api-admin"); var state = await restarted.GetStringAsync("api/features/effective"); Assert.Contains("\"key\":\"installments\",\"kind\":\"module\"", state); Assert.Matches("\\\"key\\\":\\\"installments\\\".*?\\\"isEnabled\\\":true", state); Assert.Matches("\\\"key\\\":\\\"expiry-dates\\\".*?\\\"isEnabled\\\":false.*?\\\"source\\\":\\\"administrator\\\"", state); }
            }
            finally { if (server != null) server.Dispose(); System.Data.SQLite.SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        }

        private static AlKhayalServer Start(string path, string url) { var server = new AlKhayalServer(); server.Start(new HostOptions { DatabasePath = path, Url = url, FrontendPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "missing-frontend") }); return server; }
        private static HttpClient Client(string url) { return new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(url) }; }
        private static StringContent Json(string json) { return new StringContent(json, Encoding.UTF8, "application/json"); }
        private static async Task<string> Login(HttpClient client, string username) { var response = await client.PostAsync("api/auth/login", Json("{\"username\":\"" + username + "\",\"password\":\"secret\"}")); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return Regex.Match(await response.Content.ReadAsStringAsync(), "\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value; }
        private static Task<HttpResponseMessage> Put(HttpClient client, string csrf, string path, string json) { return Send(client, csrf, HttpMethod.Put, path, json); }
        private static Task<HttpResponseMessage> Post(HttpClient client, string csrf, string path, string json) { return Send(client, csrf, HttpMethod.Post, path, json); }
        private static Task<HttpResponseMessage> Send(HttpClient client, string csrf, HttpMethod method, string path, string json) { var request = new HttpRequestMessage(method, "api/features/" + path) { Content = Json(json) }; request.Headers.Add("X-CSRF-Token", csrf); return client.SendAsync(request); }
        private static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    }
}
