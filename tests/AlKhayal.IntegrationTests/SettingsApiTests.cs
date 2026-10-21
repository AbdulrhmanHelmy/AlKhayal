using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Server.Host;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SettingsApiTests
    {
        [Fact]
        public async Task Settings_api_authenticates_authorizes_validates_and_persists_after_restart()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");AlKhayalServer server=null;
            try{var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var users=new SqliteUserStore(cs);var hasher=new PasswordHasher();await users.CreateAsync("settings-api-admin","مسؤول",hasher.Hash("secret"),"administrator",CancellationToken.None);await users.CreateAsync("settings-api-cashier","كاشير",hasher.Hash("secret"),"cashier",CancellationToken.None);var url="http://127.0.0.1:"+FreePort();server=Start(path,url);
                using(var anonymous=Client(url)){Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("api/settings/general")).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("api/settings/database-info")).StatusCode);}
                using(var cashier=Client(url)){var csrf=await Login(cashier,"settings-api-cashier");Assert.Equal(HttpStatusCode.OK,(await cashier.GetAsync("api/settings/general")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/settings/database-info")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Put(cashier,csrf,"company","{\"businessName\":\"غير مسموح\"}")).StatusCode);}
                using(var admin=Client(url)){var csrf=await Login(admin,"settings-api-admin");Assert.Equal(HttpStatusCode.Forbidden,(await admin.PutAsync("api/settings/general",Json("{\"languageCode\":\"ar\"}"))).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await Put(admin,csrf,"company","{\"businessName\":\"\",\"email\":\"bad\"}")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await Put(admin,csrf,"general","{\"languageCode\":\"en\",\"dateFormat\":\"x\",\"timeFormat\":\"x\"}")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await Put(admin,csrf,"company","{\"businessName\":\"مؤسسة الخيال\",\"email\":\"info@example.com\"}")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await Put(admin,csrf,"general","{\"languageCode\":\"ar\",\"dateFormat\":\"yyyy-MM-dd\",\"timeFormat\":\"HH:mm\",\"useGroupSeparators\":true,\"currencyCode\":\"USD\",\"currencySymbol\":\"$\",\"decimalPrecision\":2,\"taxEnabled\":true,\"defaultTaxRate\":10,\"pricesIncludeTax\":false}")).StatusCode);var info=await admin.GetStringAsync("api/settings/database-info");Assert.DoesNotContain("password",info,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("connectionString",info,StringComparison.OrdinalIgnoreCase);}
                server.Dispose();server=null;url="http://127.0.0.1:"+FreePort();server=Start(path,url);using(var restarted=Client(url)){await Login(restarted,"settings-api-admin");var company=await restarted.GetStringAsync("api/settings/company");var general=await restarted.GetStringAsync("api/settings/general");Assert.Contains("مؤسسة الخيال",company);Assert.Contains("\"currencyCode\":\"USD\"",general);}
            }finally{if(server!=null)server.Dispose();System.Data.SQLite.SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        private static AlKhayalServer Start(string path,string url){var server=new AlKhayalServer();server.Start(new HostOptions{DatabasePath=path,Url=url,FrontendPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"missing-frontend")});return server;}
        private static HttpClient Client(string url){return new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=new Uri(url)};}
        private static StringContent Json(string json){return new StringContent(json,Encoding.UTF8,"application/json");}
        private static async Task<string> Login(HttpClient client,string username){var response=await client.PostAsync("api/auth/login",Json("{\"username\":\""+username+"\",\"password\":\"secret\"}"));Assert.Equal(HttpStatusCode.OK,response.StatusCode);return Regex.Match(await response.Content.ReadAsStringAsync(),"\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value;}
        private static Task<HttpResponseMessage> Put(HttpClient client,string csrf,string path,string json){var request=new HttpRequestMessage(HttpMethod.Put,"api/settings/"+path){Content=Json(json)};request.Headers.Add("X-CSRF-Token",csrf);return client.SendAsync(request);}
        private static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    }
}
