using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Authentication;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Server.Host;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class FirstRunBootstrapTests
    {
        [Fact]
        public async Task Fresh_database_bootstraps_admin_logs_in_and_survives_restart()
        {
            var fixture = new Fixture();
            try
            {
                var service = fixture.Auth();
                Assert.True(await service.IsBootstrapRequiredAsync(CancellationToken.None));
                var created = await service.BootstrapAdminAsync(new BootstrapAdminCommand { Username="owner", Password="x", ConfirmPassword="x" }, CancellationToken.None);
                Assert.True(created.Succeeded); Assert.Equal("administrator", created.User.Role);
                Assert.False(await service.IsBootstrapRequiredAsync(CancellationToken.None));
                var restarted = fixture.Auth();
                var login = await restarted.LoginAsync(new LoginCommand { Username="owner", Password="x" }, CancellationToken.None);
                Assert.True(login.Succeeded); Assert.Equal("administrator", login.User.Role);
                using (var c = new SQLiteConnection(fixture.ConnectionString)) { c.Open(); Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM Users")); var hash=c.ExecuteScalar<string>("SELECT PasswordHash FROM Users"); Assert.NotEqual("x",hash); Assert.StartsWith("pbkdf2-sha256$",hash); }
            }
            finally { fixture.Dispose(); }
        }

        [Theory]
        [InlineData("administrator", true)]
        [InlineData("cashier", true)]
        [InlineData("cashier", false)]
        public async Task Any_existing_user_blocks_bootstrap(string role, bool active)
        {
            var fixture = new Fixture();
            try
            {
                await new SqliteUserStore(fixture.ConnectionString).CreateAsync("existing", "مستخدم", new PasswordHasher().Hash("p"), role, CancellationToken.None);
                if (!active) using (var c = new SQLiteConnection(fixture.ConnectionString)) { c.Open(); c.Execute("UPDATE Users SET IsActive=0"); }
                Assert.False(await fixture.Auth().IsBootstrapRequiredAsync(CancellationToken.None));
                var rejected = await fixture.Auth().BootstrapAdminAsync(new BootstrapAdminCommand { Username="second", Password="p", ConfirmPassword="p" }, CancellationToken.None);
                Assert.Equal("bootstrap_unavailable", rejected.ErrorCode);
                using (var c = new SQLiteConnection(fixture.ConnectionString)) { c.Open(); Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM Users")); }
            }
            finally { fixture.Dispose(); }
        }

        [Fact]
        public async Task Concurrent_bootstrap_creates_exactly_one_admin()
        {
            var fixture = new Fixture();
            try
            {
                var gate = new ManualResetEventSlim(false);
                Func<string, Task<bool>> attempt = name => Task.Run(async () => { gate.Wait(); return await new SqliteUserStore(fixture.ConnectionString).TryCreateFirstAdminAsync(name, name, new PasswordHasher().Hash("p"), CancellationToken.None); });
                var first = attempt("first"); var second = attempt("second"); gate.Set();
                var results = await Task.WhenAll(first, second);
                Assert.Equal(1, results.Count(x => x));
                using (var c = new SQLiteConnection(fixture.ConnectionString)) { c.Open(); Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM Users")); Assert.Equal("administrator", c.ExecuteScalar<string>("SELECT Role FROM Users")); }
            }
            finally { fixture.Dispose(); }
        }

        [Theory]
        [InlineData("x")]
        [InlineData("7")]
        [InlineData("!")]
        public async Task Simple_non_empty_passwords_are_accepted(string password)
        {
            var fixture = new Fixture();
            try { Assert.True((await fixture.Auth().BootstrapAdminAsync(new BootstrapAdminCommand { Username="admin", Password=password, ConfirmPassword=password }, CancellationToken.None)).Succeeded); }
            finally { fixture.Dispose(); }
        }

        [Fact]
        public async Task Invalid_or_mismatched_password_leaves_no_partial_user()
        {
            var fixture = new Fixture();
            try
            {
                Assert.Equal("validation", (await fixture.Auth().BootstrapAdminAsync(new BootstrapAdminCommand { Username="admin", Password="", ConfirmPassword="" }, CancellationToken.None)).ErrorCode);
                Assert.Equal("password_mismatch", (await fixture.Auth().BootstrapAdminAsync(new BootstrapAdminCommand { Username="admin", Password="a", ConfirmPassword="b" }, CancellationToken.None)).ErrorCode);
                using (var c = new SQLiteConnection(fixture.ConnectionString)) { c.Open(); Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM Users")); }
            }
            finally { fixture.Dispose(); }
        }

        [Fact]
        public async Task Bootstrap_api_is_public_once_then_permanently_conflicts()
        {
            var fixture = new Fixture(); var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); var port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var server=new AlKhayalServer(new TestLicenseGate());
            try
            {
                server.Start(new HostOptions { Url="http://127.0.0.1:"+port, DatabasePath=fixture.Path, FrontendPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"missing-frontend") });
                using (var client = new HttpClient(new HttpClientHandler { UseCookies=true }) { BaseAddress=new Uri("http://127.0.0.1:"+port) })
                {
                    Assert.Contains("\"required\":true", await client.GetStringAsync("api/auth/bootstrap-status"));
                    var created=await client.PostAsync("api/auth/bootstrap",Json("{\"username\":\"api-admin\",\"password\":\"1\",\"confirmPassword\":\"1\"}")); Assert.Equal(HttpStatusCode.Created,created.StatusCode);
                    Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("api/auth/me")).StatusCode);
                    var blocked=await client.PostAsync("api/auth/bootstrap",Json("{\"username\":\"other\",\"password\":\"1\",\"confirmPassword\":\"1\"}")); Assert.Equal(HttpStatusCode.Conflict,blocked.StatusCode);
                }
            }
            finally { server.Dispose(); fixture.Dispose(); }
        }

        private static StringContent Json(string value) { return new StringContent(value, Encoding.UTF8, "application/json"); }
        private sealed class Fixture : IDisposable
        {
            public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid()+".db");
            public string ConnectionString { get { return Db.ConnectionStringFor(Path); } }
            public Fixture() { Migrator.Run(ConnectionString); }
            public AuthService Auth() { return new AuthService(new SqliteUserStore(ConnectionString), new SqliteSessionStore(ConnectionString), new PasswordHasher(), new SecureTokenGenerator(), new SystemClock()); }
            public void Dispose() { SQLiteConnection.ClearAllPools(); if (File.Exists(Path)) File.Delete(Path); }
        }
    }
}
