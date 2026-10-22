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
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Server.Host;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class InventoryApiTests
    {
        [Fact]
        public async Task Inventory_api_requires_auth_csrf_and_existing_screen_permission()
        {
            var root=Path.Combine(Path.GetTempPath(),"alkhayal-inventory-api-"+Guid.NewGuid().ToString("N"));var db=Path.Combine(root,"test.db");var frontend=Path.Combine(root,"frontend");AlKhayalServer server=null;
            try
            {
                Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"warehouses-list\",\"title\":\"دليل المخازن\",\"module\":\"المخزن\",\"featureKey\":\"basic-inventory\"}]}",Encoding.UTF8);
                var cs=Db.ConnectionStringFor(db);Migrator.Run(cs);var users=new SqliteUserStore(cs);var hasher=new PasswordHasher();
                await users.CreateAsync("inventory-admin","مسؤول",hasher.Hash("secret"),UserRoles.Administrator,CancellationToken.None);
                await users.CreateAsync("inventory-cashier","كاشير",hasher.Hash("secret"),UserRoles.Cashier,CancellationToken.None);
                var product=await new CatalogService(new SqliteCatalogStore(cs)).CreateProductAsync(new CreateProductCommand{OperationKey="inventory-api-product",Name="شاي",BaseUnitId=1,ProductType="stock",SalePrice="10",IsActive=true},CancellationToken.None);
                Assert.True(product.Succeeded,product.ErrorMessage);
                long variant,unit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id",new{Id=product.Value.Id});}
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                server=new AlKhayalServer();server.Start(new HostOptions{DatabasePath=db,Url="http://127.0.0.1:"+port,FrontendPath=frontend});
                var baseUrl=new Uri("http://127.0.0.1:"+port);
                using(var anonymous=new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=baseUrl})
                {
                    Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("api/inventory/warehouses")).StatusCode);
                    Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsync("api/inventory/post",Json("{}"))).StatusCode);
                }
                using(var cashier=new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=baseUrl})
                {
                    var csrf=await Login(cashier,"inventory-cashier");
                    Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/inventory/balances")).StatusCode);
                    var req=new HttpRequestMessage(HttpMethod.Post,"api/inventory/post"){Content=Json("{}")};req.Headers.Add("X-CSRF-Token",csrf);
                    Assert.Equal(HttpStatusCode.Forbidden,(await cashier.SendAsync(req)).StatusCode);
                }
                using(var admin=new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=baseUrl})
                {
                    var csrf=await Login(admin,"inventory-admin");
                    Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync("api/inventory/warehouses")).StatusCode);
                    var body="{\"operationKey\":\"api-receipt-1\",\"type\":\"manual_receipt\",\"warehouseId\":1,\"reason\":\"تصحيح\",\"lines\":[{\"variantId\":"+variant+",\"productUnitId\":"+unit+",\"quantity\":\"10\",\"unitCost\":\"2\"}]}";
                    Assert.Equal(HttpStatusCode.Forbidden,(await admin.PostAsync("api/inventory/post",Json(body))).StatusCode);
                    var req=new HttpRequestMessage(HttpMethod.Post,"api/inventory/post"){Content=Json(body)};req.Headers.Add("X-CSRF-Token",csrf);
                    var response=await admin.SendAsync(req);Assert.Equal(HttpStatusCode.Created,response.StatusCode);
                    var balances=await admin.GetStringAsync("api/inventory/balances?warehouseId=1");Assert.Contains("10000000",balances);
                    var grant=new HttpRequestMessage(HttpMethod.Put,"api/users/2/screen-permissions"){Content=Json("{\"screenIds\":[\"warehouses-list\"]}")};grant.Headers.Add("X-CSRF-Token",csrf);
                    Assert.Equal(HttpStatusCode.OK,(await admin.SendAsync(grant)).StatusCode);
                }
                using(var cashier=new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=baseUrl})
                {
                    var csrf=await Login(cashier,"inventory-cashier");
                    Assert.Equal(HttpStatusCode.OK,(await cashier.GetAsync("api/inventory/products?search=%D8%B4%D8%A7%D9%8A")).StatusCode);
                    var body="{\"operationKey\":\"cashier-issue-1\",\"type\":\"manual_issue\",\"warehouseId\":1,\"reason\":\"تصحيح\",\"lines\":[{\"variantId\":"+variant+",\"productUnitId\":"+unit+",\"quantity\":\"1\"}]}";
                    var req=new HttpRequestMessage(HttpMethod.Post,"api/inventory/post"){Content=Json(body)};req.Headers.Add("X-CSRF-Token",csrf);
                    Assert.Equal(HttpStatusCode.Created,(await cashier.SendAsync(req)).StatusCode);
                    var blocked=new HttpRequestMessage(HttpMethod.Post,"api/inventory/integrity/rebuild"){Content=Json("{}")};blocked.Headers.Add("X-CSRF-Token",csrf);
                    Assert.Equal(HttpStatusCode.Forbidden,(await cashier.SendAsync(blocked)).StatusCode);
                }
            }
            finally{if(server!=null)server.Dispose();SQLiteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        private static StringContent Json(string value){return new StringContent(value,Encoding.UTF8,"application/json");}
        private static async Task<string> Login(HttpClient client,string username){var response=await client.PostAsync("api/auth/login",Json("{\"username\":\""+username+"\",\"password\":\"secret\"}"));Assert.Equal(HttpStatusCode.OK,response.StatusCode);return Regex.Match(await response.Content.ReadAsStringAsync(),"\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value;}
    }
}
