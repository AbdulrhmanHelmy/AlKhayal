using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using Dapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Operational_reports_HTTP_independent_permissions_module_gates_overview_nonleakage_and_read_only()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                OperationalReportTests.Parties(f);
                var reports=new[]{"customers","suppliers","shifts","attendance","maintenance","installments","overview"};
                using(var c=f.Open())foreach(var key in new[]{"advanced-shifts","employee-management","employee-attendance"})c.Execute("INSERT OR REPLACE INTO ModuleConfiguration(ModuleKey,IsEnabled,OverrideState,UpdatedUtc,ChangedByUserId) VALUES(@key,1,'administrator','2026-03-01',@UserId)",new{key,f.UserId});
                var frontend=Path.Combine(Path.GetDirectoryName(f.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),JsonConvert.SerializeObject(new{screens=reports.Select(r=>new{route="report-"+r,title=r,module="التقارير",featureKey=AlKhayal.Application.Reports.OperationalReportService.Feature(r)})}));
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.ConnectionString);var hasher=new PasswordHasher();
                await users.CreateAsync("ops-admin","مدير",hasher.Hash(password),UserRoles.Administrator,CancellationToken.None);
                var cashierId=await users.CreateAsync("ops-cashier","كاشير",hasher.Hash(password),UserRoles.Cashier,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    var address=new Uri("http://127.0.0.1:"+port);server.Start(new HostOptions{DatabasePath=f.DatabasePath,FrontendPath=frontend,Url=address.ToString().TrimEnd('/')});
                    using(var anon=Client(address))using(var admin=Client(address))using(var cashier=Client(address))
                    {
                        var csrf=await Login(admin,"ops-admin",password);await Login(cashier,"ops-cashier",password);
                        foreach(var report in reports)
                        {
                            var endpoint="api/reports/"+report;
                            Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(endpoint)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync(endpoint)).StatusCode);
                            var before=File.ReadAllBytes(f.DatabasePath);var response=await admin.GetAsync(endpoint+"?from=2026-03-01&to=2026-03-31");Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(before,File.ReadAllBytes(f.DatabasePath));
                            Assert.Equal(HttpStatusCode.MethodNotAllowed,(await Send(admin,System.Net.Http.HttpMethod.Post,endpoint,new{},csrf)).StatusCode);
                            var invalid=report=="overview"?new[]{"?from=bad&to=bad"}:new[]{"?page=0","?page=abc","?pageSize=101","?personId=0","?status=invented","?search="+new string('x',121),"?from=bad&to=bad"};
                            foreach(var filter in invalid)Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+filter)).StatusCode);
                        }
                        foreach(var report in reports)
                        {
                            Assert.Equal(HttpStatusCode.OK,(await Send(admin,System.Net.Http.HttpMethod.Put,"api/users/"+cashierId+"/screen-permissions",new{screenIds=new[]{"report-"+report}},csrf)).StatusCode);
                            foreach(var target in reports)Assert.Equal(target==report?HttpStatusCode.OK:HttpStatusCode.Forbidden,(await cashier.GetAsync("api/reports/"+target)).StatusCode);
                            if(report=="overview")Assert.Empty((JArray)JObject.Parse(await (await cashier.GetAsync("api/reports/overview")).Content.ReadAsStringAsync())["data"]);
                        }
                        await Send(admin,System.Net.Http.HttpMethod.Put,"api/users/"+cashierId+"/screen-permissions",new{screenIds=new[]{"report-overview","report-customers"}},csrf);
                        var metrics=(JArray)JObject.Parse(await (await cashier.GetAsync("api/reports/overview?from=2026-03-01&to=2026-03-31")).Content.ReadAsStringAsync())["data"];
                        Assert.Single(metrics);Assert.Equal("customers",(string)metrics[0]["key"]);Assert.Equal("105.0000",(string)metrics[0]["value"]);Assert.True((bool)metrics[0]["current"]);
                        using(var c=f.Open())c.Execute("INSERT OR REPLACE INTO ModuleConfiguration(ModuleKey,IsEnabled,OverrideState,UpdatedUtc,ChangedByUserId) VALUES('maintenance-management',0,'administrator','2026-03-01',@UserId)",new{f.UserId});
                        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("api/reports/maintenance")).StatusCode);
                        metrics=(JArray)JObject.Parse(await (await admin.GetAsync("api/reports/overview")).Content.ReadAsStringAsync())["data"];Assert.DoesNotContain(metrics,x=>(string)x["key"]=="maintenance");
                    }
                }
            }
        }
    }
}
