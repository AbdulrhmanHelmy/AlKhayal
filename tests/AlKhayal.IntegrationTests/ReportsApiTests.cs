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
        [Theory]
        [InlineData("sales")]
        [InlineData("purchases")]
        [InlineData("returns")]
        [InlineData("payment-methods")]
        [InlineData("expenses")]
        public async Task Reports_http_enforces_session_effective_screen_and_rejects_invalid_parameters(string report)
        {
            using(var f=new TreasuryPersistenceTests.Fixture()) {
                var frontend=Path.Combine(Path.GetDirectoryName(f.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"pos\",\"title\":\"الكاشير\",\"module\":\"المبيعات\"},{\"route\":\"report-"+report+"\",\"title\":\"تقرير\",\"module\":\"التقارير\"}]}");
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.ConnectionString);var hasher=new PasswordHasher();
                await users.CreateAsync("reports-admin","مدير",hasher.Hash(password),UserRoles.Administrator,CancellationToken.None);
                var cashierId=await users.CreateAsync("reports-cashier","كاشير",hasher.Hash(password),UserRoles.Cashier,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer(new TestLicenseGate())) {
                    var address=new Uri("http://127.0.0.1:"+port);server.Start(new HostOptions{DatabasePath=f.DatabasePath,FrontendPath=frontend,Url=address.ToString().TrimEnd('/')});
                    using(var anon=Client(address))using(var admin=Client(address))using(var cashier=Client(address)) {
                        var endpoint="api/reports/"+report+"?from=2026-03-01&to=2026-03-01";
                        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(endpoint)).StatusCode);
                        var csrf=await Login(admin,"reports-admin",password);await Login(cashier,"reports-cashier",password);
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync(endpoint)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync(endpoint)).StatusCode);
                        foreach(var invalid in new[]{"&page=0","&pageSize=101","&page=banana","&pageSize=-1"})Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+invalid)).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+"&search="+new string('x',121))).StatusCode);
                        if(report=="sales") {
                            Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+"&method=bogus")).StatusCode);
                            Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+"&cashierId=0")).StatusCode);
                        } else if(report=="purchases") Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+"&supplierId=0")).StatusCode);
                        else if(report=="expenses") Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+"&categoryId=0")).StatusCode);
                        else Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(endpoint+"&type=bogus")).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("api/reports/"+report+"?from=2026-03-02&to=2026-03-01")).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync("api/reports/"+report)).StatusCode);
                        var access=await Send(admin,HttpMethod.Put,"api/users/"+cashierId+"/screen-permissions",new{screenIds=new[]{"report-"+report}},csrf);Assert.Equal(HttpStatusCode.OK,access.StatusCode);
                        var response=await cashier.GetAsync(endpoint);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True((bool)JObject.Parse(await response.Content.ReadAsStringAsync())["success"]);
                        // Report permission does not grant invoice mutations or another report.
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/reports/"+(report=="sales"?"returns":"sales")+"?from=2026-03-01&to=2026-03-01")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Post,"api/sales/drafts",new{},null)).StatusCode);
                        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await Send(admin,HttpMethod.Post,endpoint,new{},csrf)).StatusCode);
                    }
                }
            }
        }
    }
}
