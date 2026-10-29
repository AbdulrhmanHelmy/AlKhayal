using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Sales
{
    public sealed class SaleLine
    {
        public long Id { get; set; }
        public long VariantId { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public long ProductUnitId { get; set; }
        public string UnitName { get; set; }
        public string Quantity { get; set; }
        public string ReturnedQuantity { get; set; }
        public long? InventoryDocumentLineId { get; set; }
        public string BatchLotNumber { get; set; }
        public string ExpirationDate { get; set; }
        public List<SaleTrackingUnit> PhysicalUnits { get; set; } = new List<SaleTrackingUnit>();
        public List<SaleTrackingUnit> AvailableTrackingUnits { get; set; } = new List<SaleTrackingUnit>();
        public string UnitPrice { get; set; }
        public string Discount { get; set; }
        public long InvoiceDiscountScaled { get; set; }
        public long TaxScaled { get; set; }
        public long LineTotalScaled { get; set; }
        public string LineTotal { get; set; }
    }
    public sealed class SaleTrackingUnit { public string Serial { get; set; } public List<string> Imeis { get; set; } = new List<string>(); }
    public sealed class SaleInvoice
    {
        public long Id { get; set; }
        public string InvoiceNumber { get; set; }
        public string InvoiceDate { get; set; }
        public string PostedUtc { get; set; }
        public long? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public long WarehouseId { get; set; }
        public string WarehouseName { get; set; }
        public long CashierUserId { get; set; }
        public string CashierName { get; set; }
        public string Status { get; set; }
        public bool HasReturns { get; set; }
        public bool IsFullyReturned { get; set; }
        public string ReturnStatus => !HasReturns ? "none" : IsFullyReturned ? "full" : "partial";
        public string PaymentMode { get; set; }
        public long SubtotalScaled { get; set; }
        public long DiscountScaled { get; set; }
        public long TaxScaled { get; set; }
        public long TotalScaled { get; set; }
        public long PaidScaled { get; set; }
        public long ReturnedScaled { get; set; }
        public long OutstandingScaled { get; set; }
        public List<SaleReturnSummary> Returns { get; set; } = new List<SaleReturnSummary>();
        public long? InventoryDocumentId { get; set; }
        public long? TreasuryAccountId { get; set; }
        public string TreasuryAccountName { get; set; }
        public long? TreasuryMovementId { get; set; }
        public string OperationKey { get; set; }
        public string RequestHash { get; set; }
        public long Version { get; set; }
        public bool Replayed { get; set; }
        public List<SaleLine> Lines { get; set; } = new List<SaleLine>();
    }
    public sealed class SaleDraftCommand
    {
        public long? CustomerId { get; set; }
        public long WarehouseId { get; set; }
        public string InvoiceDate { get; set; }
        public string PaymentMode { get; set; }
        public string Discount { get; set; }
        public List<SaleLine> Lines { get; set; } = new List<SaleLine>();
    }
    public sealed class SalePostResult
    {
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public SaleInvoice Invoice { get; set; }
        public static SalePostResult Fail(string code,string message) { return new SalePostResult { ErrorCode=code, ErrorMessage=message }; }
    }
    public sealed class SaleCancellationResult { public bool Succeeded { get; set; } public bool Replayed { get; set; } public string ErrorCode { get; set; } public string ErrorMessage { get; set; } public SaleInvoice Invoice { get; set; } }
    public sealed class SalePaymentCommand { public string Amount { get; set; } public string Method { get; set; } public string OperationKey { get; set; } public string OccurredOn { get; set; } public string Notes { get; set; } }
    public sealed class SalePaymentResult { public bool Succeeded { get; set; } public bool Replayed { get; set; } public string ErrorCode { get; set; } public string ErrorMessage { get; set; } public SaleInvoice Invoice { get; set; } public long PaymentId { get; set; } }
    public sealed class SaleReturnLineCommand { public long InvoiceLineId { get; set; } public string Quantity { get; set; } public List<string> Serials { get; set; } = new List<string>(); }
    public sealed class SaleReturnCommand { public string OperationKey { get; set; } public string Reason { get; set; } public List<SaleReturnLineCommand> Lines { get; set; } = new List<SaleReturnLineCommand>(); }
    public sealed class SaleReturnResult { public bool Succeeded { get; set; } public bool Replayed { get; set; } public string ErrorCode { get; set; } public string ErrorMessage { get; set; } public long ReturnId { get; set; } public string ReturnNumber { get; set; } public long InventoryDocumentId { get; set; } public long TotalScaled { get; set; } public long CashRefundScaled { get; set; } public long CreditReductionScaled { get; set; } }
    public sealed class SaleReturnSummary { public long Id { get; set; } public string ReturnNumber { get; set; } public long TotalScaled { get; set; } public long CashRefundScaled { get; set; } public long CreditReductionScaled { get; set; } public string Reason { get; set; } public string CreatedUtc { get; set; } }
    public interface ISalesStore
    {
        Task<SaleInvoice> CreateDraftAsync(SaleDraftCommand command,long subtotal,long discount,long tax,long total,long userId,CancellationToken ct);
        Task<SaleInvoice> UpdateDraftAsync(long id,SaleDraftCommand command,long subtotal,long discount,long tax,long total,long userId,CancellationToken ct);
        Task<SaleInvoice> GetAsync(long id,CancellationToken ct);
        Task<IReadOnlyList<SaleInvoice>> ListAsync(string search,string from,string to,CancellationToken ct);
        Task<SalePostResult> PostAsync(long id,long version,string key,string hash,long userId,CancellationToken ct);
        Task<SalePaymentResult> AddPaymentAsync(long id,SalePaymentCommand command,long userId,CancellationToken ct);
        Task<SaleReturnResult> ReturnAsync(long id,SaleReturnCommand command,long userId,CancellationToken ct);
        Task<SaleCancellationResult> DeleteDraftAsync(long id,string operationKey,long userId,CancellationToken ct);
        Task<SaleCancellationResult> CancelAsync(long id,string operationKey,string reason,long userId,CancellationToken ct);
    }
}
