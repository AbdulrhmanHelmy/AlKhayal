using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Inventory;
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore
    {
        public async Task<SaleReturnResult> ReturnAsync(long id,SaleReturnCommand command,long userId,CancellationToken ct)
        {
            var hash=ReturnHash(id,command,userId);
            try
            {
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var prior=await c.QuerySingleOrDefaultAsync<ReturnResultRow>(Cmd("SELECT Id,ReturnNumber,InventoryDocumentId,TotalScaled,CashRefundScaled,CreditReductionScaled,RequestHash FROM SalesReturns WHERE OperationKey=@Key",new{Key=command.OperationKey},tx,ct)).ConfigureAwait(false);
                    if(prior!=null){tx.Commit();return prior.RequestHash==hash?prior.ToResult(true):new SaleReturnResult{ErrorCode="operation_key_conflict",ErrorMessage="مفتاح العملية مستخدم لبيانات مختلفة."};}
                    var invoice=await c.QuerySingleOrDefaultAsync<ReturnInvoice>(Cmd("SELECT Id,CustomerId,WarehouseId,Status,PaymentMode,InvoiceNumber,PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                    if(invoice==null)return new SaleReturnResult{ErrorCode="not_found",ErrorMessage="الفاتورة غير موجودة."};
                    if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalePaymentSelections WHERE SalesInvoiceId=@Id AND Method IN ('wallet','card')",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return new SaleReturnResult{ErrorCode="electronic_reversal_unsupported",ErrorMessage="إرجاع فاتورة إلكترونية يتطلب ردًا إلكترونيًا موثقًا غير متاح حاليًا؛ لم يتغير المخزون أو الدفع."};
                    if(invoice.Status!="posted"||await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesCancellations WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return new SaleReturnResult{ErrorCode="return_not_allowed",ErrorMessage="لا يمكن إرجاع فاتورة غير منشورة أو ملغاة."};
                    var rows=new List<ReturnLine>();var seen=new HashSet<long>();long total=0;
                    foreach(var request in command.Lines)
                    {
                        if(!seen.Add(request.InvoiceLineId))return new SaleReturnResult{ErrorCode="duplicate_line",ErrorMessage="لا تكرر بند الفاتورة في طلب الإرجاع."};
                        long qty;if(!ScaleInput(request.Quantity,6,out qty)||qty<=0)return new SaleReturnResult{ErrorCode="invalid_quantity",ErrorMessage="كمية الإرجاع غير صحيحة."};
                        var line=await c.QuerySingleOrDefaultAsync<ReturnLine>(Cmd(@"SELECT l.Id,l.VariantId,l.ProductUnitId,l.LineTotalScaled,l.InventoryDocumentLineId,l.QuantityScaled SoldQuantity,
                            COALESCE((SELECT SUM(QuantityScaled) FROM SalesReturnLines WHERE SalesInvoiceLineId=l.Id),0) ReturnedQuantity,
                            d.BatchId,d.ToBaseNumerator,d.ToBaseDenominator
                            FROM SalesInvoiceLines l JOIN InventoryDocumentLines d ON d.Id=l.InventoryDocumentLineId
                            WHERE l.Id=@LineId AND l.SalesInvoiceId=@InvoiceId",new{LineId=request.InvoiceLineId,InvoiceId=id},tx,ct)).ConfigureAwait(false);
                        if(line==null||qty>line.SoldQuantity-line.ReturnedQuantity)return new SaleReturnResult{ErrorCode="return_quantity_exceeded",ErrorMessage="كمية الإرجاع أكبر من الكمية المتبقية من الفاتورة."};
                        var saleMovement=await c.ExecuteScalarAsync<long>(Cmd("SELECT Id FROM StockMovements WHERE DocumentLineId=@Id AND QuantityBaseScaled<0",new{Id=line.InventoryDocumentLineId},tx,ct)).ConfigureAwait(false);
                        var soldUnitCount=await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM StockMovementUnits WHERE MovementId=@Id",new{Id=saleMovement},tx,ct)).ConfigureAwait(false);
                        var product=await c.QuerySingleOrDefaultAsync<ReturnProduct>(Cmd(@"SELECT v.ProductId,COALESCE((SELECT a.IsEnabled FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='batch-tracking'),0) BatchTracked
                            FROM ProductVariants v WHERE v.Id=@Id",new{Id=line.VariantId},tx,ct)).ConfigureAwait(false);
                        if(product==null)return new SaleReturnResult{ErrorCode="not_found",ErrorMessage="المنتج غير موجود."};
                        var baseQty=checked(qty*line.ToBaseNumerator/line.ToBaseDenominator);
                        if(baseQty<=0||qty*line.ToBaseNumerator%line.ToBaseDenominator!=0)return new SaleReturnResult{ErrorCode="invalid_unit_quantity",ErrorMessage="الكمية لا تطابق تحويل وحدة البيع."};
                        if(soldUnitCount>0)
                        {
                            var serials=request.Serials??new List<string>();
                            if(baseQty%1000000L!=0||serials.Count!=baseQty/1000000L||serials.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=serials.Count)return new SaleReturnResult{ErrorCode="serial_required",ErrorMessage="اختر رقمًا تسلسليًا متاحًا لكل وحدة مرتجعة."};
                            line.PhysicalUnitIds=(await c.QueryAsync<long>(Cmd(@"SELECT p.Id FROM StockMovementUnits smu JOIN InventoryPhysicalUnits p ON p.Id=smu.PhysicalUnitId JOIN InventoryIdentifiers i ON i.PhysicalUnitId=p.Id AND i.Kind='serial'
                                WHERE smu.MovementId=@Movement AND p.WarehouseId IS NULL AND i.DisplayValue IN @Serials",new{Movement=saleMovement,Serials=serials},tx,ct)).ConfigureAwait(false)).ToList();
                            if(line.PhysicalUnitIds.Count!=serials.Count)return new SaleReturnResult{ErrorCode="serial_not_available",ErrorMessage="أحد الأرقام التسلسلية لا يتبع الفاتورة أو سبق إرجاعه."};
                            line.Serials=serials;
                        }
                        else if(request.Serials!=null&&request.Serials.Count>0)return new SaleReturnResult{ErrorCode="unexpected_serial",ErrorMessage="الأرقام التسلسلية غير مطلوبة لهذا الصنف."};
                        if(product.BatchTracked&&line.BatchId==null)return new SaleReturnResult{ErrorCode="batch_required",ErrorMessage="لا يمكن إرجاع منتج يتطلب رقم تشغيلة دون تشغيلة البيع الأصلية."};
                        var returnedAmount=await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(AmountScaled),0) FROM SalesReturnLines WHERE SalesInvoiceLineId=@Id",new{Id=line.Id},tx,ct)).ConfigureAwait(false);
                        line.RequestedQuantity=qty;line.BaseQuantity=baseQty;line.SaleMovementId=saleMovement;
                        line.Amount=qty+line.ReturnedQuantity==line.SoldQuantity?line.LineTotalScaled-returnedAmount:checked((long)(decimal.Floor((decimal)line.LineTotalScaled*qty/line.SoldQuantity/100m)*100m));
                        if(line.Amount<=0)return new SaleReturnResult{ErrorCode="zero_return",ErrorMessage="قيمة الإرجاع أقل من أصغر وحدة نقدية."};
                        rows.Add(line);total=checked(total+line.Amount);
                    }
                    if(rows.Count==0)return new SaleReturnResult{ErrorCode="empty_return",ErrorMessage="اختر بندًا واحدًا على الأقل."};
                    var netPaid=await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(CASE WHEN EventType IN ('cash_sale','customer_collection') THEN AmountScaled WHEN EventType='refund' AND PaymentMethod<>'credit_adjustment' THEN -AmountScaled ELSE 0 END),0) FROM SalesPayments WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                    var cash=Math.Min(total,Math.Max(0,netPaid));var credit=total-cash;
                    if(credit>0&&(invoice.PaymentMode!="credit"||!invoice.CustomerId.HasValue))return new SaleReturnResult{ErrorCode="refund_unfunded",ErrorMessage="قيمة الإرجاع تتجاوز المدفوع ولا يوجد رصيد آجل للعميل."};
                    long balanceAfterCredit=0;
                    if(invoice.PaymentMode=="credit" && invoice.CustomerId.HasValue)
                    {
                        var balance=await c.ExecuteScalarAsync<long>(Cmd("SELECT CAST(ROUND(Balance*10000) AS INTEGER) FROM Customers WHERE Id=@Id",new{Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                        var settlement=SalesRefundPolicy.CapDebtReduction(total,balance);
                        cash=settlement.Cash; credit=settlement.Credit;
                        balanceAfterCredit=checked(balance-credit);
                    }
                    var now=Now();var returnNumber="RET-"+DateTime.UtcNow.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8).ToUpperInvariant();
                    await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,Notes,CreatedByUserId,CreatedUtc,OperationKey,RequestHash)
                        VALUES('manual_receipt','draft',@Warehouse,@Reason,@Notes,@User,@Now,@Key,@Hash)",new{Warehouse=invoice.WarehouseId,Reason="إرجاع فاتورة بيع "+invoice.InvoiceNumber,Notes="salesReturn="+command.OperationKey,User=userId,Now=now,Key="sales-return:"+command.OperationKey,Hash=hash},tx,ct)).ConfigureAwait(false);
                    var documentId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    var made=new List<MadeLine>();long lineValueTotal=0;var lineNumber=0;
                    foreach(var line in rows)
                    {
                        lineNumber++;
                        await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryDocumentLines(DocumentId,LineNumber,VariantId,BatchId,EnteredQuantityScaled,ProductUnitId,ToBaseNumerator,ToBaseDenominator,BaseQuantityScaled)
                            VALUES(@Doc,@Number,@Variant,@Batch,@Quantity,@Unit,@Numerator,@Denominator,@Base)",new{Doc=documentId,Number=lineNumber,Variant=line.VariantId,Batch=line.BatchId,Quantity=line.RequestedQuantity,Unit=line.ProductUnitId,Numerator=line.ToBaseNumerator,Denominator=line.ToBaseDenominator,Base=line.BaseQuantity},tx,ct)).ConfigureAwait(false);
                        var documentLineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                        var allocations=(await c.QueryAsync<ReturnCost>(Cmd(@"SELECT a.LayerId CostLayerId,a.QuantityScaled OriginalQuantity,a.ValueScaled OriginalValue,l.WarehouseId,l.VariantId,l.BatchId,l.OriginMovementId,l.AcquiredUtc,l.UnitCostScaled,
                            COALESCE((SELECT SUM(ca.QuantityScaled) FROM SalesReturnCostAllocations ca JOIN SalesReturnLines rl ON rl.Id=ca.SalesReturnLineId WHERE ca.CostLayerId=a.LayerId AND rl.SalesInvoiceLineId=@LineId),0) ReturnedQuantity,
                            COALESCE((SELECT SUM(ca.ValueScaled) FROM SalesReturnCostAllocations ca JOIN SalesReturnLines rl ON rl.Id=ca.SalesReturnLineId WHERE ca.CostLayerId=a.LayerId AND rl.SalesInvoiceLineId=@LineId),0) ReturnedValue
                            FROM InventoryCostAllocations a JOIN InventoryCostLayers l ON l.Id=a.LayerId WHERE a.MovementId=@Movement ORDER BY a.Id",new{LineId=line.Id,Movement=line.SaleMovementId},tx,ct)).ConfigureAwait(false)).ToList();
                        var remainder=line.BaseQuantity;long value=0;
                        foreach(var allocation in allocations)
                        {
                            if(remainder==0)break;var available=allocation.OriginalQuantity-allocation.ReturnedQuantity;if(available<=0)continue;
                            var take=Math.Min(remainder,available);var part=InventoryCosting.AllocateLayer(allocation.OriginalQuantity,allocation.OriginalValue,available,allocation.OriginalValue-allocation.ReturnedValue,take);
                            allocation.ReturnQuantity=take;allocation.ReturnValue=part;value=checked(value+part);remainder-=take;
                        }
                        if(remainder!=0)return new SaleReturnResult{ErrorCode="return_cost_unavailable",ErrorMessage="تعذر تتبع تكلفة الكمية الأصلية لإتمام الإرجاع."};
                        var movement=await SqliteInventoryStore.InsertMovementAsync(c,tx,documentId,documentLineId,invoice.WarehouseId,line.VariantId,line.BatchId,line.BaseQuantity,value,null,now,ct).ConfigureAwait(false);
                        foreach(var allocation in allocations.Where(x=>x.ReturnQuantity>0))
                            await SqliteInventoryStore.AddLayerAsync(c,tx,invoice.WarehouseId,line.VariantId,line.BatchId,movement,allocation.CostLayerId,allocation.OriginMovementId,allocation.AcquiredUtc,"receipt",allocation.ReturnQuantity,allocation.ReturnValue,allocation.UnitCostScaled,ct).ConfigureAwait(false);
                        await SqliteInventoryStore.ApplyBalanceAsync(c,tx,invoice.WarehouseId,line.VariantId,line.BatchId,line.BaseQuantity,value,now,ct).ConfigureAwait(false);
                        foreach(var physicalId in line.PhysicalUnitIds)
                        {
                            await c.ExecuteAsync(Cmd("UPDATE InventoryPhysicalUnits SET WarehouseId=@Warehouse,UpdatedUtc=@Now WHERE Id=@Id AND WarehouseId IS NULL",new{Warehouse=invoice.WarehouseId,Now=now,Id=physicalId},tx,ct)).ConfigureAwait(false);
                            await c.ExecuteAsync(Cmd("INSERT INTO StockMovementUnits(MovementId,PhysicalUnitId) VALUES(@Movement,@Id)",new{Movement=movement,Id=physicalId},tx,ct)).ConfigureAwait(false);
                        }
                        lineValueTotal=checked(lineValueTotal+value);made.Add(new MadeLine{Line=line,MovementId=movement,Allocations=allocations.Where(x=>x.ReturnQuantity>0).ToList()});
                    }
                    await c.ExecuteAsync(Cmd("UPDATE InventoryDocuments SET Status='posted',PostedUtc=@Now WHERE Id=@Id",new{Now=now,Id=documentId},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("INSERT INTO InventoryAudit(DocumentId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@User,'posted',@Now,@Details)",new{Id=documentId,User=userId,Now=now,Details="إرجاع فاتورة "+invoice.InvoiceNumber},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd(@"INSERT INTO SalesReturns(ReturnNumber,SalesInvoiceId,InventoryDocumentId,OperationKey,RequestHash,TotalScaled,CashRefundScaled,CreditReductionScaled,Reason,CreatedByUserId,CreatedUtc)
                        VALUES(@Number,@Invoice,@Document,@Key,@Hash,@Total,@Cash,@Credit,@Reason,@User,@Now)",new{Number=returnNumber,Invoice=id,Document=documentId,Key=command.OperationKey,Hash=hash,Total=total,Cash=cash,Credit=credit,Reason=command.Reason,User=userId,Now=now},tx,ct)).ConfigureAwait(false);
                    var returnId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    foreach(var item in made)
                    {
                        await c.ExecuteAsync(Cmd("INSERT INTO SalesReturnLines(SalesReturnId,SalesInvoiceLineId,QuantityScaled,AmountScaled) VALUES(@Return,@Line,@Quantity,@Amount)",new{Return=returnId,Line=item.Line.Id,Quantity=item.Line.RequestedQuantity,Amount=item.Line.Amount},tx,ct)).ConfigureAwait(false);
                        var returnLineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                        foreach(var allocation in item.Allocations)await c.ExecuteAsync(Cmd("INSERT INTO SalesReturnCostAllocations(SalesReturnLineId,CostLayerId,QuantityScaled,ValueScaled) VALUES(@Line,@Layer,@Quantity,@Value)",new{Line=returnLineId,Layer=allocation.CostLayerId,Quantity=allocation.ReturnQuantity,Value=allocation.ReturnValue},tx,ct)).ConfigureAwait(false);
                        foreach(var pair in item.Line.PhysicalUnitIds.Zip(item.Line.Serials,(physicalId,serial)=>new{physicalId,serial}))
                            await c.ExecuteAsync(Cmd("INSERT INTO SalesReturnPhysicalUnits(SalesReturnLineId,PhysicalUnitId,Serial) VALUES(@Line,@Id,@Serial)",new{Line=returnLineId,Id=pair.physicalId,Serial=pair.serial},tx,ct)).ConfigureAwait(false);
                    }
                    var date=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                    if(cash>0)await c.ExecuteAsync(Cmd(@"INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,SalesReturnId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,Notes,CreatedUtc)
                        VALUES(@Invoice,@Customer,@Return,@User,'refund','cash',@Amount,@Key,@Hash,@Date,@Notes,@Now)",new{Invoice=id,Customer=invoice.CustomerId,Return=returnId,User=userId,Amount=cash,Key="refund:"+command.OperationKey,Hash=hash,Date=date,Notes="رد فاتورة "+invoice.InvoiceNumber,Now=now},tx,ct)).ConfigureAwait(false);
                    if(credit>0)
                    {
                        await c.ExecuteAsync(Cmd(@"INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,SalesReturnId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,Notes,CreatedUtc)
                            VALUES(@Invoice,@Customer,@Return,@User,'refund','credit_adjustment',@Amount,@Key,@Hash,@Date,@Notes,@Now)",new{Invoice=id,Customer=invoice.CustomerId,Return=returnId,User=userId,Amount=credit,Key="credit-refund:"+command.OperationKey,Hash=hash,Date=date,Notes="تخفيض مديونية بإرجاع "+invoice.InvoiceNumber,Now=now},tx,ct)).ConfigureAwait(false);
                        await c.ExecuteAsync(Cmd(@"INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId,SalesReturnId)
                            VALUES(@Customer,@Key,@Hash,'reduction',@Amount,@Date,@Notes,@Now,@Reference,@Invoice,@Return)",new{Customer=invoice.CustomerId,Key="sale-return:"+command.OperationKey,Hash=hash,Amount=credit,Date=date,Notes="إرجاع فاتورة "+invoice.InvoiceNumber,Now=now,Reference=returnNumber,Invoice=id,Return=returnId},tx,ct)).ConfigureAwait(false);
                        // Balance is a legacy NUMERIC projection: SQLite subtraction uses REAL
                        // and can turn an exact final debt reduction into a negative residue.
                        // Calculate in the same scale-4 integers used by the settlement/ledger,
                        // then replace the projection inside this writer transaction.
                        await c.ExecuteAsync(Cmd("UPDATE Customers SET Balance=@Balance,UpdatedAt=@Now WHERE Id=@Id",new{Balance=balanceAfterCredit/10000m,Now=now,Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                    }
                    await c.ExecuteAsync(Cmd("UPDATE SalesInvoices SET PaidScaled=MAX(0,PaidScaled-@Amount) WHERE Id=@Id",new{Amount=cash,Id=id},tx,ct)).ConfigureAwait(false);
                    await AlKhayal.Data.Treasury.SqliteReturnTreasury.PostAsync(c,tx,true,returnId,cash,command.OperationKey,hash,userId,ct).ConfigureAwait(false);
                    tx.Commit();return new SaleReturnResult{Succeeded=true,ReturnId=returnId,ReturnNumber=returnNumber,InventoryDocumentId=documentId,TotalScaled=total,CashRefundScaled=cash,CreditReductionScaled=credit};
                }
            }
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return new SaleReturnResult{ErrorCode="database_busy",ErrorMessage="قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه."};}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return new SaleReturnResult{ErrorCode="sales_conflict",ErrorMessage="تعارض الإرجاع مع حركة أخرى؛ حدّث البيانات ثم أعد المحاولة."};}
            catch(AlKhayal.Application.Treasury.TreasuryException e){return new SaleReturnResult{ErrorCode="treasury_"+e.Code,ErrorMessage=e.Message};}
            catch(OverflowException){return new SaleReturnResult{ErrorCode="numeric_overflow",ErrorMessage="قيمة الإرجاع تتجاوز الحد المسموح."};}
        }

        private static string ReturnHash(long id,SaleReturnCommand command,long userId)
        {
            using(var sha=SHA256.Create()){var value=id+"|"+command.Reason+"|"+userId+"|"+string.Join(";",command.Lines.OrderBy(x=>x.InvoiceLineId).Select(x=>x.InvoiceLineId+":"+x.Quantity+":"+string.Join(",",(x.Serials??new List<string>()).OrderBy(s=>s,StringComparer.OrdinalIgnoreCase))));return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        }
        private sealed class ReturnInvoice{public long Id{get;set;}public long? CustomerId{get;set;}public long WarehouseId{get;set;}public string Status{get;set;}public string PaymentMode{get;set;}public string InvoiceNumber{get;set;}public long PaidScaled{get;set;}}
        private sealed class ReturnLine{public long Id{get;set;}public long VariantId{get;set;}public long ProductUnitId{get;set;}public long LineTotalScaled{get;set;}public long? InventoryDocumentLineId{get;set;}public long SoldQuantity{get;set;}public long ReturnedQuantity{get;set;}public long? BatchId{get;set;}public long ToBaseNumerator{get;set;}public long ToBaseDenominator{get;set;}public long RequestedQuantity{get;set;}public long BaseQuantity{get;set;}public long SaleMovementId{get;set;}public long Amount{get;set;}public List<long> PhysicalUnitIds{get;set;}=new List<long>();public List<string> Serials{get;set;}=new List<string>();}
        private sealed class ReturnProduct{public long ProductId{get;set;}public bool BatchTracked{get;set;}}
        private sealed class ReturnCost{public long CostLayerId{get;set;}public long OriginalQuantity{get;set;}public long OriginalValue{get;set;}public long WarehouseId{get;set;}public long VariantId{get;set;}public long? BatchId{get;set;}public long? OriginMovementId{get;set;}public string AcquiredUtc{get;set;}public long? UnitCostScaled{get;set;}public long ReturnedQuantity{get;set;}public long ReturnedValue{get;set;}public long ReturnQuantity{get;set;}public long ReturnValue{get;set;}}
        private sealed class MadeLine{public ReturnLine Line{get;set;}public long MovementId{get;set;}public List<ReturnCost> Allocations{get;set;}}
        private sealed class ReturnResultRow{public long Id{get;set;}public string ReturnNumber{get;set;}public long InventoryDocumentId{get;set;}public long TotalScaled{get;set;}public long CashRefundScaled{get;set;}public long CreditReductionScaled{get;set;}public string RequestHash{get;set;}public SaleReturnResult ToResult(bool replay){return new SaleReturnResult{Succeeded=true,Replayed=replay,ReturnId=Id,ReturnNumber=ReturnNumber,InventoryDocumentId=InventoryDocumentId,TotalScaled=TotalScaled,CashRefundScaled=CashRefundScaled,CreditReductionScaled=CreditReductionScaled};}}
    }
}
