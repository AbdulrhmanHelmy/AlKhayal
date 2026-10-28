using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Purchasing;

namespace AlKhayal.Application.Returns
{
    public sealed class ReturnRecord
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string Number { get; set; }
        public long InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string PartyName { get; set; }
        public string WarehouseName { get; set; }
        public string CreatedUtc { get; set; }
        public string ActorName { get; set; }
        public string Reason { get; set; }
        public long TotalScaled { get; set; }
        public string Total => MoneyPolicy.FormatForUser(TotalScaled);
        public long CashScaled { get; set; }
        public string Cash => MoneyPolicy.FormatForUser(CashScaled);
        public string AccountAdjustment => MoneyPolicy.FormatForUser(TotalScaled - CashScaled);
        public string Settlement { get; set; }
        public List<ReturnRecordLine> Lines { get; set; } = new List<ReturnRecordLine>();
        public List<ReturnMoneyMovement> Movements { get; set; } = new List<ReturnMoneyMovement>();
    }
    public sealed class ReturnRecordLine
    {
        public string ProductName { get; set; }
        public string UnitName { get; set; }
        public long QuantityScaled { get; set; }
        public string Quantity => (QuantityScaled / 1000000m).ToString("0.######", CultureInfo.InvariantCulture);
        public long AmountScaled { get; set; }
        public string Amount => MoneyPolicy.FormatForUser(AmountScaled);
        public List<string> Serials { get; set; } = new List<string>();
    }
    public sealed class ReturnMoneyMovement
    {
        public long Id { get; set; }
        public string AccountName { get; set; }
        public string Type { get; set; }
        public string Role { get; set; }
        public long AmountScaled { get; set; }
        public string Amount => MoneyPolicy.FormatForUser(AmountScaled);
    }
    public sealed class ReturnInvoice
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string Number { get; set; }
        public string PartyName { get; set; }
        public long WarehouseId { get; set; }
        public string PaymentMode { get; set; }
        public List<ReturnInvoiceLine> Lines { get; set; } = new List<ReturnInvoiceLine>();
    }
    public sealed class ReturnInvoiceLine
    {
        public long Id { get; set; }
        public string ProductName { get; set; }
        public string UnitName { get; set; }
        public string Quantity { get; set; }
        public string Available { get; set; }
        public List<ReturnUnit> Units { get; set; } = new List<ReturnUnit>();
    }
    public sealed class ReturnUnit { public string Serial { get; set; } public List<string> Imeis { get; set; } = new List<string>(); }
    public interface IReturnReadStore
    {
        Task<IReadOnlyList<ReturnRecord>> ListAsync(string search, CancellationToken ct);
        Task<ReturnRecord> GetAsync(string type, long id, CancellationToken ct);
        Task<long?> FindInvoiceAsync(string type, string number, CancellationToken ct);
    }
    public sealed class ReturnService
    {
        private readonly IReturnReadStore _store;
        private readonly SalesService _sales;
        private readonly PurchaseService _purchases;
        public ReturnService(IReturnReadStore store, SalesService sales, PurchaseService purchases) { _store = store; _sales = sales; _purchases = purchases; }
        public Task<IReadOnlyList<ReturnRecord>> ListAsync(string search, CancellationToken ct) => _store.ListAsync((search ?? "").Trim(), ct);
        public Task<ReturnRecord> GetAsync(string type, long id, CancellationToken ct) { ValidateType(type); if (id <= 0) throw new ArgumentException("اختر مرتجعًا صحيحًا."); return _store.GetAsync(type, id, ct); }
        public async Task<ReturnInvoice> LookupAsync(string type, string number, CancellationToken ct)
        {
            ValidateType(type); number = (number ?? "").Trim(); if (number.Length == 0 || number.Length > 120) throw new ArgumentException("أدخل رقم الفاتورة الصحيح.");
            var id = await _store.FindInvoiceAsync(type, number, ct).ConfigureAwait(false); if (!id.HasValue) return null;
            if (type == "sales")
            {
                var sale = await _sales.GetAsync(id.Value, ct).ConfigureAwait(false); if (sale == null || sale.Status != "posted") return null;
                return new ReturnInvoice { Id = sale.Id, Type = type, Number = sale.InvoiceNumber, PartyName = sale.CustomerName, WarehouseId = sale.WarehouseId, PaymentMode = sale.PaymentMode,
                    Lines = sale.Lines.Select(x => new ReturnInvoiceLine { Id = x.Id, ProductName = x.ProductName + " — " + x.VariantName, UnitName = x.UnitName, Quantity = x.Quantity,
                        Available = Math.Max(0, decimal.Parse(x.Quantity, CultureInfo.InvariantCulture) - decimal.Parse(x.ReturnedQuantity ?? "0", CultureInfo.InvariantCulture)).ToString("0.######", CultureInfo.InvariantCulture),
                        Units = x.AvailableTrackingUnits.Select(u => new ReturnUnit { Serial = u.Serial, Imeis = u.Imeis }).ToList() }).ToList() };
            }
            var purchase = await _purchases.GetAsync(id.Value, ct).ConfigureAwait(false); if (purchase == null || purchase.Status != "posted") return null;
            var lines = await _purchases.ReturnableLinesAsync(purchase.Id, purchase.WarehouseId, ct).ConfigureAwait(false);
            return new ReturnInvoice { Id = purchase.Id, Type = type, Number = purchase.InvoiceNumber, PartyName = purchase.SupplierName, WarehouseId = purchase.WarehouseId, PaymentMode = purchase.PaymentMode,
                Lines = lines.Select(x => new ReturnInvoiceLine { Id = x.PurchaseInvoiceLineId, ProductName = x.ProductName + " — " + x.VariantName, UnitName = x.UnitName, Quantity = x.Quantity, Available = x.AvailableQuantity,
                    Units = x.PhysicalUnits.Select(u => new ReturnUnit { Serial = u.Serial, Imeis = u.Imeis }).ToList() }).ToList() };
        }
        private static void ValidateType(string type) { if (type != "sales" && type != "purchase") throw new ArgumentException("اختر فاتورة بيع أو شراء."); }
    }
}
