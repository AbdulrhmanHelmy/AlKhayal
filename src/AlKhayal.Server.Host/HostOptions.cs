using System; using System.IO;
namespace AlKhayal.Server.Host
{
    public sealed class HostOptions
    {
        public HostOptions()
        {
            Url = Read("ALKHAYAL_SERVER_URL", "http://127.0.0.1:5137");
            DatabasePath = Read("ALKHAYAL_DATABASE_PATH", AlKhayal.Data.Database.Db.DefaultDatabasePath());
            FrontendPath = Read("ALKHAYAL_FRONTEND_PATH", Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..","..","frontend")));
        }
        public string Url { get; set; }
        public string DatabasePath { get; set; }
        public string FrontendPath { get; set; }
        public bool IsHttps { get { return Url.StartsWith("https://",StringComparison.OrdinalIgnoreCase); } }
        private static string Read(string name,string fallback) { var value=Environment.GetEnvironmentVariable(name); return string.IsNullOrWhiteSpace(value)?fallback:value; }
    }
}
