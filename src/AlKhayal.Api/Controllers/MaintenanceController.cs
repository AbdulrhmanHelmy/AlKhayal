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
using AlKhayal.Application.Maintenance;
using AlKhayal.Application.Customers;
using AlKhayal.Application.Users;
using AlKhayal.Core.Models;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/maintenance/tickets")]
    public sealed class MaintenanceController : ApiController
    {
        private const string CookieName="alkhayal_session";
        private readonly MaintenanceService _maintenance;private readonly AuthService _auth;private readonly UserManagementService _users;private readonly FeatureService _features;
        public MaintenanceController(MaintenanceService maintenance,AuthService auth,UserManagementService users,FeatureService features){_maintenance=maintenance;_auth=auth;_users=users;_features=features;}

        [HttpGet,Route("")]
        public async Task<IHttpActionResult> List(long? customerId=null,CancellationToken ct=default(CancellationToken))
        {
            if(!await IsAuthenticated(ct))return UnauthorizedResult();if(!await CanUse("maintenance-tracking",ct))return Denied();
            try{return Ok(ApiEnvelope<object>.Ok(_maintenance.List(customerId)));}catch(ArgumentException ex){return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message));}
        }

        [HttpPost,Route("")]
        public async Task<IHttpActionResult> Create(MaintenanceTicketRequest request,CancellationToken ct)
        {
            if(!await IsAuthenticated(ct))return UnauthorizedResult();if(!await CanUse("maintenance-intake",ct))return Denied();if(!await HasValidCsrf(ct))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            try{return Content(HttpStatusCode.Created,ApiEnvelope<MaintenanceTicket>.Ok(_maintenance.Create(request==null?null:new MaintenanceTicketCommand{CustomerId=request.CustomerId,Phone=request.Phone,Device=request.Device,Model=request.Model,Serial=request.Serial,Accessories=request.Accessories,Issue=request.Issue,Technician=request.Technician,EstimatedCost=request.EstimatedCost,AdvancePaid=request.AdvancePaid,Notes=request.Notes,ReceivedOn=request.ReceivedOn,ExpectedOn=request.ExpectedOn,OperationKey=request.OperationKey})));}
            catch(ArgumentException ex){return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message));}catch(CustomerOperationKeyConflictException ex){return Content(HttpStatusCode.Conflict,ApiEnvelope<object>.Fail("operation_key_conflict",ex.Message));}catch(KeyNotFoundException ex){return Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found",ex.Message));}catch(OverflowException){return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation","المبلغ يتجاوز الحد المسموح."));}catch(System.Data.SQLite.SQLiteException ex){System.Diagnostics.Trace.TraceError("Maintenance intake failed: SQLite code {0}",ex.ResultCode);return Content(HttpStatusCode.ServiceUnavailable,ApiEnvelope<object>.Fail("database_unavailable","تعذر حفظ تذكرة الصيانة."));}
        }

        [HttpPut,Route("{id:long}/status")]
        public async Task<IHttpActionResult> UpdateStatus(long id,MaintenanceStatusRequest request,CancellationToken ct)
        {
            if(!await IsAuthenticated(ct))return UnauthorizedResult();if(!await CanUse("maintenance-tracking",ct))return Denied();if(!await HasValidCsrf(ct))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            try{return Ok(ApiEnvelope<MaintenanceTicket>.Ok(_maintenance.UpdateStatus(id,request==null?null:request.Status,request==null?null:request.OperationKey)));}catch(ArgumentException ex){return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message));}catch(CustomerOperationKeyConflictException ex){return Content(HttpStatusCode.Conflict,ApiEnvelope<object>.Fail("operation_key_conflict",ex.Message));}catch(KeyNotFoundException ex){return Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found",ex.Message));}catch(System.Data.SQLite.SQLiteException ex){System.Diagnostics.Trace.TraceError("Maintenance status failed: SQLite code {0}",ex.ResultCode);return Content(HttpStatusCode.ServiceUnavailable,ApiEnvelope<object>.Fail("database_unavailable","تعذر حفظ حالة الصيانة."));}
        }

        [HttpPost,Route("{id:long}/deliver")]
        public async Task<IHttpActionResult> Deliver(long id,MaintenanceDeliveryRequest request,CancellationToken ct)
        {
            if(!await IsAuthenticated(ct))return UnauthorizedResult();if(!await CanUse("maintenance-tracking",ct))return Denied();if(!await HasValidCsrf(ct))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."));
            try{return Ok(ApiEnvelope<MaintenanceTicket>.Ok(_maintenance.Deliver(id,request==null?0:request.FinalCost,request==null?null:request.SettlementType,request==null?null:request.DeliveredOn,request==null?null:request.OperationKey)));}catch(ArgumentException ex){return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message));}catch(CustomerOperationKeyConflictException ex){return Content(HttpStatusCode.Conflict,ApiEnvelope<object>.Fail("operation_key_conflict",ex.Message));}catch(KeyNotFoundException ex){return Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found",ex.Message));}catch(OverflowException){return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation","المبلغ يتجاوز الحد المسموح."));}catch(System.Data.SQLite.SQLiteException ex){System.Diagnostics.Trace.TraceError("Maintenance delivery failed: SQLite code {0}",ex.ResultCode);return Content(HttpStatusCode.ServiceUnavailable,ApiEnvelope<object>.Fail("database_unavailable","تعذر تأكيد التسليم. أعد المحاولة بالمفتاح نفسه."));}
        }

        private async Task<bool> CanUse(string screen,CancellationToken ct){var user=await _auth.GetCurrentUserAsync(ReadCookie(),ct).ConfigureAwait(false);if(user==null)return false;if(user.Role==UserRoles.Administrator)return true;var access=await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);return access.ScreenIds.Contains(screen,StringComparer.OrdinalIgnoreCase);}
        private async Task<bool> HasValidCsrf(CancellationToken ct){return await _auth.ValidateCsrfAsync(ReadCookie(),ReadCsrf(),ct).ConfigureAwait(false);}
        private async Task<bool> IsAuthenticated(CancellationToken ct){return await _auth.GetCurrentUserAsync(ReadCookie(),ct).ConfigureAwait(false)!=null;}
        private IHttpActionResult Denied(){return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية شاشة الصيانة."));}
        private IHttpActionResult UnauthorizedResult(){return Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولاً."));}
        private string ReadCookie(){System.Collections.Generic.IEnumerable<string> values;if(!Request.Headers.TryGetValues("Cookie",out values))return null;foreach(var header in values)foreach(var part in header.Split(';')){var pair=part.Trim().Split(new[]{'='},2);if(pair.Length==2&&pair[0]==CookieName)return pair[1];}return null;}
        private string ReadCsrf(){System.Collections.Generic.IEnumerable<string> values;return Request.Headers.TryGetValues("X-CSRF-Token",out values)?values.FirstOrDefault():null;}
    }
}
