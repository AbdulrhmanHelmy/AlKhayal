using System.Collections.Generic;

namespace AlKhayal.Api.Contracts
{
    public sealed class PurchaseDraftRequest
    {
        public long SupplierId { get; set; }
        public long WarehouseId { get; set; }
        public string InvoiceNumber { get; set; }
        public string InvoiceDate { get; set; }
        public string PaymentMode { get; set; }
        public string Notes { get; set; }
        public List<PurchaseLineRequest> Lines { get; set; }
    }
    public sealed class PurchaseLineRequest
    {
        public long VariantId { get; set; }
        public long ProductUnitId { get; set; }
        public string Quantity { get; set; }
        public string PurchaseUnitPrice { get; set; }
        public string SaleUnitPrice { get; set; }
        public string LotNumber { get; set; }
        public string ExpirationDate { get; set; }
        public List<PurchasePhysicalUnitRequest> PhysicalUnits { get; set; }
    }
    public sealed class PurchasePhysicalUnitRequest
    {
        public string Serial { get; set; }
        public List<string> Imeis { get; set; }
    }
    public sealed class PurchasePostRequest { public string OperationKey { get; set; } }
    public sealed class PurchaseReturnRequest
    {
        public long WarehouseId { get; set; }
        public string ReturnMode { get; set; }
        public string OperationKey { get; set; }
        public List<PurchaseReturnLineRequest> Lines { get; set; }
    }
    public sealed class PurchaseReturnLineRequest
    {
        public long PurchaseInvoiceLineId { get; set; }
        public string Quantity { get; set; }
        public List<PurchasePhysicalUnitRequest> PhysicalUnits { get; set; }
    }
}
