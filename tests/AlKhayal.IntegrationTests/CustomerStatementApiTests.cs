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
        public async Task Customer_statement_http_collection_increase_csrf_permissions_and_replay_are_persisted()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var frontend=Path.Combine(Path.GetDirectoryName(f.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"customer-statement\",\"title\":\"كشف حساب العميل\",\"module\":\"العملاء\"}]}");
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.ConnectionString);var hasher=new PasswordHasher();
                await users.CreateAsync("customer-owner","مدير",hasher.Hash(password),UserRoles.Administrator,CancellationToken.None);
                var cashierId=await users.CreateAsync("customer-cashier","كاشير",hasher.Hash(password),UserRoles.Cashier,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer())
                {
                    var url=new Uri("http://127.0.0.1:"+port);server.Start(new HostOptions{DatabasePath=f.DatabasePath,FrontendPath=frontend,Url=url.ToString().TrimEnd('/')});
                    using(var admin=Client(url))using(var cashier=Client(url))using(var anon=Client(url))
                    {
                        var csrf=await Login(admin,"customer-owner",password);var cashierCsrf=await Login(cashier,"customer-cashier",password);
                        var create=await Send(admin,HttpMethod.Post,"api/customers",new{name="عميل كشف API",phone="010",balance=100,status="نشط"},csrf);Assert.Equal(HttpStatusCode.Created,create.StatusCode);
                        var id=(long)JObject.Parse(await create.Content.ReadAsStringAsync())["data"]["id"];
                        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync("api/customers/"+id+"/statement")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/customers/"+id+"/statement")).StatusCode);
                        var increase=new{type="increase",amount="150.50",occurredOn="2026-10-28",notes="إضافة موثقة",operationKey="statement-api-increase"};
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,HttpMethod.Post,"api/customers/"+id+"/transactions",increase,null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Post,"api/customers/"+id+"/transactions",increase,cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(admin,HttpMethod.Put,"api/users/"+cashierId+"/screen-permissions",new{screenIds=new[]{"customer-statement"}},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Created,(await Send(cashier,HttpMethod.Post,"api/customers/"+id+"/transactions",increase,cashierCsrf)).StatusCode);
                        var replay=await Send(cashier,HttpMethod.Post,"api/customers/"+id+"/transactions",increase,cashierCsrf);Assert.Equal(HttpStatusCode.Created,replay.StatusCode);Assert.Contains("\"replayed\":true",await replay.Content.ReadAsStringAsync());
                        Assert.Equal(HttpStatusCode.Created,(await Send(cashier,HttpMethod.Post,"api/customers/"+id+"/transactions",new{type="collection",amount="50.25",occurredOn="2026-10-28",notes="تحصيل موثق",operationKey="statement-api-collection"},cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(cashier,HttpMethod.Post,"api/customers/"+id+"/transactions",new{type="collection",amount="201",occurredOn="2026-10-28",operationKey="statement-api-overpay"},cashierCsrf)).StatusCode);
                        var result=JObject.Parse(await cashier.GetStringAsync("api/customers/"+id+"/statement"))["data"];
                        Assert.Equal(200.25m,(decimal)result["currentBalance"]);Assert.Equal(200.25m,(decimal)result["closingBalance"]);
                        Assert.Contains("إضافة موثقة",result.ToString());Assert.Contains("تحصيل موثق",result.ToString());
                        var restarted=new AlKhayal.Application.Customers.CustomerAccountService(new AlKhayal.Data.Customers.SqliteCustomerAccountRepository(f.ConnectionString));Assert.Equal(200.25m,restarted.GetStatement(id,null,null).ClosingBalance);
                    }
                }
            }
        }
    }
}
