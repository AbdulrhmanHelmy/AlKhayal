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
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/catalog")]
    public sealed class CatalogController : ApiController
    {
        private readonly CatalogService _catalog; private readonly AuthService _auth; private readonly UserManagementService _users; private readonly FeatureService _features;
        public CatalogController(CatalogService catalog,AuthService auth,UserManagementService users,FeatureService features){_catalog=catalog;_auth=auth;_users=users;_features=features;}

        [HttpGet,Route("products")]
        public async Task<IHttpActionResult> Products(string search=null,long? categoryId=null,bool? isActive=null,bool includeArchived=false,int page=1,int pageSize=25,CancellationToken ct=default(CancellationToken))
        {
            var access=await RequireAny(ct,false,false,"products-list","product-edit").ConfigureAwait(false); if(access.Error!=null)return access.Error;
            if(includeArchived&&access.User.Role!=UserRoles.Administrator)return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("forbidden","عرض المنتجات المؤرشفة متاح لمسؤول النظام فقط."));
            return Result(await _catalog.ListProductsAsync(new ProductQuery{Search=search,CategoryId=categoryId,IsActive=isActive,IncludeArchived=includeArchived,Page=page,PageSize=pageSize},ct).ConfigureAwait(false));
        }

        [HttpGet,Route("products/{id:long}")]
        public async Task<IHttpActionResult> Product(long id,CancellationToken ct){var access=await RequireAny(ct,false,false,"products-list","product-add","product-edit","product-quick-add").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.GetProductDetailAsync(id,ct).ConfigureAwait(false));}

        [HttpPost,Route("products")]
        public async Task<IHttpActionResult> CreateProduct(ProductRequest request,CancellationToken ct)
        {
            var access=await RequireAny(ct,true,false,"product-add","product-quick-add").ConfigureAwait(false);if(access.Error!=null)return access.Error;
            if(access.User.Role!=UserRoles.Administrator&&request!=null&&!request.IsActive)return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("forbidden","إنشاء منتج غير نشط متاح لمسؤول النظام فقط."));
            var create=request==null?null:request.ToCreate();if(create!=null)create.UserId=access.User.Id;var result=await _catalog.CreateProductAsync(create,ct).ConfigureAwait(false);return Result(result,HttpStatusCode.Created);
        }

        [HttpPut,Route("products/{id:long}")]
        public async Task<IHttpActionResult> UpdateProduct(long id,ProductRequest request,CancellationToken ct)
        {
            var access=await RequireAny(ct,true,false,"products-list","product-edit").ConfigureAwait(false);if(access.Error!=null)return access.Error;
            if(access.User.Role!=UserRoles.Administrator&&request!=null){var current=await _catalog.GetProductAsync(id,ct).ConfigureAwait(false);if(current.Succeeded&&current.Value.IsActive!=request.IsActive)return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("forbidden","تغيير حالة المنتج متاح لمسؤول النظام فقط."));}
            var update=request==null?null:request.ToUpdate(id);if(update!=null)update.UserId=access.User.Id;var result=await _catalog.UpdateProductAsync(update,ct).ConfigureAwait(false);return Result(result);
        }

        [HttpPut,Route("products/{id:long}/state")]
        public async Task<IHttpActionResult> ProductState(long id,ProductStateRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"products-list").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.SetProductStateAsync(request==null?null:new SetProductStateCommand{ProductId=id,IsActive=request.IsActive,Version=request.Version},ct).ConfigureAwait(false));}

        [HttpPost,Route("products/{id:long}/archive")]
        public async Task<IHttpActionResult> ArchiveProduct(long id,VersionRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"products-list").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.ArchiveProductAsync(request==null?null:new ArchiveProductCommand{ProductId=id,Version=request.Version},ct).ConfigureAwait(false));}

        [HttpGet,Route("categories")]
        public async Task<IHttpActionResult> Categories(bool includeInactive=false,CancellationToken ct=default(CancellationToken)){var access=await RequireAny(ct,false,false,"products-list","product-add","product-edit","product-quick-add","product-categories").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Ok(ApiEnvelope<IReadOnlyList<ProductCategory>>.Ok(await _catalog.ListCategoriesAsync(includeInactive,ct).ConfigureAwait(false)));}
        [HttpPost,Route("categories")]
        public async Task<IHttpActionResult> CreateCategory(CategoryRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"product-categories").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.SaveCategoryAsync(request==null?null:request.ToCommand(null),ct).ConfigureAwait(false),HttpStatusCode.Created);}
        [HttpPut,Route("categories/{id:long}")]
        public async Task<IHttpActionResult> UpdateCategory(long id,CategoryRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"product-categories").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.SaveCategoryAsync(request==null?null:request.ToCommand(id),ct).ConfigureAwait(false));}

        [HttpGet,Route("units")]
        public async Task<IHttpActionResult> Units(bool includeInactive=false,CancellationToken ct=default(CancellationToken)){var access=await RequireAny(ct,false,false,"products-list","product-add","product-edit","product-quick-add","unit-add").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Ok(ApiEnvelope<IReadOnlyList<UnitOfMeasurement>>.Ok(await _catalog.ListUnitsAsync(includeInactive,ct).ConfigureAwait(false)));}
        [HttpPost,Route("units")]
        public async Task<IHttpActionResult> CreateUnit(UnitRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"unit-add").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.SaveUnitAsync(request==null?null:request.ToCommand(null),ct).ConfigureAwait(false),HttpStatusCode.Created);}
        [HttpPut,Route("units/{id:long}")]
        public async Task<IHttpActionResult> UpdateUnit(long id,UnitRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"unit-add").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.SaveUnitAsync(request==null?null:request.ToCommand(id),ct).ConfigureAwait(false));}

        [HttpGet,Route("migration-issues")]
        public async Task<IHttpActionResult> MigrationIssues(CancellationToken ct){var access=await RequireAny(ct,false,true,"products-list").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Ok(ApiEnvelope<IReadOnlyList<ProductMigrationIssue>>.Ok(await _catalog.ListMigrationIssuesAsync(ct).ConfigureAwait(false)));}

        [HttpGet,Route("attributes")]
        public async Task<IHttpActionResult> Attributes(string kind=null,bool includeInactive=false,CancellationToken ct=default(CancellationToken)){var access=await RequireAny(ct,false,false,"products-list","product-add","product-edit","product-quick-add").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.ListAttributesAsync(kind,includeInactive,ct).ConfigureAwait(false));}

        [HttpPost,Route("attributes")]
        public async Task<IHttpActionResult> CreateAttribute(AttributeRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"product-add","product-edit").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.SaveAttributeAsync(request==null?null:request.ToCreate(),access.User.Id,ct).ConfigureAwait(false),HttpStatusCode.Created);}

        [HttpPut,Route("attributes/{id:long}")]
        public async Task<IHttpActionResult> UpdateAttribute(long id,AttributeUpdateRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"product-add","product-edit").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.UpdateAttributeAsync(request==null?null:request.ToCommand(id),access.User.Id,ct).ConfigureAwait(false));}

        [HttpPost,Route("attributes/{attributeId:long}/values")]
        public async Task<IHttpActionResult> CreateAttributeValue(long attributeId,AttributeValueRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"product-add","product-edit").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.SaveAttributeValueAsync(request==null?null:request.ToCreate(attributeId),access.User.Id,ct).ConfigureAwait(false),HttpStatusCode.Created);}

        [HttpPut,Route("attribute-values/{id:long}")]
        public async Task<IHttpActionResult> UpdateAttributeValue(long id,AttributeValueUpdateRequest request,CancellationToken ct){var access=await RequireAny(ct,true,true,"product-add","product-edit").ConfigureAwait(false);if(access.Error!=null)return access.Error;return Result(await _catalog.UpdateAttributeValueAsync(request==null?null:request.ToCommand(id),access.User.Id,ct).ConfigureAwait(false));}

        [HttpGet,Route("barcodes/lookup")]
        public async Task<IHttpActionResult> LookupBarcode(string code,CancellationToken ct)
        {
            var access=await RequireAny(ct,false,false,"pos","sales-management","products-list","product-edit").ConfigureAwait(false);if(access.Error!=null)return access.Error;
            var effective=await _features.GetEffectiveAsync(ct).ConfigureAwait(false);if(!effective.Capabilities.Any(item=>item.Key=="barcode-scanning"&&item.IsEnabled))return Content(HttpStatusCode.Conflict,ApiEnvelope<object>.Fail("feature_disabled","ميزة مسح الباركود غير مفعلة."));
            return Result(await _catalog.LookupBarcodeAsync(code,ct).ConfigureAwait(false));
        }

        private async Task<AuthCheck> RequireAny(CancellationToken ct,bool csrf,bool administratorOnly,params string[] screens)
        {
            var token=ReadToken();var user=await _auth.GetCurrentUserAsync(token,ct).ConfigureAwait(false);
            if(user==null)return new AuthCheck{Error=Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولاً."))};
            if(administratorOnly&&user.Role!=UserRoles.Administrator)return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("forbidden","هذا الإجراء متاح لمسؤول النظام فقط."))};
            if(user.Role!=UserRoles.Administrator)
            {
                var effective=await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);
                if(!screens.Any(id=>effective.ScreenIds.Contains(id,StringComparer.OrdinalIgnoreCase)))return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية الشاشة المطلوبة."))};
            }
            if(csrf&&!await _auth.ValidateCsrfAsync(token,Header("X-CSRF-Token"),ct).ConfigureAwait(false))return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."))};
            return new AuthCheck{User=user};
        }
        private IHttpActionResult Result<T>(CatalogResult<T> result,HttpStatusCode success=HttpStatusCode.OK)
        {
            if(result.Succeeded)return Content(success,ApiEnvelope<T>.Ok(result.Value));
            var status=result.ErrorCode=="not_found"||result.ErrorCode=="barcode_not_found"?HttpStatusCode.NotFound:result.ErrorCode=="barcode_retired"?HttpStatusCode.Gone:result.ErrorCode=="version_conflict"||result.ErrorCode=="operation_key_conflict"||result.ErrorCode=="duplicate_category"||result.ErrorCode=="duplicate_unit"||result.ErrorCode=="category_cycle"||result.ErrorCode=="archived"||result.ErrorCode=="duplicate_attribute"||result.ErrorCode=="duplicate_attribute_value"||result.ErrorCode=="duplicate_combination"||result.ErrorCode=="duplicate_sku"||result.ErrorCode=="duplicate_barcode"||result.ErrorCode=="conversion_locked"||result.ErrorCode=="base_unit_locked"||result.ErrorCode=="variant_mode_locked"||result.ErrorCode=="variant_set_incomplete"||result.ErrorCode=="tracking_history_locked"?HttpStatusCode.Conflict:HttpStatusCode.BadRequest;
            return Content(status,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));
        }
        private string ReadToken(){IEnumerable<string> values;if(!Request.Headers.TryGetValues("Cookie",out values))return null;foreach(var header in values)foreach(var part in header.Split(';')){var pair=part.Trim().Split(new[]{'='},2);if(pair.Length==2&&pair[0]=="alkhayal_session")return pair[1];}return null;}
        private string Header(string name){IEnumerable<string> values;return Request.Headers.TryGetValues(name,out values)?values.FirstOrDefault():null;}
        private sealed class AuthCheck{public UserIdentity User{get;set;}public IHttpActionResult Error{get;set;}}
    }

    public sealed class ProductRequest
    {
        public string OperationKey{get;set;} public string Name{get;set;} public string Description{get;set;} public long? CategoryId{get;set;} public long BaseUnitId{get;set;} public string ProductType{get;set;} public string SalePrice{get;set;} public string PurchasePrice{get;set;} public bool IsActive{get;set;} public long Version{get;set;} public ProductConfigurationCommand Configuration{get;set;}
        public CreateProductCommand ToCreate(){return new CreateProductCommand{OperationKey=OperationKey,Name=Name,Description=Description,CategoryId=CategoryId,BaseUnitId=BaseUnitId,ProductType=ProductType,SalePrice=SalePrice,PurchasePrice=PurchasePrice,IsActive=IsActive,Configuration=Configuration};}
        public UpdateProductCommand ToUpdate(long id){return new UpdateProductCommand{ProductId=id,Name=Name,Description=Description,CategoryId=CategoryId,BaseUnitId=BaseUnitId,ProductType=ProductType,SalePrice=SalePrice,PurchasePrice=PurchasePrice,IsActive=IsActive,Version=Version,Configuration=Configuration};}
    }
    public sealed class ProductStateRequest{public bool IsActive{get;set;}public long Version{get;set;}}
    public sealed class VersionRequest{public long Version{get;set;}}
    public sealed class CategoryRequest{public long? ParentCategoryId{get;set;}public string Name{get;set;}public bool IsActive{get;set;}=true;public int SortOrder{get;set;}public SaveCategoryCommand ToCommand(long? id){return new SaveCategoryCommand{CategoryId=id,ParentCategoryId=ParentCategoryId,Name=Name,IsActive=IsActive,SortOrder=SortOrder};}}
    public sealed class UnitRequest{public string Code{get;set;}public string Name{get;set;}public string Symbol{get;set;}public bool IsActive{get;set;}=true;public SaveUnitCommand ToCommand(long? id){return new SaveUnitCommand{UnitId=id,Code=Code,Name=Name,Symbol=Symbol,IsActive=IsActive};}}
    public sealed class AttributeRequest{public string Key{get;set;}public string Name{get;set;}public string Kind{get;set;}public int SortOrder{get;set;}public bool IsActive{get;set;}=true;public SaveAttributeCommand ToCreate(){return new SaveAttributeCommand{Key=Key,Name=Name,Kind=Kind,SortOrder=SortOrder,IsActive=IsActive};}}
    public sealed class AttributeUpdateRequest{public string Name{get;set;}public int SortOrder{get;set;}public bool IsActive{get;set;}public long Version{get;set;}public UpdateAttributeCommand ToCommand(long id){return new UpdateAttributeCommand{Id=id,Name=Name,SortOrder=SortOrder,IsActive=IsActive,Version=Version};}}
    public sealed class AttributeValueRequest{public string Value{get;set;}public string ColorHex{get;set;}public int SortOrder{get;set;}public bool IsActive{get;set;}=true;public SaveAttributeValueCommand ToCreate(long id){return new SaveAttributeValueCommand{AttributeId=id,Value=Value,ColorHex=ColorHex,SortOrder=SortOrder,IsActive=IsActive};}}
    public sealed class AttributeValueUpdateRequest{public string Value{get;set;}public string ColorHex{get;set;}public int SortOrder{get;set;}public bool IsActive{get;set;}public long Version{get;set;}public UpdateAttributeValueCommand ToCommand(long id){return new UpdateAttributeValueCommand{Id=id,Value=Value,ColorHex=ColorHex,SortOrder=SortOrder,IsActive=IsActive,Version=Version};}}
}
