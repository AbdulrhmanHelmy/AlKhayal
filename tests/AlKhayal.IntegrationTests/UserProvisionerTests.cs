using System;
using System.Data.SQLite;
using System.IO;
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
    public sealed class UserProvisionerTests
    {
        [Fact]
        public async Task Provisioner_accepts_a_single_character_password_and_stores_only_its_hash()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            const string singleCharacterPassword = "x";
            try
            {
                var options = new HostOptions { DatabasePath = path };
                UserProvisioner.Provision(options, "short-password-user", singleCharacterPassword);

                var connectionString = Db.ConnectionStringFor(path);
                string storedHash;
                using (var connection = new SQLiteConnection(connectionString))
                {
                    storedHash = await connection.QuerySingleAsync<string>(
                        "SELECT PasswordHash FROM Users WHERE Username=@Username",
                        new { Username = "short-password-user" });
                }

                Assert.NotEqual(singleCharacterPassword, storedHash);
                Assert.StartsWith("pbkdf2-sha256$", storedHash);
                Assert.True(new PasswordHasher().Verify(singleCharacterPassword, storedHash));

                var users = new SqliteUserStore(connectionString);
                var auth = new AuthService(users, new SqliteSessionStore(connectionString), new PasswordHasher(), new SecureTokenGenerator(), new SystemClock());
                var login = await auth.LoginAsync(
                    new LoginCommand { Username = "short-password-user", Password = singleCharacterPassword },
                    CancellationToken.None);
                Assert.True(login.Succeeded);
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task Password_reset_replaces_the_hash_for_an_existing_user()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            const string originalPassword = "old";
            const string replacementPassword = "x";
            try
            {
                var options = new HostOptions { DatabasePath = path };
                UserProvisioner.Provision(options, "existing-user", originalPassword);
                UserProvisioner.ResetPassword(options, "existing-user", replacementPassword);

                var connectionString = Db.ConnectionStringFor(path);
                var users = new SqliteUserStore(connectionString);
                var record = await users.FindByUsernameAsync("existing-user", CancellationToken.None);
                Assert.NotNull(record);
                Assert.NotEqual(replacementPassword, record.PasswordHash);
                Assert.StartsWith("pbkdf2-sha256$", record.PasswordHash);

                var auth = new AuthService(users, new SqliteSessionStore(connectionString), new PasswordHasher(), new SecureTokenGenerator(), new SystemClock());
                var oldLogin = await auth.LoginAsync(
                    new LoginCommand { Username = "existing-user", Password = originalPassword },
                    CancellationToken.None);
                var newLogin = await auth.LoginAsync(
                    new LoginCommand { Username = "existing-user", Password = replacementPassword },
                    CancellationToken.None);

                Assert.False(oldLogin.Succeeded);
                Assert.True(newLogin.Succeeded);
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
