using System;
using System.IO;
using System.Data.SQLite;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Server.Host;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class CatalogApiTests
    {
        [Fact]
        public async Task Catalog_api_enforces_authentication_csrf_screen_permissions_and_restart_persistence()
        {
            var root=Path.Combine(Path.GetTempPath(),"alkhayal-catalog-api-"+Guid.NewGuid().ToString("N"));var path=Path.Combine(root,"catalog.db");var frontend=Path.Combine(root,"frontend");AlKhayalServer server=null;
            try
            {
                Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"products-list\",\"title\":\"المنتجات\",\"module\":\"المخزن\",\"featureKey\":\"product-catalog\"},{\"route\":\"product-add\",\"title\":\"إضافة\",\"module\":\"المخزن\",\"featureKey\":\"product-catalog\"},{\"route\":\"product-edit\",\"title\":\"تعديل\",\"module\":\"المخزن\",\"featureKey\":\"product-catalog\"},{\"route\":\"product-categories\",\"title\":\"التصنيفات\",\"module\":\"الإعدادات\",\"featureKey\":\"product-catalog\",\"administratorOnly\":true},{\"route\":\"unit-add\",\"title\":\"الوحدات\",\"module\":\"الإعدادات\",\"featureKey\":\"product-catalog\",\"administratorOnly\":true}]}",Encoding.UTF8);
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var users=new SqliteUserStore(cs);var hasher=new PasswordHasher();var adminId=await users.CreateAsync("catalog-admin","مسؤول",hasher.Hash("secret"),UserRoles.Administrator,CancellationToken.None);await users.CreateAsync("catalog-cashier","كاشير",hasher.Hash("secret"),UserRoles.Cashier,CancellationToken.None);
                var variantProduct=await new SqliteCatalogStore(cs).CreateAggregateProductAsync(new CreateProductRecord
                {
                    OperationKey="api-summary-shirt",RequestHash="api-summary-shirt-hash",Name="تي شيرت API",BaseUnitId=1,ProductType="stock",SellPriceScaled=5050000,IsActive=true,UserId=adminId,
                    Configuration=new ProductConfigurationRecord
                    {
                        VariantMode=true,CapabilityKeys=new List<string>{"sizes","colors","product-variants","batch-tracking","expiry-dates"},
                        Attributes=new List<ProductAttributeAssignmentConfigRecord>
                        {
                            new ProductAttributeAssignmentConfigRecord{AttributeId=1,Values=new List<string>{"M","L"}},
                            new ProductAttributeAssignmentConfigRecord{AttributeId=2,Values=new List<string>{"أسود","أبيض"}}
                        }
                    }
                },CancellationToken.None);
                var url="http://127.0.0.1:"+FreePort();server=new AlKhayalServer();server.Start(new HostOptions{DatabasePath=path,Url=url,FrontendPath=frontend});
                using(var anonymous=Client(url))Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("api/catalog/products")).StatusCode);
                long productId;
                using(var admin=Client(url))
                {
                    var csrf=await Login(admin,"catalog-admin");
                    Assert.Equal(HttpStatusCode.Forbidden,(await admin.PostAsync("api/catalog/products",Json("{\"operationKey\":\"missing-csrf\",\"name\":\"x\",\"baseUnitId\":1,\"productType\":\"stock\",\"salePrice\":\"1\",\"isActive\":true}"))).StatusCode);
                    var category=await Post(admin,csrf,"api/catalog/categories","{\"name\":\"مشروبات\",\"isActive\":true,\"sortOrder\":1}");Assert.Equal(HttpStatusCode.Created,category.StatusCode);var categoryId=Regex.Match(await category.Content.ReadAsStringAsync(),"\\\"id\\\":(\\d+)").Groups[1].Value;
                    var created=await Post(admin,csrf,"api/catalog/products","{\"operationKey\":\"api-product-1\",\"name\":\"شاي API\",\"categoryId\":"+categoryId+",\"baseUnitId\":1,\"productType\":\"stock\",\"salePrice\":\"14.125\",\"purchasePrice\":\"10.33335\",\"isActive\":true}");Assert.Equal(HttpStatusCode.Created,created.StatusCode);var text=await created.Content.ReadAsStringAsync();productId=long.Parse(Regex.Match(text,"\\\"id\\\":(\\d+)").Groups[1].Value);Assert.Contains("14.1250",text);Assert.Contains("10.3334",text);
                    var invalidCost=await Post(admin,csrf,"api/catalog/products","{\"operationKey\":\"api-product-negative\",\"name\":\"تكلفة سالبة\",\"baseUnitId\":1,\"productType\":\"stock\",\"salePrice\":\"1\",\"purchasePrice\":\"-1\",\"isActive\":true}");Assert.Equal(HttpStatusCode.BadRequest,invalidCost.StatusCode);
                    var catalogJson=await admin.GetStringAsync("api/catalog/products?page=1&pageSize=10");
                    Assert.Contains("\"sizeValues\":[\"M\",\"L\"]",catalogJson);
                    Assert.Contains("\"colorValues\":[\"أسود\",\"أبيض\"]",catalogJson);
                    Assert.Contains("\"variantCount\":4",catalogJson);
                    Assert.Contains("\"hasExpiryTracking\":true",catalogJson);
                    var detailJson=await admin.GetStringAsync("api/catalog/products/"+variantProduct.Id);
                    Assert.Contains("\"isAssigned\":true",detailJson);
                    Assert.Contains("\"attributeValueIds\"",detailJson);
                    Assert.Equal(HttpStatusCode.OK,(await Put(admin,csrf,"api/users/2/screen-permissions","{\"screenIds\":[\"products-list\"]}")).StatusCode);
                }
                using(var cashier=Client(url))
                {
                    var csrf=await Login(cashier,"catalog-cashier");Assert.Equal(HttpStatusCode.OK,(await cashier.GetAsync("api/catalog/products?search=%D8%B4%D8%A7%D9%8A&page=1&pageSize=10")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden,(await Post(cashier,csrf,"api/catalog/products","{\"operationKey\":\"cashier-create\",\"name\":\"ممنوع\",\"baseUnitId\":1,\"productType\":\"stock\",\"salePrice\":\"1\",\"isActive\":true}")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden,(await Put(cashier,csrf,"api/catalog/products/"+productId,"{\"name\":\"محاولة\",\"baseUnitId\":1,\"productType\":\"stock\",\"salePrice\":\"1\",\"isActive\":false,\"version\":1}")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden,(await Put(cashier,csrf,"api/catalog/products/"+productId+"/state","{\"isActive\":false,\"version\":1}")).StatusCode);
                }
                server.Dispose();server=null;
                var secondUrl="http://127.0.0.1:"+FreePort();server=new AlKhayalServer();server.Start(new HostOptions{DatabasePath=path,Url=secondUrl,FrontendPath=frontend});
                using(var admin=Client(secondUrl)){await Login(admin,"catalog-admin");var text=await admin.GetStringAsync("api/catalog/products/"+productId);Assert.Contains("شاي API",text);Assert.Contains("10.3334",text);var list=await admin.GetStringAsync("api/catalog/products?page=1&pageSize=10");Assert.Contains("\"variantCount\":4",list);Assert.Contains("\"sizeValues\":[\"M\",\"L\"]",list);}
            }
            finally{if(server!=null)server.Dispose();SQLiteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
        }

        private static HttpClient Client(string url){return new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=new Uri(url)};}
        private static StringContent Json(string json){return new StringContent(json,Encoding.UTF8,"application/json");}
        private static async Task<string> Login(HttpClient client,string username){var response=await client.PostAsync("api/auth/login",Json("{\"username\":\""+username+"\",\"password\":\"secret\"}"));Assert.Equal(HttpStatusCode.OK,response.StatusCode);return Regex.Match(await response.Content.ReadAsStringAsync(),"\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value;}
        private static Task<HttpResponseMessage> Put(HttpClient client,string csrf,string path,string body){return Send(client,csrf,HttpMethod.Put,path,body);}
        private static Task<HttpResponseMessage> Post(HttpClient client,string csrf,string path,string body){return Send(client,csrf,HttpMethod.Post,path,body);}
        private static Task<HttpResponseMessage> Send(HttpClient client,string csrf,HttpMethod method,string path,string body){var request=new HttpRequestMessage(method,path){Content=Json(body)};request.Headers.Add("X-CSRF-Token",csrf);return client.SendAsync(request);}
        private static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    }
}
