using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Catalog
{
    public sealed class CatalogProductRecord
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long? CategoryId { get; set; }
        public string CategoryName { get; set; }
        public long BaseUnitId { get; set; }
        public string BaseUnitName { get; set; }
        public string UnitSymbol { get; set; }
        public string ProductType { get; set; }
        public long SellPriceScaled { get; set; }
        public long? PurchasePriceScaled { get; set; }
        public bool IsActive { get; set; }
        public string ArchivedUtc { get; set; }
        public string MigrationState { get; set; }
        public string PrimarySku { get; set; }
        public int VariantCount { get; set; }
        public int BarcodeCount { get; set; }
        public string PrimaryBarcode { get; set; }
        public IReadOnlyList<string> SizeValues { get; set; }
        public IReadOnlyList<string> ColorValues { get; set; }
        public bool HasExpiryTracking { get; set; }
        public bool HasBatchTracking { get; set; }
        public bool HasSerialTracking { get; set; }
        public bool HasImeiTracking { get; set; }
        public bool VariantMode { get; set; }
        public long Version { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public long AvailableQuantityScaled { get; set; }
    }

    public sealed class ProductStoreQuery
    {
        public string Search { get; set; }
        public long? CategoryId { get; set; }
        public bool? IsActive { get; set; }
        public bool IncludeArchived { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
        public long? PosWarehouseId { get; set; }
        public string SinceDate { get; set; }
    }

    public sealed class ProductStorePage
    {
        public IReadOnlyList<CatalogProductRecord> Items { get; set; }
        public long TotalCount { get; set; }
    }

    public sealed class CreateProductRecord
    {
        public string OperationKey { get; set; }
        public string RequestHash { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long? CategoryId { get; set; }
        public long BaseUnitId { get; set; }
        public string ProductType { get; set; }
        public long SellPriceScaled { get; set; }
        public long? PurchasePriceScaled { get; set; }
        public bool IsActive { get; set; }
        public long UserId { get; set; }
        public bool VariantMode { get; set; }
        public ProductConfigurationRecord Configuration { get; set; }
    }

    public sealed class UpdateProductRecord
    {
        public long ProductId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long? CategoryId { get; set; }
        public long BaseUnitId { get; set; }
        public string ProductType { get; set; }
        public long SellPriceScaled { get; set; }
        public long? PurchasePriceScaled { get; set; }
        public bool IsActive { get; set; }
        public long Version { get; set; }
        public long UserId { get; set; }
        public bool VariantMode { get; set; }
        public ProductConfigurationRecord Configuration { get; set; }
    }

    public sealed class CatalogProductDetailRecord
    {
        public CatalogProductRecord Product { get; set; }
        public bool VariantMode { get; set; }
        public IReadOnlyList<ProductCapabilityAssignmentRecord> Capabilities { get; set; }
        public IReadOnlyList<ProductAttributeAssignmentRecord> Attributes { get; set; }
        public IReadOnlyList<ProductUnitRecord> Units { get; set; }
        public IReadOnlyList<ProductVariantRecord> Variants { get; set; }
    }

    public sealed class ProductCapabilityAssignmentRecord
    {
        public string CapabilityKey { get; set; }
        public bool IsEnabled { get; set; }
        public string AssignedUtc { get; set; }
        public string UpdatedUtc { get; set; }
        public long ChangedByUserId { get; set; }
    }

    public sealed class ProductAttributeAssignmentRecord
    {
        public long AttributeId { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public int SortOrder { get; set; }
        public IReadOnlyList<ProductAttributeValueAssignmentRecord> Values { get; set; }
    }

    public sealed class ProductAttributeValueAssignmentRecord
    {
        public long ValueId { get; set; }
        public string Value { get; set; }
        public string ColorHex { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class ProductUnitRecord
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public long UnitId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public long ToBaseNumerator { get; set; }
        public long ToBaseDenominator { get; set; }
        public bool IsBase { get; set; }
        public bool IsSaleUnit { get; set; }
        public bool IsPurchaseUnit { get; set; }
        public bool IsActive { get; set; }
        public string FirstUsedUtc { get; set; }
        public long Version { get; set; }
        public bool IsLocked { get; set; }
    }

    public sealed class ProductVariantRecord
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public string Name { get; set; }
        public string NormalizedName { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public string CombinationKey { get; set; }
        public long? SellPriceOverrideScaled { get; set; }
        public long? PurchasePriceOverrideScaled { get; set; }
        public string ArchivedUtc { get; set; }
        public long Version { get; set; }
        public IReadOnlyList<long> AttributeValueIds { get; set; }
        public ProductSkuRecord Sku { get; set; }
        public IReadOnlyList<ProductBarcodeRecord> Barcodes { get; set; }
    }

    public sealed class ProductSkuRecord
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public long VariantId { get; set; }
        public string Sku { get; set; }
        public string NormalizedSku { get; set; }
        public string RetiredUtc { get; set; }
        public long? RetiredByUserId { get; set; }
        public string CreatedUtc { get; set; }
        public long CreatedByUserId { get; set; }
    }

    public sealed class ProductBarcodeRecord
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public long VariantId { get; set; }
        public long ProductUnitId { get; set; }
        public string Barcode { get; set; }
        public string NormalizedBarcode { get; set; }
        public string BarcodeType { get; set; }
        public string Format { get; set; }
        public bool IsPrimary { get; set; }
        public string RetiredUtc { get; set; }
        public long? RetiredByUserId { get; set; }
        public string CreatedUtc { get; set; }
        public long CreatedByUserId { get; set; }
    }

    public sealed class ProductConfigurationRecord
    {
        public bool VariantMode { get; set; }
        public IReadOnlyList<string> CapabilityKeys { get; set; }
        public IReadOnlyList<ProductAttributeAssignmentConfigRecord> Attributes { get; set; }
        public IReadOnlyList<ProductUnitConfigRecord> Units { get; set; }
        public IReadOnlyList<ProductVariantConfigRecord> Variants { get; set; }
    }

    public sealed class ProductAttributeAssignmentConfigRecord
    {
        public long AttributeId { get; set; }
        public IReadOnlyList<long> ValueIds { get; set; }
        public IReadOnlyList<string> Values { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class ProductUnitConfigRecord
    {
        public long? Id { get; set; }
        public long UnitId { get; set; }
        public long ToBaseNumerator { get; set; }
        public long ToBaseDenominator { get; set; }
        public bool IsBase { get; set; }
        public bool IsSaleUnit { get; set; }
        public bool IsPurchaseUnit { get; set; }
        public bool IsActive { get; set; }
        public long? Version { get; set; }
    }

    public sealed class ProductVariantConfigRecord
    {
        public long? Id { get; set; }
        public string Name { get; set; }
        public IReadOnlyList<long> AttributeValueIds { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public bool Archive { get; set; }
        public long? SellPriceOverrideScaled { get; set; }
        public long? PurchasePriceOverrideScaled { get; set; }
        public string Sku { get; set; }
        public IReadOnlyList<ProductBarcodeConfigRecord> Barcodes { get; set; }
    }

    public sealed class ProductBarcodeConfigRecord
    {
        public long? Id { get; set; }
        public string Barcode { get; set; }
        public string BarcodeType { get; set; }
        public long? ProductUnitId { get; set; }
        public bool IsPrimary { get; set; }
        public bool Retire { get; set; }
    }

    public sealed class ProductAttributeRecord
    {
        public long Id { get; set; }
        public string AttributeKey { get; set; }
        public string NormalizedKey { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public long Version { get; set; }
        public IReadOnlyList<ProductAttributeValueRecord> Values { get; set; }
    }

    public sealed class ProductAttributeValueRecord
    {
        public long Id { get; set; }
        public long AttributeId { get; set; }
        public string Value { get; set; }
        public string NormalizedValue { get; set; }
        public string ColorHex { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public long Version { get; set; }
    }

    public sealed class CreateAttributeRecord
    {
        public string AttributeKey { get; set; }
        public string NormalizedKey { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public long UserId { get; set; }
    }

    public sealed class UpdateAttributeRecord
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public long Version { get; set; }
        public long UserId { get; set; }
    }

    public sealed class CreateAttributeValueRecord
    {
        public long AttributeId { get; set; }
        public string Value { get; set; }
        public string NormalizedValue { get; set; }
        public string ColorHex { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public long UserId { get; set; }
    }

    public sealed class UpdateAttributeValueRecord
    {
        public long Id { get; set; }
        public string Value { get; set; }
        public string NormalizedValue { get; set; }
        public string ColorHex { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public long Version { get; set; }
        public long UserId { get; set; }
    }

    public sealed class BarcodeLookupRecord
    {
        public long BarcodeId { get; set; }
        public string Code { get; set; }
        public string Type { get; set; }
        public bool IsBarcodeRetired { get; set; }
        public long ProductId { get; set; }
        public string ProductName { get; set; }
        public bool IsProductActive { get; set; }
        public string ProductArchivedUtc { get; set; }
        public long VariantId { get; set; }
        public string VariantName { get; set; }
        public bool IsDefaultVariant { get; set; }
        public bool IsVariantActive { get; set; }
        public string VariantArchivedUtc { get; set; }
        public long ProductUnitId { get; set; }
        public long UnitId { get; set; }
        public string UnitName { get; set; }
        public string UnitSymbol { get; set; }
        public bool IsUnitActive { get; set; }
        public long Numerator { get; set; }
        public long Denominator { get; set; }
        public long EffectiveSalePriceScaled { get; set; }
        public long? EffectivePurchasePriceScaled { get; set; }
    }

    public interface ICatalogFeatureReader
    {
        Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(CancellationToken ct);
        Task<bool> IsFeatureEnabledAsync(string featureKey, CancellationToken ct);
    }

    public interface ICatalogStore
    {
        Task<ProductStorePage> ListProductsAsync(ProductStoreQuery query, CancellationToken ct);
        Task<CatalogProductRecord> FindProductAsync(long productId, CancellationToken ct);
        Task<CatalogProductDetailRecord> GetProductDetailAsync(long productId, CancellationToken ct);
        Task<CatalogProductRecord> CreateAggregateProductAsync(CreateProductRecord record, CancellationToken ct);
        Task<CatalogProductRecord> UpdateAggregateProductAsync(UpdateProductRecord record, CancellationToken ct);
        Task<CatalogProductRecord> CreateProductAsync(CreateProductRecord record, CancellationToken ct);
        Task<CatalogProductRecord> UpdateProductAsync(UpdateProductRecord record, CancellationToken ct);
        Task<CatalogProductRecord> SetProductStateAsync(long productId, bool isActive, long version, CancellationToken ct);
        Task<CatalogProductRecord> ArchiveProductAsync(long productId, long version, CancellationToken ct);
        Task<IReadOnlyList<ProductCategory>> ListCategoriesAsync(bool includeInactive, CancellationToken ct);
        Task<ProductCategory> SaveCategoryAsync(SaveCategoryCommand command, string normalizedName, CancellationToken ct);
        Task<IReadOnlyList<UnitOfMeasurement>> ListUnitsAsync(bool includeInactive, CancellationToken ct);
        Task<UnitOfMeasurement> SaveUnitAsync(SaveUnitCommand command, string normalizedCode, CancellationToken ct);
        Task<IReadOnlyList<ProductMigrationIssue>> ListMigrationIssuesAsync(CancellationToken ct);
        Task<IReadOnlyList<ProductAttributeRecord>> ListAttributesAsync(string kind, bool includeInactive, CancellationToken ct);
        Task<ProductAttributeRecord> GetAttributeAsync(long id, CancellationToken ct);
        Task<ProductAttributeRecord> CreateAttributeAsync(CreateAttributeRecord record, CancellationToken ct);
        Task<ProductAttributeRecord> UpdateAttributeAsync(UpdateAttributeRecord record, CancellationToken ct);
        Task<ProductAttributeValueRecord> GetAttributeValueAsync(long id, CancellationToken ct);
        Task<ProductAttributeValueRecord> CreateAttributeValueAsync(CreateAttributeValueRecord record, CancellationToken ct);
        Task<ProductAttributeValueRecord> UpdateAttributeValueAsync(UpdateAttributeValueRecord record, CancellationToken ct);
        Task<BarcodeLookupRecord> LookupBarcodeAsync(string normalizedBarcode, CancellationToken ct);
    }

    public sealed class CatalogStoreException : Exception
    {
        public CatalogStoreException(string code, string message) : base(message) { Code = code; }
        public string Code { get; private set; }
    }
}
