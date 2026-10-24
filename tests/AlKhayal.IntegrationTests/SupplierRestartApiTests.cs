using System;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Catalog;
using AlKhayal.Core.Models;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Suppliers;
using AlKhayal.Server.Host;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SupplierRestartApiTests
    {
        [Fact]
        public async Task Authenticated_supplier_list_survives_purchase_payment_and_application_restart()
        {
            var root = Path.Combine(Path.GetTempPath(), "alkhayal-supplier-restart-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "supplier-restart.db");
            var connectionString = Db.ConnectionStringFor(path);
            AlKhayalServer server = null;
            var productId = 0L;
            var variantId = 0L;
            var productUnitId = 0L;
            var supplierId = 0L;
            try
            {
                Migrator.Run(connectionString);
                var users = new SqliteUserStore(connectionString);
                var adminId = await users.CreateAsync("supplier-restart-admin", "مسؤول اختبار", new PasswordHasher().Hash("test-only-secret"), "administrator", CancellationToken.None);
                var product = await new CatalogService(new SqliteCatalogStore(connectionString)).CreateProductAsync(
                    new CreateProductCommand { OperationKey = "supplier-restart-product", Name = "صنف اختبار إعادة التشغيل", BaseUnitId = 1, ProductType = "stock", SalePrice = "15.00", PurchasePrice = "10.13", IsActive = true, UserId = adminId }, CancellationToken.None);
                Assert.True(product.Succeeded, product.ErrorMessage);
                productId = product.Value.Id;
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    variantId = connection.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id", new { Id = productId });
                    productUnitId = connection.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1", new { Id = productId });
                }

                var frontendPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "frontend"));
                var firstUrl = "http://127.0.0.1:" + FreePort();
                server = new AlKhayalServer();
                server.Start(new HostOptions { DatabasePath = path, Url = firstUrl, FrontendPath = frontendPath });
                long purchaseId;
                using (var client = Client(firstUrl))
                {
                    var csrf = await Login(client);
                    var supplierResponse = await Send(client, HttpMethod.Post, "api/suppliers", csrf,
                        "{\"code\":\"RESTART-SUP\",\"name\":\"مورد إعادة التشغيل\",\"phone\":\"01000000000\",\"status\":\"نشط\"}");
                    Assert.Equal(HttpStatusCode.Created, supplierResponse.StatusCode);
                    supplierId = long.Parse(Regex.Match(await supplierResponse.Content.ReadAsStringAsync(), "\\\"id\\\":(\\d+)").Groups[1].Value);

                    // A supplier without payment rows is the exact SQLite COALESCE case that used to throw in Dapper.
                    var initialList = await client.GetAsync("api/suppliers");
                    Assert.Equal(HttpStatusCode.OK, initialList.StatusCode);
                    Assert.Contains("\"paid\":0", await initialList.Content.ReadAsStringAsync());

                    var draftResponse = await Send(client, HttpMethod.Post, "api/purchases/drafts", csrf,
                        "{\"supplierId\":" + supplierId + ",\"warehouseId\":1,\"invoiceNumber\":\"RESTART-INV-1\",\"invoiceDate\":\"2026-10-24\",\"paymentMode\":\"credit\",\"lines\":[{\"variantId\":" + variantId + ",\"productUnitId\":" + productUnitId + ",\"quantity\":\"2.000000\",\"purchaseUnitPrice\":\"10.13\",\"saleUnitPrice\":\"15.00\"}]}");
                    Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
                    var draftText = await draftResponse.Content.ReadAsStringAsync();
                    purchaseId = long.Parse(Regex.Match(draftText, "\\\"id\\\":(\\d+)").Groups[1].Value);
                    var posted = await Send(client, HttpMethod.Post, "api/purchases/" + purchaseId + "/post", csrf, "{\"operationKey\":\"restart-purchase-post\"}");
                    Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

                    var payment = await Send(client, HttpMethod.Post, "api/suppliers/" + supplierId + "/payments", csrf,
                        "{\"purchaseInvoiceId\":" + purchaseId + ",\"operationKey\":\"restart-purchase-payment\",\"amount\":5.13,\"paymentDate\":\"2026-10-24\",\"method\":\"cash\"}");
                    Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
                }

                // Dispose the API (all composition-owned connections are short-lived), clear pooled SQLite handles,
                // rerun migrations twice as startup does, and construct a fresh application against the same file.
                server.Dispose(); server = null;
                SQLiteConnection.ClearAllPools();
                Migrator.Run(connectionString);
                Migrator.Run(connectionString);
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    Assert.Equal(18, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Suppliers WHERE Id=@Id", new { Id = supplierId }));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseInvoices WHERE Id=@Id AND Status='posted'", new { Id = purchaseId }));
                    Assert.Equal(2, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE PurchaseInvoiceId=@Id", new { Id = purchaseId }));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers WHERE VariantId=@Id AND Basis='receipt'", new { Id = variantId }));
                    Assert.Empty(connection.Query<dynamic>("PRAGMA foreign_key_check"));
                }

                var secondUrl = "http://127.0.0.1:" + FreePort();
                server = new AlKhayalServer();
                server.Start(new HostOptions { DatabasePath = path, Url = secondUrl, FrontendPath = frontendPath });
                using (var client = Client(secondUrl))
                {
                    await Login(client);
                    var response = await client.GetAsync("api/suppliers");
                    var json = await response.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Contains("\"id\":" + supplierId, json);
                    Assert.Contains("مورد إعادة التشغيل", json);
                    Assert.Contains("\"payable\":15.13", json);
                    Assert.Contains("\"paid\":5.13", json);
                }

                server.Dispose(); server = null;
                SQLiteConnection.ClearAllPools();
                Migrator.Run(connectionString);
                Migrator.Run(connectionString);
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Suppliers WHERE Id=@Id", new { Id = supplierId }));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseInvoices WHERE Id=@Id", new { Id = purchaseId }));
                    Assert.Equal(2, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SupplierLedgerEntries WHERE PurchaseInvoiceId=@Id", new { Id = purchaseId }));
                    Assert.Equal(1, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers WHERE VariantId=@Id AND Basis='receipt'", new { Id = variantId }));
                    Assert.Equal("ok", connection.ExecuteScalar<string>("PRAGMA integrity_check"));
                    Assert.Empty(connection.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
            finally
            {
                if (server != null) server.Dispose();
                SQLiteConnection.ClearAllPools();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static HttpClient Client(string url) { return new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(url) }; }
        private static StringContent Json(string text) { return new StringContent(text, Encoding.UTF8, "application/json"); }
        private static async Task<string> Login(HttpClient client)
        {
            var response = await client.PostAsync("api/auth/login", Json("{\"username\":\"supplier-restart-admin\",\"password\":\"test-only-secret\"}"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var token = Regex.Match(await response.Content.ReadAsStringAsync(), "\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(token));
            return token;
        }
        private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string csrf, string body)
        {
            var request = new HttpRequestMessage(method, path) { Content = Json(body) };
            request.Headers.Add("X-CSRF-Token", csrf);
            return client.SendAsync(request);
        }
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
        }
    }
}
