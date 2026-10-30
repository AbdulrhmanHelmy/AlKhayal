using System;
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
using AlKhayal.Core.Models;
using AlKhayal.Application.Payments;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/payment-methods")]
    public sealed class PaymentMethodsController : ApiController
    {
        private readonly PaymentInstrumentService _payments;private readonly AuthService _auth;private readonly UserManagementService _users;private readonly FeatureService _features;
        public PaymentMethodsController(PaymentInstrumentService payments,AuthService auth,UserManagementService users,FeatureService features){_payments=payments;_auth=auth;_users=users;_features=features;}
        [HttpGet,Route("active")] public async Task<IHttpActionResult> Active(CancellationToken ct) {var a=await Require(ct,false,true).ConfigureAwait(false);if(a.Error!=null)return a.Error;var rows=await _payments.ListAsync(null,true,ct).ConfigureAwait(false);return Ok(ApiEnvelope<object>.Ok(rows.Select(x=>new{x.Id,x.Type,x.Name,x.Number,x.Provider})));}
        [HttpGet,Route("instruments")] public async Task<IHttpActionResult> List(CancellationToken ct,string type=null) {var a=await Require(ct,false).ConfigureAwait(false);if(a.Error!=null)return a.Error;try{return Ok(ApiEnvelope<object>.Ok(await _payments.ListAsync(type,false,ct).ConfigureAwait(false)));}catch(ArgumentException ex){return Bad(ex.Message);}}
        [HttpPost,Route("instruments")] public Task<IHttpActionResult> Create(PaymentInstrument value,CancellationToken ct){return Save(0,value,ct);}
        [HttpPut,Route("instruments/{id:long}")] public Task<IHttpActionResult> Update(long id,PaymentInstrument value,CancellationToken ct){return Save(id,value,ct);}
        private async Task<IHttpActionResult> Save(long id,PaymentInstrument value,CancellationToken ct){var a=await Require(ct,true).ConfigureAwait(false);if(a.Error!=null)return a.Error;if(value==null||!ModelState.IsValid)return Bad("أدخل بيانات الوسيلة بشكل صحيح.");value.Id=id;try{return Ok(ApiEnvelope<object>.Ok(await _payments.SaveAsync(value,a.User.Id,ct).ConfigureAwait(false)));}catch(ArgumentException ex){return Bad(ex.Message);}catch(InvalidOperationException ex){return Content(HttpStatusCode.Conflict,ApiEnvelope<object>.Fail("version_conflict",ex.Message));}}
        [HttpGet,Route("history")] public async Task<IHttpActionResult> History(CancellationToken ct,long? instrumentId=null,long? cashierId=null,string cashierName=null,string from=null,string to=null,string invoiceNumber=null,string status=null,int page=1,int pageSize=20){var a=await Require(ct,false).ConfigureAwait(false);if(a.Error!=null)return a.Error;try{return Ok(ApiEnvelope<object>.Ok(await _payments.HistoryAsync(new PaymentHistoryQuery{InstrumentId=instrumentId,CashierId=cashierId,CashierName=cashierName,From=from,To=to,InvoiceNumber=invoiceNumber,Status=status,Page=page,PageSize=pageSize},ct).ConfigureAwait(false)));}catch(ArgumentException ex){return Bad(ex.Message);}}
        [HttpGet,Route("overview")] public async Task<IHttpActionResult> Overview(CancellationToken ct){var a=await Require(ct,false).ConfigureAwait(false);if(a.Error!=null)return a.Error;return Ok(ApiEnvelope<object>.Ok(await _payments.OverviewAsync(ct).ConfigureAwait(false)));}
        private IHttpActionResult Bad(string message){return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",message));}
        private async Task<AuthCheck> Require(CancellationToken ct,bool write,bool checkout=false){var token=ReadCookie();var user=await _auth.GetCurrentUserAsync(token,ct).ConfigureAwait(false);if(user==null)return new AuthCheck{Error=Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولًا."))};if(user.Role!=UserRoles.Administrator){if(!checkout)return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("administrator_required","إدارة وسائل الدفع متاحة للمدير فقط."))};var access=await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);if(!new[]{"pos","sales-management","sales-list","sales-returns"}.Any(x=>access.ScreenIds.Contains(x,StringComparer.OrdinalIgnoreCase)))return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية شاشة المبيعات."))};}if(write&&!await _auth.ValidateCsrfAsync(token,ReadHeader("X-CSRF-Token"),ct).ConfigureAwait(false))return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."))};return new AuthCheck{User=user};}
        private string ReadCookie(){IEnumerable<string> values;if(!Request.Headers.TryGetValues("Cookie",out values))return null;foreach(var header in values)foreach(var part in header.Split(';')){var pair=part.Trim().Split(new[]{'='},2);if(pair.Length==2&&pair[0]=="alkhayal_session")return pair[1];}return null;}private string ReadHeader(string n){IEnumerable<string> values;return Request.Headers.TryGetValues(n,out values)?values.FirstOrDefault():null;}
        private sealed class AuthCheck{public UserIdentity User{get;set;}public IHttpActionResult Error{get;set;}}
    }
}
