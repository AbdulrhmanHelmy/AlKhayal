using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Features;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/auth")]
    public sealed class ScreenAccessController : ApiController
    {
        private readonly UserManagementService _users; private readonly FeatureService _features; private readonly AuthService _auth;
        public ScreenAccessController(UserManagementService users, FeatureService features, AuthService auth) { _users=users; _features=features; _auth=auth; }
        [HttpGet, Route("me/screen-access")]
        public async Task<IHttpActionResult> Get(CancellationToken ct) { var user=await _auth.GetCurrentUserAsync(ReadToken(),ct); if(user==null) return Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولاً.")); return Ok(ApiEnvelope<EffectiveScreenAccess>.Ok(await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct),ct))); }
        private string ReadToken() { IEnumerable<string> values; if(!Request.Headers.TryGetValues("Cookie",out values)) return null; foreach(var header in values) foreach(var part in header.Split(';')) { var pair=part.Trim().Split(new[]{'='},2); if(pair.Length==2 && pair[0]=="alkhayal_session") return pair[1]; } return null; }
    }
}
