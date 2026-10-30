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
        public async Task Payment_methods_http_checkout_uses_server_total_and_rejects_partial_split_and_forged_instruments()
        {
            using(var sales=new SalesTreasuryTests.Fixture()){
                await sales.Prepare();var f=sales.Db;
                var frontend=Path.Combine(Path.GetDirectoryName(f.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[]}");
                var password=Guid.NewGuid().ToString("N");await new SqliteUserStore(f.ConnectionString).CreateAsync("checkout-admin","مدير",new PasswordHasher().Hash(password),UserRoles.Administrator,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer(new TestLicenseGate())){
                    var url=new Uri("http://127.0.0.1:"+port);server.Start(new HostOptions{DatabasePath=f.DatabasePath,FrontendPath=frontend,Url=url.ToString().TrimEnd('/')});
                    using(var admin=Client(url)){
                        var csrf=await Login(admin,"checkout-admin",password);
                        var instrument=await Send(admin,HttpMethod.Post,"api/payment-methods/instruments",new{type="wallet",name="محفظة",number="00123",isActive=true},csrf);Assert.Equal(HttpStatusCode.OK,instrument.StatusCode);
                        var instrumentId=(long)JObject.Parse(await instrument.Content.ReadAsStringAsync())["data"]["id"];
                        var draft=new JObject{["warehouseId"]=1,["invoiceDate"]="2026-10-28",["paymentMode"]="cash",["immediateMethod"]="wallet",["paymentInstrumentId"]=instrumentId,["discount"]="1.00",["totalScaled"]=1,
                            ["lines"]=JArray.FromObject(new[]{new{variantId=sales.Variant,productUnitId=sales.Unit,quantity="1",unitPrice="12.34"}})};
                        draft["paymentAmount"]="1.00";Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,HttpMethod.Post,"api/sales/drafts",draft,csrf)).StatusCode);draft.Remove("paymentAmount");
                        draft["payments"]=JArray.FromObject(new[]{new{method="cash",amount="1.00"},new{method="wallet",amount="10.34"}});Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,HttpMethod.Post,"api/sales/drafts",draft,csrf)).StatusCode);draft.Remove("payments");
                        draft["immediateMethod"]="card";Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,HttpMethod.Post,"api/sales/drafts",draft,csrf)).StatusCode);draft["immediateMethod"]="wallet";
                        var response=await Send(admin,HttpMethod.Post,"api/sales/drafts",draft,csrf);Assert.Equal(HttpStatusCode.Created,response.StatusCode);var saved=JObject.Parse(await response.Content.ReadAsStringAsync())["data"];var invoiceId=(long)saved["id"];Assert.Equal(113400,(long)saved["totalScaled"]);
                        response=await Send(admin,HttpMethod.Post,"api/sales/"+invoiceId+"/post",new{operationKey="http-wallet-post"},csrf);Assert.Equal(HttpStatusCode.OK,response.StatusCode);saved=JObject.Parse(await response.Content.ReadAsStringAsync())["data"];Assert.Equal(113400,(long)saved["paidScaled"]);Assert.Equal(instrumentId,(long)saved["paymentInstrumentId"]);
                        Assert.Equal(HttpStatusCode.OK,(await Send(admin,HttpMethod.Post,"api/sales/"+invoiceId+"/post",new{operationKey="http-wallet-post"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,HttpMethod.Post,"api/sales/"+invoiceId+"/payments",new{amount="1",method="card",operationKey="extra-payment"},csrf)).StatusCode);
                        var history=JObject.Parse(await admin.GetStringAsync("api/payment-methods/history?instrumentId="+instrumentId))["data"];Assert.Equal(1,(long)history["count"]);Assert.Equal(113400,(long)history["totalScaled"]);Assert.Equal((string)saved["invoiceNumber"],(string)history["items"][0]["invoiceNumber"]);
                    }
                }
            }
        }

        [Fact]
        public async Task Payment_methods_http_admin_csrf_cashier_effective_screen_access_and_no_unrelated_feature_gate()
        {
            using(var f=new TreasuryPersistenceTests.Fixture()){
                var frontend=Path.Combine(Path.GetDirectoryName(f.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"pos\",\"title\":\"الكاشير\",\"module\":\"المبيعات\"},{\"route\":\"payment-wallets\",\"title\":\"المحافظ\",\"module\":\"وسائل الدفع\",\"administratorOnly\":true}]}");
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.ConnectionString);var hasher=new PasswordHasher();
                await users.CreateAsync("payment-admin","مدير",hasher.Hash(password),UserRoles.Administrator,CancellationToken.None);
                var cashierId=await users.CreateAsync("payment-cashier","كاشير",hasher.Hash(password),UserRoles.Cashier,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer(new TestLicenseGate())){
                    var url=new Uri("http://127.0.0.1:"+port);server.Start(new HostOptions{DatabasePath=f.DatabasePath,FrontendPath=frontend,Url=url.ToString().TrimEnd('/')});
                    using(var admin=Client(url))using(var cashier=Client(url))using(var anon=Client(url)){
                        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync("api/payment-methods/active")).StatusCode);
                        var csrf=await Login(admin,"payment-admin",password);var cashierCsrf=await Login(cashier,"payment-cashier",password);
                        var command=new{type="wallet",name="محفظة",number="00123",provider="مقدم",isActive=true};
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,HttpMethod.Post,"api/payment-methods/instruments",command,null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/payment-methods/active")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/payment-methods/instruments")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/payment-methods/history")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/payment-methods/overview")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Post,"api/payment-methods/instruments",command,cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(admin,HttpMethod.Put,"api/users/"+cashierId+"/screen-permissions",new{screenIds=new[]{"pos"}},csrf)).StatusCode);
                        var response=await Send(admin,HttpMethod.Post,"api/payment-methods/instruments",command,csrf);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                        var saved=JObject.Parse(await response.Content.ReadAsStringAsync())["data"];var id=(long)saved["id"];Assert.Equal("00123",(string)saved["number"]);
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,HttpMethod.Post,"api/payment-methods/instruments",command,csrf)).StatusCode);
                        var active=(JArray)JObject.Parse(await cashier.GetStringAsync("api/payment-methods/active"))["data"];Assert.Single(active);Assert.Null(active[0]["notes"]);Assert.Equal("00123",(string)active[0]["number"]);
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Put,"api/payment-methods/instruments/"+id,command,cashierCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(admin,HttpMethod.Put,"api/payment-methods/instruments/"+id,new{type="wallet",name="معدلة",number="00123",provider="مقدم",isActive=false,version=1},csrf)).StatusCode);
                        Assert.Empty((JArray)JObject.Parse(await cashier.GetStringAsync("api/payment-methods/active"))["data"]);
                        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("api/payment-methods/history?page=0")).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync("api/payment-methods/history")).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync("api/payment-methods/overview")).StatusCode);
                    }
                }
            }
        }
    }
}
