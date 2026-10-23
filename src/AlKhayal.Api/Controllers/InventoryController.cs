using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Features;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/inventory")]
    public sealed class InventoryController : ApiController
    {
        private readonly InventoryService _inventory;private readonly AuthService _auth;private readonly UserManagementService _users;private readonly FeatureService _features;private readonly CatalogService _catalog;
        public InventoryController(InventoryService inventory,AuthService auth,UserManagementService users,FeatureService features,CatalogService catalog){_inventory=inventory;_auth=auth;_users=users;_features=features;_catalog=catalog;}

        [HttpGet,Route("products")]
        public async Task<IHttpActionResult> Products(string search=null,int page=1,int pageSize=30,CancellationToken ct=default(CancellationToken))
        {
            var access=await Require(ct,false,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;
            var result=await _catalog.ListProductsAsync(new ProductQuery{Search=search,IsActive=true,Page=page,PageSize=Math.Min(100,Math.Max(1,pageSize))},ct).ConfigureAwait(false);
            return result.Succeeded?Content(HttpStatusCode.OK,ApiEnvelope<CatalogPage>.Ok(result.Value)):Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));
        }
        [HttpGet,Route("products/{id:long}")]
        public async Task<IHttpActionResult> Product(long id,CancellationToken ct)
        {
            var access=await Require(ct,false,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;
            var result=await _catalog.GetProductDetailAsync(id,ct).ConfigureAwait(false);
            return result.Succeeded?Content(HttpStatusCode.OK,ApiEnvelope<CatalogProductDetail>.Ok(result.Value)):Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));
        }

        [HttpGet,Route("warehouses")]
        public async Task<IHttpActionResult> Warehouses(CancellationToken ct){var access=await Require(ct,false,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Ok(ApiEnvelope<IReadOnlyList<WarehouseRecord>>.Ok(await _inventory.WarehousesAsync(ct).ConfigureAwait(false)));}
        [HttpGet,Route("warehouses/{id:long}")]
        public async Task<IHttpActionResult> Warehouse(long id,CancellationToken ct){var access=await Require(ct,false,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;var record=(await _inventory.WarehousesAsync(ct).ConfigureAwait(false)).FirstOrDefault(x=>x.Id==id);return record==null?Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found","المخزن غير موجود.")):Content(HttpStatusCode.OK,ApiEnvelope<WarehouseRecord>.Ok(record));}
        [HttpPost,Route("warehouses")]
        public async Task<IHttpActionResult> CreateWarehouse(WarehouseCommand command,CancellationToken ct){var access=await Require(ct,true,true).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _inventory.SaveWarehouseAsync(command,ct).ConfigureAwait(false),HttpStatusCode.Created);}
        [HttpPut,Route("warehouses/{id:long}")]
        public async Task<IHttpActionResult> UpdateWarehouse(long id,WarehouseCommand command,CancellationToken ct){var access=await Require(ct,true,true).ConfigureAwait(false);if(access.Error!=null)return access.Error;if(command!=null)command.Id=id;return Result(await _inventory.SaveWarehouseAsync(command,ct).ConfigureAwait(false));}
        [HttpGet,Route("balances")]
        public async Task<IHttpActionResult> Balances(long? warehouseId=null,long? productId=null,CancellationToken ct=default(CancellationToken)){var access=await Require(ct,false,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Ok(ApiEnvelope<IReadOnlyList<InventoryBalanceRecord>>.Ok(await _inventory.BalancesAsync(warehouseId,productId,ct).ConfigureAwait(false)));}
        [HttpGet,Route("movements")]
        public async Task<IHttpActionResult> Movements(long? warehouseId=null,long? productId=null,string type=null,string reference=null,string from=null,string to=null,int page=1,int pageSize=25,CancellationToken ct=default(CancellationToken)){var access=await Require(ct,false,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Ok(ApiEnvelope<InventoryPage<InventoryMovementRecord>>.Ok(await _inventory.MovementsAsync(warehouseId,productId,type,reference,from,to,page,pageSize,ct).ConfigureAwait(false)));}
        [HttpPost,Route("post")]
        public async Task<IHttpActionResult> Post(InventoryPostCommand command,CancellationToken ct){var access=await Require(ct,true,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;if(command!=null)command.UserId=access.User.Id;return Result(await _inventory.PostAsync(command,ct).ConfigureAwait(false),HttpStatusCode.Created);}
        [HttpPost,Route("cost-basis/reconcile")]
        public async Task<IHttpActionResult> ReconcileCost(InventoryCostReconciliationCommand command,CancellationToken ct){var access=await Require(ct,true,true).ConfigureAwait(false);if(access.Error!=null)return access.Error;if(command!=null)command.UserId=access.User.Id;return Result(await _inventory.ReconcileCostAsync(command,ct).ConfigureAwait(false));}
        [HttpPost,Route("counts")]
        public async Task<IHttpActionResult> CreateCount(CreateCountRequest request,CancellationToken ct){var access=await Require(ct,true,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;if(request==null||request.WarehouseId<=0)return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("invalid_count","اختر المخزن."));return Result(await _inventory.CreateCountAsync(request.WarehouseId,access.User.Id,request.VariantIds,ct).ConfigureAwait(false),HttpStatusCode.Created);}
        [HttpGet,Route("counts")]
        public async Task<IHttpActionResult> Counts(long warehouseId,CancellationToken ct){var access=await Require(ct,false,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Ok(ApiEnvelope<IReadOnlyList<InventoryCountRecord>>.Ok(await _inventory.CountsAsync(warehouseId,ct).ConfigureAwait(false)));}
        [HttpPost,Route("counts/{id:long}/post")]
        public async Task<IHttpActionResult> PostCount(long id,InventoryCountPostCommand command,CancellationToken ct){var access=await Require(ct,true,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;if(command!=null)command.UserId=access.User.Id;return Result(await _inventory.PostCountAsync(id,command,ct).ConfigureAwait(false));}
        [HttpGet,Route("counts/{id:long}")]
        public async Task<IHttpActionResult> GetCount(long id,CancellationToken ct){var access=await Require(ct,false,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _inventory.GetCountAsync(id,ct).ConfigureAwait(false));}
        [HttpPost,Route("counts/{id:long}/cancel")]
        public async Task<IHttpActionResult> CancelCount(long id,CancellationToken ct){var access=await Require(ct,true,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _inventory.CancelCountAsync(id,access.User.Id,ct).ConfigureAwait(false));}
        [HttpGet,Route("tracking")]
        public async Task<IHttpActionResult> Tracking(string identifier,CancellationToken ct){var access=await Require(ct,false,false).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _inventory.TrackingAsync(identifier,ct).ConfigureAwait(false));}
        [HttpPost,Route("documents/{id:long}/reverse")]
        public async Task<IHttpActionResult> Reverse(long id,InventoryPostCommand command,CancellationToken ct){var access=await Require(ct,true,true).ConfigureAwait(false);if(access.Error!=null)return access.Error;if(command!=null)command.UserId=access.User.Id;return Result(await _inventory.ReverseAsync(id,command,ct).ConfigureAwait(false));}
        [HttpGet,Route("integrity")]
        public async Task<IHttpActionResult> Integrity(CancellationToken ct){var access=await Require(ct,false,true).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Ok(ApiEnvelope<object>.Ok(new{consistent=await _inventory.VerifyProjectionAsync(ct).ConfigureAwait(false)}));}
        [HttpPost,Route("integrity/rebuild")]
        public async Task<IHttpActionResult> Rebuild(CancellationToken ct){var access=await Require(ct,true,true).ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _inventory.RebuildProjectionAsync(access.User.Id,ct).ConfigureAwait(false));}

        private async Task<AuthCheck> Require(CancellationToken ct,bool csrf,bool admin)
        {
            var token=ReadToken();var user=await _auth.GetCurrentUserAsync(token,ct).ConfigureAwait(false);
            if(user==null)return new AuthCheck{Error=Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولًا."))};
            if(admin&&user.Role!=UserRoles.Administrator)return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("forbidden","هذا الإجراء متاح لمسؤول النظام فقط."))};
            if(user.Role!=UserRoles.Administrator)
            {
                var effective=await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);
                if(!effective.ScreenIds.Contains("warehouses-list",StringComparer.OrdinalIgnoreCase))return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية دليل المخازن."))};
            }
            if(csrf&&!await _auth.ValidateCsrfAsync(token,Header("X-CSRF-Token"),ct).ConfigureAwait(false))return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."))};
            return new AuthCheck{User=user};
        }
        private IHttpActionResult Result<T>(InventoryResult<T> result,HttpStatusCode success=HttpStatusCode.OK)
        {
            if(result.Succeeded)return Content(success,ApiEnvelope<T>.Ok(result.Value));
            var status=result.ErrorCode=="not_found"?HttpStatusCode.NotFound:result.ErrorCode=="invalid_operation"||result.ErrorCode=="invalid_type"||result.ErrorCode=="invalid_quantity"||result.ErrorCode=="invalid_cost"||result.ErrorCode=="cost_required"||result.ErrorCode=="sale_price_required"||result.ErrorCode=="invalid_cost_basis"||result.ErrorCode=="reason_required"?HttpStatusCode.BadRequest:HttpStatusCode.Conflict;
            return Content(status,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));
        }
        private string ReadToken(){IEnumerable<string> values;if(!Request.Headers.TryGetValues("Cookie",out values))return null;foreach(var header in values)foreach(var part in header.Split(';')){var pair=part.Trim().Split(new[]{'='},2);if(pair.Length==2&&pair[0]=="alkhayal_session")return pair[1];}return null;}
        private string Header(string name){IEnumerable<string> values;return Request.Headers.TryGetValues(name,out values)?values.FirstOrDefault():null;}
        private sealed class AuthCheck{public UserIdentity User{get;set;}public IHttpActionResult Error{get;set;}}
    }
    public sealed class CreateCountRequest { public long WarehouseId{get;set;} public IReadOnlyList<long> VariantIds{get;set;} }
}
