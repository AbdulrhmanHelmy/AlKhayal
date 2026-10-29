using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Features;
using AlKhayal.Application.Finance;

namespace AlKhayal.Application.Catalog
{
    public sealed class CatalogService
    {
        public const long MoneyScale = MoneyPolicy.InternalScale;
        private static readonly HashSet<string> ProductFormCapabilities = new HashSet<string>(new[] { "expiry-dates", "batch-tracking", "sizes", "colors", "product-variants" }, System.StringComparer.OrdinalIgnoreCase);
        private readonly ICatalogStore _store;
        private readonly ICatalogFeatureReader _features;
        public CatalogService(ICatalogStore store) : this(store, null) { }
        public CatalogService(ICatalogStore store, ICatalogFeatureReader features) { _store=store; _features=features; }

        public async Task<CatalogResult<CatalogPage>> ListProductsAsync(ProductQuery query, CancellationToken ct)
        {
            query = query ?? new ProductQuery();
            if (query.Page < 1) query.Page = 1;
            if (query.PageSize < 1 || query.PageSize > 100) query.PageSize = 25;
            if(query.PosWarehouseId.HasValue && query.PosWarehouseId.Value<=0) return CatalogResult<CatalogPage>.Fail("validation","المخزن غير صالح.");
            var page = await _store.ListProductsAsync(new ProductStoreQuery { Search=TrimToNull(query.Search), CategoryId=query.CategoryId, IsActive=query.IsActive, IncludeArchived=query.IncludeArchived, Offset=(query.Page-1)*query.PageSize, Limit=query.PageSize, PosWarehouseId=query.PosWarehouseId, SinceDate=query.PosWarehouseId.HasValue?DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):null }, ct).ConfigureAwait(false);
            return CatalogResult<CatalogPage>.Ok(new CatalogPage { Items=page.Items.Select(ToProduct).ToList(), Page=query.Page, PageSize=query.PageSize, TotalCount=page.TotalCount });
        }

        public async Task<CatalogResult<CatalogProduct>> GetProductAsync(long id, CancellationToken ct)
        {
            if (id <= 0) return CatalogResult<CatalogProduct>.Fail("validation", "معرف المنتج غير صالح.");
            var item = await _store.FindProductAsync(id, ct).ConfigureAwait(false);
            return item == null ? CatalogResult<CatalogProduct>.Fail("not_found", "المنتج غير موجود.") : CatalogResult<CatalogProduct>.Ok(ToProduct(item));
        }

        public async Task<CatalogResult<CatalogProductDetail>> GetProductDetailAsync(long id, CancellationToken ct)
        {
            if (id <= 0) return CatalogResult<CatalogProductDetail>.Fail("validation", "معرف المنتج غير صالح.");
            var item = await _store.GetProductDetailAsync(id, ct).ConfigureAwait(false);
            if (item == null) return CatalogResult<CatalogProductDetail>.Fail("not_found", "المنتج غير موجود.");
            var states = _features == null ? null : await _features.GetEffectiveFeaturesAsync(ct).ConfigureAwait(false);
            return CatalogResult<CatalogProductDetail>.Ok(ToDetail(item, states));
        }

        public async Task<CatalogResult<CatalogProduct>> CreateProductAsync(CreateProductCommand command, CancellationToken ct)
        {
            long price; long? purchasePrice;
            var validation = ValidateProduct(command == null ? null : command.Name, command == null ? null : command.Description, command == null ? 0 : command.BaseUnitId, command == null ? null : command.ProductType, command == null ? null : command.SalePrice, command == null ? null : command.PurchasePrice, out price, out purchasePrice);
            if (validation != null) return CatalogResult<CatalogProduct>.Fail("validation", validation);
            if (string.IsNullOrWhiteSpace(command.OperationKey) || command.OperationKey.Trim().Length > 80) return CatalogResult<CatalogProduct>.Fail("validation", "مفتاح العملية مطلوب وبحد أقصى 80 حرفًا.");
            Normalize(command);
            var record = new CreateProductRecord { OperationKey=command.OperationKey.Trim(), RequestHash=Hash(command), Name=command.Name, Description=command.Description, CategoryId=command.CategoryId, BaseUnitId=command.BaseUnitId, ProductType=command.ProductType, SellPriceScaled=price, PurchasePriceScaled=purchasePrice, IsActive=command.IsActive, UserId=command.UserId, Configuration=ToRecord(command.Configuration) };
            if (command.Configuration == null) return await StoreResult(() => _store.CreateProductAsync(record, ct)).ConfigureAwait(false);
            var featureError = await ValidateNewCapabilities(command.Configuration, null, ct).ConfigureAwait(false);
            if (featureError != null) return CatalogResult<CatalogProduct>.Fail(featureError.Item1, featureError.Item2);
            return await StoreResult(() => _store.CreateAggregateProductAsync(record, ct)).ConfigureAwait(false);
        }

        public async Task<CatalogResult<CatalogProduct>> UpdateProductAsync(UpdateProductCommand command, CancellationToken ct)
        {
            long price; long? purchasePrice;
            var validation = ValidateProduct(command == null ? null : command.Name, command == null ? null : command.Description, command == null ? 0 : command.BaseUnitId, command == null ? null : command.ProductType, command == null ? null : command.SalePrice, command == null ? null : command.PurchasePrice, out price, out purchasePrice);
            if (validation != null || command.ProductId <= 0 || command.Version <= 0) return CatalogResult<CatalogProduct>.Fail("validation", validation ?? "معرف المنتج وإصداره مطلوبان.");
            Normalize(command);
            var record = new UpdateProductRecord { ProductId=command.ProductId, Name=command.Name, Description=command.Description, CategoryId=command.CategoryId, BaseUnitId=command.BaseUnitId, ProductType=command.ProductType, SellPriceScaled=price, PurchasePriceScaled=purchasePrice, IsActive=command.IsActive, Version=command.Version, UserId=command.UserId, Configuration=ToRecord(command.Configuration) };
            if (command.Configuration == null) return await StoreResult(() => _store.UpdateProductAsync(record, ct)).ConfigureAwait(false);
            var featureError = await ValidateNewCapabilities(command.Configuration, command.ProductId, ct).ConfigureAwait(false);
            if (featureError != null) return CatalogResult<CatalogProduct>.Fail(featureError.Item1, featureError.Item2);
            return await StoreResult(() => _store.UpdateAggregateProductAsync(record, ct)).ConfigureAwait(false);
        }

        public async Task<CatalogResult<IReadOnlyList<AttributeDto>>> ListAttributesAsync(string kind, bool includeInactive, CancellationToken ct)
        {
            try { return CatalogResult<IReadOnlyList<AttributeDto>>.Ok((await _store.ListAttributesAsync(kind, includeInactive, ct).ConfigureAwait(false)).Select(ToAttribute).ToList()); }
            catch (CatalogStoreException ex) { return CatalogResult<IReadOnlyList<AttributeDto>>.Fail(ex.Code, ex.Message); }
        }

        public async Task<CatalogResult<AttributeDto>> SaveAttributeAsync(SaveAttributeCommand command, long userId, CancellationToken ct)
        {
            if (command == null || !string.Equals(command.Kind, "custom", StringComparison.OrdinalIgnoreCase)) return CatalogResult<AttributeDto>.Fail("validation", "لا يمكن إنشاء إلا السمات المخصصة.");
            try { return CatalogResult<AttributeDto>.Ok(ToAttribute(await _store.CreateAttributeAsync(new CreateAttributeRecord { AttributeKey=command.Key, Name=command.Name, Kind="custom", SortOrder=command.SortOrder, IsActive=command.IsActive, UserId=userId }, ct).ConfigureAwait(false))); }
            catch (CatalogStoreException ex) { return CatalogResult<AttributeDto>.Fail(ex.Code, ex.Message); }
        }

        public async Task<CatalogResult<AttributeDto>> UpdateAttributeAsync(UpdateAttributeCommand command, long userId, CancellationToken ct)
        {
            if (command == null) return CatalogResult<AttributeDto>.Fail("validation", "بيانات السمة مطلوبة.");
            try { return CatalogResult<AttributeDto>.Ok(ToAttribute(await _store.UpdateAttributeAsync(new UpdateAttributeRecord { Id=command.Id, Name=command.Name, SortOrder=command.SortOrder, IsActive=command.IsActive, Version=command.Version, UserId=userId }, ct).ConfigureAwait(false))); }
            catch (CatalogStoreException ex) { return CatalogResult<AttributeDto>.Fail(ex.Code, ex.Message); }
        }

        public async Task<CatalogResult<AttributeValueDto>> SaveAttributeValueAsync(SaveAttributeValueCommand command, long userId, CancellationToken ct)
        {
            if (command == null) return CatalogResult<AttributeValueDto>.Fail("validation", "بيانات قيمة السمة مطلوبة.");
            try { return CatalogResult<AttributeValueDto>.Ok(ToAttributeValue(await _store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId=command.AttributeId, Value=command.Value, ColorHex=command.ColorHex, SortOrder=command.SortOrder, IsActive=command.IsActive, UserId=userId }, ct).ConfigureAwait(false))); }
            catch (CatalogStoreException ex) { return CatalogResult<AttributeValueDto>.Fail(ex.Code, ex.Message); }
        }

        public async Task<CatalogResult<AttributeValueDto>> UpdateAttributeValueAsync(UpdateAttributeValueCommand command, long userId, CancellationToken ct)
        {
            if (command == null) return CatalogResult<AttributeValueDto>.Fail("validation", "بيانات قيمة السمة مطلوبة.");
            try { return CatalogResult<AttributeValueDto>.Ok(ToAttributeValue(await _store.UpdateAttributeValueAsync(new UpdateAttributeValueRecord { Id=command.Id, Value=command.Value, ColorHex=command.ColorHex, SortOrder=command.SortOrder, IsActive=command.IsActive, Version=command.Version, UserId=userId }, ct).ConfigureAwait(false))); }
            catch (CatalogStoreException ex) { return CatalogResult<AttributeValueDto>.Fail(ex.Code, ex.Message); }
        }

        public async Task<CatalogResult<BarcodeLookupResult>> LookupBarcodeAsync(string code, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code)) return CatalogResult<BarcodeLookupResult>.Fail("validation", "رمز الباركود مطلوب.");
            try
            {
                var row = await _store.LookupBarcodeAsync(code, ct).ConfigureAwait(false);
                if (row == null) return CatalogResult<BarcodeLookupResult>.Fail("barcode_not_found", "الباركود غير موجود.");
                if (row.IsBarcodeRetired) return CatalogResult<BarcodeLookupResult>.Fail("barcode_retired", "الباركود متقاعد ولا يمكن استخدامه.");
                var reason = row.ProductArchivedUtc != null ? "product_archived" : !row.IsProductActive ? "product_inactive" : row.VariantArchivedUtc != null ? "variant_archived" : !row.IsVariantActive ? "variant_inactive" : !row.IsUnitActive ? "unit_inactive" : null;
                return CatalogResult<BarcodeLookupResult>.Ok(new BarcodeLookupResult { BarcodeId=row.BarcodeId, Code=row.Code, Type=row.Type, ProductId=row.ProductId, ProductName=row.ProductName, VariantId=row.VariantId, VariantName=row.VariantName, IsDefaultVariant=row.IsDefaultVariant, ProductUnitId=row.ProductUnitId, UnitId=row.UnitId, UnitName=row.UnitName, UnitSymbol=row.UnitSymbol, Numerator=row.Numerator, Denominator=row.Denominator, EffectiveSalePrice=FormatMoney(row.EffectiveSalePriceScaled), EffectivePurchasePrice=row.EffectivePurchasePriceScaled.HasValue?FormatMoney(row.EffectivePurchasePriceScaled.Value):null, IsSellable=reason==null, NotSellableReason=reason });
            }
            catch (CatalogStoreException ex) { return CatalogResult<BarcodeLookupResult>.Fail(ex.Code, ex.Message); }
        }

        public async Task<CatalogResult<CatalogProduct>> SetProductStateAsync(SetProductStateCommand command, CancellationToken ct)
        {
            if (command == null || command.ProductId <= 0 || command.Version <= 0) return CatalogResult<CatalogProduct>.Fail("validation", "معرف المنتج وإصداره مطلوبان.");
            if (command.IsActive)
            {
                var current = await _store.FindProductAsync(command.ProductId, ct).ConfigureAwait(false);
                if (current == null) return CatalogResult<CatalogProduct>.Fail("not_found", "المنتج غير موجود.");
                if (current.MigrationState == "review_required") return CatalogResult<CatalogProduct>.Fail("review_required", "يجب تصحيح بيانات المنتج القديم وحفظها قبل تفعيله.");
                if (current.ArchivedUtc != null) return CatalogResult<CatalogProduct>.Fail("archived", "لا يمكن تفعيل منتج مؤرشف.");
            }
            return await StoreResult(() => _store.SetProductStateAsync(command.ProductId, command.IsActive, command.Version, ct)).ConfigureAwait(false);
        }

        public Task<CatalogResult<CatalogProduct>> ArchiveProductAsync(ArchiveProductCommand command, CancellationToken ct)
        {
            if (command == null || command.ProductId <= 0 || command.Version <= 0) return Task.FromResult(CatalogResult<CatalogProduct>.Fail("validation", "معرف المنتج وإصداره مطلوبان."));
            return StoreResult(() => _store.ArchiveProductAsync(command.ProductId, command.Version, ct));
        }

        public Task<IReadOnlyList<ProductCategory>> ListCategoriesAsync(bool includeInactive, CancellationToken ct) { return _store.ListCategoriesAsync(includeInactive, ct); }
        public async Task<CatalogResult<ProductCategory>> SaveCategoryAsync(SaveCategoryCommand command, CancellationToken ct)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 120) return CatalogResult<ProductCategory>.Fail("validation", "اسم التصنيف مطلوب وبحد أقصى 120 حرفًا.");
            if (command.CategoryId.HasValue && command.CategoryId.Value <= 0 || command.ParentCategoryId.HasValue && command.ParentCategoryId.Value <= 0) return CatalogResult<ProductCategory>.Fail("validation", "مرجع التصنيف غير صالح.");
            command.Name=command.Name.Trim();
            try { return CatalogResult<ProductCategory>.Ok(await _store.SaveCategoryAsync(command, NormalizeKey(command.Name), ct).ConfigureAwait(false)); }
            catch (CatalogStoreException exception) { return CatalogResult<ProductCategory>.Fail(exception.Code, exception.Message); }
        }

        public Task<IReadOnlyList<UnitOfMeasurement>> ListUnitsAsync(bool includeInactive, CancellationToken ct) { return _store.ListUnitsAsync(includeInactive, ct); }
        public async Task<CatalogResult<UnitOfMeasurement>> SaveUnitAsync(SaveUnitCommand command, CancellationToken ct)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Code) || command.Code.Trim().Length > 32 || command.Code.Trim().Any(char.IsWhiteSpace) || string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 80 || string.IsNullOrWhiteSpace(command.Symbol) || command.Symbol.Trim().Length > 20) return CatalogResult<UnitOfMeasurement>.Fail("validation", "كود الوحدة بلا مسافات واسمها ورمزها مطلوبة ضمن الأطوال المسموحة.");
            if (command.UnitId.HasValue && command.UnitId.Value <= 0) return CatalogResult<UnitOfMeasurement>.Fail("validation", "معرف الوحدة غير صالح.");
            command.Code=command.Code.Trim().ToUpperInvariant(); command.Name=command.Name.Trim(); command.Symbol=command.Symbol.Trim();
            try { return CatalogResult<UnitOfMeasurement>.Ok(await _store.SaveUnitAsync(command, NormalizeKey(command.Code), ct).ConfigureAwait(false)); }
            catch (CatalogStoreException exception) { return CatalogResult<UnitOfMeasurement>.Fail(exception.Code, exception.Message); }
        }

        public Task<IReadOnlyList<ProductMigrationIssue>> ListMigrationIssuesAsync(CancellationToken ct) { return _store.ListMigrationIssuesAsync(ct); }

        public static bool TryScaleMoney(string text, out long scaled)
        {
            return MoneyPolicy.TryParseUserAmount(text, false, out scaled);
        }

        public static string FormatMoney(long scaled) { return MoneyPolicy.FormatForUser(scaled); }
        private static string ValidateProduct(string name, string description, long unitId, string type, string price, string purchasePrice, out long scaled, out long? purchaseScaled)
        {
            scaled=0; purchaseScaled=null;
            if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>200) return "اسم المنتج مطلوب وبحد أقصى 200 حرف.";
            if(description!=null&&description.Trim().Length>1000) return "وصف المنتج يجب ألا يتجاوز 1000 حرف.";
            if(unitId<=0) return "وحدة القياس الأساسية مطلوبة.";
            type=string.IsNullOrWhiteSpace(type)?ProductTypes.Stock:type.Trim().ToLowerInvariant();
            if(!ProductTypes.IsValid(type)) return "نوع المنتج غير صالح.";
            if(!TryScaleMoney(price,out scaled)) return "سعر البيع يجب أن يكون رقمًا غير سالب بدقة لا تتجاوز خانتين عشريتين.";
            if(!string.IsNullOrWhiteSpace(purchasePrice))
            {
                long value;
                if(!TryScaleMoney(purchasePrice,out value)) return "سعر الشراء يجب أن يكون رقمًا غير سالب بدقة لا تتجاوز خانتين عشريتين.";
                purchaseScaled=value;
            }
            return null;
        }
        private static void Normalize(CreateProductCommand command) { command.Name=command.Name.Trim(); command.Description=TrimToNull(command.Description); command.ProductType=string.IsNullOrWhiteSpace(command.ProductType)?ProductTypes.Stock:command.ProductType.Trim().ToLowerInvariant(); }
        private static void Normalize(UpdateProductCommand command) { command.Name=command.Name.Trim(); command.Description=TrimToNull(command.Description); command.ProductType=string.IsNullOrWhiteSpace(command.ProductType)?ProductTypes.Stock:command.ProductType.Trim().ToLowerInvariant(); }
        private static string NormalizeKey(string value) { return string.Join(" ",value.Trim().Split((char[])null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant(); }
        private static string TrimToNull(string value) { return string.IsNullOrWhiteSpace(value)?null:value.Trim(); }
        private static string Hash(CreateProductCommand command)
        {
            var config=ToRecord(command.Configuration);
            var source=string.Join("\u001f",new[]{command.Name,command.Description??"",command.CategoryId.HasValue?command.CategoryId.Value.ToString(CultureInfo.InvariantCulture):"",command.BaseUnitId.ToString(CultureInfo.InvariantCulture),command.ProductType,command.SalePrice.Trim(),string.IsNullOrWhiteSpace(command.PurchasePrice)?"":command.PurchasePrice.Trim(),command.IsActive?"1":"0",config==null?"":string.Join(",",(config.CapabilityKeys??new string[0]).OrderBy(k=>k,StringComparer.OrdinalIgnoreCase))});
            if (config != null)
            {
                var details=new StringBuilder(source);
                foreach(var attribute in config.Attributes??new ProductAttributeAssignmentConfigRecord[0])
                    details.Append("|a:").Append(attribute.AttributeId).Append(':').Append(string.Join(",",attribute.ValueIds??new long[0])).Append(':').Append(string.Join(",",attribute.Values??new string[0]));
                foreach(var unit in config.Units??new ProductUnitConfigRecord[0])
                    details.Append("|u:").Append(unit.Id).Append(':').Append(unit.UnitId).Append(':').Append(unit.ToBaseNumerator).Append(':').Append(unit.ToBaseDenominator).Append(':').Append(unit.IsSaleUnit).Append(':').Append(unit.IsPurchaseUnit).Append(':').Append(unit.IsActive);
                foreach(var variant in config.Variants??new ProductVariantConfigRecord[0])
                {
                    details.Append("|v:").Append(variant.Id).Append(':').Append(variant.Name).Append(':').Append(string.Join(",",variant.AttributeValueIds??new long[0])).Append(':').Append(variant.IsDefault).Append(':').Append(variant.IsActive).Append(':').Append(variant.Archive).Append(':').Append(variant.Sku);
                    foreach(var barcode in variant.Barcodes??new ProductBarcodeConfigRecord[0]) details.Append("|b:").Append(barcode.Id).Append(':').Append(barcode.Barcode).Append(':').Append(barcode.BarcodeType).Append(':').Append(barcode.ProductUnitId).Append(':').Append(barcode.Retire);
                }
                source=details.ToString();
            }
            using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source))).Replace("-","").ToLowerInvariant();
        }

        private async Task<Tuple<string,string>> ValidateNewCapabilities(ProductConfigurationCommand config, long? productId, CancellationToken ct)
        {
            if (config == null || _features == null) return null;
            var states = await _features.GetEffectiveFeaturesAsync(ct).ConfigureAwait(false);
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (productId.HasValue)
            {
                var detail = await _store.GetProductDetailAsync(productId.Value, ct).ConfigureAwait(false);
                if (detail != null)
                    foreach (var capability in detail.Capabilities ?? new ProductCapabilityAssignmentRecord[0])
                        if (capability.IsEnabled) existing.Add(capability.CapabilityKey);
            }
            var requested = new HashSet<string>(config.CapabilityKeys ?? new string[0], StringComparer.OrdinalIgnoreCase);
            if (config.VariantMode || (config.Attributes != null && config.Attributes.Any())) requested.Add("product-variants");
            if (requested.Contains("sizes") || requested.Contains("colors")) requested.Add("product-variants");
            foreach (var key in requested.ToArray())
            {
                var definition=FeatureRegistry.Find(key);
                if (definition != null) foreach (var dependency in definition.Dependencies) requested.Add(dependency);
            }
            foreach (var key in requested)
            {
                if (ProductFormCapabilities.Contains(key)) continue;
                if (existing.Contains(key)) continue;
                bool enabled;
                if (!states.TryGetValue(key, out enabled) || !enabled)
                    return Tuple.Create("capability_disabled", "الميزة العامة غير مفعلة: " + (FeatureRegistry.Find(key)?.ArabicName ?? key));
            }
            return null;
        }

        private static ProductConfigurationRecord ToRecord(ProductConfigurationCommand value)
        {
            if (value == null) return null;
            var capabilities = new HashSet<string>(value.CapabilityKeys ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var usesVariants = value.VariantMode || capabilities.Contains("sizes") || capabilities.Contains("colors") || (value.Attributes != null && value.Attributes.Any());
            if (usesVariants) capabilities.Add("product-variants");
            foreach (var key in capabilities.ToArray())
            {
                var definition=FeatureRegistry.Find(key);
                if (definition != null) foreach (var dependency in definition.Dependencies) capabilities.Add(dependency);
            }
            return new ProductConfigurationRecord { VariantMode=usesVariants, CapabilityKeys=capabilities.ToList(), Attributes=(value.Attributes??new ProductAttributeAssignmentCommand[0]).Select(a=>new ProductAttributeAssignmentConfigRecord { AttributeId=a.AttributeId, ValueIds=a.ValueIds, Values=a.Values, SortOrder=a.SortOrder }).ToList(), Units=(value.Units??new ProductUnitCommand[0]).Select(u=>new ProductUnitConfigRecord { Id=u.Id, UnitId=u.UnitId, ToBaseNumerator=u.Numerator, ToBaseDenominator=u.Denominator, IsBase=u.IsBase, IsSaleUnit=u.IsSaleUnit, IsPurchaseUnit=u.IsPurchaseUnit, IsActive=u.IsActive, Version=u.Version }).ToList(), Variants=(value.Variants??new ProductVariantCommand[0]).Select(v=>new ProductVariantConfigRecord { Id=v.Id, Name=v.Name, AttributeValueIds=v.AttributeValueIds, IsDefault=v.IsDefault, IsActive=v.IsActive, Archive=v.Archive, Sku=v.Sku, Barcodes=(v.Barcodes??new ProductBarcodeCommand[0]).Select(b=>new ProductBarcodeConfigRecord { Id=b.Id, Barcode=b.Code, BarcodeType=b.Type, ProductUnitId=b.ProductUnitId, IsPrimary=b.IsPrimary, Retire=b.Retire }).ToList() }).ToList() };
        }

        private static AttributeDto ToAttribute(ProductAttributeRecord value) { return new AttributeDto { Id=value.Id, Key=value.AttributeKey, Name=value.Name, Kind=value.Kind, IsSystem=value.IsSystem, IsActive=value.IsActive, SortOrder=value.SortOrder, Version=value.Version, Values=(value.Values??new ProductAttributeValueRecord[0]).Select(ToAttributeValue).ToList() }; }
        private static AttributeValueDto ToAttributeValue(ProductAttributeValueRecord value) { return new AttributeValueDto { Id=value.Id, AttributeId=value.AttributeId, Value=value.Value, ColorHex=value.ColorHex, SortOrder=value.SortOrder, IsActive=value.IsActive, Version=value.Version }; }
        private static CatalogProductDetail ToDetail(CatalogProductDetailRecord value, IReadOnlyDictionary<string, bool> featureStates)
        {
            var p=ToProduct(value.Product);
            var assigned=(value.Capabilities??new ProductCapabilityAssignmentRecord[0]).ToDictionary(c=>c.CapabilityKey,StringComparer.OrdinalIgnoreCase);
            var capabilityKeys=FeatureRegistry.All.Where(item => item.Kind == FeatureKinds.Capability && !item.IsCore).Select(item => item.Key).ToArray();
            var capabilities=capabilityKeys.Select(key => { ProductCapabilityAssignmentRecord row; var isAssigned=assigned.TryGetValue(key,out row) && row.IsEnabled; var globallyEnabled=true; var hasState=featureStates != null && featureStates.TryGetValue(key,out globallyEnabled); if(!hasState) globallyEnabled=true; return new ProductCapabilityItem { Key=key, IsAssigned=isAssigned, IsGloballyEnabled=globallyEnabled, State=isAssigned && globallyEnabled?"enabled-and-applicable":isAssigned?"assigned-but-disabled":"not-assigned"}; }).ToList();
            var attrs=(value.Attributes??new ProductAttributeAssignmentRecord[0]).Select(a=>new ProductAttributeDetail { AttributeId=a.AttributeId, Key=a.Key, Name=a.Name, Kind=a.Kind, SortOrder=a.SortOrder, Values=(a.Values??new ProductAttributeValueAssignmentRecord[0]).Select(v=>new ProductAttributeValueDetail { ValueId=v.ValueId, Value=v.Value, ColorHex=v.ColorHex, SortOrder=v.SortOrder }).ToList() }).ToList();
            var units=(value.Units??new ProductUnitRecord[0]).Select(u=>new ProductUnitDetail { Id=u.Id, UnitId=u.UnitId, Code=u.Code, Name=u.Name, Symbol=u.Symbol, Numerator=u.ToBaseNumerator, Denominator=u.ToBaseDenominator, IsBase=u.IsBase, IsSaleUnit=u.IsSaleUnit, IsPurchaseUnit=u.IsPurchaseUnit, IsActive=u.IsActive, IsLocked=u.IsLocked, Version=u.Version }).ToList();
            var variants=(value.Variants??new ProductVariantRecord[0]).Select(v=>new ProductVariantDetail { Id=v.Id, Name=v.Name, IsDefault=v.IsDefault, IsActive=v.IsActive, IsArchived=v.ArchivedUtc!=null, AttributeValueIds=v.AttributeValueIds??new long[0], Sku=v.Sku==null?null:v.Sku.Sku, SalePriceOverride=v.SellPriceOverrideScaled.HasValue?FormatMoney(v.SellPriceOverrideScaled.Value):null, PurchasePriceOverride=v.PurchasePriceOverrideScaled.HasValue?FormatMoney(v.PurchasePriceOverrideScaled.Value):null, EffectiveSalePrice=FormatMoney(v.SellPriceOverrideScaled??value.Product.SellPriceScaled), EffectivePurchasePrice=(v.PurchasePriceOverrideScaled??value.Product.PurchasePriceScaled).HasValue?FormatMoney((v.PurchasePriceOverrideScaled??value.Product.PurchasePriceScaled).Value):null, Version=v.Version, Barcodes=(v.Barcodes??new ProductBarcodeRecord[0]).Select(b=>new ProductBarcodeDetail { Id=b.Id, Code=b.Barcode, Type=b.BarcodeType, Format=b.Format, ProductUnitId=b.ProductUnitId, IsPrimary=b.IsPrimary, IsRetired=b.RetiredUtc!=null }).ToList() }).ToList();
            return new CatalogProductDetail { Id=p.Id, Name=p.Name, Description=p.Description, CategoryId=p.CategoryId, CategoryName=p.CategoryName, BaseUnitId=p.BaseUnitId, BaseUnitName=p.BaseUnitName, UnitSymbol=p.UnitSymbol, ProductType=p.ProductType, SalePrice=p.SalePrice, PurchasePrice=p.PurchasePrice, IsActive=p.IsActive, IsArchived=p.IsArchived, MigrationState=p.MigrationState, UpdatedUtc=p.UpdatedUtc, VariantMode=value.VariantMode, Capabilities=capabilities, Attributes=attrs, Units=units, Variants=variants, Version=p.Version };
        }
        private async Task<CatalogResult<CatalogProduct>> StoreResult(Func<Task<CatalogProductRecord>> action)
        {
            try { var value=await action().ConfigureAwait(false); return CatalogResult<CatalogProduct>.Ok(ToProduct(value)); }
            catch(CatalogStoreException exception) { return CatalogResult<CatalogProduct>.Fail(exception.Code,exception.Message); }
        }
        private static CatalogProduct ToProduct(CatalogProductRecord item) { return new CatalogProduct { Id=item.Id,Name=item.Name,Description=item.Description,CategoryId=item.CategoryId,CategoryName=item.CategoryName,BaseUnitId=item.BaseUnitId,BaseUnitName=item.BaseUnitName,UnitSymbol=item.UnitSymbol,ProductType=item.ProductType,SalePrice=FormatMoney(item.SellPriceScaled),PurchasePrice=item.PurchasePriceScaled.HasValue?FormatMoney(item.PurchasePriceScaled.Value):null,IsActive=item.IsActive,IsArchived=item.ArchivedUtc!=null,MigrationState=item.MigrationState,PrimarySku=item.PrimarySku,VariantCount=item.VariantCount,BarcodeCount=item.BarcodeCount,PrimaryBarcode=item.PrimaryBarcode,SizeValues=item.SizeValues??new string[0],ColorValues=item.ColorValues??new string[0],HasBatchTracking=item.HasBatchTracking,HasExpiryTracking=item.HasExpiryTracking,HasSerialTracking=item.HasSerialTracking,HasImeiTracking=item.HasImeiTracking,Version=item.Version,UpdatedUtc=item.UpdatedUtc,AvailableQuantityScaled=item.AvailableQuantityScaled }; }
    }
}
