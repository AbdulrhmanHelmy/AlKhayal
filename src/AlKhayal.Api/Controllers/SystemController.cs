using System; using System.Web.Http; using AlKhayal.Api.Contracts;
namespace AlKhayal.Api.Controllers { [RoutePrefix("api/system")] public sealed class SystemController : ApiController { [HttpGet, Route("health")] public IHttpActionResult Health() { return Ok(ApiEnvelope<object>.Ok(new { status="ok", utc=DateTime.UtcNow.ToString("o"), version="0.1.0" })); } } }
