using System;
using System.Collections.Generic;

namespace AlKhayal.Application.Catalog
{
    public sealed class CatalogProductDetail
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
        public string SalePrice { get; set; }
        public string PurchasePrice { get; set; }
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }
        public string MigrationState { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public bool VariantMode { get; set; }
        public IReadOnlyList<ProductCapabilityItem> Capabilities { get; set; }
        public IReadOnlyList<ProductAttributeDetail> Attributes { get; set; }
        public IReadOnlyList<ProductUnitDetail> Units { get; set; }
        public IReadOnlyList<ProductVariantDetail> Variants { get; set; }
        public long Version { get; set; }
    }

    public sealed class ProductCapabilityItem
    {
        public string Key { get; set; }
        public bool IsAssigned { get; set; }
        public bool IsGloballyEnabled { get; set; }
        public string State { get; set; }
    }

    public sealed class ProductAttributeDetail
    {
        public long AttributeId { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public int SortOrder { get; set; }
        public IReadOnlyList<ProductAttributeValueDetail> Values { get; set; }
    }

    public sealed class ProductAttributeValueDetail
    {
        public long ValueId { get; set; }
        public string Value { get; set; }
        public string ColorHex { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class ProductUnitDetail
    {
        public long Id { get; set; }
        public long UnitId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public long Numerator { get; set; }
        public long Denominator { get; set; }
        public bool IsBase { get; set; }
        public bool IsSaleUnit { get; set; }
        public bool IsPurchaseUnit { get; set; }
        public bool IsActive { get; set; }
        public bool IsLocked { get; set; }
        public long Version { get; set; }
    }

    public sealed class ProductVariantDetail
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }
        public IReadOnlyList<long> AttributeValueIds { get; set; }
        public string Sku { get; set; }
        public string SalePriceOverride { get; set; }
        public string PurchasePriceOverride { get; set; }
        public string EffectiveSalePrice { get; set; }
        // Exact base-unit money for conversion before rounding the selected sale unit.
        public string EffectiveSalePriceExact { get; set; }
        public string EffectivePurchasePrice { get; set; }
        public long Version { get; set; }
        public IReadOnlyList<ProductBarcodeDetail> Barcodes { get; set; }
    }

    public sealed class ProductBarcodeDetail
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Type { get; set; }
        public string Format { get; set; }
        public long ProductUnitId { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsRetired { get; set; }
    }

    public sealed class ProductConfigurationCommand
    {
        public bool VariantMode { get; set; }
        public IReadOnlyList<string> CapabilityKeys { get; set; }
        public IReadOnlyList<ProductAttributeAssignmentCommand> Attributes { get; set; }
        public IReadOnlyList<ProductUnitCommand> Units { get; set; }
        public IReadOnlyList<ProductVariantCommand> Variants { get; set; }
    }

    public sealed class ProductAttributeAssignmentCommand
    {
        public long AttributeId { get; set; }
        public IReadOnlyList<long> ValueIds { get; set; }
        // Free-text values are intentionally product-form input, not a required catalog picker.
        // The store resolves them to the system size/color values inside the aggregate transaction.
        public IReadOnlyList<string> Values { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class ProductUnitCommand
    {
        public long? Id { get; set; }
        public long UnitId { get; set; }
        public long Numerator { get; set; }
        public long Denominator { get; set; }
        public bool IsBase { get; set; }
        public bool IsSaleUnit { get; set; }
        public bool IsPurchaseUnit { get; set; }
        public bool IsActive { get; set; }
        public long? Version { get; set; }
    }

    public sealed class ProductVariantCommand
    {
        public long? Id { get; set; }
        public string Name { get; set; }
        public IReadOnlyList<long> AttributeValueIds { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public bool Archive { get; set; }
        public string SalePriceOverride { get; set; }
        public string PurchasePriceOverride { get; set; }
        public string Sku { get; set; }
        public IReadOnlyList<ProductBarcodeCommand> Barcodes { get; set; }
    }

    public sealed class ProductBarcodeCommand
    {
        public long? Id { get; set; }
        public string Code { get; set; }
        public string Type { get; set; }
        public long? ProductUnitId { get; set; }
        public bool IsPrimary { get; set; }
        public bool Retire { get; set; }
    }

    public sealed class BarcodeLookupResult
    {
        public long BarcodeId { get; set; }
        public string Code { get; set; }
        public string Type { get; set; }
        public long ProductId { get; set; }
        public string ProductName { get; set; }
        public long VariantId { get; set; }
        public string VariantName { get; set; }
        public bool IsDefaultVariant { get; set; }
        public long ProductUnitId { get; set; }
        public long UnitId { get; set; }
        public string UnitName { get; set; }
        public string UnitSymbol { get; set; }
        public long Numerator { get; set; }
        public long Denominator { get; set; }
        public string EffectiveSalePrice { get; set; }
        public string EffectivePurchasePrice { get; set; }
        public bool IsSellable { get; set; }
        public string NotSellableReason { get; set; }
    }

    public sealed class AttributeDto
    {
        public long Id { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public long Version { get; set; }
        public IReadOnlyList<AttributeValueDto> Values { get; set; }
    }

    public sealed class AttributeValueDto
    {
        public long Id { get; set; }
        public long AttributeId { get; set; }
        public string Value { get; set; }
        public string ColorHex { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public long Version { get; set; }
    }

    public sealed class SaveAttributeCommand
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class UpdateAttributeCommand
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public long Version { get; set; }
    }

    public sealed class SaveAttributeValueCommand
    {
        public long AttributeId { get; set; }
        public string Value { get; set; }
        public string ColorHex { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class UpdateAttributeValueCommand
    {
        public long Id { get; set; }
        public string Value { get; set; }
        public string ColorHex { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public long Version { get; set; }
    }
}
