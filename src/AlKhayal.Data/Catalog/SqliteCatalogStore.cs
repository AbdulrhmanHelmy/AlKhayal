using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using Dapper;

namespace AlKhayal.Data.Catalog
{
    public sealed partial class SqliteCatalogStore : ICatalogStore
    {
        private readonly string _connectionString;
        private const string ProductSelect = @"SELECT p.Id, p.Name, d.Description, d.CategoryId, c.Name CategoryName,
            d.BaseUnitId, u.Name BaseUnitName, u.Symbol UnitSymbol, d.ProductType, d.SellPriceScaled, d.PurchasePriceScaled,
            d.IsActive, d.ArchivedUtc, d.MigrationState, d.VariantMode, d.Version, d.UpdatedUtc,
            COALESCE((SELECT ps.Sku FROM ProductSkus ps JOIN ProductVariants pv ON pv.Id=ps.VariantId WHERE pv.ProductId=p.Id AND pv.IsDefault=1 AND ps.RetiredUtc IS NULL LIMIT 1), (SELECT ps.Sku FROM ProductSkus ps WHERE ps.ProductId=p.Id AND ps.RetiredUtc IS NULL ORDER BY ps.Id LIMIT 1)) AS PrimarySku,
            (SELECT COUNT(*) FROM ProductVariants pv WHERE pv.ProductId=p.Id AND pv.IsActive=1 AND pv.ArchivedUtc IS NULL) AS VariantCount,
            (SELECT COUNT(*) FROM ProductBarcodes pb WHERE pb.ProductId=p.Id AND pb.RetiredUtc IS NULL) AS BarcodeCount
            FROM Products p JOIN ProductDetails d ON d.ProductId=p.Id
            JOIN Units u ON u.Id=d.BaseUnitId LEFT JOIN ProductCategories c ON c.Id=d.CategoryId";

        public SqliteCatalogStore(string connectionString) { _connectionString=connectionString; }

        public async Task<ProductStorePage> ListProductsAsync(ProductStoreQuery query, CancellationToken ct)
        {
            var where=new List<string>();
            if(!query.IncludeArchived) where.Add("d.ArchivedUtc IS NULL");
            if(query.CategoryId.HasValue) where.Add("d.CategoryId=@CategoryId");
            if(query.IsActive.HasValue) where.Add("d.IsActive=@IsActive");
            if(!string.IsNullOrWhiteSpace(query.Search))
                where.Add("(p.Name LIKE @Search ESCAPE '\\' OR IFNULL(d.Description,'') LIKE @Search ESCAPE '\\' OR EXISTS (SELECT 1 FROM ProductSkus ps WHERE ps.ProductId=p.Id AND ps.RetiredUtc IS NULL AND ps.Sku LIKE @Search ESCAPE '\\') OR EXISTS (SELECT 1 FROM ProductBarcodes pb WHERE pb.ProductId=p.Id AND pb.RetiredUtc IS NULL AND pb.Barcode LIKE @Search ESCAPE '\\'))");
            var predicate=where.Count==0?"":" WHERE "+string.Join(" AND ",where);
            var parameters=new { query.CategoryId, query.IsActive, Search=Like(query.Search), query.Offset, query.Limit };
            using(var connection=new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var count=await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Products p JOIN ProductDetails d ON d.ProductId=p.Id"+predicate,parameters,cancellationToken:ct)).ConfigureAwait(false);
                var items=(await connection.QueryAsync<CatalogProductRecord>(new CommandDefinition(ProductSelect+predicate+" ORDER BY p.Name, p.Id LIMIT @Limit OFFSET @Offset",parameters,cancellationToken:ct)).ConfigureAwait(false)).ToList();
                await AttachProductSummariesAsync(connection,items,ct).ConfigureAwait(false);
                return new ProductStorePage{Items=items,TotalCount=count};
            }
        }

        public async Task<CatalogProductRecord> FindProductAsync(long productId, CancellationToken ct)
        {
            using(var connection=new SQLiteConnection(_connectionString))
            {
                var product=await connection.QuerySingleOrDefaultAsync<CatalogProductRecord>(new CommandDefinition(ProductSelect+" WHERE p.Id=@ProductId",new{ProductId=productId},cancellationToken:ct)).ConfigureAwait(false);
                if(product!=null) await AttachProductSummariesAsync(connection,new[]{product},ct).ConfigureAwait(false);
                return product;
            }
        }

        private sealed class SummaryValueRow { public long ProductId { get; set; } public string Kind { get; set; } public string Value { get; set; } }
        private sealed class SummaryCapabilityRow { public long ProductId { get; set; } public string CapabilityKey { get; set; } }

        private static async Task AttachProductSummariesAsync(SQLiteConnection connection, IReadOnlyList<CatalogProductRecord> products, CancellationToken ct)
        {
            if(products.Count==0)return;
            var byId=products.ToDictionary(p=>p.Id);
            var ids=byId.Keys.ToArray();
            var sizes=products.ToDictionary(p=>p.Id,p=>new List<string>());
            var colors=products.ToDictionary(p=>p.Id,p=>new List<string>());
            var values=await connection.QueryAsync<SummaryValueRow>(new CommandDefinition(@"
                SELECT va.ProductId, pa.Kind, pav.Value
                FROM ProductAttributeValueAssignments va
                JOIN ProductAttributes pa ON pa.Id=va.AttributeId
                JOIN ProductAttributeValues pav ON pav.Id=va.AttributeValueId
                WHERE va.ProductId IN @ProductIds AND pa.Kind IN ('size','color')
                ORDER BY va.ProductId, pa.Kind, va.SortOrder, va.AttributeValueId",
                new{ProductIds=ids},cancellationToken:ct)).ConfigureAwait(false);
            foreach(var row in values)
            {
                var list=row.Kind=="size"?sizes[row.ProductId]:colors[row.ProductId];
                if(!list.Contains(row.Value))list.Add(row.Value);
            }
            foreach(var product in products)
            {
                product.SizeValues=sizes[product.Id];
                product.ColorValues=colors[product.Id];
            }
            var capabilities=await connection.QueryAsync<SummaryCapabilityRow>(new CommandDefinition(@"
                SELECT ProductId, CapabilityKey FROM ProductCapabilityAssignments
                WHERE ProductId IN @ProductIds AND IsEnabled=1
                AND CapabilityKey IN ('expiry-dates','batch-tracking','serial-numbers','imei-tracking')",
                new{ProductIds=ids},cancellationToken:ct)).ConfigureAwait(false);
            foreach(var row in capabilities)
            {
                var product=byId[row.ProductId];
                if(row.CapabilityKey=="expiry-dates")product.HasExpiryTracking=true;
                else if(row.CapabilityKey=="batch-tracking")product.HasBatchTracking=true;
                else if(row.CapabilityKey=="serial-numbers")product.HasSerialTracking=true;
                else if(row.CapabilityKey=="imei-tracking")product.HasImeiTracking=true;
            }
        }

        public Task<CatalogProductRecord> CreateProductAsync(CreateProductRecord record, CancellationToken ct)
        {
            return CreateAggregateProductAsync(record, ct);
        }

        public Task<CatalogProductRecord> UpdateProductAsync(UpdateProductRecord record, CancellationToken ct)
        {
            return UpdateAggregateProductAsync(record, ct);
        }

        public Task<CatalogProductRecord> SetProductStateAsync(long productId, bool isActive, long version, CancellationToken ct) { return ChangeState(productId,isActive,false,version,ct); }
        public Task<CatalogProductRecord> ArchiveProductAsync(long productId, long version, CancellationToken ct) { return ChangeState(productId,false,true,version,ct); }

        private async Task<CatalogProductRecord> ChangeState(long productId,bool isActive,bool archive,long version,CancellationToken ct)
        {
            using(var connection=new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using(var transaction=connection.BeginTransaction())
                {
                    if (isActive)
                    {
                        var activeVariants = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition("SELECT COUNT(*) FROM ProductVariants WHERE ProductId=@ProductId AND IsActive=1 AND ArchivedUtc IS NULL",
                            new { ProductId = productId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        var activeBaseUnit = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition("SELECT COUNT(*) FROM ProductUnits WHERE ProductId=@ProductId AND IsBase=1 AND IsActive=1",
                            new { ProductId = productId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (activeVariants == 0 || activeBaseUnit == 0)
                            throw Error("validation", "لا يمكن تنشيط منتج لا يحتوي على متغير نشط ووحدة قياس أساسية نشطة.");
                    }

                    var now=UtcNow();
                    var affected=await connection.ExecuteAsync(new CommandDefinition("UPDATE ProductDetails SET IsActive=@IsActive,ArchivedUtc=CASE WHEN @Archive=1 THEN @Now ELSE ArchivedUtc END,Version=Version+1,UpdatedUtc=@Now WHERE ProductId=@ProductId AND Version=@Version AND ArchivedUtc IS NULL",new{ProductId=productId,Version=version,IsActive=isActive,Archive=archive,Now=now},transaction,cancellationToken:ct)).ConfigureAwait(false);
                    if(affected==0) await ThrowProductWriteFailure(connection,transaction,productId,ct).ConfigureAwait(false);
                    transaction.Commit();
                }
            }
            return await FindProductAsync(productId,ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<ProductCategory>> ListCategoriesAsync(bool includeInactive,CancellationToken ct)
        {
            using(var connection=new SQLiteConnection(_connectionString))
                return (await connection.QueryAsync<ProductCategory>(new CommandDefinition("SELECT Id,ParentCategoryId,Name,IsActive,SortOrder,CreatedUtc,UpdatedUtc FROM ProductCategories "+(includeInactive?"":"WHERE IsActive=1 ")+"ORDER BY ParentCategoryId,SortOrder,Name",cancellationToken:ct)).ConfigureAwait(false)).ToList();
        }

        public async Task<ProductCategory> SaveCategoryAsync(SaveCategoryCommand command,string normalizedName,CancellationToken ct)
        {
            using(var connection=new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using(var transaction=connection.BeginTransaction())
                {
                    if(command.ParentCategoryId.HasValue)
                    {
                        var exists=await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM ProductCategories WHERE Id=@Id",new{Id=command.ParentCategoryId.Value},transaction,cancellationToken:ct)).ConfigureAwait(false);
                        if(exists==0) throw Error("invalid_parent","التصنيف الأب غير موجود.");
                        if(command.CategoryId.HasValue)
                        {
                            if(command.CategoryId.Value==command.ParentCategoryId.Value) throw Error("category_cycle","لا يمكن جعل التصنيف تابعًا لنفسه.");
                            var cycle=await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"WITH RECURSIVE descendants(Id) AS (SELECT Id FROM ProductCategories WHERE ParentCategoryId=@Id UNION ALL SELECT c.Id FROM ProductCategories c JOIN descendants d ON c.ParentCategoryId=d.Id) SELECT COUNT(*) FROM descendants WHERE Id=@ParentId",new{Id=command.CategoryId.Value,ParentId=command.ParentCategoryId.Value},transaction,cancellationToken:ct)).ConfigureAwait(false);
                            if(cycle>0) throw Error("category_cycle","لا يمكن نقل التصنيف تحت أحد تصنيفاته الفرعية.");
                        }
                    }
                    var duplicate=await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM ProductCategories WHERE NormalizedName=@Name AND IFNULL(ParentCategoryId,0)=IFNULL(@ParentId,0) AND (@Id IS NULL OR Id<>@Id)",new{Name=normalizedName,ParentId=command.ParentCategoryId,Id=command.CategoryId},transaction,cancellationToken:ct)).ConfigureAwait(false);
                    if(duplicate>0) throw Error("duplicate_category","يوجد تصنيف بالاسم نفسه تحت هذا المستوى.");
                    var now=UtcNow(); long id;
                    if(command.CategoryId.HasValue)
                    {
                        var affected=await connection.ExecuteAsync(new CommandDefinition("UPDATE ProductCategories SET ParentCategoryId=@ParentCategoryId,Name=@Name,NormalizedName=@NormalizedName,IsActive=@IsActive,SortOrder=@SortOrder,UpdatedUtc=@Now WHERE Id=@Id",new{command.ParentCategoryId,command.Name,NormalizedName=normalizedName,command.IsActive,command.SortOrder,Now=now,Id=command.CategoryId.Value},transaction,cancellationToken:ct)).ConfigureAwait(false);
                        if(affected==0) throw Error("not_found","التصنيف غير موجود."); id=command.CategoryId.Value;
                    }
                    else id=await connection.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO ProductCategories(ParentCategoryId,Name,NormalizedName,IsActive,SortOrder,CreatedUtc,UpdatedUtc) VALUES(@ParentCategoryId,@Name,@NormalizedName,@IsActive,@SortOrder,@Now,@Now); SELECT last_insert_rowid();",new{command.ParentCategoryId,command.Name,NormalizedName=normalizedName,command.IsActive,command.SortOrder,Now=now},transaction,cancellationToken:ct)).ConfigureAwait(false);
                    transaction.Commit();
                    return await FindCategory(id,ct).ConfigureAwait(false);
                }
            }
        }

        public async Task<IReadOnlyList<UnitOfMeasurement>> ListUnitsAsync(bool includeInactive,CancellationToken ct)
        {
            using(var connection=new SQLiteConnection(_connectionString))
                return (await connection.QueryAsync<UnitOfMeasurement>(new CommandDefinition("SELECT Id,Code,Name,Symbol,IsActive,IsSystem,CreatedUtc,UpdatedUtc FROM Units "+(includeInactive?"":"WHERE IsActive=1 ")+"ORDER BY IsSystem DESC,Name",cancellationToken:ct)).ConfigureAwait(false)).ToList();
        }

        public async Task<UnitOfMeasurement> SaveUnitAsync(SaveUnitCommand command,string normalizedCode,CancellationToken ct)
        {
            using(var connection=new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using(var transaction=connection.BeginTransaction())
                {
                    var duplicate=await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Units WHERE NormalizedCode=@Code AND (@Id IS NULL OR Id<>@Id)",new{Code=normalizedCode,Id=command.UnitId},transaction,cancellationToken:ct)).ConfigureAwait(false);
                    if(duplicate>0) throw Error("duplicate_unit","كود الوحدة مستخدم بالفعل.");
                    var now=UtcNow(); long id;
                    if(command.UnitId.HasValue)
                    {
                        var system=await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition("SELECT IsSystem FROM Units WHERE Id=@Id",new{Id=command.UnitId.Value},transaction,cancellationToken:ct)).ConfigureAwait(false);
                        if(!system.HasValue) throw Error("not_found","الوحدة غير موجودة.");
                        if(system.Value==1&&!command.IsActive) throw Error("protected_unit","لا يمكن تعطيل وحدة القطعة الافتراضية.");
                        await connection.ExecuteAsync(new CommandDefinition("UPDATE Units SET Code=@Code,NormalizedCode=@NormalizedCode,Name=@Name,Symbol=@Symbol,IsActive=@IsActive,UpdatedUtc=@Now WHERE Id=@Id",new{command.Code,NormalizedCode=normalizedCode,command.Name,command.Symbol,command.IsActive,Now=now,Id=command.UnitId.Value},transaction,cancellationToken:ct)).ConfigureAwait(false); id=command.UnitId.Value;
                    }
                    else id=await connection.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO Units(Code,NormalizedCode,Name,Symbol,IsActive,IsSystem,CreatedUtc,UpdatedUtc) VALUES(@Code,@NormalizedCode,@Name,@Symbol,@IsActive,0,@Now,@Now); SELECT last_insert_rowid();",new{command.Code,NormalizedCode=normalizedCode,command.Name,command.Symbol,command.IsActive,Now=now},transaction,cancellationToken:ct)).ConfigureAwait(false);
                    transaction.Commit();
                    return await FindUnit(id,ct).ConfigureAwait(false);
                }
            }
        }

        public async Task<IReadOnlyList<ProductMigrationIssue>> ListMigrationIssuesAsync(CancellationToken ct)
        {
            using(var connection=new SQLiteConnection(_connectionString))
                return (await connection.QueryAsync<ProductMigrationIssue>(new CommandDefinition("SELECT i.Id,i.ProductId,p.Name ProductName,i.IssueCode,i.Details,i.OriginalPriceType,i.OriginalPriceText,i.CreatedUtc FROM ProductMigrationIssues i JOIN Products p ON p.Id=i.ProductId WHERE i.ResolvedUtc IS NULL ORDER BY i.CreatedUtc,i.Id",cancellationToken:ct)).ConfigureAwait(false)).ToList();
        }

        private async Task ValidateReferences(SQLiteConnection connection,SQLiteTransaction transaction,long? categoryId,long unitId,CancellationToken ct)
        {
            if(categoryId.HasValue&&await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM ProductCategories WHERE Id=@Id AND IsActive=1",new{Id=categoryId.Value},transaction,cancellationToken:ct)).ConfigureAwait(false)==0) throw Error("invalid_category","التصنيف غير موجود أو غير نشط.");
            if(await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Units WHERE Id=@Id AND IsActive=1",new{Id=unitId},transaction,cancellationToken:ct)).ConfigureAwait(false)==0) throw Error("invalid_unit","وحدة القياس غير موجودة أو غير نشطة.");
        }
        private static async Task ThrowProductWriteFailure(SQLiteConnection connection,SQLiteTransaction transaction,long productId,CancellationToken ct)
        {
            var row=await connection.QuerySingleOrDefaultAsync<ProductStateRow>(new CommandDefinition("SELECT Version,ArchivedUtc FROM ProductDetails WHERE ProductId=@ProductId",new{ProductId=productId},transaction,cancellationToken:ct)).ConfigureAwait(false);
            if(row==null) throw Error("not_found","المنتج غير موجود.");
            if(row.ArchivedUtc!=null) throw Error("archived","المنتج مؤرشف ولا يمكن تعديله.");
            throw Error("version_conflict","عُدلت بيانات المنتج في جلسة أخرى. أعد التحميل ثم حاول مرة أخرى.");
        }
        private async Task<ProductCategory> FindCategory(long id,CancellationToken ct) { using(var c=new SQLiteConnection(_connectionString)) return await c.QuerySingleAsync<ProductCategory>(new CommandDefinition("SELECT Id,ParentCategoryId,Name,IsActive,SortOrder,CreatedUtc,UpdatedUtc FROM ProductCategories WHERE Id=@Id",new{Id=id},cancellationToken:ct)).ConfigureAwait(false); }
        private async Task<UnitOfMeasurement> FindUnit(long id,CancellationToken ct) { using(var c=new SQLiteConnection(_connectionString)) return await c.QuerySingleAsync<UnitOfMeasurement>(new CommandDefinition("SELECT Id,Code,Name,Symbol,IsActive,IsSystem,CreatedUtc,UpdatedUtc FROM Units WHERE Id=@Id",new{Id=id},cancellationToken:ct)).ConfigureAwait(false); }
        private static string UtcNow(){return DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);}
        private static string Like(string value){return string.IsNullOrWhiteSpace(value)?null:"%"+value.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";}
        private static CatalogStoreException Error(string code,string message){return new CatalogStoreException(code,message);}
        private sealed class OperationRow{public string RequestHash{get;set;}public long ProductId{get;set;}}
        private sealed class ProductStateRow{public long Version{get;set;}public string ArchivedUtc{get;set;}}
    }
}
