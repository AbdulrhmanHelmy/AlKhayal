using System.Collections.Generic;
using System.Linq;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Returns;
using Dapper;

namespace AlKhayal.Data.Returns
{
    public sealed class SqliteReturnReadStore : IReturnReadStore
    {
        private readonly string _cs;
        public SqliteReturnReadStore(string connectionString) { _cs = connectionString; }
        private const string Records = @"SELECT r.Id,'sales' Type,r.ReturnNumber Number,r.SalesInvoiceId InvoiceId,i.InvoiceNumber,COALESCE(p.Name,'نقدي مباشر') PartyName,w.Name WarehouseName,r.CreatedUtc,u.DisplayName ActorName,r.Reason,r.TotalScaled,r.CashRefundScaled CashScaled,CASE WHEN r.CashRefundScaled=0 THEN 'credit' WHEN r.CreditReductionScaled=0 THEN 'cash' ELSE 'mixed' END Settlement
            FROM SalesReturns r JOIN SalesInvoices i ON i.Id=r.SalesInvoiceId LEFT JOIN Customers p ON p.Id=i.CustomerId JOIN Warehouses w ON w.Id=i.WarehouseId JOIN Users u ON u.Id=r.CreatedByUserId
            UNION ALL SELECT r.Id,'purchase','PUR-RET-'||r.Id,r.PurchaseInvoiceId,i.InvoiceNumber,p.Name,w.Name,r.CreatedUtc,u.DisplayName,'',r.TotalScaled,CASE WHEN r.ReturnMode='cash_refund' THEN r.TotalScaled ELSE 0 END,CASE WHEN r.ReturnMode='cash_refund' THEN 'cash' ELSE 'credit' END
            FROM PurchaseReturns r JOIN PurchaseInvoices i ON i.Id=r.PurchaseInvoiceId JOIN Suppliers p ON p.Id=r.SupplierId JOIN Warehouses w ON w.Id=r.WarehouseId JOIN Users u ON u.Id=r.CreatedByUserId WHERE r.Status='posted'";
        public async Task<IReadOnlyList<ReturnRecord>> ListAsync(string search, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_cs)) { await c.OpenAsync(ct).ConfigureAwait(false); return (await c.QueryAsync<ReturnRecord>(new CommandDefinition("SELECT * FROM (" + Records + ") WHERE @search='' OR instr(Number,@search)>0 OR instr(InvoiceNumber,@search)>0 OR instr(PartyName,@search)>0 ORDER BY CreatedUtc DESC,Type,Id DESC LIMIT 200", new { search }, cancellationToken: ct)).ConfigureAwait(false)).ToList(); }
        }
        public async Task<long?> FindInvoiceAsync(string type, string number, CancellationToken ct)
        {
            var sql = type == "sales" ? "SELECT Id FROM SalesInvoices WHERE InvoiceNumber=@number AND Status='posted' AND NOT EXISTS(SELECT 1 FROM SalesCancellations WHERE SalesInvoiceId=SalesInvoices.Id)" : "SELECT Id FROM PurchaseInvoices WHERE InvoiceNumber=@number AND Status='posted'";
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                var ids = (await c.QueryAsync<long>(new CommandDefinition(sql, new { number }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                if (ids.Count > 1) throw new System.ArgumentException("رقم الفاتورة مكرر لدى أكثر من مورد؛ راجع أرقام الفواتير أولًا.");
                return ids.Count == 0 ? null : (long?)ids[0];
            }
        }
        public async Task<ReturnRecord> GetAsync(string type, long id, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    var result = await c.QuerySingleOrDefaultAsync<ReturnRecord>(new CommandDefinition("SELECT * FROM (" + Records + ") WHERE Type=@type AND Id=@id", new { type, id }, tx, cancellationToken: ct)).ConfigureAwait(false); if (result == null) return null;
                    var sql = type == "sales" ? @"SELECT rl.Id,p.Name ProductName,u.Name UnitName,rl.QuantityScaled,rl.AmountScaled FROM SalesReturnLines rl JOIN SalesInvoiceLines il ON il.Id=rl.SalesInvoiceLineId JOIN ProductVariants v ON v.Id=il.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.Id=il.ProductUnitId JOIN Units u ON u.Id=pu.UnitId WHERE rl.SalesReturnId=@id ORDER BY rl.Id" : @"SELECT rl.Id,p.Name ProductName,u.Name UnitName,rl.QuantityScaled,rl.AccountAmountScaled AmountScaled FROM PurchaseReturnLines rl JOIN PurchaseInvoiceLines il ON il.Id=rl.PurchaseInvoiceLineId JOIN ProductVariants v ON v.Id=il.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.Id=il.ProductUnitId JOIN Units u ON u.Id=pu.UnitId WHERE rl.PurchaseReturnId=@id ORDER BY rl.Id";
                    var lines = (await c.QueryAsync<LineRow>(new CommandDefinition(sql, new { id }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    foreach (var line in lines)
                    {
                        if (type == "sales") line.Serials = (await c.QueryAsync<string>(new CommandDefinition("SELECT Serial FROM SalesReturnPhysicalUnits WHERE SalesReturnLineId=@Id", new { line.Id }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                        else line.Serials = (await c.QueryAsync<string>(new CommandDefinition(@"SELECT ip.DisplayValue FROM PurchaseReturnLines rl JOIN PurchaseReturns r ON r.Id=rl.PurchaseReturnId JOIN PurchaseInvoiceLines il ON il.Id=rl.PurchaseInvoiceLineId JOIN InventoryDocumentLines dl ON dl.DocumentId=r.InventoryDocumentId AND dl.LineNumber=(SELECT COUNT(*) FROM PurchaseReturnLines previous WHERE previous.PurchaseReturnId=rl.PurchaseReturnId AND previous.Id<=rl.Id) AND dl.VariantId=il.VariantId AND dl.ProductUnitId=il.ProductUnitId JOIN StockMovements sm ON sm.DocumentLineId=dl.Id JOIN StockMovementUnits mu ON mu.MovementId=sm.Id JOIN InventoryIdentifiers ip ON ip.PhysicalUnitId=mu.PhysicalUnitId AND ip.Kind='serial' WHERE rl.Id=@Id", new { line.Id }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    }
                    result.Lines = lines.Select(x => new ReturnRecordLine { ProductName = x.ProductName, UnitName = x.UnitName, QuantityScaled = x.QuantityScaled, AmountScaled = x.AmountScaled, Serials = x.Serials }).ToList();
                    result.Movements = (await c.QueryAsync<ReturnMoneyMovement>(new CommandDefinition("SELECT m.Id,a.Name AccountName,m.Type,l.Role,m.AmountScaled FROM ReturnTreasuryLinks l JOIN TreasuryMovements m ON m.Id=l.MovementId JOIN TreasuryAccounts a ON a.Id=m.AccountId WHERE (@type='sales' AND l.SalesReturnId=@id) OR (@type='purchase' AND l.PurchaseReturnId=@id) ORDER BY l.Id", new { type, id }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    tx.Commit(); return result;
                }
            }
        }
        private sealed class LineRow { public long Id { get; set; } public string ProductName { get; set; } public string UnitName { get; set; } public long QuantityScaled { get; set; } public long AmountScaled { get; set; } public List<string> Serials { get; set; } = new List<string>(); }
    }
}
