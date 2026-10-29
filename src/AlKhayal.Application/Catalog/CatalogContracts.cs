using System;
using System.Collections.Generic;

namespace AlKhayal.Application.Catalog
{
    public static class ProductTypes
    {
        public const string Stock = "stock";
        public const string Service = "service";
        public const string NonStock = "non_stock";
        public static bool IsValid(string value) { return value == Stock || value == Service || value == NonStock; }
    }

    public sealed class CatalogProduct
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
        public long Version { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public long AvailableQuantityScaled { get; set; }
    }

    public sealed class CatalogPage
    {
        public IReadOnlyList<CatalogProduct> Items { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long TotalCount { get; set; }
    }

    public sealed class ProductCategory
    {
        public long Id { get; set; }
        public long? ParentCategoryId { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public sealed class UnitOfMeasurement
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public bool IsActive { get; set; }
        public bool IsSystem { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public sealed class ProductMigrationIssue
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public string ProductName { get; set; }
        public string IssueCode { get; set; }
        public string Details { get; set; }
        public string OriginalPriceType { get; set; }
        public string OriginalPriceText { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public sealed class ProductQuery
    {
        public string Search { get; set; }
        public long? CategoryId { get; set; }
        public bool? IsActive { get; set; }
        public bool IncludeArchived { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public long? PosWarehouseId { get; set; }
    }

    public sealed class CreateProductCommand
    {
        public string OperationKey { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long? CategoryId { get; set; }
        public long BaseUnitId { get; set; }
        public string ProductType { get; set; }
        public string SalePrice { get; set; }
        public string PurchasePrice { get; set; }
        public bool IsActive { get; set; }
        public long UserId { get; set; }
        public ProductConfigurationCommand Configuration { get; set; }
    }

    public sealed class UpdateProductCommand
    {
        public long ProductId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long? CategoryId { get; set; }
        public long BaseUnitId { get; set; }
        public string ProductType { get; set; }
        public string SalePrice { get; set; }
        public string PurchasePrice { get; set; }
        public bool IsActive { get; set; }
        public long Version { get; set; }
        public long UserId { get; set; }
        public ProductConfigurationCommand Configuration { get; set; }
    }

    public sealed class SetProductStateCommand { public long ProductId { get; set; } public bool IsActive { get; set; } public long Version { get; set; } }
    public sealed class ArchiveProductCommand { public long ProductId { get; set; } public long Version { get; set; } }
    public sealed class SaveCategoryCommand { public long? CategoryId { get; set; } public long? ParentCategoryId { get; set; } public string Name { get; set; } public bool IsActive { get; set; } public int SortOrder { get; set; } }
    public sealed class SaveUnitCommand { public long? UnitId { get; set; } public string Code { get; set; } public string Name { get; set; } public string Symbol { get; set; } public bool IsActive { get; set; } }

    public sealed class CatalogResult<T>
    {
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public T Value { get; set; }
        public static CatalogResult<T> Ok(T value) { return new CatalogResult<T> { Succeeded=true, Value=value }; }
        public static CatalogResult<T> Fail(string code, string message) { return new CatalogResult<T> { ErrorCode=code, ErrorMessage=message }; }
    }
}
