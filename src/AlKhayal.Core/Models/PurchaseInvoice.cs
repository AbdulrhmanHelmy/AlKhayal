using System.Collections.Generic;

namespace AlKhayal.Core.Models
{
    public sealed class PurchaseInvoice
    {
        public long Id { get; set; }
        public long SupplierId { get; set; }
        public string SupplierName { get; set; }
        public long WarehouseId { get; set; }
        public string WarehouseName { get; set; }
        public string InvoiceNumber { get; set; }
        public string InvoiceDate { get; set; }
        public string PaymentMode { get; set; }
        public long? TreasuryAccountId { get; set; }
        public string TreasuryAccountName { get; set; }
        public long? TreasuryMovementId { get; set; }
        public string Status { get; set; }
        public bool HasReturns { get; set; }
        public bool IsFullyReturned { get; set; }
        public string ReturnStatus => !HasReturns ? "none" : IsFullyReturned ? "full" : "partial";
        public string Notes { get; set; }
        public long TotalScaled { get; set; }
        public string TotalFormatted { get; set; }
        public long Version { get; set; }
        public long? InventoryDocumentId { get; set; }
        public string PostOperationKey { get; set; }
        public string PostRequestHash { get; set; }
        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }
        public List<PurchaseInvoiceLine> Lines { get; set; } = new List<PurchaseInvoiceLine>();
    }

    public sealed class PurchaseInvoiceLine
    {
        public long Id { get; set; }
        public long VariantId { get; set; }
        public long ProductId { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public long ProductUnitId { get; set; }
        public string UnitName { get; set; }
        public string Quantity { get; set; }
        public string PurchaseUnitPrice { get; set; }
        public string SaleUnitPrice { get; set; }
        public long LineTotalScaled { get; set; }
        public string LineTotalFormatted { get; set; }
        public string LotNumber { get; set; }
        public string ExpirationDate { get; set; }
        public List<PurchasePhysicalUnit> PhysicalUnits { get; set; } = new List<PurchasePhysicalUnit>();
    }

    public sealed class PurchasePhysicalUnit
    {
        public string Serial { get; set; }
        public List<string> Imeis { get; set; } = new List<string>();
    }

    public sealed class PurchaseDraftCommand
    {
        public long SupplierId { get; set; }
        public long WarehouseId { get; set; }
        public string InvoiceNumber { get; set; }
        public string InvoiceDate { get; set; }
        public string PaymentMode { get; set; }
        public long? TreasuryAccountId { get; set; }
        public string Notes { get; set; }
        public List<PurchaseInvoiceLine> Lines { get; set; } = new List<PurchaseInvoiceLine>();
    }

    public sealed class PurchasePostResult
    {
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public PurchaseInvoice Invoice { get; set; }
        public bool Replayed { get; set; }
        public static PurchasePostResult Fail(string code, string message) { return new PurchasePostResult { ErrorCode = code, ErrorMessage = message }; }
        public static PurchasePostResult Ok(PurchaseInvoice invoice, bool replayed) { return new PurchasePostResult { Succeeded = true, Invoice = invoice, Replayed = replayed }; }
    }

    public sealed class PurchaseReturn
    {
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public long Id { get; set; }
        public long PurchaseInvoiceId { get; set; }
        public long SupplierId { get; set; }
        public long WarehouseId { get; set; }
        public string ReturnMode { get; set; }
        public string RequestHash { get; set; }
        public string Status { get; set; }
        public long TotalScaled { get; set; }
        public string TotalFormatted { get; set; }
        public long? InventoryDocumentId { get; set; }
        public bool Replayed { get; set; }
        public List<PurchaseReturnLine> Lines { get; set; } = new List<PurchaseReturnLine>();
        public static PurchaseReturn Fail(string code, string message) { return new PurchaseReturn { ErrorCode = code, ErrorMessage = message }; }
        public static PurchaseReturn Ok(PurchaseReturn value) { value.Succeeded = true; return value; }
    }

    public sealed class PurchaseReturnLine
    {
        public long Id { get; set; }
        public long PurchaseInvoiceLineId { get; set; }
        public long VariantId { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public long ProductUnitId { get; set; }
        public string UnitName { get; set; }
        public string Quantity { get; set; }
        public long QuantityScaled { get; set; }
        public string AvailableQuantity { get; set; }
        public long AccountAmountScaled { get; set; }
        public string AccountAmountFormatted { get; set; }
        public long InventoryValueScaled { get; set; }
        public string InventoryValueFormatted { get; set; }
        public long VarianceScaled { get; set; }
        public string VarianceFormatted { get; set; }
        public long? OriginMovementId { get; set; }
        public List<PurchasePhysicalUnit> PhysicalUnits { get; set; } = new List<PurchasePhysicalUnit>();
    }

    public sealed class PurchaseReturnCommand
    {
        public long WarehouseId { get; set; }
        public string ReturnMode { get; set; }
        public List<PurchaseReturnLine> Lines { get; set; } = new List<PurchaseReturnLine>();
    }
}
