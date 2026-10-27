using System; using System.IO; using System.Net; using System.Net.Http; using System.Net.Http.Headers; using System.Threading; using System.Threading.Tasks; using Xunit; using AlKhayal.Data.Authentication; using AlKhayal.Data.Database; using AlKhayal.Server.Host;
namespace AlKhayal.IntegrationTests
{
    public sealed class ApiEndpointTests
    {
        [Fact] public async Task Health_login_me_and_logout_use_real_owin_api()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db"); var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0); listener.Start(); var port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var url="http://127.0.0.1:"+port; AlKhayalServer server=null;
            try { var cs=Db.ConnectionStringFor(path); Migrator.Run(cs); var users=new SqliteUserStore(cs); await users.CreateAsync("api-user","مستخدم الواجهة",new PasswordHasher().Hash("secret"),"administrator",CancellationToken.None); server=new AlKhayalServer(); server.Start(new HostOptions{Url=url,DatabasePath=path,FrontendPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"missing-frontend")}); using(var client=new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=new Uri(url)}) { var health=await client.GetAsync("api/system/health"); Assert.Equal(HttpStatusCode.OK,health.StatusCode); var login=await client.PostAsync("api/auth/login",new StringContent("{\"username\":\"api-user\",\"password\":\"secret\"}",System.Text.Encoding.UTF8,"application/json")); Assert.Equal(HttpStatusCode.OK,login.StatusCode); var loginBody=await login.Content.ReadAsStringAsync(); var csrf=System.Text.RegularExpressions.Regex.Match(loginBody,"\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value; Assert.False(string.IsNullOrWhiteSpace(csrf)); var me=await client.GetAsync("api/auth/me"); Assert.Equal(HttpStatusCode.OK,me.StatusCode); Assert.Contains("api-user",await me.Content.ReadAsStringAsync()); var rejected=await client.PostAsync("api/auth/logout",new StringContent("{}",System.Text.Encoding.UTF8,"application/json")); Assert.Equal(HttpStatusCode.Forbidden,rejected.StatusCode); var logoutRequest=new HttpRequestMessage(HttpMethod.Post,"api/auth/logout"){Content=new StringContent("{}",System.Text.Encoding.UTF8,"application/json")}; logoutRequest.Headers.Add("X-CSRF-Token",csrf); var logout=await client.SendAsync(logoutRequest); Assert.Equal(HttpStatusCode.OK,logout.StatusCode); var after=await client.GetAsync("api/auth/me"); Assert.Equal(HttpStatusCode.Unauthorized,after.StatusCode); } }
            finally { if(server!=null)server.Dispose(); System.Data.SQLite.SQLiteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
        }
        [Fact] public async Task Touch_pos_browse_and_customer_search_routes_use_authenticated_disposable_host()
        {
            var path=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"alkhayal-pos-api-"+Guid.NewGuid().ToString("N")+".db"));
            var operatorPath=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AlKhayal","alkhayal.db"));
            Assert.False(string.Equals(path,operatorPath,StringComparison.OrdinalIgnoreCase));Assert.False(path.StartsWith(Path.GetDirectoryName(operatorPath)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
            var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();AlKhayalServer server=null;
            try
            {
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);await new SqliteUserStore(cs).CreateAsync("pos-api-user","مستخدم اختبار",new PasswordHasher().Hash("secret"),"administrator",CancellationToken.None);
                var url="http://127.0.0.1:"+port;server=new AlKhayalServer();server.Start(new HostOptions{Url=url,DatabasePath=path,FrontendPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"missing-frontend")});
                using(var client=new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=new Uri(url)})
                {
                    var anonymous=await client.GetAsync("api/inventory/products?posWarehouseId=1&page=1&pageSize=40");Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
                    var login=await client.PostAsync("api/auth/login",new StringContent("{\"username\":\"pos-api-user\",\"password\":\"secret\"}",System.Text.Encoding.UTF8,"application/json"));Assert.Equal(HttpStatusCode.OK,login.StatusCode);
                    var browse=await client.GetAsync("api/inventory/products?posWarehouseId=1&page=1&pageSize=40");Assert.Equal(HttpStatusCode.OK,browse.StatusCode);Assert.Contains("totalCount",await browse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("api/inventory/categories")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("api/customers/search?query=POS")).StatusCode);
                    var balancePage=await client.GetAsync("api/inventory/balance-page?warehouseId=1&search=test&page=1&pageSize=25");Assert.Equal(HttpStatusCode.OK,balancePage.StatusCode);Assert.Contains("totalCount",await balancePage.Content.ReadAsStringAsync());
                    var suppliers=await client.GetAsync("api/suppliers?search=test&page=1&pageSize=25");Assert.Equal(HttpStatusCode.OK,suppliers.StatusCode);Assert.Contains("totalCount",await suppliers.Content.ReadAsStringAsync());
                    var loginBody=await login.Content.ReadAsStringAsync();var csrf=System.Text.RegularExpressions.Regex.Match(loginBody,"\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value;
                    var create=new HttpRequestMessage(HttpMethod.Post,"api/customers"){Content=new StringContent("{\"name\":\"عميل جديد\",\"phone\":\"0100\"}",System.Text.Encoding.UTF8,"application/json")};create.Headers.Add("X-CSRF-Token",csrf);
                    var created=await client.SendAsync(create);Assert.Equal(HttpStatusCode.Created,created.StatusCode);Assert.Contains("CUS-001",await created.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("api/inventory/products?posWarehouseId=999&page=1")).StatusCode);
                }
            }
            finally{if(server!=null)server.Dispose();System.Data.SQLite.SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
    }
}
