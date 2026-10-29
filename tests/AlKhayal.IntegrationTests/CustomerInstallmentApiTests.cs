using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Customers;
using AlKhayal.Application.Users;
using AlKhayal.Core.Models;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using AlKhayal.Server.Host;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Installment_sale_and_payment_http_flow_updates_customer_statement_and_enforces_screen_access()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var frontend = Path.Combine(Path.GetDirectoryName(f.DatabasePath), "frontend"); Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), "{\"screens\":[{\"route\":\"installment-system\",\"title\":\"البيع بالتقسيط\",\"module\":\"التقسيط\"},{\"route\":\"installment-payment\",\"title\":\"سداد القسط\",\"module\":\"التقسيط\"},{\"route\":\"customer-statement\",\"title\":\"كشف حساب العميل\",\"module\":\"العملاء\"}]}");
                var catalog = new CatalogService(new SqliteCatalogStore(f.ConnectionString));
                var unit = (await catalog.SaveUnitAsync(new SaveUnitCommand { Code = "API-INST", Name = "قطعة", Symbol = "قطعة", IsActive = true }, CancellationToken.None)).Value;
                var product = (await catalog.CreateProductAsync(new CreateProductCommand { OperationKey = "api-installment-product", Name = "منتج آجل", BaseUnitId = unit.Id, ProductType = ProductTypes.Stock, SalePrice = "1000.00", IsActive = true }, CancellationToken.None)).Value;
                var customer = new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer { Name = "عميل عقد API", Phone = "012", Status = "نشط" });
                var password = Guid.NewGuid().ToString("N"); var users = new SqliteUserStore(f.ConnectionString); var hasher = new PasswordHasher();
                await users.CreateAsync("installment-admin", "مدير", hasher.Hash(password), UserRoles.Administrator, CancellationToken.None);
                var cashierId = await users.CreateAsync("installment-cashier", "كاشير", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                using (var server = new AlKhayalServer())
                {
                    var url = new Uri("http://127.0.0.1:" + port); server.Start(new HostOptions { DatabasePath = f.DatabasePath, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') });
                    using (var admin = Client(url)) using (var cashier = Client(url))
                    {
                        var csrf = await Login(admin, "installment-admin", password); var cashierCsrf = await Login(cashier, "installment-cashier", password);
                        var salePath = "api/customers/" + customer.Id + "/installments";
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, salePath, new { }, cashierCsrf)).StatusCode);
                        var request = new { productId = product.Id, productPrice = 1000m, downPayment = 100m, interestRate = 10m, installmentCount = 2, intervalMonths = 1, firstDueOn = "2026-11-29", operationKey = "api-installment-create" };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Post, salePath, request, null)).StatusCode);
                        var created = await Send(admin, HttpMethod.Post, salePath, request, csrf); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                        var contract = JObject.Parse(await created.Content.ReadAsStringAsync())["data"];
                        Assert.Equal(1090m, (decimal)contract["totalAmount"]); var contractId = (long)contract["id"];
                        Assert.Equal(HttpStatusCode.Created, (await Send(admin, HttpMethod.Post, salePath, request, csrf)).StatusCode);
                        var paymentPath = salePath + "/" + contractId + "/payments/1";
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, paymentPath, new { paidOn = "2026-11-29", operationKey = "api-installment-pay-denied" }, cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + cashierId + "/screen-permissions", new { screenIds = new[] { "installment-system", "installment-payment", "customer-statement" } }, csrf)).StatusCode);
                        var payRequest = new { paidOn = "2026-11-29", operationKey = "api-installment-pay-1" };
                        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, HttpMethod.Post, paymentPath, payRequest, cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, HttpMethod.Post, paymentPath, payRequest, cashierCsrf)).StatusCode);
                        var statement = JObject.Parse(await cashier.GetStringAsync("api/customers/" + customer.Id + "/statement"))["data"];
                        Assert.Equal(495m, (decimal)statement["currentBalance"]);
                        Assert.Contains("بيع بالتقسيط", statement.ToString()); Assert.Contains("مقدم بيع بالتقسيط", statement.ToString()); Assert.Contains("سداد القسط رقم 1", statement.ToString());
                        var contracts = JObject.Parse(await cashier.GetStringAsync("api/customers/" + customer.Id + "/installments"))["data"];
                        Assert.Single((JArray)contracts); Assert.Equal("مسدد", (string)contracts[0]["schedule"][0]["status"]);
                    }
                }
            }
        }
    }
}
