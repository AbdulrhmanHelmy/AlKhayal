using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Inventory
{
    public sealed class InventoryResult<T>
    {
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public T Value { get; set; }
        public static InventoryResult<T> Ok(T value) { return new InventoryResult<T> { Succeeded=true, Value=value }; }
        public static InventoryResult<T> Fail(string code,string message) { return new InventoryResult<T> { ErrorCode=code, ErrorMessage=message }; }
    }

    public sealed class WarehouseRecord
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Location { get; set; }
        public bool IsActive { get; set; }
        public bool IsDefault { get; set; }
        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }
        public long ProductCount { get; set; }
        public string Quantity { get; set; }
        public string Value { get; set; }
        public long QuantityScaled { get; set; }
        public long ValueScaled { get; set; }
    }

    public sealed class WarehouseCommand
    {
        public long? Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Location { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsDefault { get; set; }
    }

    public sealed class InventoryUnitInput
    {
        public string Serial { get; set; }
        public IReadOnlyList<string> Imeis { get; set; }
    }

    public sealed class InventoryLineInput
    {
        public long VariantId { get; set; }
        public long ProductUnitId { get; set; }
        public string Quantity { get; set; }
        public string UnitCost { get; set; }
        public string SalePrice { get; set; }
        public string LotNumber { get; set; }
        public string ExpirationDate { get; set; }
        public IReadOnlyList<InventoryUnitInput> PhysicalUnits { get; set; }
        public long? ExpectedVersion { get; set; }
        public string ExpectedQuantity { get; set; }
        public long? RequiredOriginMovementId { get; set; }
    }

    public sealed class InventoryPostCommand
    {
        public string OperationKey { get; set; }
        public string Type { get; set; }
        public long WarehouseId { get; set; }
        public long? DestinationWarehouseId { get; set; }
        public string Reason { get; set; }
        public string Notes { get; set; }
        public long UserId { get; set; }
        public long? ReversesDocumentId { get; set; }
        public IReadOnlyList<InventoryLineInput> Lines { get; set; }
    }

    public sealed class InventoryPostResult
    {
        public long DocumentId { get; set; }
        public string Reference { get; set; }
        public bool Replayed { get; set; }
    }

    public sealed class InventoryBalanceRecord
    {
        public long WarehouseId { get; set; }
        public long ProductId { get; set; }
        public long VariantId { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public long? BatchId { get; set; }
        public string LotNumber { get; set; }
        public string ExpirationDate { get; set; }
        public long QuantityBaseScaled { get; set; }
        public long ValueScaled { get; set; }
        public long Version { get; set; }
        public long? SalePriceScaled { get; set; }
        public bool CostBasisComplete { get; set; }
        public long? UnresolvedLayerId { get; set; }
        public bool HasReconciledCostBasis { get; set; }
    }
    public sealed class InventoryValuation
    {
        public long WarehouseId { get; set; }
        public long? ProductId { get; set; }
        public long QuantityBaseScaled { get; set; }
        public string CostValue { get; set; }
        public string PotentialSellingValue { get; set; }
        public string EstimatedGrossMargin { get; set; }
        public bool CostComplete { get; set; }
        public bool SellingComplete { get; set; }
        public long VariantCount { get; set; }
    }

    public sealed class InventoryMovementRecord
    {
        public long Id { get; set; }
        public long DocumentId { get; set; }
        public string DocumentType { get; set; }
        public string CreatedUtc { get; set; }
        public long WarehouseId { get; set; }
        public string WarehouseName { get; set; }
        public string RelatedWarehouseName { get; set; }
        public long ProductId { get; set; }
        public long VariantId { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public string LotNumber { get; set; }
        public long? BatchId { get; set; }
        public long QuantityBaseScaled { get; set; }
        public long ValueScaled { get; set; }
        public long ProductUnitId { get; set; }
        public long? UnitCostScaled { get; set; }
        public string UnitName { get; set; }
        public long EnteredQuantityScaled { get; set; }
        public long ToBaseNumerator { get; set; }
        public long ToBaseDenominator { get; set; }
        public long UserId { get; set; }
        public string Reason { get; set; }
        public string Notes { get; set; }
    }

    public sealed class InventoryPage<T>
    {
        public IReadOnlyList<T> Items { get; set; }
        public long TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public sealed class InventoryTrackingRecord
    {
        public long PhysicalUnitId { get; set; }
        public long VariantId { get; set; }
        public long? BatchId { get; set; }
        public long? WarehouseId { get; set; }
        public string Serial { get; set; }
        public IReadOnlyList<string> Imeis { get; set; }
    }
    public sealed class InventoryRebuildResult
    {
        public long DocumentId { get; set; }
        public string BackupPath { get; set; }
        public long BalanceRows { get; set; }
    }

    public sealed class InventoryCountRecord
    {
        public long Id { get; set; }
        public long WarehouseId { get; set; }
        public string Status { get; set; }
        public string CreatedUtc { get; set; }
        public IReadOnlyList<InventoryCountLine> Lines { get; set; }
    }
    public sealed class InventoryCountLine
    {
        public long VariantId { get; set; }
        public long? BatchId { get; set; }
        public string LotNumber { get; set; }
        public string ExpirationDate { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public long RecordedQuantityScaled { get; set; }
        public long CountedQuantityScaled { get; set; }
        public string CountedQuantity { get; set; }
        public long BalanceVersion { get; set; }
        public string UnitCost { get; set; }
        public bool SerialTracked { get; set; }
        public IReadOnlyList<InventoryUnitInput> PhysicalUnits { get; set; }
    }
    public sealed class InventoryCountPostCommand
    {
        public string OperationKey { get; set; }
        public string Reason { get; set; }
        public string Notes { get; set; }
        public long UserId { get; set; }
        public IReadOnlyList<InventoryCountLine> Lines { get; set; }
    }

    public sealed class InventoryCostReconciliationCommand
    {
        public string OperationKey { get; set; }
        public long LayerId { get; set; }
        public long ExpectedVersion { get; set; }
        public string UnitCost { get; set; }
        public string Evidence { get; set; }
        public long UserId { get; set; }
    }

    public interface IInventoryStore
    {
        Task<InventoryResult<long>> ReconcileCostAsync(InventoryCostReconciliationCommand command,string requestHash,CancellationToken ct);
        Task<IReadOnlyList<WarehouseRecord>> WarehousesAsync(CancellationToken ct);
        Task<InventoryResult<WarehouseRecord>> SaveWarehouseAsync(WarehouseCommand command,bool multipleWarehouses,CancellationToken ct);
        Task<InventoryResult<InventoryPostResult>> PostAsync(InventoryPostCommand command,string requestHash,CancellationToken ct);
        Task<IReadOnlyList<InventoryBalanceRecord>> BalancesAsync(long? warehouseId,long? productId,CancellationToken ct);
        Task<InventoryPage<InventoryBalanceRecord>> BalancePageAsync(long warehouseId,string search,int page,int pageSize,CancellationToken ct);
        Task<InventoryValuation> ValuationAsync(long warehouseId,long? productId,CancellationToken ct);
        Task<InventoryPage<InventoryMovementRecord>> MovementsAsync(long? warehouseId,long? productId,string type,string reference,string from,string to,int page,int pageSize,CancellationToken ct);
        Task<InventoryResult<InventoryCountRecord>> CreateCountAsync(long warehouseId,long userId,IReadOnlyList<long> variantIds,CancellationToken ct);
        Task<IReadOnlyList<InventoryCountRecord>> CountsAsync(long warehouseId,CancellationToken ct);
        Task<InventoryResult<InventoryPostResult>> PostCountAsync(long countId,InventoryCountPostCommand command,string requestHash,CancellationToken ct);
        Task<InventoryResult<InventoryCountRecord>> GetCountAsync(long countId,CancellationToken ct);
        Task<InventoryResult<InventoryCountRecord>> CancelCountAsync(long countId,long userId,CancellationToken ct);
        Task<InventoryResult<InventoryTrackingRecord>> TrackingAsync(string identifier,CancellationToken ct);
        Task<InventoryResult<InventoryPostResult>> ReverseAsync(long documentId,InventoryPostCommand command,string requestHash,CancellationToken ct);
        Task<bool> VerifyProjectionAsync(CancellationToken ct);
        Task<InventoryResult<InventoryRebuildResult>> RebuildProjectionAsync(long userId,CancellationToken ct);
    }
}
