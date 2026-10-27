using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Sales;
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore
    {
        private sealed class CancellationRow { public long SalesInvoiceId{get;set;} public string RequestHash{get;set;} }
        private sealed class CancellationInvoice { public long Id{get;set;} public string Status{get;set;} public string PaymentMode{get;set;} public long PaidScaled{get;set;} public long TotalScaled{get;set;} public long? CustomerId{get;set;} public long WarehouseId{get;set;} public long? InventoryDocumentId{get;set;} public string InvoiceNumber{get;set;} public string InvoiceDate{get;set;} public string PostedUtc{get;set;} }
        private sealed class CancellationMovement { public long Id{get;set;} public long DocumentLineId{get;set;} public long VariantId{get;set;} public long? BatchId{get;set;} public long QuantityBaseScaled{get;set;} public long ValueScaled{get;set;} public long ProductUnitId{get;set;} public long EnteredQuantityScaled{get;set;} public long ToBaseNumerator{get;set;} public long ToBaseDenominator{get;set;} }
        private sealed class CancellationAllocation { public long LayerId{get;set;} public long QuantityScaled{get;set;} public long ValueScaled{get;set;} public long? OriginMovementId{get;set;} public long? UnitCostScaled{get;set;} }
        private sealed class CancellationBalance { public long QuantityBaseScaled{get;set;} public long ValueScaled{get;set;} }
        private static SaleCancellationResult CancelFail(string code,string message){return new SaleCancellationResult{ErrorCode=code,ErrorMessage=message};}
        private static string CancellationHash(long id,string key,string reason,long userId)
        {using(var sha=SHA256.Create())return "v1:"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id.ToString(CultureInfo.InvariantCulture)+"|"+key+"|"+reason+"|"+userId.ToString(CultureInfo.InvariantCulture)))).Replace("-","");}

        public async Task<SaleCancellationResult> DeleteDraftAsync(long id,string operationKey,long userId,CancellationToken ct)
        {
            var hash=CancellationHash(id,operationKey,"draft",userId);
            try{using(var c=Open())using(var tx=c.BeginTransaction())
            {
                var prior=await c.QuerySingleOrDefaultAsync<CancellationRow>(Cmd("SELECT SalesInvoiceId,RequestHash FROM SalesDraftDeletions WHERE OperationKey=@Key",new{Key=operationKey},tx,ct)).ConfigureAwait(false);
                if(prior!=null)return prior.SalesInvoiceId==id&&prior.RequestHash==hash?new SaleCancellationResult{Succeeded=true,Replayed=true}:CancelFail("operation_key_conflict","مفتاح العملية مستخدم لطلب مختلف.");
                var status=await c.ExecuteScalarAsync<string>(Cmd("SELECT Status FROM SalesInvoices WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                if(status==null)return CancelFail("not_found","الفاتورة غير موجودة.");
                if(status!="draft")return CancelFail("not_draft","الفاتورة منشورة؛ استخدم إجراء الإلغاء المالي.");
                if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesDraftDeletions WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return CancelFail("already_deleted","حُذفت المسودة سابقًا.");
                await c.ExecuteAsync(Cmd("INSERT INTO SalesDraftDeletions(SalesInvoiceId,OperationKey,RequestHash,ActorUserId,CreatedUtc) VALUES(@Id,@Key,@Hash,@UserId,@Now)",new{Id=id,Key=operationKey,Hash=hash,UserId=userId,Now=Now()},tx,ct)).ConfigureAwait(false);
                tx.Commit();return new SaleCancellationResult{Succeeded=true};
            }}catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return CancelFail("database_busy","قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه.");}
              catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return CancelFail("sales_conflict","تعارض حذف المسودة مع عملية أخرى. حدّث القائمة.");}
        }

        public async Task<SaleCancellationResult> CancelAsync(long id,string operationKey,string reason,long userId,CancellationToken ct)
        {
            var hash=CancellationHash(id,operationKey,reason,userId);
            try{using(var c=Open())using(var tx=c.BeginTransaction())
            {
                var prior=await c.QuerySingleOrDefaultAsync<CancellationRow>(Cmd("SELECT SalesInvoiceId,RequestHash FROM SalesCancellations WHERE OperationKey=@Key",new{Key=operationKey},tx,ct)).ConfigureAwait(false);
                if(prior!=null)return prior.SalesInvoiceId==id&&prior.RequestHash==hash?new SaleCancellationResult{Succeeded=true,Replayed=true,Invoice=await GetAsync(id,ct).ConfigureAwait(false)}:CancelFail("operation_key_conflict","مفتاح العملية مستخدم لطلب مختلف.");
                var invoice=await c.QuerySingleOrDefaultAsync<CancellationInvoice>(Cmd("SELECT Id,Status,PaymentMode,PaidScaled,TotalScaled,CustomerId,WarehouseId,InventoryDocumentId,InvoiceNumber,InvoiceDate,PostedUtc FROM SalesInvoices WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                if(invoice==null)return CancelFail("not_found","الفاتورة غير موجودة.");
                if(invoice.Status!="posted")return CancelFail("not_posted","يمكن حذف المسودة فقط؛ الإلغاء المالي يتطلب فاتورة منشورة.");
                if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesCancellations WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return CancelFail("already_canceled","الفاتورة ملغاة بالفعل.");
                if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesReturns WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return CancelFail("linked_returns","للفاتورة مرتجعات مرتبطة. عالجها بسياسة محاسبية معتمدة قبل الإلغاء.");
                if(invoice.PaymentMode=="cash")return CancelFail("cash_refund_policy","الفاتورة النقدية مسددة؛ يتطلب إلغاؤها سياسة موثقة لرد النقد ولم تُنفّذ بعد.");
                if(invoice.PaidScaled!=0||await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesPayments WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return CancelFail("linked_payments","للفاتورة تحصيلات مرتبطة؛ لا يمكن إلغاؤها دون تسوية المدفوعات.");
                if(!invoice.CustomerId.HasValue||!invoice.InventoryDocumentId.HasValue)return CancelFail("invoice_integrity","بيانات الفاتورة غير مكتملة؛ لا يمكن الإلغاء بأمان.");
                if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM CustomerTransactions WHERE SalesInvoiceId=@Id AND TransactionType<>'increase'",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return CancelFail("linked_collections","للفاتورة حركات عميل مرتبطة؛ لا يمكن إلغاؤها بأمان.");
                if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@CustomerId AND TransactionType IN ('collection','legacy_payment') AND CreatedUtc>=@PostedUtc",new{invoice.CustomerId,invoice.PostedUtc},tx,ct)).ConfigureAwait(false)>0)return CancelFail("possible_collection","توجد تحصيلات لاحقة للعميل قد تخص الفاتورة؛ يلزم تسويتها أولًا.");
                var customerBalance=await c.ExecuteScalarAsync<decimal>(Cmd("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                if(customerBalance<invoice.TotalScaled/10000m)return CancelFail("ledger_mismatch","رصيد العميل لا يسمح بعكس كامل الفاتورة.");
                var movements=(await c.QueryAsync<CancellationMovement>(Cmd(@"SELECT m.Id,m.DocumentLineId,m.VariantId,m.BatchId,m.QuantityBaseScaled,m.ValueScaled,l.ProductUnitId,l.EnteredQuantityScaled,l.ToBaseNumerator,l.ToBaseDenominator FROM StockMovements m JOIN InventoryDocumentLines l ON l.Id=m.DocumentLineId WHERE m.DocumentId=@Id AND m.WarehouseId=@WarehouseId ORDER BY l.LineNumber",new{Id=invoice.InventoryDocumentId,invoice.WarehouseId},tx,ct)).ConfigureAwait(false)).ToList();
                var expected=await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesInvoiceLines WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                if(movements.Count!=expected||movements.Any(x=>x.QuantityBaseScaled>=0||x.ValueScaled>0))return CancelFail("inventory_integrity","حركات مخزون الفاتورة لا تطابق بنودها.");
                if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM StockMovementUnits WHERE MovementId IN (SELECT Id FROM StockMovements WHERE DocumentId=@Id)",new{Id=invoice.InventoryDocumentId},tx,ct)).ConfigureAwait(false)>0)return CancelFail("tracked_stock_policy","تتضمن الفاتورة وحدات متسلسلة؛ يلزم إجراء إرجاع محدد للأرقام قبل الإلغاء.");
                var now=Now();
                await c.ExecuteAsync(Cmd("INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,Notes,CreatedByUserId,CreatedUtc,PostedUtc,OperationKey,RequestHash,ReversesDocumentId) VALUES('manual_receipt','draft',@WarehouseId,@Reason,@Notes,@UserId,@Now,NULL,@Key,@Hash,@Original)",new{invoice.WarehouseId,Reason="إلغاء فاتورة بيع "+invoice.InvoiceNumber,Notes=reason,UserId=userId,Now=now,Key="sale-cancel:"+operationKey,Hash=hash,Original=invoice.InventoryDocumentId},tx,ct)).ConfigureAwait(false);
                var documentId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                var lineNumber=0;
                foreach(var movement in movements)
                {
                    lineNumber++;
                    var allocations=(await c.QueryAsync<CancellationAllocation>(Cmd("SELECT a.LayerId,a.QuantityScaled,a.ValueScaled,l.OriginMovementId,l.UnitCostScaled FROM InventoryCostAllocations a JOIN InventoryCostLayers l ON l.Id=a.LayerId WHERE a.MovementId=@Id ORDER BY a.Id",new{Id=movement.Id},tx,ct)).ConfigureAwait(false)).ToList();
                    if(allocations.Count==0||allocations.Sum(x=>(decimal)x.QuantityScaled)!=-(decimal)movement.QuantityBaseScaled||allocations.Sum(x=>(decimal)x.ValueScaled)!=-(decimal)movement.ValueScaled)return CancelFail("fifo_integrity","تخصيصات FIFO لا تطابق الصرف الأصلي؛ لم يتغير المخزون.");
                    var balance=await c.QuerySingleOrDefaultAsync<CancellationBalance>(Cmd("SELECT QuantityBaseScaled,ValueScaled FROM InventoryBalances WHERE WarehouseId=@WarehouseId AND VariantId=@VariantId AND BatchId IS @BatchId",new{invoice.WarehouseId,movement.VariantId,movement.BatchId},tx,ct)).ConfigureAwait(false);
                    var layerTotals=await c.QuerySingleAsync<CancellationBalance>(Cmd("SELECT COALESCE(SUM(RemainingQuantityScaled),0) QuantityBaseScaled,COALESCE(SUM(RemainingValueScaled),0) ValueScaled FROM InventoryCostLayers WHERE WarehouseId=@WarehouseId AND VariantId=@VariantId AND BatchId IS @BatchId",new{invoice.WarehouseId,movement.VariantId,movement.BatchId},tx,ct)).ConfigureAwait(false);
                    if(balance==null||balance.QuantityBaseScaled!=layerTotals.QuantityBaseScaled||balance.ValueScaled!=layerTotals.ValueScaled)return CancelFail("fifo_integrity","الرصيد وطبقات FIFO غير متطابقين؛ لم يتغير المخزون.");
                    await c.ExecuteAsync(Cmd("INSERT INTO InventoryDocumentLines(DocumentId,LineNumber,VariantId,BatchId,EnteredQuantityScaled,ProductUnitId,ToBaseNumerator,ToBaseDenominator,BaseQuantityScaled) VALUES(@DocumentId,@LineNumber,@VariantId,@BatchId,@EnteredQuantityScaled,@ProductUnitId,@ToBaseNumerator,@ToBaseDenominator,@BaseQuantityScaled)",new{DocumentId=documentId,LineNumber=lineNumber,movement.VariantId,movement.BatchId,movement.EnteredQuantityScaled,movement.ProductUnitId,movement.ToBaseNumerator,movement.ToBaseDenominator,BaseQuantityScaled=-movement.QuantityBaseScaled},tx,ct)).ConfigureAwait(false);
                    var lineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("INSERT INTO StockMovements(DocumentId,DocumentLineId,WarehouseId,VariantId,BatchId,QuantityBaseScaled,ValueScaled,CreatedUtc) VALUES(@DocumentId,@LineId,@WarehouseId,@VariantId,@BatchId,@Quantity,@Value,@Now)",new{DocumentId=documentId,LineId=lineId,invoice.WarehouseId,movement.VariantId,movement.BatchId,Quantity=-movement.QuantityBaseScaled,Value=-movement.ValueScaled,Now=now},tx,ct)).ConfigureAwait(false);
                    var newMovementId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    foreach(var allocation in allocations)
                        await c.ExecuteAsync(Cmd("INSERT INTO InventoryCostLayers(WarehouseId,VariantId,BatchId,SourceMovementId,ParentLayerId,OriginMovementId,AcquiredUtc,Basis,QuantityScaled,ValueScaled,UnitCostScaled,RemainingQuantityScaled,RemainingValueScaled) VALUES(@WarehouseId,@VariantId,@BatchId,@MovementId,@ParentId,@OriginId,@Now,'receipt',@Quantity,@Value,@Cost,@Quantity,@Value)",new{invoice.WarehouseId,movement.VariantId,movement.BatchId,MovementId=newMovementId,ParentId=allocation.LayerId,OriginId=allocation.OriginMovementId,Now=now,Quantity=allocation.QuantityScaled,Value=allocation.ValueScaled,Cost=allocation.UnitCostScaled},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("UPDATE InventoryBalances SET QuantityBaseScaled=QuantityBaseScaled+@Quantity,ValueScaled=ValueScaled+@Value,Version=Version+1,UpdatedUtc=@Now WHERE WarehouseId=@WarehouseId AND VariantId=@VariantId AND BatchId IS @BatchId",new{Quantity=-movement.QuantityBaseScaled,Value=-movement.ValueScaled,Now=now,invoice.WarehouseId,movement.VariantId,movement.BatchId},tx,ct)).ConfigureAwait(false);
                }
                await c.ExecuteAsync(Cmd("UPDATE InventoryDocuments SET Status='posted',PostedUtc=@Now WHERE Id=@Id AND Status='draft'",new{Now=now,Id=documentId},tx,ct)).ConfigureAwait(false);
                await c.ExecuteAsync(Cmd("INSERT INTO InventoryAudit(DocumentId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@UserId,'posted',@Now,@Details)",new{Id=documentId,UserId=userId,Now=now,Details="salesInvoiceId="+id},tx,ct)).ConfigureAwait(false);
                await c.ExecuteAsync(Cmd("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId) VALUES(@CustomerId,@Key,@Hash,'reduction',@Amount,@Date,@Notes,@Now,@Reference,@Id)",new{invoice.CustomerId,Key="sale-cancel-ledger:"+operationKey,Hash=hash,Amount=invoice.TotalScaled,Date=invoice.InvoiceDate,Notes="إلغاء فاتورة بيع "+invoice.InvoiceNumber,Now=now,Reference=invoice.InvoiceNumber,Id=id},tx,ct)).ConfigureAwait(false);
                await c.ExecuteAsync(Cmd("UPDATE Customers SET Balance=Balance-@Amount,UpdatedAt=@Now WHERE Id=@Id AND Balance>=@Amount",new{Amount=invoice.TotalScaled/10000m,Now=now,Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                await c.ExecuteAsync(Cmd("INSERT INTO SalesCancellations(SalesInvoiceId,InventoryDocumentId,OperationKey,RequestHash,Reason,ActorUserId,CreatedUtc) VALUES(@Id,@DocumentId,@Key,@Hash,@Reason,@UserId,@Now)",new{Id=id,DocumentId=documentId,Key=operationKey,Hash=hash,Reason=reason,UserId=userId,Now=now},tx,ct)).ConfigureAwait(false);
                tx.Commit();return new SaleCancellationResult{Succeeded=true,Invoice=await GetAsync(id,ct).ConfigureAwait(false)};
            }}catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return CancelFail("database_busy","قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه.");}
              catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return CancelFail("sales_conflict","تعارض الإلغاء مع حركة أخرى؛ لم تُحفظ أي تسوية جزئية.");}
        }
    }
}
