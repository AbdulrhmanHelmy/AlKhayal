using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using Dapper;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Discounted_credit_returns_HTTP_complete_replay_and_reject_overreturn(bool unified)
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                await f.Receipt("3","2");var sale=await f.Sale("3","0.01","0.02");
                var frontend=Path.Combine(Path.GetDirectoryName(f.Db.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"sales-returns\",\"title\":\"إرجاع\",\"module\":\"المبيعات\"},{\"route\":\"returns-overview\",\"title\":\"المرتجعات\",\"module\":\"المرتجعات\"}]}");
                var password=Guid.NewGuid().ToString("N");
                await new SqliteUserStore(f.Cs).CreateAsync("return-hotfix-admin","مدير",new PasswordHasher().Hash(password),UserRoles.Administrator,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    var address=new Uri("http://127.0.0.1:"+port);server.Start(new HostOptions { DatabasePath=f.Db.DatabasePath,FrontendPath=frontend,Url=address.ToString().TrimEnd('/') });
                    using(var admin=Client(address))
                    {
                        var csrf=await Login(admin,"return-hotfix-admin",password);
                        var route=unified?"api/returns":"api/sales/"+sale.Id+"/returns";
                        for(var i=0;i<3;i++)
                        {
                            var cmd=new { type="sales",invoiceId=sale.Id,operationKey="http-hotfix-"+i,reason="إرجاع",lines=new[] { new { invoiceLineId=sale.Lines.Single().Id,quantity="1" } } };
                            var response=await Send(admin,HttpMethod.Post,route,cmd,csrf);
                            Assert.Equal(unified?HttpStatusCode.Created:HttpStatusCode.OK,response.StatusCode);
                            var data=JObject.Parse(await response.Content.ReadAsStringAsync())["data"];
                            var replay=await Send(admin,HttpMethod.Post,route,cmd,csrf);Assert.Equal(response.StatusCode,replay.StatusCode);
                            var again=JObject.Parse(await replay.Content.ReadAsStringAsync())["data"];
                            Assert.Equal((long)data[unified?"id":"returnId"],(long)again[unified?"id":"returnId"]);
                        }
                        var over=await Send(admin,HttpMethod.Post,route,new { type="sales",invoiceId=sale.Id,operationKey="http-over",reason="زيادة",lines=new[] { new { invoiceLineId=sale.Lines.Single().Id,quantity="1" } } },csrf);
                        Assert.Equal(HttpStatusCode.Conflict,over.StatusCode);
                        Assert.Contains("return_quantity_exceeded",await over.Content.ReadAsStringAsync());
                        using(var c=f.Db.Open()) { Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));Assert.Equal(299700,c.ExecuteScalar<long>("SELECT SUM(TotalScaled) FROM SalesReturns"));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements")); }
                    }
                }
            }
        }
    }
}
