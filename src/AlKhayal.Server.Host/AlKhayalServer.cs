
using System;
using System.IO;
using AlKhayal.Api;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.BusinessActivities;
using AlKhayal.Application.Features;
using AlKhayal.Application.Users;
using AlKhayal.Application.Settings;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Customers;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.BusinessActivities;
using AlKhayal.Data.Features;
using AlKhayal.Data.Users;
using AlKhayal.Data.Settings;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Database;
using Microsoft.Owin;
using Microsoft.Owin.Hosting;

namespace AlKhayal.Server.Host
{
    public sealed class AlKhayalServer
    {
        private IDisposable _server;

        public HostOptions Options { get; private set; }

        public void Start(HostOptions options)
        {
            Options = options;

            Directory.CreateDirectory(
                Path.GetDirectoryName(options.DatabasePath));

            var connectionString =
                Db.ConnectionStringFor(options.DatabasePath);

            Migrator.Run(connectionString);

            var users = new SqliteUserStore(connectionString);
            var sessions = new SqliteSessionStore(connectionString);

            var auth = new AuthService(
                users,
                sessions,
                new PasswordHasher(),
                new SecureTokenGenerator(),
                new SystemClock());

            var activityStore =
                new SqliteBusinessActivityStore(connectionString);

            var features = new FeatureService(
                new SqliteFeatureStore(connectionString),
                activityStore);

            var activities = new BusinessActivityService(
                activityStore,
                features);

            var userManagement = new UserManagementService(
                new SqliteUserManagementStore(connectionString),
                new JsonScreenCatalog(options.FrontendPath),
                new PasswordHasher());

            var settingsStore =
                new SqliteSettingsStore(
                    connectionString,
                    options.DatabasePath);

            var settings = new SettingsService(
                settingsStore,
                settingsStore);

            var catalog = new CatalogService(
                new SqliteCatalogStore(connectionString),
                new CatalogFeatureReader(features));

            var inventory = new InventoryService(
                new SqliteInventoryStore(connectionString),
                features);

            var customers = new CustomerService(
                new SqliteCustomerRepository(connectionString));

            var accounts = new CustomerAccountService(
                new SqliteCustomerAccountRepository(connectionString));

            var config = ApiBootstrap.CreateConfiguration(type =>
                type == typeof(AuthService)
                    ? (object)auth
                : type == typeof(BusinessActivityService)
                    ? activities
                : type == typeof(FeatureService)
                    ? features
                : type == typeof(UserManagementService)
                    ? userManagement
                : type == typeof(SettingsService)
                    ? settings
                : type == typeof(CatalogService)
                    ? catalog
                : type == typeof(InventoryService)
                    ? inventory
                : type == typeof(CustomerService)
                    ? customers
                : type == typeof(CustomerAccountService)
                    ? accounts
                : null);

            _server = WebApp.Start(options.Url, app =>
            {
                app.UseWebApi(config);
            });
        }

        public void Stop()
        {
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }
        }
    }
}
