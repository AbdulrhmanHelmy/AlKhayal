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
        public async Task Supplier_account_debt_invoice_http_replay_payment_and_restart_read_are_persisted()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var frontend=Path.Combine(Path.GetDirectoryName(f.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"supplier-payment\",\"title\":\"حسابات الموردين\",\"module\":\"الموردين\"}]}");
                var password=Guid.NewGuid().ToString("N");
                await new SqliteUserStore(f.ConnectionString).CreateAsync("account-owner","مسؤول",new PasswordHasher().Hash(password),UserRoles.Administrator,CancellationToken.None);
                var cashierId=await new SqliteUserStore(f.ConnectionString).CreateAsync("supplier-cashier","كاشير",new PasswordHasher().Hash(password),UserRoles.Cashier,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    server.Start(new HostOptions{DatabasePath=f.DatabasePath,FrontendPath=frontend,Url="http://127.0.0.1:"+port});
                    using(var client=Client(new Uri("http://127.0.0.1:"+port)))
                    {
                        var csrf=await Login(client,"account-owner",password);
                        var treasuryAccountId=await FundSupplierPaymentAccount(f.ConnectionString);
                        var created=await Send(client,HttpMethod.Post,"api/suppliers",new{code="ACCOUNT-SUP",name="مورد حسابات",phone="010",status="نشط"},csrf);
                        Assert.Equal(HttpStatusCode.Created,created.StatusCode);var supplier=(long)JObject.Parse(await created.Content.ReadAsStringAsync())["data"]["id"];
                        var body=new{operationKey="http-debt",invoiceDate="2026-10-28",total="12.34",notes="فاتورة مديونية جديدة"};
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(client,HttpMethod.Post,"api/suppliers/"+supplier+"/invoices",body,null)).StatusCode);
                        var invoice=await Send(client,HttpMethod.Post,"api/suppliers/"+supplier+"/invoices",body,csrf);
                        Assert.Equal(HttpStatusCode.Created,invoice.StatusCode);var invoiceId=(long)JObject.Parse(await invoice.Content.ReadAsStringAsync())["data"]["id"];
                        var retry=await Send(client,HttpMethod.Post,"api/suppliers/"+supplier+"/invoices",body,csrf);
                        Assert.Equal(HttpStatusCode.Created,retry.StatusCode);Assert.Contains("\"replayed\":true",await retry.Content.ReadAsStringAsync());
                        Assert.Equal(HttpStatusCode.Conflict,(await Send(client,HttpMethod.Post,"api/suppliers/"+supplier+"/invoices",new{operationKey="http-debt",invoiceDate="2026-10-28",total="12.35",notes="فاتورة مديونية جديدة"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,HttpMethod.Post,"api/suppliers/"+supplier+"/payments",new{operationKey="http-debt-pay",invoiceId,amount="2.34",paymentDate="2026-10-28",method="نقدي"},csrf)).StatusCode);
                        var paymentBody=new{operationKey="http-debt-pay",invoiceId,amount="2.34",paymentDate="2026-10-28",method="نقدي",treasuryAccountId};
                        var payment=await Send(client,HttpMethod.Post,"api/suppliers/"+supplier+"/payments",paymentBody,csrf);
                        Assert.Equal(HttpStatusCode.Created,payment.StatusCode);Assert.NotNull(JObject.Parse(await payment.Content.ReadAsStringAsync())["data"]["treasuryMovementId"]);
                        var paymentReplay=await Send(client,HttpMethod.Post,"api/suppliers/"+supplier+"/payments",paymentBody,csrf);Assert.Equal(HttpStatusCode.Created,paymentReplay.StatusCode);Assert.Contains("\"replayed\":true",await paymentReplay.Content.ReadAsStringAsync());
                        Assert.Contains("\"balance\":\"97.66\"",await client.GetStringAsync("api/treasury/accounts"));
                        var invoices=JObject.Parse(await client.GetStringAsync("api/suppliers/"+supplier+"/invoices"))["data"];
                        Assert.Single(invoices);Assert.Equal(10m,(decimal)invoices[0]["remaining"]);Assert.Equal("posted",(string)invoices[0]["documentStatus"]);
                        var restarted=new AlKhayal.Application.Suppliers.SupplierAccountService(new AlKhayal.Data.Suppliers.SqliteSupplierAccountRepository(f.ConnectionString));
                        Assert.Equal(10m,restarted.GetInvoices(supplier)[0].Remaining);
                        using(var cashier=Client(new Uri("http://127.0.0.1:"+port)))
                        {
                            var cashierCsrf=await Login(cashier,"supplier-cashier",password);
                            var onAccount=new{operationKey="cashier-on-account",amount="1.00",paymentDate="2026-10-28",method="تحويل",treasuryAccountId};
                            Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Post,"api/suppliers/"+supplier+"/payments",onAccount,cashierCsrf)).StatusCode);
                            Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Put,"api/users/"+cashierId+"/screen-permissions",new{screenIds=new[]{"supplier-payment"}},csrf)).StatusCode);
                            Assert.Equal(HttpStatusCode.OK,(await cashier.GetAsync("api/treasury/accounts")).StatusCode);
                            Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Post,"api/treasury/withdraw",new{accountId=treasuryAccountId,amount="1.00",operationKey="manual-denied"},cashierCsrf)).StatusCode);
                            Assert.Equal(HttpStatusCode.Created,(await Send(cashier,HttpMethod.Post,"api/suppliers/"+supplier+"/payments",onAccount,cashierCsrf)).StatusCode);
                            Assert.Equal(9m,(decimal)JObject.Parse(await client.GetStringAsync("api/suppliers/"+supplier+"/invoices"))["data"][0]["remaining"]);
                            Assert.Contains("\"balance\":\"96.66\"",await cashier.GetStringAsync("api/treasury/accounts"));
                        }
                    }
                }
            }
        }
        internal static async Task<long> FundSupplierPaymentAccount(string connectionString)
        {
            var treasury=new AlKhayal.Application.Treasury.TreasuryService(new AlKhayal.Data.Treasury.SqliteTreasuryStore(connectionString));
            var account=await treasury.SaveAsync(null,new AlKhayal.Application.Treasury.TreasuryAccountCommand{Code=Guid.NewGuid().ToString("N"),Name="حساب سداد المورد",Type="cash"},1,CancellationToken.None);
            await treasury.PostAsync(new AlKhayal.Application.Treasury.TreasuryMovementCommand{AccountId=account.Id,Amount="100",OperationKey=Guid.NewGuid().ToString("N")},"deposit",1,CancellationToken.None);return account.Id;
        }
    }
}
