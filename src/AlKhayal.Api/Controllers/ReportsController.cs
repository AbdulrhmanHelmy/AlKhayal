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
using AlKhayal.Application.Reports;
using AlKhayal.Application.Users;
using AlKhayal.Core.Models;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/reports")]
    public sealed class ReportsController : ApiController
    {
        private readonly OperationalReportService _operations;
        private readonly BusinessOverviewService _overview;
        private readonly SalesReportService _reports;
        private readonly TransactionReportService _transactions;
        private readonly NetProfitService _profit;
        private readonly StockReportService _stock;
        private readonly AuthService _auth;
        private readonly UserManagementService _users;
        private readonly FeatureService _features;
        public ReportsController(SalesReportService reports, TransactionReportService transactions, NetProfitService profit, AuthService auth, UserManagementService users, FeatureService features, StockReportService stock=null, OperationalReportService operations=null, BusinessOverviewService overview=null) { _operations=operations; _overview=overview; _stock=stock; _reports=reports; _transactions=transactions; _profit=profit; _auth=auth; _users=users; _features=features; }
        [HttpGet,Route("customers")]
        public Task<IHttpActionResult> Customers(CancellationToken ct,string from=null,string to=null,string search=null,long? personId=null,string status=null,int page=1,int pageSize=20)
        { return Operational("customers",new OperationalReportQuery{From=from,To=to,Search=search,PersonId=personId,Status=status,Page=page,PageSize=pageSize},ct); }
        [HttpGet,Route("suppliers")]
        public Task<IHttpActionResult> Suppliers(CancellationToken ct,string from=null,string to=null,string search=null,long? personId=null,string status=null,int page=1,int pageSize=20)
        { return Operational("suppliers",new OperationalReportQuery{From=from,To=to,Search=search,PersonId=personId,Status=status,Page=page,PageSize=pageSize},ct); }
        [HttpGet,Route("shifts")]
        public Task<IHttpActionResult> Shifts(CancellationToken ct,string from=null,string to=null,string search=null,long? personId=null,string status=null,int page=1,int pageSize=20)
        { return Operational("shifts",new OperationalReportQuery{From=from,To=to,Search=search,PersonId=personId,Status=status,Page=page,PageSize=pageSize},ct); }
        [HttpGet,Route("attendance")]
        public Task<IHttpActionResult> Attendance(CancellationToken ct,string from=null,string to=null,string search=null,long? personId=null,string status=null,int page=1,int pageSize=20)
        { return Operational("attendance",new OperationalReportQuery{From=from,To=to,Search=search,PersonId=personId,Status=status,Page=page,PageSize=pageSize},ct); }
        [HttpGet,Route("maintenance")]
        public Task<IHttpActionResult> Maintenance(CancellationToken ct,string from=null,string to=null,string search=null,long? personId=null,string status=null,int page=1,int pageSize=20)
        { return Operational("maintenance",new OperationalReportQuery{From=from,To=to,Search=search,PersonId=personId,Status=status,Page=page,PageSize=pageSize},ct); }
        [HttpGet,Route("installments")]
        public Task<IHttpActionResult> Installments(CancellationToken ct,string from=null,string to=null,string search=null,long? personId=null,string status=null,int page=1,int pageSize=20)
        { return Operational("installments",new OperationalReportQuery{From=from,To=to,Search=search,PersonId=personId,Status=status,Page=page,PageSize=pageSize},ct); }
        private async Task<bool> ModuleAvailable(string report,CancellationToken ct)
        {
            var key=OperationalReportService.Feature(report);
            if(key==null)return true;
            var features=await _features.GetEffectiveAsync(ct).ConfigureAwait(false);
            return features.Modules.Any(x=>x.Key==key && x.IsEnabled);
        }
        private async Task<IHttpActionResult> Operational(string report,OperationalReportQuery query,CancellationToken ct)
        {
            var denied=await Denied("report-"+report,ct).ConfigureAwait(false);
            if(denied!=null)return denied;
            if(!await ModuleAvailable(report,ct).ConfigureAwait(false))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("feature_disabled","الوحدة غير مفعلة."));
            if(!ModelState.IsValid)return Invalid();
            try { return Ok(ApiEnvelope<object>.Ok(await _operations.QueryAsync(report,query,ct).ConfigureAwait(false))); }
            catch(ArgumentException ex) { return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message)); }
        }
        [HttpGet,Route("overview")]
        public async Task<IHttpActionResult> Overview(CancellationToken ct,string from=null,string to=null)
        {
            var denied=await Denied("report-overview",ct).ConfigureAwait(false);
            if(denied!=null)return denied;
            if(!ModelState.IsValid)return Invalid();
            var allowed=new HashSet<string>(StringComparer.Ordinal);
            foreach(var report in new[]{"sales","returns","net-profit","payment-methods","customers","suppliers","maintenance","installments"})
                if(await Denied("report-"+report,ct).ConfigureAwait(false)==null && await ModuleAvailable(report,ct).ConfigureAwait(false))allowed.Add(report);
            try { return Ok(ApiEnvelope<object>.Ok(await _overview.QueryAsync(new ReportQuery{From=from,To=to},allowed,ct).ConfigureAwait(false))); }
            catch(ArgumentException ex) { return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message)); }
        }
        [HttpGet,Route("product-profit")]
        public Task<IHttpActionResult> ProductProfit(CancellationToken ct,string from=null,string to=null,string search=null,long? categoryId=null,long? warehouseId=null,string status=null,string type=null,string reference=null,string sort="quantity",int page=1,int pageSize=20)
        { return Stock("product-profit",new StockReportQuery{From=from,To=to,Search=search,CategoryId=categoryId,WarehouseId=warehouseId,Status=status,Type=type,Reference=reference,Sort=sort,Page=page,PageSize=pageSize},ct); }
        [HttpGet,Route("top-products")]
        public Task<IHttpActionResult> TopProducts(CancellationToken ct,string from=null,string to=null,string search=null,long? categoryId=null,long? warehouseId=null,string status=null,string type=null,string reference=null,string sort="quantity",int page=1,int pageSize=20)
        { return Stock("top-products",new StockReportQuery{From=from,To=to,Search=search,CategoryId=categoryId,WarehouseId=warehouseId,Status=status,Type=type,Reference=reference,Sort=sort,Page=page,PageSize=pageSize},ct); }
        [HttpGet,Route("inventory-valuation")]
        public Task<IHttpActionResult> InventoryValuation(CancellationToken ct,string from=null,string to=null,string search=null,long? categoryId=null,long? warehouseId=null,string status=null,string type=null,string reference=null,string sort="quantity",int page=1,int pageSize=20)
        { return Stock("inventory-valuation",new StockReportQuery{From=from,To=to,Search=search,CategoryId=categoryId,WarehouseId=warehouseId,Status=status,Type=type,Reference=reference,Sort=sort,Page=page,PageSize=pageSize},ct); }
        [HttpGet,Route("low-stock")]
        public Task<IHttpActionResult> LowStock(CancellationToken ct,string from=null,string to=null,string search=null,long? categoryId=null,long? warehouseId=null,string status=null,string type=null,string reference=null,string sort="quantity",int page=1,int pageSize=20)
        { return Stock("low-stock",new StockReportQuery{From=from,To=to,Search=search,CategoryId=categoryId,WarehouseId=warehouseId,Status=status,Type=type,Reference=reference,Sort=sort,Page=page,PageSize=pageSize},ct); }
        [HttpGet,Route("inventory-movements")]
        public Task<IHttpActionResult> InventoryMovements(CancellationToken ct,string from=null,string to=null,string search=null,long? categoryId=null,long? warehouseId=null,string status=null,string type=null,string reference=null,string sort="quantity",int page=1,int pageSize=20)
        { return Stock("inventory-movements",new StockReportQuery{From=from,To=to,Search=search,CategoryId=categoryId,WarehouseId=warehouseId,Status=status,Type=type,Reference=reference,Sort=sort,Page=page,PageSize=pageSize},ct); }
        private async Task<IHttpActionResult> Stock(string report,StockReportQuery q,CancellationToken ct)
        {
            var denied=await Denied("report-"+report,ct).ConfigureAwait(false);
            if(denied!=null)return denied;
            if(!ModelState.IsValid)return Invalid();
            try { return Ok(ApiEnvelope<object>.Ok(await _stock.QueryAsync(report,q,ct).ConfigureAwait(false))); }
            catch(ArgumentException ex) {return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message));}
        }
        [HttpGet,Route("net-profit")]
        public async Task<IHttpActionResult> NetProfit(CancellationToken ct,string from=null,string to=null,string breakdown="daily",int page=1,int pageSize=20)
        {
            var denied=await Denied("report-net-profit",ct).ConfigureAwait(false);
            if(denied!=null) return denied;
            if(!ModelState.IsValid) return Invalid();
            try
            {
                var result=await _profit.QueryAsync(new NetProfitQuery{From=from,To=to,Breakdown=breakdown,Page=page,PageSize=pageSize},ct).ConfigureAwait(false);
                var actor=await _auth.GetCurrentUserAsync(SessionToken(),ct).ConfigureAwait(false);
                result.CanClassify=actor!=null && actor.Role==UserRoles.Administrator;
                return Ok(ApiEnvelope<object>.Ok(result));
            }
            catch(ArgumentException ex) { return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message)); }
        }
        [HttpPost,Route("net-profit/expenses/{id:long}/classification")]
        public async Task<IHttpActionResult> ClassifyExpense(long id,ExpenseClassificationCommand command,CancellationToken ct)
        {
            var token=SessionToken(); var user=await _auth.GetCurrentUserAsync(token,ct).ConfigureAwait(false);
            if(user==null) return Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولًا."));
            if(user.Role!=UserRoles.Administrator) return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("admin_required","تصنيف المصروفات متاح للمدير فقط."));
            if(!await _auth.ValidateCsrfAsync(token,Header("X-CSRF-Token"),ct).ConfigureAwait(false))
                return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب."));
            if(!ModelState.IsValid) return Invalid();
            try { await _profit.ClassifyAsync(id,command,user.Id,ct).ConfigureAwait(false);return Ok(ApiEnvelope<object>.Ok(new{saved=true})); }
            catch(ArgumentException ex) {return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message));}
            catch(AlKhayal.Application.Expenses.ExpenseException ex) {return Content(ex.Code=="not_found"?HttpStatusCode.NotFound:HttpStatusCode.Conflict,ApiEnvelope<object>.Fail(ex.Code,ex.Message));}
            catch(System.Data.SQLite.SQLiteException ex)
            {
                System.Diagnostics.Trace.TraceError("Expense classification failed: SQLite code {0}.",ex.ResultCode);
                return Content(HttpStatusCode.ServiceUnavailable,ApiEnvelope<object>.Fail("database_unavailable","تعذر تأكيد التصنيف؛ أعد المحاولة بنفس الطلب."));
            }
        }
        private string Header(string name) { IEnumerable<string> values;return Request.Headers.TryGetValues(name,out values)?values.FirstOrDefault():null; }
        private string SessionToken()
        {
            var header=Header("Cookie");return header==null?null:header.Split(';').Select(x=>x.Trim().Split(new[]{'='},2)).Where(x=>x.Length==2 && x[0]=="alkhayal_session").Select(x=>x[1]).FirstOrDefault();
        }
        [HttpGet,Route("sales")]
        public async Task<IHttpActionResult> Sales(CancellationToken ct, string from=null, string to=null, string search=null, string method=null, long? cashierId=null, int page=1, int pageSize=20)
        {
            var denied = await Denied("report-sales", ct).ConfigureAwait(false);
            if (denied != null) return denied;
            if (!ModelState.IsValid) return Invalid();
            try { return Ok(ApiEnvelope<object>.Ok(await _reports.QueryAsync(new SalesReportQuery { From=from,To=to,Search=search,Method=method,CashierId=cashierId,Page=page,PageSize=pageSize },ct).ConfigureAwait(false))); }
            catch(ArgumentException ex) { return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",ex.Message)); }
        }

        [HttpGet, Route("purchases")]
        public Task<IHttpActionResult> Purchases(CancellationToken ct, string from=null, string to=null, string search=null, long? supplierId=null, string party=null, int page=1, int pageSize=20)
        { return Transaction("purchases", new TransactionReportQuery { From=from,To=to,Search=search,SupplierId=supplierId,Party=party,Page=page,PageSize=pageSize }, ct); }

        [HttpGet, Route("returns")]
        public Task<IHttpActionResult> Returns(CancellationToken ct, string from=null, string to=null, string search=null, string type=null, string party=null, int page=1, int pageSize=20)
        { return Transaction("returns", new TransactionReportQuery { From=from,To=to,Search=search,Type=type,Party=party,Page=page,PageSize=pageSize }, ct); }

        [HttpGet, Route("payment-methods")]
        public Task<IHttpActionResult> Payments(CancellationToken ct, string from=null, string to=null, string search=null, string type=null, int page=1, int pageSize=20)
        { return Transaction("payment-methods", new TransactionReportQuery { From=from,To=to,Search=search,Type=type,Page=page,PageSize=pageSize }, ct); }

        [HttpGet, Route("expenses")]
        public async Task<IHttpActionResult> Expenses(CancellationToken ct, string from=null, string to=null, string search=null, long? categoryId=null, int page=1, int pageSize=20)
        {
            var denied = await Denied("report-expenses", ct).ConfigureAwait(false);
            if (denied != null) return denied;
            if (!ModelState.IsValid) return Invalid();
            try { return Ok(ApiEnvelope<object>.Ok(await _transactions.ExpensesAsync(new TransactionReportQuery { From=from, To=to, Search=search, CategoryId=categoryId, Page=page, PageSize=pageSize }, ct).ConfigureAwait(false))); }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
        }

        private async Task<IHttpActionResult> Transaction(string report, TransactionReportQuery query, CancellationToken ct)
        {
            var denied = await Denied("report-" + report, ct).ConfigureAwait(false);
            if (denied != null) return denied;
            if (!ModelState.IsValid) return Invalid();
            try
            {
                var data = report == "purchases" ? await _transactions.PurchasesAsync(query, ct).ConfigureAwait(false)
                    : report == "returns" ? await _transactions.ReturnsAsync(query, ct).ConfigureAwait(false)
                    : await _transactions.PaymentsAsync(query, ct).ConfigureAwait(false);
                return Ok(ApiEnvelope<object>.Ok(data));
            }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
        }
        private IHttpActionResult Invalid() => Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", "مرشحات التقرير غير صحيحة."));
        private async Task<IHttpActionResult> Denied(string screen, CancellationToken ct)
        {
            IEnumerable<string> headers;
            var cookie = Request.Headers.TryGetValues("Cookie", out headers) ? headers.SelectMany(x=>x.Split(';')).Select(x=>x.Trim().Split(new[]{'='},2)).FirstOrDefault(x=>x.Length==2 && x[0]=="alkhayal_session") : null;
            var user=await _auth.GetCurrentUserAsync(cookie == null ? null : cookie[1],ct).ConfigureAwait(false);
            if(user==null) return Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولًا."));
            if(user.Role!=UserRoles.Administrator) {
                var access=await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);
                if(!access.ScreenIds.Contains(screen,StringComparer.OrdinalIgnoreCase)) return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية التقرير."));
            }
            return null;
        }
    }
}
