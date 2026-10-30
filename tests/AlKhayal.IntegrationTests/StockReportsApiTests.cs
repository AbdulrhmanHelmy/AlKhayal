using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Five_stock_reports_HTTP_independent_permissions_invalid_filters_and_read_only_GETs()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("2","2");await f.Sale("1");
                var reports=new[]{"product-profit","inventory-valuation","top-products","low-stock","inventory-movements"};
                var frontend=Path.Combine(Path.GetDirectoryName(f.Db.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),JsonConvert.SerializeObject(new{screens=Array.ConvertAll(reports,r=>new{route="report-"+r,title=r,module="التقارير"})}));
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.Cs);var hasher=new PasswordHasher();
                await users.CreateAsync("stock-admin","مدير",hasher.Hash(password),UserRoles.Administrator,CancellationToken.None);
                var cashierId=await users.CreateAsync("stock-cashier","كاشير",hasher.Hash(password),UserRoles.Cashier,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    var address=new Uri("http://127.0.0.1:"+port);server.Start(new HostOptions{DatabasePath=f.Db.DatabasePath,FrontendPath=frontend,Url=address.ToString().TrimEnd('/')});
                    using(var anon=Client(address))using(var admin=Client(address))using(var cashier=Client(address))
                    {
                        var csrf=await Login(admin,"stock-admin",password);await Login(cashier,"stock-cashier",password);
                        foreach(var report in reports)
                        {
                            string endpoint="api/reports/"+report;
                            Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(endpoint)).StatusCode);
                            Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync(endpoint)).StatusCode);
                            foreach(var filter in new[]{"?page=0","?page=abc","?pageSize=101","?warehouseId=0","?categoryId=-1","?status=invented","?type=invalid","?sort=invalid","?search="+new string('x',121)})
                                Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+filter)).StatusCode);
                            var before=File.ReadAllBytes(f.Db.DatabasePath);var response=await admin.GetAsync(endpoint);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                            var data=JObject.Parse(await response.Content.ReadAsStringAsync())["data"];Assert.NotNull(data["summary"]);Assert.NotNull(data["items"]);
                            if(report=="top-products" || report=="low-stock")
                            {
                                Assert.Null(data["summary"]["cost"]);Assert.Null(data["summary"]["profit"]);
                                foreach(var row in data["items"]) {Assert.Null(row["cost"]);Assert.Null(row["profit"]);Assert.Null(row["salePrice"]);}
                            }
                            Assert.Equal(before,File.ReadAllBytes(f.Db.DatabasePath));
                            Assert.Equal(HttpStatusCode.MethodNotAllowed,(await Send(admin,HttpMethod.Post,endpoint,new{},csrf)).StatusCode);
                        }
                        foreach(var report in reports)
                        {
                            Assert.Equal(HttpStatusCode.OK,(await Send(admin,HttpMethod.Put,"api/users/"+cashierId+"/screen-permissions",new{screenIds=new[]{"report-"+report}},csrf)).StatusCode);
                            foreach(var target in reports)Assert.Equal(target==report?HttpStatusCode.OK:HttpStatusCode.Forbidden,(await cashier.GetAsync("api/reports/"+target)).StatusCode);
                        }
                        foreach(var report in new[]{"inventory-valuation","low-stock"})Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("api/reports/"+report+"?from=2026-01-01&to=2026-01-31")).StatusCode);
                        foreach(var report in new[]{"product-profit","top-products","inventory-movements"})Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("api/reports/"+report+"?from=bad&to=bad")).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("api/reports/product-profit?warehouseId=1")).StatusCode);
                    }
                }
            }
        }
    }
}
