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
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Net_profit_HTTP_permissions_filters_CSRF_classification_and_read_only()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                var voucher=await f.Expense("160");
                var frontend=Path.Combine(Path.GetDirectoryName(f.Db.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"pos\",\"title\":\"بيع\",\"module\":\"المبيعات\"},{\"route\":\"report-sales\",\"title\":\"مبيعات\",\"module\":\"التقارير\"},{\"route\":\"report-net-profit\",\"title\":\"أرباح\",\"module\":\"التقارير\"}]}");
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.Cs);var hasher=new PasswordHasher();
                await users.CreateAsync("profit-admin","مدير",hasher.Hash(password),UserRoles.Administrator,CancellationToken.None);
                var cashierId=await users.CreateAsync("profit-cashier","كاشير",hasher.Hash(password),UserRoles.Cashier,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    var address=new Uri("http://127.0.0.1:"+port);server.Start(new HostOptions{DatabasePath=f.Db.DatabasePath,FrontendPath=frontend,Url=address.ToString().TrimEnd('/')});
                    using(var anon=Client(address))using(var admin=Client(address))using(var cashier=Client(address))
                    {
                        const string endpoint="api/reports/net-profit";
                        var classify=endpoint+"/expenses/"+voucher.Id+"/classification";
                        var cmd=new{classification="included",reason="مراجعة المدير",expectedRevision=0,operationKey="profit-http-class"};
                        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(endpoint)).StatusCode);
                        Assert.Equal(HttpStatusCode.Unauthorized,(await Send(anon,HttpMethod.Post,classify,cmd,null)).StatusCode);
                        var csrf=await Login(admin,"profit-admin",password);var cashierCsrf=await Login(cashier,"profit-cashier",password);
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync(endpoint)).StatusCode);
                        // Granting another report cannot disclose profit.
                        Assert.Equal(HttpStatusCode.OK,(await Send(admin,HttpMethod.Put,"api/users/"+cashierId+"/screen-permissions",new{screenIds=new[]{"report-sales"}},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync(endpoint)).StatusCode);
                        foreach(var filter in new[]{"?from=bad&to=bad","?from=2026-02-01","?from=2026-02-02&to=2026-02-01","?breakdown=weekly","?breakdown=%27%3BDROP%20TABLE%20SalesInvoices%3B--","?page=0","?page=abc","?pageSize=101"})
                            Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+filter)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,HttpMethod.Post,classify,cmd,null)).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,HttpMethod.Post,classify,new{classification="guess",reason="x"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(admin,HttpMethod.Post,classify,cmd,csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(admin,HttpMethod.Post,classify,cmd,csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,HttpMethod.Post,classify,new{classification="excluded",reason="تصحيح",expectedRevision=0,operationKey="stale-http"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.NotFound,(await Send(admin,HttpMethod.Post,endpoint+"/expenses/999999/classification",new{classification="included",reason="مراجعة",expectedRevision=0,operationKey="not-found"},csrf)).StatusCode);
                        var access=await Send(admin,HttpMethod.Put,"api/users/"+cashierId+"/screen-permissions",new{screenIds=new[]{"report-net-profit"}},csrf);Assert.Equal(HttpStatusCode.OK,access.StatusCode);
                        var today=DateTime.Today.ToString("yyyy-MM-dd");var url=endpoint+"?from="+today+"&to="+today;
                        var before=File.ReadAllBytes(f.Db.DatabasePath);
                        var response=await admin.GetAsync(url);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                        var data=JObject.Parse(await response.Content.ReadAsStringAsync())["data"];Assert.True((bool)data["canClassify"]);Assert.Equal("-160.0000",(string)data["summary"]["netProfit"]);
                        response=await cashier.GetAsync(url);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.False((bool)JObject.Parse(await response.Content.ReadAsStringAsync())["data"]["canClassify"]);
                        Assert.Equal(before,File.ReadAllBytes(f.Db.DatabasePath));
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Post,classify,cmd,cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/reports/sales")).StatusCode);
                        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await Send(admin,HttpMethod.Post,endpoint,new{},csrf)).StatusCode);
                    }
                }
            }
        }
    }
}
