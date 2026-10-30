using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
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
    // Phase 1 QA batch 1 additions: QA-004 (8-hour session expiry boundary) and
    // QA-005 (session cookie flags and secret-free login/me responses).
    // Both tests run a real OWIN host against a disposable GUID SQLite database
    // addressed through an explicit HostOptions.DatabasePath; the operator
    // database at %LOCALAPPDATA%\AlKhayal\alkhayal.db is never opened.
    // The HTTP client disables automatic cookie handling so the raw Set-Cookie
    // header is observable and the identical cookie value is replayed explicitly.
    // Expiry is exercised without any sleeps: the isolated Sessions.ExpiresUtc is
    // moved to an expired UTC value directly in the disposable database.
    public sealed class QaPhase1SessionSecurityTests
    {
        private const string CookieName = "alkhayal_session";

        [Fact]
        public async Task QA004_expired_session_is_rejected_removed_and_not_restored_by_restart()
        {
            var root = Path.Combine(Path.GetTempPath(), "alkhayal-qa4-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(root, "test.db");
            Assert.NotEqual(Db.DefaultDatabasePath(), path);
            AlKhayalServer server = null;
            try
            {
                Directory.CreateDirectory(root);
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var users = new SqliteUserStore(cs);
                await users.CreateAsync("qa-admin", "مسؤول الجودة", new PasswordHasher().Hash("qa-secret"), "administrator", CancellationToken.None);

                var options = new HostOptions { DatabasePath = path, Url = "http://127.0.0.1:" + FreePort(), FrontendPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "missing-frontend") };
                server = new AlKhayalServer(new TestLicenseGate());
                server.Start(options);

                string token;
                using (var client = Client(options.Url))
                {
                    // Authenticated access works before expiry.
                    var login = await client.PostAsync("api/auth/login", Json("{\"username\":\"qa-admin\",\"password\":\"qa-secret\"}"));
                    Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                    token = ExtractToken(ReadSetCookieHeader(login));

                    Assert.Equal(HttpStatusCode.OK, (await GetWithCookieAsync(client, token, "api/auth/me")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await GetWithCookieAsync(client, token, "api/users")).StatusCode);

                    // The freshly created session expires at the 8-hour boundary (no sleeps).
                    var checkedAt = DateTime.UtcNow;
                    var expiresUtc = ReadExpiresUtc(cs, token);
                    Assert.InRange(expiresUtc, checkedAt.AddHours(8).AddMinutes(-2), checkedAt.AddHours(8).AddMinutes(2));
                    Assert.Equal(1, CountSessions(cs, token));

                    // Isolate the session in the disposable database at an expired UTC value.
                    using (var c = new SQLiteConnection(cs)) { c.Open(); using (var cmd = c.CreateCommand()) { cmd.CommandText = "UPDATE Sessions SET ExpiresUtc=@ExpiresUtc WHERE Token=@Token"; cmd.Parameters.AddWithValue("@ExpiresUtc", DateTime.UtcNow.AddMinutes(-1).ToString("o")); cmd.Parameters.AddWithValue("@Token", token); Assert.Equal(1, cmd.ExecuteNonQuery()); } }

                    // The same cookie is rejected everywhere after expiry.
                    Assert.Equal(HttpStatusCode.Unauthorized, (await GetWithCookieAsync(client, token, "api/auth/me")).StatusCode);
                    Assert.Equal(HttpStatusCode.Unauthorized, (await GetWithCookieAsync(client, token, "api/users")).StatusCode);

                    // The expired session row is removed, not left behind.
                    Assert.Equal(0, CountSessions(cs, token));
                }

                // A host restart on the same database does not restore the expired session.
                server.Dispose();
                var restarted = new HostOptions { DatabasePath = path, Url = "http://127.0.0.1:" + FreePort(), FrontendPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "missing-frontend") };
                server = new AlKhayalServer(new TestLicenseGate());
                server.Start(restarted);
                using (var client = Client(restarted.Url))
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, (await GetWithCookieAsync(client, token, "api/auth/me")).StatusCode);
                    Assert.Equal(0, CountSessions(cs, token));
                }
            }
            finally { if (server != null) server.Dispose(); SQLiteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public async Task QA004_csrf_token_is_invalid_for_an_expired_session()
        {
            var root = Path.Combine(Path.GetTempPath(), "alkhayal-qa4b-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(root, "test.db");
            Assert.NotEqual(Db.DefaultDatabasePath(), path);
            AlKhayalServer server = null;
            try
            {
                Directory.CreateDirectory(root);
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var users = new SqliteUserStore(cs);
                await users.CreateAsync("qa-admin", "مسؤول الجودة", new PasswordHasher().Hash("qa-secret"), "administrator", CancellationToken.None);

                var options = new HostOptions { DatabasePath = path, Url = "http://127.0.0.1:" + FreePort(), FrontendPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "missing-frontend") };
                server = new AlKhayalServer(new TestLicenseGate());
                server.Start(options);
                using (var client = Client(options.Url))
                {
                    var login = await client.PostAsync("api/auth/login", Json("{\"username\":\"qa-admin\",\"password\":\"qa-secret\"}"));
                    Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                    var token = ExtractToken(ReadSetCookieHeader(login));
                    var csrf = Regex.Match(await login.Content.ReadAsStringAsync(), "\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value;
                    Assert.False(string.IsNullOrWhiteSpace(csrf));

                    // Isolate the session at an expired UTC value; no request has consumed it yet.
                    using (var c = new SQLiteConnection(cs)) { c.Open(); using (var cmd = c.CreateCommand()) { cmd.CommandText = "UPDATE Sessions SET ExpiresUtc=@ExpiresUtc WHERE Token=@Token"; cmd.Parameters.AddWithValue("@ExpiresUtc", DateTime.UtcNow.AddMinutes(-1).ToString("o")); cmd.Parameters.AddWithValue("@Token", token); Assert.Equal(1, cmd.ExecuteNonQuery()); } }

                    // The previously valid CSRF token must no longer authorize a state-changing call.
                    var logout = await PostLogoutWithCsrfAsync(client, token, csrf);
                    Assert.Equal(HttpStatusCode.Forbidden, logout.StatusCode);
                    Assert.Contains("invalid_csrf", await logout.Content.ReadAsStringAsync());
                }
            }
            finally { if (server != null) server.Dispose(); SQLiteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public async Task QA005_session_cookie_sets_HttpOnly_and_SameSite_and_login_me_responses_leak_no_secrets()
        {
            var root = Path.Combine(Path.GetTempPath(), "alkhayal-qa5-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(root, "test.db");
            Assert.NotEqual(Db.DefaultDatabasePath(), path);
            AlKhayalServer server = null;
            try
            {
                Directory.CreateDirectory(root);
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var users = new SqliteUserStore(cs);
                await users.CreateAsync("qa-admin", "مسؤول الجودة", new PasswordHasher().Hash("qa-secret"), "administrator", CancellationToken.None);

                var options = new HostOptions { DatabasePath = path, Url = "http://127.0.0.1:" + FreePort(), FrontendPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "missing-frontend") };
                server = new AlKhayalServer(new TestLicenseGate());
                server.Start(options);
                using (var client = Client(options.Url))
                {
                    var login = await client.PostAsync("api/auth/login", Json("{\"username\":\"qa-admin\",\"password\":\"qa-secret\"}"));
                    Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                    var body = await login.Content.ReadAsStringAsync();

                    // The login response body carries no session token or password material.
                    Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("sessionToken", body, StringComparison.OrdinalIgnoreCase);

                    // The session cookie is HttpOnly and SameSite=Lax and carries the token only in the header.
                    var cookieHeader = ReadSetCookieHeader(login);
                    Assert.StartsWith(CookieName + "=", cookieHeader);
                    Assert.Contains("HttpOnly", cookieHeader);
                    Assert.Contains("SameSite=Lax", cookieHeader);
                    Assert.Contains("Path=/", cookieHeader);
                    var token = ExtractToken(cookieHeader);
                    Assert.DoesNotContain(token, body, StringComparison.Ordinal);

                    // The me response exposes identity and CSRF only, never the session token or hash.
                    var me = await GetWithCookieAsync(client, token, "api/auth/me");
                    Assert.Equal(HttpStatusCode.OK, me.StatusCode);
                    var meBody = await me.Content.ReadAsStringAsync();
                    Assert.Contains("qa-admin", meBody);
                    Assert.Contains("csrfToken", meBody);
                    Assert.DoesNotContain(token, meBody, StringComparison.Ordinal);
                    Assert.DoesNotContain("passwordHash", meBody, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("sessionToken", meBody, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally { if (server != null) server.Dispose(); SQLiteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static string ReadSetCookieHeader(HttpResponseMessage response)
        {
            IEnumerable<string> values;
            Assert.True(response.Headers.TryGetValues("Set-Cookie", out values), "the login response must set the session cookie");
            var header = values.FirstOrDefault(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal));
            Assert.NotNull(header);
            return header;
        }

        private static string ExtractToken(string cookieHeader)
        {
            var pair = cookieHeader.Substring(CookieName.Length + 1);
            var separator = pair.IndexOf(';');
            var token = separator < 0 ? pair : pair.Substring(0, separator);
            Assert.False(string.IsNullOrWhiteSpace(token));
            return token;
        }

        private static HttpClient Client(string url) { return new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(url) }; }
        private static StringContent Json(string json) { return new StringContent(json, Encoding.UTF8, "application/json"); }
        private static async Task<HttpResponseMessage> GetWithCookieAsync(HttpClient client, string token, string requestUri) { var request = new HttpRequestMessage(HttpMethod.Get, requestUri); request.Headers.TryAddWithoutValidation("Cookie", CookieName + "=" + token); return await client.SendAsync(request); }
        private static Task<HttpResponseMessage> PostLogoutWithCsrfAsync(HttpClient client, string token, string csrf) { var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout") { Content = Json("{}") }; request.Headers.TryAddWithoutValidation("Cookie", CookieName + "=" + token); request.Headers.Add("X-CSRF-Token", csrf); return client.SendAsync(request); }
        private static DateTime ReadExpiresUtc(string cs, string token) { using (var c = new SQLiteConnection(cs)) { c.Open(); using (var cmd = c.CreateCommand()) { cmd.CommandText = "SELECT ExpiresUtc FROM Sessions WHERE Token=@Token"; cmd.Parameters.AddWithValue("@Token", token); var value = cmd.ExecuteScalar() as string; Assert.NotNull(value); return DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind); } } }
        private static int CountSessions(string cs, string token) { using (var c = new SQLiteConnection(cs)) { c.Open(); using (var cmd = c.CreateCommand()) { cmd.CommandText = "SELECT COUNT(*) FROM Sessions WHERE Token=@Token"; cmd.Parameters.AddWithValue("@Token", token); return Convert.ToInt32(cmd.ExecuteScalar()); } } }
        private static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    }
}
