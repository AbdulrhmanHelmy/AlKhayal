using System.Web.Http; using Newtonsoft.Json; using Newtonsoft.Json.Serialization;
namespace AlKhayal.Api
{
    public static class ApiBootstrap
    {
        public static HttpConfiguration CreateConfiguration(System.Func<System.Type, object> resolver)
        { var config=new HttpConfiguration(); config.MapHttpAttributeRoutes(); config.DependencyResolver=new SimpleResolver(resolver); config.Formatters.JsonFormatter.SerializerSettings.ContractResolver=new CamelCasePropertyNamesContractResolver(); config.Formatters.JsonFormatter.SerializerSettings.NullValueHandling=NullValueHandling.Ignore; config.IncludeErrorDetailPolicy=IncludeErrorDetailPolicy.LocalOnly; return config; }
    }
    internal sealed class SimpleResolver : System.Web.Http.Dependencies.IDependencyResolver { private readonly System.Func<System.Type,object> _get; public SimpleResolver(System.Func<System.Type,object> get){_get=get;} public object GetService(System.Type t){var value=_get(t); if(value!=null)return value; if(t==typeof(Controllers.AuthController)) return new Controllers.AuthController((Application.Authentication.AuthService)_get(typeof(Application.Authentication.AuthService))); if(t==typeof(Controllers.SystemController)) return new Controllers.SystemController(); return null;} public System.Collections.Generic.IEnumerable<object> GetServices(System.Type t){return new object[0];} public System.Web.Http.Dependencies.IDependencyScope BeginScope(){return this;} public void Dispose(){} }
}
