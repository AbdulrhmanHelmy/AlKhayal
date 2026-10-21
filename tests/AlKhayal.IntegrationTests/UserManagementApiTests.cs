using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Server.Host;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class UserManagementApiTests
    {
        [Fact]
        public async Task Admin_manages_users_cashier_is_forbidden_and_sessions_follow_account_state()
        {
            var root=Path.Combine(Path.GetTempPath(),"alkhayal-users-"+Guid.NewGuid().ToString("N"));var path=Path.Combine(root,"test.db");AlKhayalServer server=null;
            try
            {
                Directory.CreateDirectory(Path.Combine(root,"frontend","data"));File.WriteAllText(Path.Combine(root,"frontend","data","screens.json"),"{\"screens\":[{\"route\":\"dashboard\",\"title\":\"الرئيسية\",\"module\":\"الرئيسية\"},{\"route\":\"pos\",\"title\":\"نقطة البيع\",\"module\":\"المبيعات\"},{\"route\":\"users-list\",\"title\":\"المستخدمون\",\"module\":\"الإعدادات\",\"administratorOnly\":true}]}",Encoding.UTF8);
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var users=new SqliteUserStore(cs);var hasher=new PasswordHasher();await users.CreateAsync("admin","مسؤول",hasher.Hash("secret"),UserRoles.Administrator,CancellationToken.None);await users.CreateAsync("cashier","كاشير",hasher.Hash("secret"),UserRoles.Cashier,CancellationToken.None);
                var url="http://127.0.0.1:"+FreePort();server=new AlKhayalServer();server.Start(new HostOptions{DatabasePath=path,Url=url,FrontendPath=Path.Combine(root,"frontend")});
                using(var anonymous=Client(url))
                {
                    Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("api/users")).StatusCode);
                    var registryResponse=await anonymous.GetAsync("data/screens.json");Assert.Equal(HttpStatusCode.OK,registryResponse.StatusCode);Assert.Contains("no-store",string.Join(",",registryResponse.Headers.GetValues("Cache-Control")));
                }
                using(var cashier=Client(url))
                {
                    var cashierCsrf=await Login(cashier,"cashier","secret");Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/users")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Put(cashier,cashierCsrf,"api/users/2/screen-permissions","{\"screenIds\":[\"dashboard\"]}")).StatusCode);
                }
                long createdId;
                using(var admin=Client(url))
                {
                    var csrf=await Login(admin,"admin","secret");var list=await admin.GetStringAsync("api/users");Assert.DoesNotContain("passwordHash",list,StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(HttpStatusCode.Forbidden,(await admin.PostAsync("api/users",Json("{\"username\":\"no-csrf\",\"displayName\":\"x\",\"role\":\"cashier\",\"password\":\"x\",\"isActive\":true}"))).StatusCode);
                    var created=await Post(admin,csrf,"api/users","{\"username\":\"newcashier\",\"displayName\":\"كاشير جديد\",\"role\":\"cashier\",\"password\":\"new-secret\",\"isActive\":true}");Assert.Equal(HttpStatusCode.Created,created.StatusCode);var text=await created.Content.ReadAsStringAsync();createdId=long.Parse(Regex.Match(text,"\\\"id\\\":(\\d+)").Groups[1].Value);
                    Assert.Equal(HttpStatusCode.Conflict,(await Post(admin,csrf,"api/users","{\"username\":\"NEWCASHIER\",\"displayName\":\"مكرر\",\"role\":\"cashier\",\"password\":\"x\",\"isActive\":true}")).StatusCode);
                    Assert.Equal(HttpStatusCode.BadRequest,(await Put(admin,csrf,"api/users/"+createdId+"/screen-permissions","{\"screenIds\":[\"users-list\"]}")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK,(await Put(admin,csrf,"api/users/"+createdId+"/screen-permissions","{\"screenIds\":[\"dashboard\",\"pos\"]}")).StatusCode);
                    Assert.Equal(HttpStatusCode.Conflict,(await Put(admin,csrf,"api/users/1/status","{\"isActive\":false}")).StatusCode);
                }
                using(var managed=Client(url))
                {
                    await Login(managed,"newcashier","new-secret");var access=await managed.GetStringAsync("api/auth/me/screen-access");Assert.Contains("dashboard",access);Assert.Contains("pos",access);Assert.DoesNotContain("users-list",access);
                    using(var admin=Client(url)){var csrf=await Login(admin,"admin","secret");Assert.Equal(HttpStatusCode.OK,(await Put(admin,csrf,"api/users/"+createdId+"/status","{\"isActive\":false}")).StatusCode);}
                    Assert.Equal(HttpStatusCode.Unauthorized,(await managed.GetAsync("api/auth/me")).StatusCode);
                }
                using(var admin=Client(url))
                {
                    var csrf=await Login(admin,"admin","secret");await Put(admin,csrf,"api/users/"+createdId+"/status","{\"isActive\":true}");Assert.Equal(HttpStatusCode.OK,(await Post(admin,csrf,"api/users/"+createdId+"/reset-password","{\"password\":\"changed\"}")).StatusCode);
                }
                using(var oldPassword=Client(url))Assert.Equal(HttpStatusCode.Unauthorized,(await oldPassword.PostAsync("api/auth/login",Json("{\"username\":\"newcashier\",\"password\":\"new-secret\"}"))).StatusCode);
                using(var newPassword=Client(url))await Login(newPassword,"newcashier","changed");
            }
            finally { if(server!=null)server.Dispose();System.Data.SQLite.SQLiteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true); }
        }

        private static HttpClient Client(string url){return new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=new Uri(url)};}
        private static StringContent Json(string json){return new StringContent(json,Encoding.UTF8,"application/json");}
        private static async Task<string> Login(HttpClient client,string username,string password){var response=await client.PostAsync("api/auth/login",Json("{\"username\":\""+username+"\",\"password\":\""+password+"\"}"));Assert.Equal(HttpStatusCode.OK,response.StatusCode);return Regex.Match(await response.Content.ReadAsStringAsync(),"\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value;}
        private static Task<HttpResponseMessage> Put(HttpClient client,string csrf,string path,string body){return Send(client,csrf,HttpMethod.Put,path,body);}
        private static Task<HttpResponseMessage> Post(HttpClient client,string csrf,string path,string body){return Send(client,csrf,HttpMethod.Post,path,body);}
        private static Task<HttpResponseMessage> Send(HttpClient client,string csrf,HttpMethod method,string path,string body){var request=new HttpRequestMessage(method,path){Content=Json(body)};request.Headers.Add("X-CSRF-Token",csrf);return client.SendAsync(request);}
        private static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    }
}
