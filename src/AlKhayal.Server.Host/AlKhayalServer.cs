using System;
using System.IO;
using System.Text;
using System.Threading;
using AlKhayal.Api;
using AlKhayal.Application.Authentication;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using Microsoft.Owin;
using Microsoft.Owin.Hosting;
using Owin;

[assembly: OwinStartup(typeof(AlKhayal.Server.Host.AlKhayalStartup))]

namespace AlKhayal.Server.Host
{
    public sealed class AlKhayalServer : IDisposable
    {
        private IDisposable _server;

        public HostOptions Options { get; private set; }

        public void Start(HostOptions options)
        {
            Options = options;
            Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath));
            var connectionString = Db.ConnectionStringFor(options.DatabasePath);
            Migrator.Run(connectionString);
            var users = new SqliteUserStore(connectionString);
            var sessions = new SqliteSessionStore(connectionString);
            var auth = new AuthService(users, sessions, new PasswordHasher(), new SecureTokenGenerator(), new SystemClock());
            var config = ApiBootstrap.CreateConfiguration(type => type == typeof(AuthService) ? auth : null);

            _server = WebApp.Start(options.Url, app =>
            {
                app.UseWebApi(config);
                if (!Directory.Exists(options.FrontendPath)) return;

                var fileOptions = new Microsoft.Owin.StaticFiles.FileServerOptions
                {
                    EnableDefaultFiles = true,
                    FileSystem = new Microsoft.Owin.FileSystems.PhysicalFileSystem(options.FrontendPath)
                };
                var contentTypes = new Microsoft.Owin.StaticFiles.ContentTypes.FileExtensionContentTypeProvider();
                contentTypes.Mappings[".json"] = "application/json; charset=utf-8";
                fileOptions.StaticFileOptions.ContentTypeProvider = contentTypes;
                app.UseFileServer(fileOptions);
            });
        }

        public void Dispose()
        {
            if (_server != null) _server.Dispose();
        }
    }

    public sealed class AlKhayalStartup
    {
        public void Configuration(IAppBuilder app) { }
    }

    public static class UserProvisioner
    {
        public static int Provision(HostOptions options, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Username is required.");
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("Password cannot be empty.");

            username = username.Trim();
            var connectionString = Db.ConnectionStringFor(options.DatabasePath);
            Migrator.Run(connectionString);
            var users = new SqliteUserStore(connectionString);
            var existing = users.FindByUsernameAsync(username, CancellationToken.None).GetAwaiter().GetResult();
            if (existing != null) throw new InvalidOperationException("The requested username already exists.");

            var passwordHash = new PasswordHasher().Hash(password);
            users.CreateAsync(username, username, passwordHash, "administrator", CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }

        public static int ResetPassword(HostOptions options, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Username is required.");
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("Password cannot be empty.");

            username = username.Trim();
            var connectionString = Db.ConnectionStringFor(options.DatabasePath);
            Migrator.Run(connectionString);
            var users = new SqliteUserStore(connectionString);
            var existing = users.FindByUsernameAsync(username, CancellationToken.None).GetAwaiter().GetResult();
            if (existing == null) throw new InvalidOperationException("The requested username does not exist.");

            var passwordHash = new PasswordHasher().Hash(password);
            users.UpdatePasswordHashAsync(existing.Id, passwordHash, CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
    }

    internal static class ConsoleSecretReader
    {
        public static string ReadPassword()
        {
            Console.Write("Password: ");
            if (Console.IsInputRedirected)
            {
                var redirected = Console.ReadLine();
                return redirected ?? string.Empty;
            }

            var password = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return password.ToString();
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0) password.Length--;
                    continue;
                }
                if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var options = new HostOptions();
            if (args.Length > 0 &&
                (args[0].Equals("--provision-user", StringComparison.OrdinalIgnoreCase) ||
                 args[0].Equals("--provision-demo", StringComparison.OrdinalIgnoreCase)))
            {
                var username = args.Length > 1 ? args[1] : "demo";
                UserProvisioner.Provision(options, username, ConsoleSecretReader.ReadPassword());
                Console.WriteLine("User provisioned. The password was stored only as a salted PBKDF2 hash.");
                return;
            }
            if (args.Length > 0 && args[0].Equals("--reset-password", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2) throw new InvalidOperationException("Username is required for --reset-password.");
                UserProvisioner.ResetPassword(options, args[1], ConsoleSecretReader.ReadPassword());
                Console.WriteLine("Password reset. Only a new salted PBKDF2 hash was stored.");
                return;
            }

            using (var server = new AlKhayalServer())
            {
                server.Start(options);
                Console.WriteLine("AlKhayal API listening at " + options.Url);
                using (var quit = new ManualResetEvent(false))
                {
                    Console.CancelKeyPress += (sender, eventArgs) =>
                    {
                        eventArgs.Cancel = true;
                        quit.Set();
                    };
                    quit.WaitOne();
                }
            }
        }
    }
}
