
using System;
using System.Collections.Generic;
using System.Web.Http;
using System.Web.Http.Dependencies;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AlKhayal.Api
{
    public static class ApiBootstrap
    {
        public static HttpConfiguration CreateConfiguration(
            Func<Type, object> resolver)
        {
            var config = new HttpConfiguration();

            config.MapHttpAttributeRoutes();
            config.DependencyResolver = new SimpleResolver(resolver);

            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();

            config.Formatters.JsonFormatter.SerializerSettings.NullValueHandling =
                NullValueHandling.Ignore;

            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.LocalOnly;

            return config;
        }
    }

    internal sealed class SimpleResolver : IDependencyResolver
    {
        private readonly Func<Type, object> _get;

        public SimpleResolver(Func<Type, object> get)
        {
            _get = get;
        }

        public object GetService(Type t)
        {
            var value = _get(t);
            if (value != null)
                return value;

            if (t == typeof(Controllers.LabelsController))
                return new Controllers.LabelsController(
                    (Application.Printing.LabelService)_get(typeof(Application.Printing.LabelService)),
                    (Application.Catalog.BarcodeService)_get(typeof(Application.Catalog.BarcodeService)),
                    (Application.Authentication.AuthService)_get(typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.PrintingController))
                return new Controllers.PrintingController(
                    (Application.Printing.PrintingService)_get(typeof(Application.Printing.PrintingService)),
                    (Application.Authentication.AuthService)_get(typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.WorkforceController))
                return new Controllers.WorkforceController(
                    (Application.Workforce.WorkforceService)_get(typeof(Application.Workforce.WorkforceService)),
                    (Application.Authentication.AuthService)_get(typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));
            if (t == typeof(Controllers.AuthController))
                return new Controllers.AuthController(
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)));

            if (t == typeof(Controllers.BusinessActivitiesController))
                return new Controllers.BusinessActivitiesController(
                    (Application.BusinessActivities.BusinessActivityService)_get(
                        typeof(Application.BusinessActivities.BusinessActivityService)),
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)));

            if (t == typeof(Controllers.FeaturesController))
                return new Controllers.FeaturesController(
                    (Application.Features.FeatureService)_get(
                        typeof(Application.Features.FeatureService)),
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)));

            if (t == typeof(Controllers.UsersController))
                return new Controllers.UsersController(
                    (Application.Users.UserManagementService)_get(
                        typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(
                        typeof(Application.Features.FeatureService)),
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)));

            if (t == typeof(Controllers.ScreenAccessController))
                return new Controllers.ScreenAccessController(
                    (Application.Users.UserManagementService)_get(
                        typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(
                        typeof(Application.Features.FeatureService)),
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)));

            if (t == typeof(Controllers.SettingsController))
                return new Controllers.SettingsController(
                    (Application.Settings.SettingsService)_get(
                        typeof(Application.Settings.SettingsService)),
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)));

            if (t == typeof(Controllers.CatalogController))
                return new Controllers.CatalogController(
                    (Application.Catalog.CatalogService)_get(
                        typeof(Application.Catalog.CatalogService)),
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(
                        typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(
                        typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.InventoryController))
                return new Controllers.InventoryController(
                    (Application.Inventory.InventoryService)_get(
                        typeof(Application.Inventory.InventoryService)),
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(
                        typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(
                        typeof(Application.Features.FeatureService)),
                    (Application.Catalog.CatalogService)_get(
                        typeof(Application.Catalog.CatalogService)));

            if (t == typeof(Controllers.CustomersController))
                return new Controllers.CustomersController(
                    (Application.Customers.CustomerService)_get(
                        typeof(Application.Customers.CustomerService)),
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)),
                    (Application.Customers.CustomerAccountService)_get(
                        typeof(Application.Customers.CustomerAccountService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.SuppliersController))
                return new Controllers.SuppliersController(
                    (Application.Suppliers.SupplierService)_get(
                        typeof(Application.Suppliers.SupplierService)),
                    (Application.Authentication.AuthService)_get(
                        typeof(Application.Authentication.AuthService)),
                    (Application.Suppliers.SupplierAccountService)_get(
                        typeof(Application.Suppliers.SupplierAccountService)),
                    (Application.Suppliers.SupplierLedgerService)_get(
                        typeof(Application.Suppliers.SupplierLedgerService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.PurchasesController))
                return new Controllers.PurchasesController(
                    (Application.Purchasing.PurchaseService)_get(typeof(Application.Purchasing.PurchaseService)),
                    (Application.Authentication.AuthService)_get(typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.SalesController))
                return new Controllers.SalesController(
                    (Application.Sales.SalesService)_get(typeof(Application.Sales.SalesService)),
                    (Application.Authentication.AuthService)_get(typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.ReturnsController))
                return new Controllers.ReturnsController(
                    (Application.Returns.ReturnService)_get(typeof(Application.Returns.ReturnService)),
                    (Application.Sales.SalesService)_get(typeof(Application.Sales.SalesService)),
                    (Application.Purchasing.PurchaseService)_get(typeof(Application.Purchasing.PurchaseService)),
                    (Application.Authentication.AuthService)_get(typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.ExpensesController))
                return new Controllers.ExpensesController(
                    (Application.Expenses.ExpenseService)_get(typeof(Application.Expenses.ExpenseService)),
                    (Application.Authentication.AuthService)_get(typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.TreasuryController))
                return new Controllers.TreasuryController(
                    (Application.Treasury.TreasuryService)_get(typeof(Application.Treasury.TreasuryService)),
                    (Application.Authentication.AuthService)_get(typeof(Application.Authentication.AuthService)),
                    (Application.Users.UserManagementService)_get(typeof(Application.Users.UserManagementService)),
                    (Application.Features.FeatureService)_get(typeof(Application.Features.FeatureService)));

            if (t == typeof(Controllers.SystemController))
                return new Controllers.SystemController();

            return null;
        }

        public IEnumerable<object> GetServices(Type t)
        {
            return new object[0];
        }

        public IDependencyScope BeginScope()
        {
            return this;
        }

        public void Dispose()
        {
        }
    }
}
