using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Finance;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using AlKhayal.Data.Inventory;
using Dapper;

namespace AlKhayal.Data.Purchasing
{
    public sealed class SqlitePurchaseRepository : IPurchaseRepository
    {
        private readonly string _connectionString;
        private readonly SqliteInventoryStore _inventory;
        public SqlitePurchaseRepository(string connectionString)
        { _connectionString = connectionString + "Default Timeout=5;"; _inventory = new SqliteInventoryStore(connectionString); }

        public async Task<PurchaseInvoice> CreateDraftAsync(PurchaseDraftCommand command, long totalScaled, long userId, CancellationToken ct)
        {
            using (var c = Open()) using (var tx = c.BeginTransaction())
            {
                var now = Now(); var number = NormalizeNumber(command.InvoiceNumber);
                await c.ExecuteAsync(Cmd(@"INSERT INTO PurchaseInvoices(SupplierId,WarehouseId,InvoiceNumber,InvoiceDate,PaymentMode,Status,Notes,TotalScaled,CreatedByUserId,UpdatedByUserId,CreatedUtc,UpdatedUtc)
                    VALUES(@SupplierId,@WarehouseId,@InvoiceNumber,@InvoiceDate,@PaymentMode,'draft',@Notes,@Total,@UserId,@UserId,@Now,@Now)", new { command.SupplierId, command.WarehouseId, InvoiceNumber = number, command.InvoiceDate, command.PaymentMode, Notes = command.Notes ?? "", Total = totalScaled, UserId = userId, Now = now }, tx, ct)).ConfigureAwait(false);
                var id = await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()", null, tx, ct)).ConfigureAwait(false);
                await SaveLinesAsync(c, tx, id, command.Lines, ct).ConfigureAwait(false);
                await AuditAsync(c, tx, id, userId, "created", now, "إنشاء مسودة", ct).ConfigureAwait(false);
                tx.Commit(); return await GetAsync(id, ct).ConfigureAwait(false);
            }
        }

        public async Task<PurchaseInvoice> UpdateDraftAsync(long id, PurchaseDraftCommand command, long totalScaled, long userId, CancellationToken ct)
        {
            using (var c = Open()) using (var tx = c.BeginTransaction())
            {
                var row = await c.QuerySingleOrDefaultAsync<PurchaseInvoice>(Cmd("SELECT * FROM PurchaseInvoices WHERE Id=@Id", new { Id = id }, tx, ct)).ConfigureAwait(false);
                if (row == null) return null;
                if (row.Status != "draft") throw new InvalidOperationException("الفاتورة المنشورة غير قابلة للتعديل.");
                var now = Now();
                var changed = await c.ExecuteAsync(Cmd(@"UPDATE PurchaseInvoices SET SupplierId=@SupplierId,WarehouseId=@WarehouseId,InvoiceNumber=@InvoiceNumber,InvoiceDate=@InvoiceDate,PaymentMode=@PaymentMode,Notes=@Notes,TotalScaled=@Total,Version=Version+1,UpdatedByUserId=@UserId,UpdatedUtc=@Now WHERE Id=@Id AND Status='draft' AND Version=@Version",
                    new { command.SupplierId, command.WarehouseId, InvoiceNumber = NormalizeNumber(command.InvoiceNumber), command.InvoiceDate, command.PaymentMode, Notes = command.Notes ?? "", Total = totalScaled, UserId = userId, Now = now, Id = id, row.Version }, tx, ct)).ConfigureAwait(false);
                if (changed != 1) throw new InvalidOperationException("تغيرت المسودة أثناء التعديل. حدّث الشاشة ثم أعد المحاولة.");
                await c.ExecuteAsync(Cmd("DELETE FROM PurchaseInvoiceLineImeis WHERE PurchaseInvoiceLineUnitId IN (SELECT Id FROM PurchaseInvoiceLineUnits WHERE PurchaseInvoiceLineId IN (SELECT Id FROM PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id))", new { Id = id }, tx, ct)).ConfigureAwait(false);
                await c.ExecuteAsync(Cmd("DELETE FROM PurchaseInvoiceLineUnits WHERE PurchaseInvoiceLineId IN (SELECT Id FROM PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id)", new { Id = id }, tx, ct)).ConfigureAwait(false);
                await c.ExecuteAsync(Cmd("DELETE FROM PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id", new { Id = id }, tx, ct)).ConfigureAwait(false);
                await SaveLinesAsync(c, tx, id, command.Lines, ct).ConfigureAwait(false);
                await AuditAsync(c, tx, id, userId, "draft_updated", now, "تعديل المسودة", ct).ConfigureAwait(false);
                tx.Commit(); return await GetAsync(id, ct).ConfigureAwait(false);
            }
        }

        public async Task<PurchaseInvoice> GetAsync(long id, CancellationToken ct)
        {
            using (var c = Open())
            {
                var invoice = await c.QuerySingleOrDefaultAsync<PurchaseInvoice>(Cmd(@"SELECT p.*,s.Name SupplierName,w.Name WarehouseName FROM PurchaseInvoices p JOIN Suppliers s ON s.Id=p.SupplierId JOIN Warehouses w ON w.Id=p.WarehouseId WHERE p.Id=@Id", new { Id = id }, null, ct)).ConfigureAwait(false);
                if (invoice == null) return null;
                invoice.TotalFormatted = FormatScaled(invoice.TotalScaled, 4);
                var lines = (await c.QueryAsync<PurchaseInvoiceLine>(Cmd(@"SELECT l.Id,l.VariantId,v.ProductId,p.Name ProductName,v.Name VariantName,l.ProductUnitId,u.Name UnitName,
                    CAST(l.QuantityScaled AS TEXT) Quantity,CAST(l.PurchaseUnitPriceScaled AS TEXT) PurchaseUnitPrice,CAST(l.SaleUnitPriceScaled AS TEXT) SaleUnitPrice,l.LineTotalScaled,l.LotNumber,l.ExpirationDate
                    FROM PurchaseInvoiceLines l JOIN ProductVariants v ON v.Id=l.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.Id=l.ProductUnitId JOIN Units u ON u.Id=pu.UnitId
                    WHERE l.PurchaseInvoiceId=@Id ORDER BY l.LineNumber", new { Id = id }, null, ct)).ConfigureAwait(false)).ToList();
                foreach (var line in lines)
                {
                    line.LineTotalFormatted = FormatScaled(line.LineTotalScaled, 4);
                    line.Quantity = FormatScaled(ParseScaled(line.Quantity), 6); line.PurchaseUnitPrice = FormatScaled(ParseScaled(line.PurchaseUnitPrice), 4); line.SaleUnitPrice = FormatScaled(ParseScaled(line.SaleUnitPrice), 4);
                    var units = await c.QueryAsync<PurchasePhysicalUnit>(Cmd("SELECT Serial FROM PurchaseInvoiceLineUnits WHERE PurchaseInvoiceLineId=@Id ORDER BY UnitNumber", new { Id = line.Id }, null, ct)).ConfigureAwait(false);
                    line.PhysicalUnits = units.ToList();
                    foreach (var unit in line.PhysicalUnits) unit.Imeis = (await c.QueryAsync<string>(Cmd("SELECT Imei FROM PurchaseInvoiceLineImeis i JOIN PurchaseInvoiceLineUnits u ON u.Id=i.PurchaseInvoiceLineUnitId WHERE u.PurchaseInvoiceLineId=@Id AND u.Serial=@Serial ORDER BY Imei", new { Id = line.Id, unit.Serial }, null, ct)).ConfigureAwait(false)).ToList();
                }
                invoice.Lines = lines; return invoice;
            }
        }

        public async Task<IReadOnlyList<PurchaseInvoice>> ListAsync(long? supplierId, string from, string to, CancellationToken ct)
        {
            using (var c = Open())
            {
                var rows = (await c.QueryAsync<PurchaseInvoice>(new CommandDefinition(@"SELECT p.Id,p.SupplierId,s.Name SupplierName,p.WarehouseId,w.Name WarehouseName,p.InvoiceNumber,p.InvoiceDate,p.PaymentMode,p.Status,p.Notes,p.TotalScaled,p.InventoryDocumentId,p.Version,p.CreatedUtc,p.UpdatedUtc
                    FROM PurchaseInvoices p JOIN Suppliers s ON s.Id=p.SupplierId JOIN Warehouses w ON w.Id=p.WarehouseId
                    WHERE (@SupplierId IS NULL OR p.SupplierId=@SupplierId) AND (@From IS NULL OR p.InvoiceDate>=@From) AND (@To IS NULL OR p.InvoiceDate<=@To)
                    ORDER BY p.InvoiceDate DESC,p.Id DESC LIMIT 500", new { SupplierId = supplierId, From = from, To = to }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                foreach (var row in rows) row.TotalFormatted = FormatScaled(row.TotalScaled, 4);
                return rows;
            }
        }

        public async Task<PurchasePostResult> PostAsync(long id, long expectedVersion, string operationKey, string requestHash, long userId, CancellationToken ct)
        {
            try
            {
                using (var c = Open()) using (var tx = c.BeginTransaction())
                {
                    var invoice = await LoadForPostAsync(c, tx, id, ct).ConfigureAwait(false);
                    if (invoice == null) return PurchasePostResult.Fail("not_found", "الفاتورة غير موجودة.");
                    if (invoice.Status == "posted")
                        return invoice.PostOperationKey == operationKey && invoice.PostRequestHash == requestHash
                            ? PurchasePostResult.Ok(invoice, true)
                            : PurchasePostResult.Fail("already_posted", "الفاتورة منشورة بالفعل.");
                    if (invoice.Version != expectedVersion) return PurchasePostResult.Fail("draft_changed", "تغيرت المسودة. حدّث الشاشة ثم أعد النشر.");

                    var inventoryCommand = new InventoryPostCommand
                    {
                        OperationKey = "purchase:" + operationKey,
                        Type = "manual_receipt",
                        WarehouseId = invoice.WarehouseId,
                        UserId = userId,
                        Reason = "استلام فاتورة شراء " + invoice.InvoiceNumber,
                        Notes = "purchaseInvoiceId=" + invoice.Id,
                        Lines = invoice.Lines.Select(x => new InventoryLineInput
                        {
                            VariantId = x.VariantId, ProductUnitId = x.ProductUnitId, Quantity = x.Quantity,
                            UnitCost = x.PurchaseUnitPrice, SalePrice = x.SaleUnitPrice, LotNumber = x.LotNumber,
                            ExpirationDate = x.ExpirationDate,
                            PhysicalUnits = x.PhysicalUnits.Select(u => new InventoryUnitInput { Serial = u.Serial, Imeis = u.Imeis }).ToList()
                        }).ToList()
                    };
                    var inventoryResult = await _inventory.PostWithinTransactionAsync(c, tx, inventoryCommand, InventoryService.CanonicalHashPost(inventoryCommand), ct).ConfigureAwait(false);
                    if (!inventoryResult.Succeeded) return PurchasePostResult.Fail(inventoryResult.ErrorCode, inventoryResult.ErrorMessage);

                    var now = Now();
                    await c.ExecuteAsync(Cmd(@"UPDATE PurchaseInvoiceLines SET InventoryDocumentLineId=(SELECT l.Id FROM InventoryDocumentLines l WHERE l.DocumentId=@DocumentId AND l.LineNumber=PurchaseInvoiceLines.LineNumber)
                        WHERE PurchaseInvoiceId=@InvoiceId", new { DocumentId = inventoryResult.Value.DocumentId, InvoiceId = id }, tx, ct)).ConfigureAwait(false);
                    var updated = await c.ExecuteAsync(Cmd(@"UPDATE PurchaseInvoices SET Status='posted',InventoryDocumentId=@InventoryDocumentId,PostedUtc=@Now,PostOperationKey=@OperationKey,PostRequestHash=@Hash,UpdatedByUserId=@UserId,UpdatedUtc=@Now WHERE Id=@Id AND Status='draft' AND Version=@Version",
                        new { InventoryDocumentId = inventoryResult.Value.DocumentId, Now = now, OperationKey = operationKey, Hash = requestHash, UserId = userId, Id = id, Version = expectedVersion }, tx, ct)).ConfigureAwait(false);
                    if (updated != 1) return PurchasePostResult.Fail("draft_changed", "تغيرت المسودة قبل النشر.");
                    var total = invoice.TotalScaled;
                    await AddLedgerEntryAsync(c, tx, invoice.SupplierId, "purchase", total, invoice.InvoiceDate, "PUR-" + invoice.Id, invoice.Id, null, null, requestHash, now, ct).ConfigureAwait(false);
                    await ChangePayableAsync(c, tx, invoice.SupplierId, total, ct).ConfigureAwait(false);
                    if (invoice.PaymentMode == "cash")
                    {
                        var paymentKey = "purchase-payment:" + operationKey;
                        await AddLedgerEntryAsync(c, tx, invoice.SupplierId, "payment", -total, invoice.InvoiceDate, "PAY-PUR-" + invoice.Id, invoice.Id, "نقدي - تسوية فاتورة شراء", paymentKey, requestHash, now, ct).ConfigureAwait(false);
                        await ChangePayableAsync(c, tx, invoice.SupplierId, -total, ct).ConfigureAwait(false);
                    }
                    await AuditAsync(c, tx, id, userId, "posted", now, "inventoryDocumentId=" + inventoryResult.Value.DocumentId.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
                    tx.Commit();
                    invoice = await GetAsync(id, ct).ConfigureAwait(false);
                    return PurchasePostResult.Ok(invoice, false);
                }
            }
            catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Busy || ex.ResultCode == SQLiteErrorCode.Locked)
            { return PurchasePostResult.Fail("database_busy", "قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه."); }
            catch (OverflowException) { return PurchasePostResult.Fail("numeric_overflow", "تجاوزت قيمة الفاتورة الحد المسموح."); }
            catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Constraint)
            { return PurchasePostResult.Fail("purchase_conflict", "تعارضت الفاتورة مع بيانات أخرى. حدّث الصفحة ثم أعد المحاولة."); }
        }

        public async Task<IReadOnlyList<PurchaseReturnLine>> ReturnableLinesAsync(long id, long warehouseId, CancellationToken ct)
        {
            using (var c = Open())
            {
                var invoice = await c.QuerySingleOrDefaultAsync<PurchaseInvoice>(Cmd("SELECT * FROM PurchaseInvoices WHERE Id=@Id AND Status='posted'", new { Id = id }, null, ct)).ConfigureAwait(false);
                if (invoice == null) return new List<PurchaseReturnLine>();
                var sourceLines = (await c.QueryAsync<ReturnSource>(Cmd(@"SELECT l.Id PurchaseInvoiceLineId,l.VariantId,p.Name ProductName,v.Name VariantName,l.ProductUnitId,u.Name UnitName,
                    l.QuantityScaled,l.PurchaseUnitPriceScaled,dl.ToBaseNumerator,dl.ToBaseDenominator,dl.BatchId,sm.Id OriginMovementId,
                    EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='serial-numbers' AND a.IsEnabled=1) SerialTracked,
                    EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='imei-tracking' AND a.IsEnabled=1) ImeiTracked
                    FROM PurchaseInvoiceLines l JOIN InventoryDocumentLines dl ON dl.Id=l.InventoryDocumentLineId
                    JOIN StockMovements sm ON sm.DocumentLineId=dl.Id AND sm.QuantityBaseScaled>0 AND sm.WarehouseId=@OriginalWarehouse
                    JOIN ProductVariants v ON v.Id=l.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.Id=l.ProductUnitId JOIN Units u ON u.Id=pu.UnitId
                    WHERE l.PurchaseInvoiceId=@Id ORDER BY l.LineNumber", new { Id = id, OriginalWarehouse = invoice.WarehouseId }, null, ct))).ToList();
                var result = new List<PurchaseReturnLine>();
                foreach (var line in sourceLines)
                {
                    var previous = await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(QuantityScaled),0) FROM PurchaseReturnLines rl JOIN PurchaseReturns r ON r.Id=rl.PurchaseReturnId WHERE rl.PurchaseInvoiceLineId=@LineId AND r.Status='posted'", new { LineId = line.PurchaseInvoiceLineId }, null, ct)).ConfigureAwait(false);
                    var availableBase = await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(RemainingQuantityScaled),0) FROM InventoryCostLayers WHERE WarehouseId=@WarehouseId AND VariantId=@VariantId AND BatchId IS @BatchId AND OriginMovementId=@OriginId", new { WarehouseId = warehouseId, line.VariantId, line.BatchId, OriginId = line.OriginMovementId }, null, ct)).ConfigureAwait(false);
                    var availableEntered = line.ToBaseNumerator <= 0 ? 0 : (long)decimal.Floor((decimal)availableBase * line.ToBaseDenominator / line.ToBaseNumerator);
                    var remainingPurchased = Math.Max(0, line.QuantityScaled - previous);
                    var physicalUnits = line.SerialTracked ? await OriginPhysicalUnitsAsync(c, null, line.OriginMovementId, warehouseId, ct).ConfigureAwait(false) : new List<PurchasePhysicalUnit>();
                    if (line.SerialTracked && line.ToBaseNumerator > 0) availableEntered = Math.Min(availableEntered, checked((long)physicalUnits.Count * 1000000L * line.ToBaseDenominator / line.ToBaseNumerator));
                    result.Add(new PurchaseReturnLine { PurchaseInvoiceLineId = line.PurchaseInvoiceLineId, VariantId = line.VariantId, ProductName = line.ProductName, VariantName = line.VariantName, ProductUnitId = line.ProductUnitId, UnitName = line.UnitName,
                        Quantity = FormatScaled(line.QuantityScaled, 6), AvailableQuantity = FormatScaled(Math.Min(remainingPurchased, availableEntered), 6), OriginMovementId = line.OriginMovementId, PhysicalUnits = physicalUnits });
                }
                return result;
            }
        }

        public async Task<PurchaseReturn> PostReturnAsync(long id, PurchaseReturnCommand command, string operationKey, string requestHash, long userId, CancellationToken ct)
        {
            try
            {
                using (var c = Open()) using (var tx = c.BeginTransaction())
                {
                    var replay = await c.QuerySingleOrDefaultAsync<PurchaseReturn>(Cmd("SELECT * FROM PurchaseReturns WHERE OperationKey=@Key", new { Key = operationKey }, tx, ct)).ConfigureAwait(false);
                    if (replay != null)
                    {
                        if (replay.RequestHash != requestHash) return PurchaseReturn.Fail("operation_key_conflict", "مفتاح العملية مستخدم لمرتجع مختلف.");
                        replay.Replayed = true; replay.Lines = await ReturnLinesAsync(c, tx, replay.Id, ct).ConfigureAwait(false); return PurchaseReturn.Ok(replay);
                    }
                    var invoice = await c.QuerySingleOrDefaultAsync<PurchaseInvoice>(Cmd("SELECT * FROM PurchaseInvoices WHERE Id=@Id AND Status='posted'", new { Id = id }, tx, ct)).ConfigureAwait(false);
                    if (invoice == null) return null;
                    var now = Now(); var prepared = new List<ReturnPostingLine>(); long total = 0;
                    foreach (var requested in command.Lines)
                    {
                        var line = await c.QuerySingleOrDefaultAsync<ReturnSource>(Cmd(@"SELECT l.Id PurchaseInvoiceLineId,l.VariantId,l.ProductUnitId,l.QuantityScaled,l.PurchaseUnitPriceScaled,dl.ToBaseNumerator,dl.ToBaseDenominator,dl.BatchId,sm.Id OriginMovementId,
                            EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='serial-numbers' AND a.IsEnabled=1) SerialTracked,
                            EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='imei-tracking' AND a.IsEnabled=1) ImeiTracked
                            FROM PurchaseInvoiceLines l JOIN InventoryDocumentLines dl ON dl.Id=l.InventoryDocumentLineId
                            JOIN StockMovements sm ON sm.DocumentLineId=dl.Id AND sm.QuantityBaseScaled>0 AND sm.WarehouseId=@OriginalWarehouse JOIN ProductVariants v ON v.Id=l.VariantId
                            WHERE l.Id=@LineId AND l.PurchaseInvoiceId=@InvoiceId", new { LineId = requested.PurchaseInvoiceLineId, InvoiceId = id, OriginalWarehouse = invoice.WarehouseId }, tx, ct)).ConfigureAwait(false);
                        if (line == null) return null;
                        long quantity;
                        if (!TryScale(requested.Quantity, 6, out quantity) || quantity <= 0) return null;
                        var exactBase = (decimal)quantity * line.ToBaseNumerator / line.ToBaseDenominator;
                        if (exactBase != decimal.Truncate(exactBase) || exactBase > long.MaxValue) return null;
                        var baseQuantity = checked((long)exactBase);
                        if (line.SerialTracked)
                        {
                            var eligibleUnits = await OriginPhysicalUnitsAsync(c, tx, line.OriginMovementId, command.WarehouseId, ct).ConfigureAwait(false);
                            var requestedUnits = requested.PhysicalUnits ?? new List<PurchasePhysicalUnit>();
                            var allowedSerials = new HashSet<string>(eligibleUnits.Select(x => x.Serial), StringComparer.OrdinalIgnoreCase);
                            var selectedSerials = requestedUnits.Select(x => x == null ? null : x.Serial).ToList();
                            if (baseQuantity % 1000000L != 0 || selectedSerials.Count != baseQuantity / 1000000L || selectedSerials.Any(x => string.IsNullOrWhiteSpace(x) || !allowedSerials.Contains(x)) || selectedSerials.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedSerials.Count)
                                return PurchaseReturn.Fail("return_serial_mismatch", "اختر الأرقام التسلسلية المتاحة من إيصال الشراء الأصلي فقط.");
                        }
                        var previouslyReturned = await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(QuantityScaled),0) FROM PurchaseReturnLines rl JOIN PurchaseReturns r ON r.Id=rl.PurchaseReturnId WHERE rl.PurchaseInvoiceLineId=@LineId AND r.Status='posted'", new { LineId = line.PurchaseInvoiceLineId }, tx, ct)).ConfigureAwait(false);
                        if (checked(previouslyReturned + quantity) > line.QuantityScaled) return null;
                        var eligible = await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(RemainingQuantityScaled),0) FROM InventoryCostLayers WHERE WarehouseId=@WarehouseId AND VariantId=@VariantId AND BatchId IS @BatchId AND OriginMovementId=@OriginId", new { WarehouseId = command.WarehouseId, line.VariantId, line.BatchId, OriginId = line.OriginMovementId }, tx, ct)).ConfigureAwait(false);
                        if (baseQuantity > eligible) return null;
                        var accountValue = MoneyPolicy.FinalizeQuantityAmount(quantity, line.PurchaseUnitPriceScaled);
                        total = checked(total + accountValue);
                        var batch = line.BatchId.HasValue ? await c.QuerySingleOrDefaultAsync<ReturnBatch>(Cmd("SELECT LotNumber,ExpirationDate FROM ProductBatches WHERE Id=@Id", new { Id = line.BatchId.Value }, tx, ct)).ConfigureAwait(false) : null;
                        prepared.Add(new ReturnPostingLine { Source = line, Quantity = requested.Quantity, QuantityScaled = quantity, BaseQuantityScaled = baseQuantity, AccountAmountScaled = accountValue, Batch = batch, PhysicalUnits = requested.PhysicalUnits ?? new List<PurchasePhysicalUnit>() });
                    }

                    await c.ExecuteAsync(Cmd(@"INSERT INTO PurchaseReturns(PurchaseInvoiceId,SupplierId,WarehouseId,ReturnMode,Status,TotalScaled,OperationKey,RequestHash,CreatedByUserId,CreatedUtc)
                        VALUES(@InvoiceId,@SupplierId,@WarehouseId,@Mode,'draft',@Total,@Key,@Hash,@UserId,@Now)", new { InvoiceId = id, invoice.SupplierId, WarehouseId = command.WarehouseId, Mode = command.ReturnMode, Total = total, Key = operationKey, Hash = requestHash, UserId = userId, Now = now }, tx, ct)).ConfigureAwait(false);
                    var returnId = await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()", null, tx, ct)).ConfigureAwait(false);
                    foreach (var line in prepared)
                    {
                        await c.ExecuteAsync(Cmd(@"INSERT INTO PurchaseReturnLines(PurchaseReturnId,PurchaseInvoiceLineId,QuantityScaled,AccountAmountScaled,InventoryValueScaled,VarianceScaled)
                            VALUES(@ReturnId,@InvoiceLineId,@Quantity,@AccountValue,0,0)", new { ReturnId = returnId, InvoiceLineId = line.Source.PurchaseInvoiceLineId, Quantity = line.QuantityScaled, AccountValue = line.AccountAmountScaled }, tx, ct)).ConfigureAwait(false);
                    }
                    var inventoryCommand = new InventoryPostCommand
                    {
                        OperationKey = "purchase-return:" + operationKey, Type = "manual_issue", WarehouseId = command.WarehouseId, UserId = userId,
                        Reason = "مرتجع شراء " + invoice.Id, Notes = "purchaseReturnId=" + returnId + ";purchaseInvoiceId=" + id,
                        Lines = prepared.Select(x => new InventoryLineInput { VariantId = x.Source.VariantId, ProductUnitId = x.Source.ProductUnitId, Quantity = x.Quantity, LotNumber = x.Batch == null ? null : x.Batch.LotNumber,
                            ExpirationDate = x.Batch == null ? null : x.Batch.ExpirationDate, RequiredOriginMovementId = x.Source.OriginMovementId,
                            PhysicalUnits = x.PhysicalUnits.Select(u => new InventoryUnitInput { Serial = u.Serial, Imeis = u.Imeis }).ToList() }).ToList()
                    };
                    var inventoryResult = await _inventory.PostWithinTransactionAsync(c, tx, inventoryCommand, InventoryService.CanonicalHashPost(inventoryCommand), ct).ConfigureAwait(false);
                    if (!inventoryResult.Succeeded) return PurchaseReturn.Fail(inventoryResult.ErrorCode, inventoryResult.ErrorMessage);
                    var lineNumber = 0;
                    foreach (var line in prepared)
                    {
                        lineNumber++;
                        var inventoryValue = -await c.ExecuteScalarAsync<long>(Cmd(@"SELECT COALESCE(SUM(m.ValueScaled),0) FROM StockMovements m JOIN InventoryDocumentLines dl ON dl.Id=m.DocumentLineId
                            WHERE dl.DocumentId=@DocumentId AND dl.LineNumber=@LineNumber AND m.WarehouseId=@WarehouseId AND m.QuantityBaseScaled<0", new { DocumentId = inventoryResult.Value.DocumentId, LineNumber = lineNumber, WarehouseId = command.WarehouseId }, tx, ct)).ConfigureAwait(false);
                        var variance = checked(line.AccountAmountScaled - inventoryValue);
                        await c.ExecuteAsync(Cmd("UPDATE PurchaseReturnLines SET InventoryValueScaled=@InventoryValue,VarianceScaled=@Variance WHERE PurchaseReturnId=@ReturnId AND PurchaseInvoiceLineId=@LineId", new { InventoryValue = inventoryValue, Variance = variance, ReturnId = returnId, LineId = line.Source.PurchaseInvoiceLineId }, tx, ct)).ConfigureAwait(false);
                    }
                    await c.ExecuteAsync(Cmd("UPDATE PurchaseReturns SET Status='posted',InventoryDocumentId=@DocumentId,PostedUtc=@Now WHERE Id=@Id AND Status='draft'", new { DocumentId = inventoryResult.Value.DocumentId, Now = now, Id = returnId }, tx, ct)).ConfigureAwait(false);
                    var entryType = command.ReturnMode == "cash_refund" ? "purchase_return_refund" : "purchase_return_credit";
                    await c.ExecuteAsync(Cmd(@"INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,PurchaseInvoiceId,Notes,OperationKey,RequestHash,CreatedUtc)
                        VALUES(@SupplierId,@Type,@Amount,@Date,@Reference,@InvoiceId,@Notes,@Key,@Hash,@Now)", new { invoice.SupplierId, Type = entryType, Amount = -total, Date = invoice.InvoiceDate, Reference = "PUR-RET-" + returnId, InvoiceId = id,
                        Notes = command.ReturnMode == "cash_refund" ? "مبلغ مسترد من المورد نقدًا؛ لا يمثل حركة خزينة" : "رصيد دائن لدى المورد", Key = "purchase-return-ledger:" + operationKey, Hash = requestHash, Now = now }, tx, ct)).ConfigureAwait(false);
                    await ChangePayableAsync(c, tx, invoice.SupplierId, -total, ct).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("INSERT INTO PurchaseReturnAudit(PurchaseReturnId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@UserId,'posted',@Now,@Details)", new { Id = returnId, UserId = userId, Now = now, Details = "inventoryDocumentId=" + inventoryResult.Value.DocumentId }, tx, ct)).ConfigureAwait(false);
                    tx.Commit();
                    var result = await GetReturnAsync(returnId, ct).ConfigureAwait(false); result.Replayed = false; return PurchaseReturn.Ok(result);
                }
            }
            catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Busy || ex.ResultCode == SQLiteErrorCode.Locked) { return PurchaseReturn.Fail("database_busy", "قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه."); }
            catch (OverflowException) { return PurchaseReturn.Fail("numeric_overflow", "قيمة المرتجع تتجاوز الحد المسموح."); }
            catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Constraint) { return PurchaseReturn.Fail("purchase_return_conflict", "تعارض المرتجع مع حركة مخزون أو إرجاع سابق."); }
        }

        public async Task<IReadOnlyList<PurchaseReturn>> ReturnsAsync(long id, CancellationToken ct)
        {
            using (var c = Open())
            {
                var rows = (await c.QueryAsync<PurchaseReturn>(Cmd("SELECT * FROM PurchaseReturns WHERE PurchaseInvoiceId=@Id ORDER BY Id DESC", new { Id = id }, null, ct)).ConfigureAwait(false)).ToList();
                foreach (var row in rows) { row.TotalFormatted = FormatScaled(row.TotalScaled, 4); row.Lines = await ReturnLinesAsync(c, null, row.Id, ct).ConfigureAwait(false); }
                return rows;
            }
        }

        private async Task<PurchaseReturn> GetReturnAsync(long id, CancellationToken ct)
        {
            using (var c = Open())
            {
                var row = await c.QuerySingleOrDefaultAsync<PurchaseReturn>(Cmd("SELECT * FROM PurchaseReturns WHERE Id=@Id", new { Id = id }, null, ct)).ConfigureAwait(false);
                if (row != null) { row.TotalFormatted = FormatScaled(row.TotalScaled, 4); row.Lines = await ReturnLinesAsync(c, null, id, ct).ConfigureAwait(false); }
                return row;
            }
        }

        private static async Task<List<PurchaseReturnLine>> ReturnLinesAsync(SQLiteConnection c, SQLiteTransaction tx, long id, CancellationToken ct)
        {
            var rows = (await c.QueryAsync<PurchaseReturnLine>(Cmd(@"SELECT rl.Id,rl.PurchaseInvoiceLineId,rl.QuantityScaled,rl.AccountAmountScaled,rl.InventoryValueScaled,rl.VarianceScaled,l.VariantId,p.Name ProductName,v.Name VariantName,l.ProductUnitId,u.Name UnitName
                FROM PurchaseReturnLines rl JOIN PurchaseInvoiceLines l ON l.Id=rl.PurchaseInvoiceLineId JOIN ProductVariants v ON v.Id=l.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.Id=l.ProductUnitId JOIN Units u ON u.Id=pu.UnitId
                WHERE rl.PurchaseReturnId=@Id ORDER BY rl.Id", new { Id = id }, tx, ct)).ConfigureAwait(false)).ToList();
            foreach (var row in rows) { row.Quantity = FormatScaled(row.QuantityScaled, 6); row.AccountAmountFormatted = FormatScaled(row.AccountAmountScaled, 4); row.InventoryValueFormatted = FormatScaled(row.InventoryValueScaled, 4); row.VarianceFormatted = FormatScaled(row.VarianceScaled, 4); }
            return rows;
        }

        private static async Task<List<PurchasePhysicalUnit>> OriginPhysicalUnitsAsync(SQLiteConnection c, SQLiteTransaction tx, long originMovementId, long warehouseId, CancellationToken ct)
        {
            var rows = await c.QueryAsync<PhysicalIdentifier>(Cmd(@"SELECT p.Id PhysicalUnitId,i.Kind,i.DisplayValue Value FROM StockMovementUnits smu
                JOIN InventoryPhysicalUnits p ON p.Id=smu.PhysicalUnitId JOIN InventoryIdentifiers i ON i.PhysicalUnitId=p.Id
                WHERE smu.MovementId=@OriginId AND p.WarehouseId=@WarehouseId ORDER BY p.Id,i.Kind,i.Id", new { OriginId = originMovementId, WarehouseId = warehouseId }, tx, ct)).ConfigureAwait(false);
            return rows.GroupBy(x => x.PhysicalUnitId).Select(group => new PurchasePhysicalUnit { Serial = group.FirstOrDefault(x => x.Kind == "serial")?.Value,
                Imeis = group.Where(x => x.Kind == "imei").Select(x => x.Value).ToList() }).Where(x => !string.IsNullOrEmpty(x.Serial)).ToList();
        }

        private sealed class ReturnSource
        {
            public long PurchaseInvoiceLineId { get; set; } public long VariantId { get; set; } public long ProductUnitId { get; set; } public long QuantityScaled { get; set; }
            public long PurchaseUnitPriceScaled { get; set; } public long ToBaseNumerator { get; set; } public long ToBaseDenominator { get; set; } public long? BatchId { get; set; } public long OriginMovementId { get; set; }
            public bool SerialTracked { get; set; } public bool ImeiTracked { get; set; }
            public string ProductName { get; set; } public string VariantName { get; set; } public string UnitName { get; set; }
        }
        private sealed class PhysicalIdentifier { public long PhysicalUnitId { get; set; } public string Kind { get; set; } public string Value { get; set; } }
        private sealed class ReturnBatch { public string LotNumber { get; set; } public string ExpirationDate { get; set; } }
        private sealed class ReturnPostingLine { public ReturnSource Source { get; set; } public string Quantity { get; set; } public long QuantityScaled { get; set; } public long BaseQuantityScaled { get; set; } public long AccountAmountScaled { get; set; } public ReturnBatch Batch { get; set; } public System.Collections.Generic.List<PurchasePhysicalUnit> PhysicalUnits { get; set; } }

        private async Task<PurchaseInvoice> LoadForPostAsync(SQLiteConnection c, SQLiteTransaction tx, long id, CancellationToken ct)
        {
            var invoice = await c.QuerySingleOrDefaultAsync<PurchaseInvoice>(Cmd("SELECT * FROM PurchaseInvoices WHERE Id=@Id", new { Id = id }, tx, ct)).ConfigureAwait(false);
            if (invoice == null) return null;
            invoice.Lines = (await c.QueryAsync<PurchaseInvoiceLine>(Cmd(@"SELECT l.Id,l.VariantId,v.ProductId,p.Name ProductName,v.Name VariantName,l.ProductUnitId,u.Name UnitName,
                CAST(l.QuantityScaled AS TEXT) Quantity,CAST(l.PurchaseUnitPriceScaled AS TEXT) PurchaseUnitPrice,CAST(l.SaleUnitPriceScaled AS TEXT) SaleUnitPrice,l.LineTotalScaled,l.LotNumber,l.ExpirationDate
                FROM PurchaseInvoiceLines l JOIN ProductVariants v ON v.Id=l.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.Id=l.ProductUnitId JOIN Units u ON u.Id=pu.UnitId
                WHERE l.PurchaseInvoiceId=@Id ORDER BY l.LineNumber", new { Id = id }, tx, ct)).ConfigureAwait(false)).ToList();
            foreach (var line in invoice.Lines)
            {
                line.Quantity = FormatScaled(ParseScaled(line.Quantity), 6); line.PurchaseUnitPrice = FormatScaled(ParseScaled(line.PurchaseUnitPrice), 4); line.SaleUnitPrice = FormatScaled(ParseScaled(line.SaleUnitPrice), 4);
                var units = await c.QueryAsync<PurchasePhysicalUnit>(Cmd("SELECT Serial FROM PurchaseInvoiceLineUnits WHERE PurchaseInvoiceLineId=@Id ORDER BY UnitNumber", new { Id = line.Id }, tx, ct)).ConfigureAwait(false); line.PhysicalUnits = units.ToList();
                foreach (var unit in line.PhysicalUnits) unit.Imeis = (await c.QueryAsync<string>(Cmd("SELECT Imei FROM PurchaseInvoiceLineImeis i JOIN PurchaseInvoiceLineUnits u ON u.Id=i.PurchaseInvoiceLineUnitId WHERE u.PurchaseInvoiceLineId=@Id AND u.Serial=@Serial ORDER BY Imei", new { Id = line.Id, unit.Serial }, tx, ct)).ConfigureAwait(false)).ToList();
            }
            return invoice;
        }

        private static async Task SaveLinesAsync(SQLiteConnection c, SQLiteTransaction tx, long invoiceId, IList<PurchaseInvoiceLine> lines, CancellationToken ct)
        {
            var number = 0;
            foreach (var line in lines)
            {
                number++;
                await c.ExecuteAsync(Cmd(@"INSERT INTO PurchaseInvoiceLines(PurchaseInvoiceId,LineNumber,VariantId,ProductUnitId,QuantityScaled,PurchaseUnitPriceScaled,SaleUnitPriceScaled,LineTotalScaled,LotNumber,ExpirationDate)
                    VALUES(@InvoiceId,@Number,@VariantId,@UnitId,@Quantity,@Cost,@Sale,@Total,@Lot,@Expiry)", new { InvoiceId = invoiceId, Number = number, line.VariantId, UnitId = line.ProductUnitId, Quantity = ScaleValue(line.Quantity, 6), Cost = ScaleValue(line.PurchaseUnitPrice, 4), Sale = ScaleValue(line.SaleUnitPrice, 4), Total = line.LineTotalScaled, Lot = line.LotNumber, Expiry = line.ExpirationDate }, tx, ct)).ConfigureAwait(false);
                var lineId = await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()", null, tx, ct)).ConfigureAwait(false);
                var unitNumber = 0;
                foreach (var unit in line.PhysicalUnits ?? new List<PurchasePhysicalUnit>())
                {
                    unitNumber++;
                    await c.ExecuteAsync(Cmd("INSERT INTO PurchaseInvoiceLineUnits(PurchaseInvoiceLineId,UnitNumber,Serial) VALUES(@LineId,@Number,@Serial)", new { LineId = lineId, Number = unitNumber, Serial = unit.Serial.Trim() }, tx, ct)).ConfigureAwait(false);
                    var unitId = await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()", null, tx, ct)).ConfigureAwait(false);
                    foreach (var imei in unit.Imeis ?? new List<string>()) await c.ExecuteAsync(Cmd("INSERT INTO PurchaseInvoiceLineImeis(PurchaseInvoiceLineUnitId,Imei) VALUES(@UnitId,@Imei)", new { UnitId = unitId, Imei = imei.Trim() }, tx, ct)).ConfigureAwait(false);
                }
            }
        }

        private static Task AuditAsync(SQLiteConnection c, SQLiteTransaction tx, long id, long userId, string type, string now, string details, CancellationToken ct)
        { return c.ExecuteAsync(Cmd("INSERT INTO PurchaseInvoiceAudit(PurchaseInvoiceId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@UserId,@Type,@Now,@Details)", new { Id = id, UserId = userId, Type = type, Now = now, Details = details }, tx, ct)); }

        private static Task AddLedgerEntryAsync(SQLiteConnection c, SQLiteTransaction tx, long supplierId, string type, long amount, string date, string reference, long invoiceId, string method, string operationKey, string hash, string now, CancellationToken ct)
        { return c.ExecuteAsync(Cmd(@"INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,PurchaseInvoiceId,PaymentMethod,Notes,OperationKey,RequestHash,CreatedUtc)
            VALUES(@SupplierId,@Type,@Amount,@Date,@Reference,@InvoiceId,@Method,'',@Key,@Hash,@Now)", new { SupplierId = supplierId, Type = type, Amount = amount, Date = date, Reference = reference, InvoiceId = invoiceId, Method = method, Key = operationKey, Hash = hash, Now = now }, tx, ct)); }

        private static async Task ChangePayableAsync(SQLiteConnection c, SQLiteTransaction tx, long supplierId, long amountScaled, CancellationToken ct)
        {
            var current = await c.QuerySingleAsync<SupplierBalance>(Cmd("SELECT Payable,SupplierCredit FROM Suppliers WHERE Id=@Id", new { Id = supplierId }, tx, ct)).ConfigureAwait(false);
            var payable = current.Payable * 10000m; var credit = current.SupplierCredit * 10000m;
            if (payable != decimal.Truncate(payable) || credit != decimal.Truncate(credit)) throw new InvalidOperationException("Supplier balance is not exactly representable at scale 4.");
            var net = checked((long)payable - (long)credit + amountScaled);
            await c.ExecuteAsync(Cmd("UPDATE Suppliers SET Payable=@Payable,SupplierCredit=@Credit,UpdatedAt=@Now WHERE Id=@Id", new { Payable = Math.Max(net, 0) / 10000m, Credit = Math.Max(-net, 0) / 10000m, Now = Now(), Id = supplierId }, tx, ct)).ConfigureAwait(false);
        }

        private sealed class SupplierBalance { public decimal Payable { get; set; } public decimal SupplierCredit { get; set; } }

        private SQLiteConnection Open() { var c = new SQLiteConnection(_connectionString); c.Open(); using (var command = new SQLiteCommand("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", c)) command.ExecuteNonQuery(); return c; }
        private static CommandDefinition Cmd(string sql, object args, SQLiteTransaction tx, CancellationToken ct) { return new CommandDefinition(sql, args, tx, cancellationToken: ct); }
        private static string Now() { return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }
        private static string NormalizeNumber(string value) { return string.IsNullOrWhiteSpace(value) ? "PUR-" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant() : value.Trim(); }
        private static long ParseScaled(string text) { long value; if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new InvalidOperationException("Invalid scaled value."); return value; }
        private static bool TryScale(string text, int places, out long scaled) { decimal value; if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) { scaled = 0; return false; } try { scaled = checked((long)(value * (places == 6 ? 1000000m : 10000m))); return true; } catch (OverflowException) { scaled = 0; return false; } }
        private static long ScaleValue(string text, int places) { decimal value; if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) throw new InvalidOperationException("Invalid decimal value."); return checked((long)(value * (places == 6 ? 1000000m : 10000m))); }
        private static string FormatScaled(long value, int scale) { return scale == 4 ? MoneyPolicy.FormatForUser(value) : (value / 1000000m).ToString("0.######", CultureInfo.InvariantCulture); }
    }
}
