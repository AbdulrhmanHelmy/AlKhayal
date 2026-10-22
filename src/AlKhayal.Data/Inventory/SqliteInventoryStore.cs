using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using Dapper;

namespace AlKhayal.Data.Inventory
{
    public sealed class SqliteInventoryStore : IInventoryStore
    {
        private readonly string _connectionString;
        public SqliteInventoryStore(string connectionString) { _connectionString=connectionString+"Default Timeout=5;"; }
        private static string Now() { return DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture); }
        private SQLiteConnection Open() { var c=new SQLiteConnection(_connectionString);c.Open();using(var cmd=new SQLiteCommand("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;",c))cmd.ExecuteNonQuery();return c; }
        private static CommandDefinition Cmd(string sql,object args,SQLiteTransaction tx,CancellationToken ct) { return new CommandDefinition(sql,args,tx,cancellationToken:ct); }
        private static InventoryResult<T> Fail<T>(string code,string message) { return InventoryResult<T>.Fail(code,message); }
        private static string Code(string value) { return (value??"").Trim().Normalize(System.Text.NormalizationForm.FormKC).ToUpperInvariant(); }

        public async Task<IReadOnlyList<WarehouseRecord>> WarehousesAsync(CancellationToken ct)
        {
            using(var c=Open())return (await c.QueryAsync<WarehouseRecord>(new CommandDefinition(@"SELECT w.*,COALESCE((SELECT COUNT(DISTINCT b.VariantId) FROM InventoryBalances b WHERE b.WarehouseId=w.Id AND b.QuantityBaseScaled>0),0) ProductCount,
                COALESCE((SELECT SUM(b.QuantityBaseScaled) FROM InventoryBalances b WHERE b.WarehouseId=w.Id),0) QuantityScaled,
                COALESCE((SELECT SUM(b.ValueScaled) FROM InventoryBalances b WHERE b.WarehouseId=w.Id),0) ValueScaled
                FROM Warehouses w ORDER BY w.IsDefault DESC,w.Name,w.Id",cancellationToken:ct)).ConfigureAwait(false))
                .Select(w=>{w.Quantity=(w.QuantityScaled/1000000m).ToString("0.######",CultureInfo.InvariantCulture);w.Value=(w.ValueScaled/10000m).ToString("0.####",CultureInfo.InvariantCulture);return w;}).ToList();
        }

        public async Task<InventoryResult<WarehouseRecord>> SaveWarehouseAsync(WarehouseCommand command,bool multipleWarehouses,CancellationToken ct)
        {
            try
            {
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var count=await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Warehouses",null,tx,ct)).ConfigureAwait(false);
                    if(!command.Id.HasValue&&count>0&&!multipleWarehouses)return Fail<WarehouseRecord>("feature_disabled","فعّل تعدد المخازن لإضافة مخزن آخر.");
                    var old=command.Id.HasValue?await c.QuerySingleOrDefaultAsync<WarehouseRecord>(Cmd("SELECT * FROM Warehouses WHERE Id=@Id",new{command.Id},tx,ct)).ConfigureAwait(false):null;
                    if(command.Id.HasValue&&old==null)return Fail<WarehouseRecord>("not_found","المخزن غير موجود.");
                    if(command.Id.HasValue&&!command.IsActive)
                    {
                        if(old.IsDefault)return Fail<WarehouseRecord>("default_warehouse","عيّن مخزنًا افتراضيًا آخر أولًا.");
                        var stock=await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM InventoryBalances WHERE WarehouseId=@Id AND QuantityBaseScaled>0",new{command.Id},tx,ct)).ConfigureAwait(false);
                        if(stock>0)return Fail<WarehouseRecord>("warehouse_has_stock","لا يمكن تعطيل مخزن يحتوي على بضاعة.");
                    }
                    if(command.Id.HasValue&&old.IsDefault&&!command.IsDefault)return Fail<WarehouseRecord>("default_warehouse","عيّن مخزنًا افتراضيًا آخر أولًا.");
                    if(command.IsDefault&&!command.IsActive)return Fail<WarehouseRecord>("invalid_warehouse","يجب أن يكون المخزن الافتراضي نشطًا.");
                    if(command.IsDefault)await c.ExecuteAsync(Cmd("UPDATE Warehouses SET IsDefault=0 WHERE IsDefault=1",null,tx,ct)).ConfigureAwait(false);
                    var now=Now();var normalized=Code(command.Code);
                    long id;
                    if(command.Id.HasValue)
                    {
                        await c.ExecuteAsync(Cmd(@"UPDATE Warehouses SET Code=@Code,NormalizedCode=@Normalized,Name=@Name,Description=@Description,Location=@Location,IsActive=@IsActive,IsDefault=@IsDefault,UpdatedUtc=@Now WHERE Id=@Id",
                            new{Id=command.Id,Code=command.Code.Trim(),Normalized=normalized,Name=command.Name.Trim(),command.Description,command.Location,command.IsActive,command.IsDefault,Now=now},tx,ct)).ConfigureAwait(false);id=command.Id.Value;
                    }
                    else
                    {
                        await c.ExecuteAsync(Cmd(@"INSERT INTO Warehouses(Code,NormalizedCode,Name,Description,Location,IsActive,IsDefault,CreatedUtc,UpdatedUtc) VALUES(@Code,@Normalized,@Name,@Description,@Location,@IsActive,@IsDefault,@Now,@Now)",
                            new{Code=command.Code.Trim(),Normalized=normalized,Name=command.Name.Trim(),command.Description,command.Location,command.IsActive,IsDefault=command.IsDefault||count==0,Now=now},tx,ct)).ConfigureAwait(false);
                        id=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    }
                    var saved=await c.QuerySingleAsync<WarehouseRecord>(Cmd("SELECT * FROM Warehouses WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                    tx.Commit();return InventoryResult<WarehouseRecord>.Ok(saved);
                }
            }
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return Fail<WarehouseRecord>("duplicate_warehouse","كود المخزن مستخدم بالفعل.");}
        }

        private sealed class VariantState
        {
            public long Id { get; set; } public long ProductId { get; set; } public bool IsActive { get; set; } public string ArchivedUtc { get; set; }
            public string ProductType { get; set; } public bool ProductActive { get; set; } public string ProductArchivedUtc { get; set; }
            public long ProductUnitId { get; set; } public long Numerator { get; set; } public long Denominator { get; set; } public bool UnitActive { get; set; }
            public bool BatchTracked { get; set; } public bool ExpiryTracked { get; set; } public bool SerialTracked { get; set; } public bool ImeiTracked { get; set; }
        }
        private sealed class BalanceState { public long Id { get; set; } public long QuantityBaseScaled { get; set; } public long ValueScaled { get; set; } public long Version { get; set; } }
        private sealed class PhysicalState { public long Id { get; set; } public long VariantId { get; set; } public long? BatchId { get; set; } public long? WarehouseId { get; set; } }
        private sealed class BatchState { public long Id { get; set; } public string ExpirationDate { get; set; } }
        private sealed class DocumentState { public long Id { get; set; } public string DocumentType { get; set; } public string Status { get; set; } public long SourceWarehouseId { get; set; } public long? DestinationWarehouseId { get; set; } public string RequestHash { get; set; } }

        public async Task<InventoryResult<InventoryPostResult>> PostAsync(InventoryPostCommand command,string requestHash,CancellationToken ct)
        {
            try
            {
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var replay=await ReplayAsync(c,tx,command.OperationKey,requestHash,ct).ConfigureAwait(false);
                    if(replay!=null)return replay;
                    var result=await PostInTransactionAsync(c,tx,command,requestHash,null,ct).ConfigureAwait(false);
                    if(!result.Succeeded)return result;
                    tx.Commit();return result;
                }
            }
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return Fail<InventoryPostResult>("database_busy","المخزون مشغول الآن. أعد المحاولة بمفتاح العملية نفسه.");}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return Fail<InventoryPostResult>("inventory_conflict","تعارضت البيانات مع حركة مخزون أخرى. حدّث الأرصدة وحاول مرة أخرى.");}
            catch(OverflowException){return Fail<InventoryPostResult>("numeric_overflow","الكمية أو القيمة تتجاوز الحد المسموح.");}
        }

        private static async Task<InventoryResult<InventoryPostResult>> ReplayAsync(SQLiteConnection c,SQLiteTransaction tx,string key,string hash,CancellationToken ct)
        {
            var existing=await c.QuerySingleOrDefaultAsync<DocumentState>(Cmd("SELECT Id,RequestHash FROM InventoryDocuments WHERE OperationKey=@Key",new{Key=key},tx,ct)).ConfigureAwait(false);
            if(existing==null)return null;
            if(existing.RequestHash==null||!existing.RequestHash.StartsWith("v2:",StringComparison.Ordinal))return Fail<InventoryPostResult>("operation_key_conflict","مفتاح العملية مستخدم لطلب سابق لا يمكن إعادة تشغيله تلقائيًا.");
            if(existing.RequestHash!=hash)return Fail<InventoryPostResult>("operation_key_conflict","مفتاح العملية مستخدم لطلب مختلف.");
            return InventoryResult<InventoryPostResult>.Ok(new InventoryPostResult{DocumentId=existing.Id,Reference="INV-"+existing.Id,Replayed=true});
        }

        private async Task<InventoryResult<InventoryPostResult>> PostInTransactionAsync(SQLiteConnection c,SQLiteTransaction tx,InventoryPostCommand command,string hash,long? draftId,CancellationToken ct)
        {
            var source=await c.QuerySingleOrDefaultAsync<WarehouseRecord>(Cmd("SELECT * FROM Warehouses WHERE Id=@Id",new{Id=command.WarehouseId},tx,ct)).ConfigureAwait(false);
            if(source==null||!source.IsActive)return Fail<InventoryPostResult>("invalid_warehouse","المخزن غير نشط أو غير موجود.");
            if(command.Type=="transfer")
            {
                if(command.DestinationWarehouseId==command.WarehouseId)return Fail<InventoryPostResult>("invalid_destination","اختر مخزنًا آخر للوجهة.");
                var destination=await c.QuerySingleOrDefaultAsync<WarehouseRecord>(Cmd("SELECT * FROM Warehouses WHERE Id=@Id",new{Id=command.DestinationWarehouseId},tx,ct)).ConfigureAwait(false);
                if(destination==null||!destination.IsActive)return Fail<InventoryPostResult>("invalid_destination","المخزن الوجهة غير نشط أو غير موجود.");
            }
            var now=Now();long documentId;
            if(draftId.HasValue)
            {
                var changed=await c.ExecuteAsync(Cmd("UPDATE InventoryDocuments SET OperationKey=@Key,RequestHash=@Hash,Reason=@Reason,Notes=@Notes WHERE Id=@Id AND Status='draft'",new{Key=command.OperationKey,Hash=hash,command.Reason,command.Notes,Id=draftId},tx,ct)).ConfigureAwait(false);
                if(changed!=1)return Fail<InventoryPostResult>("stale_count","الجرد لم يعد مسودة صالحة.");documentId=draftId.Value;
            }
            else
            {
                await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,DestinationWarehouseId,Reason,Notes,CreatedByUserId,CreatedUtc,PostedUtc,OperationKey,RequestHash,ReversesDocumentId)
                    VALUES(@Type,'draft',@WarehouseId,@DestinationWarehouseId,@Reason,@Notes,@UserId,@Now,NULL,@OperationKey,@Hash,@ReversesDocumentId)",new{command.Type,command.WarehouseId,command.DestinationWarehouseId,Reason=command.Reason??"رصيد افتتاحي",command.Notes,command.UserId,Now=now,command.OperationKey,Hash=hash,command.ReversesDocumentId},tx,ct)).ConfigureAwait(false);
                documentId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
            }
            var lineNumber=0;
            foreach(var line in command.Lines)
            {
                lineNumber++;
                var variant=await c.QuerySingleOrDefaultAsync<VariantState>(Cmd(@"SELECT v.Id,v.ProductId,v.IsActive,v.ArchivedUtc,d.ProductType,d.IsActive ProductActive,d.ArchivedUtc ProductArchivedUtc,
                    pu.Id ProductUnitId,pu.ToBaseNumerator Numerator,pu.ToBaseDenominator Denominator,pu.IsActive UnitActive,
                    EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='batch-tracking' AND a.IsEnabled=1) BatchTracked,
                    EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='expiry-dates' AND a.IsEnabled=1) ExpiryTracked,
                    EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='serial-numbers' AND a.IsEnabled=1) SerialTracked,
                    EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='imei-tracking' AND a.IsEnabled=1) ImeiTracked
                    FROM ProductVariants v JOIN ProductDetails d ON d.ProductId=v.ProductId LEFT JOIN ProductUnits pu ON pu.Id=@UnitId AND pu.ProductId=v.ProductId WHERE v.Id=@VariantId",
                    new{UnitId=line.ProductUnitId,line.VariantId},tx,ct)).ConfigureAwait(false);
                if(variant==null||variant.ProductType!="stock"||variant.ProductUnitId==0||!variant.UnitActive)
                    return Fail<InventoryPostResult>("invalid_variant","المنتج أو المتغير أو وحدة القياس غير صالح للمخزون.");
                long entered;if(!InventoryService.TryScale(line.Quantity,6,out entered)||(entered==0&&command.Type!="count"))return Fail<InventoryPostResult>("invalid_quantity","الكمية غير صالحة.");
                var numerator=variant.Numerator;var denominator=variant.Denominator;
                var exact=(decimal)entered*numerator/denominator;
                if(exact!=decimal.Truncate(exact)||exact>long.MaxValue||exact<long.MinValue)return Fail<InventoryPostResult>("invalid_quantity_precision","التحويل إلى الوحدة الأساسية لا يعطي كمية دقيقة.");
                var baseQuantity=checked((long)exact);
                if(command.Type=="manual_issue"||command.Type=="transfer"||command.Type=="reversal")baseQuantity=checked(-Math.Abs(baseQuantity));
                if(command.Type!="adjustment"&&command.Type!="manual_issue"&&command.Type!="transfer"&&command.Type!="count"&&command.Type!="reversal"&&baseQuantity<=0)return Fail<InventoryPostResult>("invalid_quantity","أدخل كمية موجبة.");
                if(baseQuantity>0&&(!variant.IsActive||variant.ArchivedUtc!=null||!variant.ProductActive||variant.ProductArchivedUtc!=null))
                    return Fail<InventoryPostResult>("inactive_variant","لا يمكن إضافة بضاعة إلى منتج غير نشط أو مؤرشف.");
                await c.ExecuteAsync(Cmd("UPDATE ProductUnits SET FirstUsedUtc=COALESCE(FirstUsedUtc,@Now) WHERE Id=@Id",new{Now=now,Id=line.ProductUnitId},tx,ct)).ConfigureAwait(false);
                long? batchId=null;
                if(variant.BatchTracked)
                {
                    string normalized;
                    if(!TrackingIdentityRules.TryNormalizeBatch(line.LotNumber,out normalized))return Fail<InventoryPostResult>("batch_required","أدخل رقم التشغيلة.");
                    var batch=await c.QuerySingleOrDefaultAsync<BatchState>(Cmd("SELECT Id,ExpirationDate FROM ProductBatches WHERE VariantId=@VariantId AND NormalizedLotNumber=@Normalized",new{line.VariantId,Normalized=normalized},tx,ct)).ConfigureAwait(false);
                    if(batch!=null)
                    {
                        batchId=batch.Id;
                        if(line.ExpirationDate!=null&&batch.ExpirationDate!=line.ExpirationDate)return Fail<InventoryPostResult>("batch_conflict","تاريخ صلاحية التشغيلة لا يطابق السجل الحالي.");
                    }
                    else
                    {
                        if(baseQuantity<0)return Fail<InventoryPostResult>("batch_not_found","التشغيلة غير موجودة في المخزون.");
                        DateTime expiry;
                        if(variant.ExpiryTracked&&!TrackingIdentityRules.TryParseExpirationDate(line.ExpirationDate,out expiry))return Fail<InventoryPostResult>("expiry_required","أدخل تاريخ صلاحية صحيحًا.");
                        await c.ExecuteAsync(Cmd("INSERT INTO ProductBatches(VariantId,LotNumber,NormalizedLotNumber,ExpirationDate,CreatedUtc) VALUES(@VariantId,@Lot,@Normalized,@Expiry,@Now)",new{line.VariantId,Lot=line.LotNumber.Trim(),Normalized=normalized,Expiry=line.ExpirationDate,Now=now},tx,ct)).ConfigureAwait(false);
                        batchId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    }
                    if(variant.ExpiryTracked&&batch!=null&&batch.ExpirationDate==null)return Fail<InventoryPostResult>("expiry_required","التشغيلة بلا تاريخ صلاحية.");
                    if(baseQuantity<0&&(command.Type=="manual_issue"||command.Type=="transfer")&&batch!=null&&batch.ExpirationDate!=null&&string.CompareOrdinal(batch.ExpirationDate,DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))<0)
                        return Fail<InventoryPostResult>("expired_batch","لا يمكن صرف تشغيلة منتهية. استخدم تسوية تلف موثقة.");
                }
                else if(!string.IsNullOrWhiteSpace(line.LotNumber))return Fail<InventoryPostResult>("unexpected_batch","هذا المنتج لا يتتبع التشغيلات.");
                var balance=await FindBalanceAsync(c,tx,command.WarehouseId,line.VariantId,batchId,ct).ConfigureAwait(false);
                if(line.ExpectedVersion.HasValue&&line.ExpectedVersion.Value!=(balance==null?0:balance.Version))return Fail<InventoryPostResult>("stale_count","تغير رصيد المخزن أثناء الجرد. أعد الجرد.");
                if(line.ExpectedQuantity!=null){long expected;if(!InventoryService.TryScale(line.ExpectedQuantity,6,out expected)||expected!=(balance==null?0:balance.QuantityBaseScaled))return Fail<InventoryPostResult>("stale_count","تغير رصيد المخزن أثناء الجرد. أعد الجرد.");}
                if(baseQuantity<0&&(balance==null||balance.QuantityBaseScaled<Math.Abs(baseQuantity)))return Fail<InventoryPostResult>("insufficient_stock","الرصيد المتاح أقل من الكمية المطلوبة.");
                if(command.Type=="opening"&&await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM StockMovements WHERE WarehouseId=@WarehouseId AND VariantId=@VariantId AND ((BatchId IS NULL AND @BatchId IS NULL) OR BatchId=@BatchId)",new{WarehouseId=command.WarehouseId,line.VariantId,BatchId=batchId},tx,ct)).ConfigureAwait(false)>0)
                    return Fail<InventoryPostResult>("opening_exists","هذا الصنف له حركة سابقة في المخزن؛ استخدم إضافة بضاعة أو تسوية.");
                if(variant.SerialTracked&&Math.Abs(baseQuantity)%1000000L!=0)return Fail<InventoryPostResult>("serial_quantity","المنتج المتسلسل يحتاج عدد وحدات صحيحًا.");
                var units=line.PhysicalUnits??new InventoryUnitInput[0];
                if(variant.SerialTracked&&units.Count!=Math.Abs(baseQuantity)/1000000L)return Fail<InventoryPostResult>("serial_quantity","عدد الأرقام الفعلية لا يطابق الكمية.");
                if(!variant.SerialTracked&&units.Count>0)return Fail<InventoryPostResult>("unexpected_identifiers","هذا المنتج لا يتتبع الأرقام الفعلية.");
                var unitCost=(long?)null;long value;
                if(baseQuantity==0)value=0;
                else if(baseQuantity>0)
                {
                    long parsed;if(!InventoryService.TryScale(line.UnitCost,4,out parsed))return Fail<InventoryPostResult>("cost_required","أدخل تكلفة اقتناء فعلية.");
                    unitCost=parsed;
                    value=checked((long)Math.Round((decimal)parsed*baseQuantity/1000000m,0,MidpointRounding.AwayFromZero));
                }
                else
                {
                    value=balance.QuantityBaseScaled==Math.Abs(baseQuantity)?-balance.ValueScaled:
                        -checked((long)Math.Round((decimal)balance.ValueScaled*Math.Abs(baseQuantity)/balance.QuantityBaseScaled,0,MidpointRounding.AwayFromZero));
                    unitCost=balance.QuantityBaseScaled==0?0:checked((long)Math.Round((decimal)balance.ValueScaled*1000000m/balance.QuantityBaseScaled,0,MidpointRounding.AwayFromZero));
                }
                long lineId;
                if(draftId.HasValue)
                {
                    lineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT Id FROM InventoryDocumentLines WHERE DocumentId=@DocumentId AND LineNumber=@LineNumber",new{DocumentId=documentId,LineNumber=lineNumber},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("UPDATE InventoryDocumentLines SET EnteredQuantityScaled=@Entered,BaseQuantityScaled=@Base,UnitCostScaled=@Cost,CountedQuantityScaled=@Counted WHERE Id=@Id",new{Entered=entered,Base=baseQuantity,Cost=unitCost,Counted=(balance==null?0:balance.QuantityBaseScaled)+baseQuantity,Id=lineId},tx,ct)).ConfigureAwait(false);
                }
                else
                {
                    await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryDocumentLines(DocumentId,LineNumber,VariantId,BatchId,EnteredQuantityScaled,ProductUnitId,ToBaseNumerator,ToBaseDenominator,BaseQuantityScaled,UnitCostScaled,ExpectedBalanceScaled,ExpectedBalanceVersion)
                        VALUES(@DocumentId,@LineNumber,@VariantId,@BatchId,@Entered,@ProductUnitId,@Numerator,@Denominator,@Base,@Cost,@Expected,@Version)",new{DocumentId=documentId,LineNumber=lineNumber,line.VariantId,BatchId=batchId,Entered=entered,line.ProductUnitId,Numerator=numerator,Denominator=denominator,Base=baseQuantity,Cost=unitCost,Expected=(object)null,Version=line.ExpectedVersion},tx,ct)).ConfigureAwait(false);
                    lineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                }
                if(baseQuantity==0)continue;
                var movementId=await InsertMovementAsync(c,tx,documentId,lineId,command.WarehouseId,line.VariantId,batchId,baseQuantity,value,unitCost,now,ct).ConfigureAwait(false);
                await ApplyBalanceAsync(c,tx,command.WarehouseId,line.VariantId,batchId,baseQuantity,value,now,ct).ConfigureAwait(false);
                if(variant.SerialTracked)
                {
                    var physical=await ApplyPhysicalAsync(c,tx,variant,line,batchId,command.WarehouseId,command.DestinationWarehouseId,baseQuantity,movementId,now,ct).ConfigureAwait(false);
                    if(!physical.Succeeded)return physical;
                }
                if(command.Type=="transfer")
                {
                    var inbound=await InsertMovementAsync(c,tx,documentId,lineId,command.DestinationWarehouseId.Value,line.VariantId,batchId,-baseQuantity,-value,unitCost,now,ct).ConfigureAwait(false);
                    await ApplyBalanceAsync(c,tx,command.DestinationWarehouseId.Value,line.VariantId,batchId,-baseQuantity,-value,now,ct).ConfigureAwait(false);
                    if(variant.SerialTracked)await c.ExecuteAsync(Cmd("INSERT INTO StockMovementUnits(MovementId,PhysicalUnitId) SELECT @Inbound,PhysicalUnitId FROM StockMovementUnits WHERE MovementId=@Outbound",new{Inbound=inbound,Outbound=movementId},tx,ct)).ConfigureAwait(false);
                }
            }
            await c.ExecuteAsync(Cmd("UPDATE InventoryDocuments SET Status='posted',PostedUtc=@Now WHERE Id=@Id AND Status='draft'",new{Now=now,Id=documentId},tx,ct)).ConfigureAwait(false);
            await c.ExecuteAsync(Cmd("INSERT INTO InventoryAudit(DocumentId,ActorUserId,EventType,EventUtc,Details) VALUES(@DocumentId,@UserId,'posted',@Now,@Details)",new{DocumentId=documentId,command.UserId,Now=now,Details=command.Reason},tx,ct)).ConfigureAwait(false);
            return InventoryResult<InventoryPostResult>.Ok(new InventoryPostResult{DocumentId=documentId,Reference="INV-"+documentId});
        }

        private static async Task<long> InsertMovementAsync(SQLiteConnection c,SQLiteTransaction tx,long documentId,long lineId,long warehouseId,long variantId,long? batchId,long quantity,long value,long? cost,string now,CancellationToken ct)
        {
            await c.ExecuteAsync(Cmd("INSERT INTO StockMovements(DocumentId,DocumentLineId,WarehouseId,VariantId,BatchId,QuantityBaseScaled,ValueScaled,UnitCostScaled,CreatedUtc) VALUES(@DocumentId,@LineId,@WarehouseId,@VariantId,@BatchId,@Quantity,@Value,@Cost,@Now)",new{DocumentId=documentId,LineId=lineId,WarehouseId=warehouseId,VariantId=variantId,BatchId=batchId,Quantity=quantity,Value=value,Cost=cost,Now=now},tx,ct)).ConfigureAwait(false);
            return await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
        }
        private static Task<BalanceState> FindBalanceAsync(SQLiteConnection c,SQLiteTransaction tx,long warehouseId,long variantId,long? batchId,CancellationToken ct)
        { return c.QuerySingleOrDefaultAsync<BalanceState>(Cmd("SELECT * FROM InventoryBalances WHERE WarehouseId=@WarehouseId AND VariantId=@VariantId AND ((@BatchId IS NULL AND BatchId IS NULL) OR BatchId=@BatchId)",new{WarehouseId=warehouseId,VariantId=variantId,BatchId=batchId},tx,ct)); }
        private static async Task ApplyBalanceAsync(SQLiteConnection c,SQLiteTransaction tx,long warehouseId,long variantId,long? batchId,long quantity,long value,string now,CancellationToken ct)
        {
            var current=await FindBalanceAsync(c,tx,warehouseId,variantId,batchId,ct).ConfigureAwait(false);
            var nextQuantity=checked((current==null?0:current.QuantityBaseScaled)+quantity);
            var nextValue=checked((current==null?0:current.ValueScaled)+value);
            if(nextQuantity<0||nextValue<0)throw new SQLiteException(SQLiteErrorCode.Constraint,"negative inventory projection");
            if(nextQuantity==0)nextValue=0;
            if(current==null)await c.ExecuteAsync(Cmd("INSERT INTO InventoryBalances(WarehouseId,VariantId,BatchId,QuantityBaseScaled,ValueScaled,Version,UpdatedUtc) VALUES(@WarehouseId,@VariantId,@BatchId,@Quantity,@Value,1,@Now)",new{WarehouseId=warehouseId,VariantId=variantId,BatchId=batchId,Quantity=nextQuantity,Value=nextValue,Now=now},tx,ct)).ConfigureAwait(false);
            else await c.ExecuteAsync(Cmd("UPDATE InventoryBalances SET QuantityBaseScaled=@Quantity,ValueScaled=@Value,Version=Version+1,UpdatedUtc=@Now WHERE Id=@Id",new{Quantity=nextQuantity,Value=nextValue,Now=now,current.Id},tx,ct)).ConfigureAwait(false);
        }

        private static async Task<InventoryResult<InventoryPostResult>> ApplyPhysicalAsync(SQLiteConnection c,SQLiteTransaction tx,VariantState variant,InventoryLineInput line,long? batchId,long warehouseId,long? destinationId,long quantity,long movementId,string now,CancellationToken ct)
        {
            foreach(var unit in line.PhysicalUnits)
            {
                string serial;
                if(!TrackingIdentityRules.TryNormalizeSerial(unit.Serial,out serial))return Fail<InventoryPostResult>("serial_required","أدخل رقمًا تسلسليًا صحيحًا لكل وحدة.");
                var imeis=unit.Imeis??new string[0];
                if(variant.ImeiTracked&&imeis.Count==0)return Fail<InventoryPostResult>("imei_required","أدخل IMEI واحدًا على الأقل لكل جهاز.");
                if(!variant.ImeiTracked&&imeis.Count>0)return Fail<InventoryPostResult>("unexpected_imei","IMEI غير مفعّل لهذا المنتج.");
                foreach(var imei in imeis){string normalized;if(!TrackingIdentityRules.TryNormalizeImei(imei,out normalized))return Fail<InventoryPostResult>("invalid_imei","رقم IMEI غير صالح.");}
                long physicalId;
                if(quantity>0)
                {
                    await c.ExecuteAsync(Cmd("INSERT INTO InventoryPhysicalUnits(VariantId,BatchId,WarehouseId,CreatedUtc,UpdatedUtc) VALUES(@VariantId,@BatchId,@WarehouseId,@Now,@Now)",new{line.VariantId,BatchId=batchId,WarehouseId=warehouseId,Now=now},tx,ct)).ConfigureAwait(false);
                    physicalId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("INSERT INTO InventoryIdentifiers(PhysicalUnitId,Kind,DisplayValue,NormalizedValue,CreatedUtc) VALUES(@Id,'serial',@Display,@Normalized,@Now)",new{Id=physicalId,Display=unit.Serial.Trim(),Normalized=serial,Now=now},tx,ct)).ConfigureAwait(false);
                    foreach(var imei in imeis)await c.ExecuteAsync(Cmd("INSERT INTO InventoryIdentifiers(PhysicalUnitId,Kind,DisplayValue,NormalizedValue,CreatedUtc) VALUES(@Id,'imei',@Display,@Normalized,@Now)",new{Id=physicalId,Display=imei.Trim(),Normalized=imei.Trim(),Now=now},tx,ct)).ConfigureAwait(false);
                }
                else
                {
                    var current=await c.QuerySingleOrDefaultAsync<PhysicalState>(Cmd(@"SELECT p.* FROM InventoryPhysicalUnits p JOIN InventoryIdentifiers i ON i.PhysicalUnitId=p.Id WHERE i.NormalizedValue=@Serial AND i.Kind='serial'",new{Serial=serial},tx,ct)).ConfigureAwait(false);
                    if(current==null||current.VariantId!=line.VariantId||current.BatchId!=batchId||current.WarehouseId!=warehouseId)return Fail<InventoryPostResult>("identifier_not_in_stock","الرقم التسلسلي غير موجود في المخزن أو التشغيلة المحددة.");
                    physicalId=current.Id;
                    var saved=(await c.QueryAsync<string>(Cmd("SELECT NormalizedValue FROM InventoryIdentifiers WHERE PhysicalUnitId=@Id AND Kind='imei' ORDER BY NormalizedValue",new{Id=physicalId},tx,ct)).ConfigureAwait(false)).ToArray();
                    if(!saved.SequenceEqual(imeis.OrderBy(x=>x,StringComparer.Ordinal)))return Fail<InventoryPostResult>("imei_mismatch","أدخل جميع أرقام IMEI المسجلة للجهاز.");
                    await c.ExecuteAsync(Cmd("UPDATE InventoryPhysicalUnits SET WarehouseId=@Destination,UpdatedUtc=@Now WHERE Id=@Id",new{Destination=destinationId,Now=now,Id=physicalId},tx,ct)).ConfigureAwait(false);
                }
                await c.ExecuteAsync(Cmd("INSERT INTO StockMovementUnits(MovementId,PhysicalUnitId) VALUES(@MovementId,@PhysicalId)",new{MovementId=movementId,PhysicalId=physicalId},tx,ct)).ConfigureAwait(false);
            }
            return InventoryResult<InventoryPostResult>.Ok(null);
        }

        public async Task<IReadOnlyList<InventoryBalanceRecord>> BalancesAsync(long? warehouseId,long? productId,CancellationToken ct)
        {
            using(var c=Open())return (await c.QueryAsync<InventoryBalanceRecord>(new CommandDefinition(@"SELECT b.WarehouseId,v.ProductId,b.VariantId,p.Name ProductName,v.Name VariantName,b.BatchId,bat.LotNumber,bat.ExpirationDate,b.QuantityBaseScaled,b.ValueScaled,b.Version
                FROM InventoryBalances b JOIN ProductVariants v ON v.Id=b.VariantId JOIN Products p ON p.Id=v.ProductId LEFT JOIN ProductBatches bat ON bat.Id=b.BatchId
                WHERE (@WarehouseId IS NULL OR b.WarehouseId=@WarehouseId) AND (@ProductId IS NULL OR v.ProductId=@ProductId)
                ORDER BY p.Name,v.Name,bat.ExpirationDate,bat.LotNumber",new{WarehouseId=warehouseId,ProductId=productId},cancellationToken:ct)).ConfigureAwait(false)).ToList();
        }

        public async Task<InventoryPage<InventoryMovementRecord>> MovementsAsync(long? warehouseId,long? productId,string type,string reference,string from,string to,int page,int pageSize,CancellationToken ct)
        {
            var where=@" FROM StockMovements m JOIN InventoryDocuments d ON d.Id=m.DocumentId JOIN Warehouses w ON w.Id=m.WarehouseId JOIN ProductVariants v ON v.Id=m.VariantId JOIN Products p ON p.Id=v.ProductId LEFT JOIN ProductBatches bat ON bat.Id=m.BatchId JOIN InventoryDocumentLines l ON l.Id=m.DocumentLineId JOIN ProductUnits pu ON pu.Id=l.ProductUnitId JOIN Units u ON u.Id=pu.UnitId
                WHERE (@WarehouseId IS NULL OR m.WarehouseId=@WarehouseId) AND (@ProductId IS NULL OR v.ProductId=@ProductId) AND (@Type IS NULL OR d.DocumentType=@Type)
                AND (@Reference IS NULL OR d.Id=@Reference) AND (@From IS NULL OR m.CreatedUtc>=@From) AND (@To IS NULL OR m.CreatedUtc<@To)";
            long parsed;if(!long.TryParse((reference??"").Replace("INV-",""),out parsed))parsed=-1;
            var args=new{WarehouseId=warehouseId,ProductId=productId,Type=string.IsNullOrWhiteSpace(type)?null:type,Reference=string.IsNullOrWhiteSpace(reference)?(long?)null:parsed,From=string.IsNullOrWhiteSpace(from)?null:from,To=string.IsNullOrWhiteSpace(to)?null:to+"T23:59:59.9999999Z",Limit=pageSize,Offset=(page-1)*pageSize};
            using(var c=Open())
            {
                var count=await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*)"+where,args,cancellationToken:ct)).ConfigureAwait(false);
                var rows=(await c.QueryAsync<InventoryMovementRecord>(new CommandDefinition(@"SELECT m.Id,m.DocumentId,d.DocumentType,m.CreatedUtc,m.WarehouseId,w.Name WarehouseName,v.ProductId,m.VariantId,p.Name ProductName,v.Name VariantName,bat.LotNumber,m.QuantityBaseScaled,m.ValueScaled,d.CreatedByUserId UserId,d.Reason,d.Notes,l.ProductUnitId,l.EnteredQuantityScaled,l.ToBaseNumerator,l.ToBaseDenominator,u.Name UnitName"+where+" ORDER BY m.Id DESC LIMIT @Limit OFFSET @Offset",args,cancellationToken:ct)).ConfigureAwait(false)).ToList();
                return new InventoryPage<InventoryMovementRecord>{Items=rows,TotalCount=count,Page=page,PageSize=pageSize};
            }
        }

        private sealed class CountDraftLine
        {
            public long Id { get; set; } public long VariantId { get; set; } public long? BatchId { get; set; }
            public string LotNumber { get; set; } public long RecordedQuantityScaled { get; set; } public long CountedQuantityScaled { get; set; } public long BalanceVersion { get; set; } public long ProductUnitId { get; set; }
            public string ProductName { get; set; } public string VariantName { get; set; }
        }
        public async Task<IReadOnlyList<InventoryCountRecord>> CountsAsync(long warehouseId,CancellationToken ct)
        {
            using(var c=Open())return (await c.QueryAsync<InventoryCountRecord>(new CommandDefinition("SELECT Id,SourceWarehouseId WarehouseId,Status,CreatedUtc FROM InventoryDocuments WHERE DocumentType='count' AND SourceWarehouseId=@WarehouseId ORDER BY Id DESC LIMIT 50",new{WarehouseId=warehouseId},cancellationToken:ct)).ConfigureAwait(false)).ToList();
        }
        public async Task<InventoryResult<InventoryCountRecord>> CreateCountAsync(long warehouseId,long userId,IReadOnlyList<long> variantIds,CancellationToken ct)
        {
            if(variantIds!=null&&variantIds.Count>100)return Fail<InventoryCountRecord>("count_limit","اختر حتى 100 متغير في الجرد المحدد.");
            using(var c=Open())using(var tx=c.BeginTransaction())
            {
                var warehouse=await c.QuerySingleOrDefaultAsync<WarehouseRecord>(Cmd("SELECT * FROM Warehouses WHERE Id=@Id",new{Id=warehouseId},tx,ct)).ConfigureAwait(false);
                if(warehouse==null||!warehouse.IsActive)return Fail<InventoryCountRecord>("invalid_warehouse","المخزن غير نشط أو غير موجود.");
                var now=Now();
                await c.ExecuteAsync(Cmd("INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,CreatedByUserId,CreatedUtc) VALUES('count','draft',@WarehouseId,'جرد المخزن',@UserId,@Now)",new{WarehouseId=warehouseId,UserId=userId,Now=now},tx,ct)).ConfigureAwait(false);
                var id=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                var balances=(await c.QueryAsync<CountDraftLine>(Cmd(@"SELECT b.VariantId,b.BatchId,bat.LotNumber,b.QuantityBaseScaled RecordedQuantityScaled,b.Version BalanceVersion,pu.Id ProductUnitId,p.Name ProductName,v.Name VariantName
                    FROM InventoryBalances b JOIN ProductVariants v ON v.Id=b.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.ProductId=v.ProductId AND pu.IsBase=1 LEFT JOIN ProductBatches bat ON bat.Id=b.BatchId
                    WHERE b.WarehouseId=@WarehouseId ORDER BY v.Id,b.BatchId",new{WarehouseId=warehouseId},tx,ct)).ConfigureAwait(false)).ToList();
                if(variantIds!=null&&variantIds.Count>0)balances=balances.Where(x=>variantIds.Contains(x.VariantId)).ToList();
                if(variantIds!=null&&variantIds.Count>0)
                {
                    var selected=variantIds.Where(x=>x>0).Distinct().ToArray();
                    var zeroRows=(await c.QueryAsync<CountDraftLine>(Cmd(@"SELECT v.Id VariantId,0 RecordedQuantityScaled,0 BalanceVersion,pu.Id ProductUnitId,p.Name ProductName,v.Name VariantName
                        FROM ProductVariants v JOIN ProductDetails d ON d.ProductId=v.ProductId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.ProductId=v.ProductId AND pu.IsBase=1
                        WHERE v.Id IN @Ids AND d.ProductType='stock' AND d.IsActive=1 AND v.IsActive=1 AND v.ArchivedUtc IS NULL
                        AND NOT EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='batch-tracking' AND a.IsEnabled=1)",new{Ids=selected},tx,ct)).ConfigureAwait(false)).ToList();
                    foreach(var row in zeroRows)if(!balances.Any(x=>x.VariantId==row.VariantId))balances.Add(row);
                }
                if(balances.Count==0)return Fail<InventoryCountRecord>("empty_count","لا توجد أرصدة أو متغيرات صالحة للجرد المحدد.");
                var lineNo=0;
                foreach(var row in balances)
                {
                    lineNo++;
                    await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryDocumentLines(DocumentId,LineNumber,VariantId,BatchId,EnteredQuantityScaled,ProductUnitId,ToBaseNumerator,ToBaseDenominator,BaseQuantityScaled,ExpectedBalanceScaled,ExpectedBalanceVersion)
                        VALUES(@DocumentId,@LineNumber,@VariantId,@BatchId,0,@ProductUnitId,1,1,0,@RecordedQuantityScaled,@BalanceVersion)",new{DocumentId=id,LineNumber=lineNo,row.VariantId,row.BatchId,row.ProductUnitId,row.RecordedQuantityScaled,row.BalanceVersion},tx,ct)).ConfigureAwait(false);
                }
                tx.Commit();
                return InventoryResult<InventoryCountRecord>.Ok(new InventoryCountRecord{Id=id,WarehouseId=warehouseId,Status="draft",CreatedUtc=now,Lines=balances.Select(x=>new InventoryCountLine{VariantId=x.VariantId,BatchId=x.BatchId,ProductName=x.ProductName,VariantName=x.VariantName,RecordedQuantityScaled=x.RecordedQuantityScaled,CountedQuantityScaled=x.RecordedQuantityScaled,BalanceVersion=x.BalanceVersion}).ToList()});
            }
        }

        public async Task<InventoryResult<InventoryPostResult>> PostCountAsync(long countId,InventoryCountPostCommand command,string requestHash,CancellationToken ct)
        {
            try
            {
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var replay=await ReplayAsync(c,tx,command.OperationKey,requestHash,ct).ConfigureAwait(false);if(replay!=null)return replay;
                    var draft=await c.QuerySingleOrDefaultAsync<DocumentState>(Cmd("SELECT * FROM InventoryDocuments WHERE Id=@Id",new{Id=countId},tx,ct)).ConfigureAwait(false);
                    if(draft==null||draft.DocumentType!="count"||draft.Status!="draft")return Fail<InventoryPostResult>("stale_count","مسودة الجرد غير صالحة للنشر.");
                    var rows=(await c.QueryAsync<CountDraftLine>(Cmd(@"SELECT l.Id,l.VariantId,l.BatchId,bat.LotNumber,l.ExpectedBalanceScaled RecordedQuantityScaled,l.ExpectedBalanceVersion BalanceVersion,l.ProductUnitId
                        FROM InventoryDocumentLines l LEFT JOIN ProductBatches bat ON bat.Id=l.BatchId WHERE l.DocumentId=@Id ORDER BY l.LineNumber",new{Id=countId},tx,ct)).ConfigureAwait(false)).ToList();
                    if(rows.Count!=command.Lines.Count)return Fail<InventoryPostResult>("count_lines_changed","راجع كل بنود الجرد قبل الترحيل.");
                    var inputs=new List<InventoryLineInput>();
                    foreach(var row in rows)
                    {
                        var matches=command.Lines.Where(x=>x.VariantId==row.VariantId&&x.BatchId==row.BatchId).ToList();
                        if(matches.Count!=1)return Fail<InventoryPostResult>("count_lines_changed","بنود الجرد غير متطابقة.");
                        var counted=matches[0];long countedScaled;
                        if(!InventoryService.TryScale(counted.CountedQuantity,6,out countedScaled)||countedScaled<0||counted.BalanceVersion!=row.BalanceVersion)return Fail<InventoryPostResult>("stale_count","تغيرت بيانات الجرد. أعد الجرد.");
                        var difference=checked(countedScaled-row.RecordedQuantityScaled);
                        inputs.Add(new InventoryLineInput{VariantId=row.VariantId,ProductUnitId=row.ProductUnitId,Quantity=(difference/1000000m).ToString("0.######",CultureInfo.InvariantCulture),UnitCost=counted.UnitCost,LotNumber=row.LotNumber,ExpectedVersion=row.BalanceVersion,ExpectedQuantity=(row.RecordedQuantityScaled/1000000m).ToString("0.######",CultureInfo.InvariantCulture),PhysicalUnits=counted.PhysicalUnits});
                    }
                    var post=new InventoryPostCommand{Type="count",OperationKey=command.OperationKey,WarehouseId=draft.SourceWarehouseId,Reason=command.Reason,Notes=command.Notes,UserId=command.UserId,Lines=inputs};
                    var result=await PostInTransactionAsync(c,tx,post,requestHash,countId,ct).ConfigureAwait(false);
                    if(!result.Succeeded)return result;tx.Commit();return result;
                }
            }
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return Fail<InventoryPostResult>("database_busy","المخزون مشغول الآن. أعد المحاولة بمفتاح العملية نفسه.");}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return Fail<InventoryPostResult>("inventory_conflict","تعارضت بيانات الجرد مع المخزون الحالي.");}
            catch(OverflowException){return Fail<InventoryPostResult>("numeric_overflow","الكمية أو القيمة تتجاوز الحد المسموح.");}
        }

        public async Task<InventoryResult<InventoryCountRecord>> GetCountAsync(long countId,CancellationToken ct)
        {
            using(var c=Open())
            {
                var doc=await c.QuerySingleOrDefaultAsync<DocumentState>(new CommandDefinition("SELECT * FROM InventoryDocuments WHERE Id=@Id AND DocumentType='count'",new{Id=countId},cancellationToken:ct)).ConfigureAwait(false);
                if(doc==null)return Fail<InventoryCountRecord>("not_found","الجرد غير موجود.");
                var rows=(await c.QueryAsync<CountDraftLine>(new CommandDefinition(@"SELECT l.VariantId,l.BatchId,l.ExpectedBalanceScaled RecordedQuantityScaled,COALESCE(l.CountedQuantityScaled,l.ExpectedBalanceScaled) CountedQuantityScaled,l.ExpectedBalanceVersion BalanceVersion,p.Name ProductName,v.Name VariantName
                    FROM InventoryDocumentLines l JOIN ProductVariants v ON v.Id=l.VariantId JOIN Products p ON p.Id=v.ProductId WHERE l.DocumentId=@Id ORDER BY l.LineNumber",new{Id=countId},cancellationToken:ct)).ConfigureAwait(false)).ToList();
                return InventoryResult<InventoryCountRecord>.Ok(new InventoryCountRecord{Id=doc.Id,WarehouseId=doc.SourceWarehouseId,Status=doc.Status,Lines=rows.Select(x=>new InventoryCountLine{VariantId=x.VariantId,BatchId=x.BatchId,ProductName=x.ProductName,VariantName=x.VariantName,RecordedQuantityScaled=x.RecordedQuantityScaled,CountedQuantityScaled=x.CountedQuantityScaled,BalanceVersion=x.BalanceVersion}).ToList()});
            }
        }

        public async Task<InventoryResult<InventoryCountRecord>> CancelCountAsync(long countId,long userId,CancellationToken ct)
        {
            using(var c=Open())using(var tx=c.BeginTransaction())
            {
                var now=Now();var changed=await c.ExecuteAsync(Cmd("UPDATE InventoryDocuments SET Status='canceled' WHERE Id=@Id AND DocumentType='count' AND Status='draft'",new{Id=countId},tx,ct)).ConfigureAwait(false);
                if(changed!=1)return Fail<InventoryCountRecord>("count_not_draft","لا يمكن إلغاء جرد مرحّل أو ملغى.");
                await c.ExecuteAsync(Cmd("INSERT INTO InventoryAudit(DocumentId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@UserId,'canceled',@Now,'إلغاء مسودة جرد')",new{Id=countId,UserId=userId,Now=now},tx,ct)).ConfigureAwait(false);
                tx.Commit();return await GetCountAsync(countId,ct).ConfigureAwait(false);
            }
        }

        private sealed class TrackingRow { public long PhysicalUnitId{get;set;}public long VariantId{get;set;}public long? BatchId{get;set;}public long? WarehouseId{get;set;}public string Serial{get;set;} }
        public async Task<InventoryResult<InventoryTrackingRecord>> TrackingAsync(string identifier,CancellationToken ct)
        {
            var normalized=Code(identifier);using(var c=Open())
            {
                var row=await c.QuerySingleOrDefaultAsync<TrackingRow>(new CommandDefinition(@"SELECT p.Id PhysicalUnitId,p.VariantId,p.BatchId,p.WarehouseId,(SELECT DisplayValue FROM InventoryIdentifiers s WHERE s.PhysicalUnitId=p.Id AND s.Kind='serial') Serial
                    FROM InventoryIdentifiers i JOIN InventoryPhysicalUnits p ON p.Id=i.PhysicalUnitId WHERE i.NormalizedValue=@Normalized",new{Normalized=normalized},cancellationToken:ct)).ConfigureAwait(false);
                if(row==null)return Fail<InventoryTrackingRecord>("not_found","الرقم غير موجود في المخزون.");
                var imeis=(await c.QueryAsync<string>(new CommandDefinition("SELECT DisplayValue FROM InventoryIdentifiers WHERE PhysicalUnitId=@Id AND Kind='imei' ORDER BY Id",new{Id=row.PhysicalUnitId},cancellationToken:ct)).ConfigureAwait(false)).ToList();
                return InventoryResult<InventoryTrackingRecord>.Ok(new InventoryTrackingRecord{PhysicalUnitId=row.PhysicalUnitId,VariantId=row.VariantId,BatchId=row.BatchId,WarehouseId=row.WarehouseId,Serial=row.Serial,Imeis=imeis});
            }
        }

        public async Task<InventoryResult<InventoryPostResult>> ReverseAsync(long documentId,InventoryPostCommand command,string requestHash,CancellationToken ct)
        {
            try
            {
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var replay=await ReplayAsync(c,tx,command.OperationKey,requestHash,ct).ConfigureAwait(false);if(replay!=null)return replay;
                    var doc=await c.QuerySingleOrDefaultAsync<DocumentState>(Cmd("SELECT * FROM InventoryDocuments WHERE Id=@Id",new{Id=documentId},tx,ct)).ConfigureAwait(false);
                    if(doc==null||doc.Status!="posted")return Fail<InventoryPostResult>("not_found","الحركة المنشورة غير موجودة.");
                    if(doc.DocumentType!="opening"&&doc.DocumentType!="manual_receipt"&&doc.DocumentType!="adjustment")return Fail<InventoryPostResult>("reversal_unsupported","تصحيح هذه الحركة يحتاج تسوية جديدة موثقة.");
                    if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM InventoryDocuments WHERE ReversesDocumentId=@Id",new{Id=documentId},tx,ct)).ConfigureAwait(false)>0)return Fail<InventoryPostResult>("already_reversed","تم عكس الحركة سابقًا.");
                    var rows=(await c.QueryAsync<InventoryMovementRecord>(Cmd("SELECT m.Id,m.DocumentId,m.WarehouseId,m.VariantId,m.BatchId,m.QuantityBaseScaled,m.ValueScaled,l.ProductUnitId,l.UnitCostScaled FROM StockMovements m JOIN InventoryDocumentLines l ON l.Id=m.DocumentLineId WHERE m.DocumentId=@Id ORDER BY l.LineNumber",new{Id=documentId},tx,ct)).ConfigureAwait(false)).ToList();
                    if(rows.Count==0||rows.Any(x=>x.QuantityBaseScaled<=0)||rows.Any(x=>x.WarehouseId!=doc.SourceWarehouseId))return Fail<InventoryPostResult>("reversal_unsupported","تصحيح هذه الحركة يحتاج تسوية جديدة موثقة.");
                    var lines=new List<InventoryLineInput>();
                    foreach(var row in rows)
                    {
                        var balance=await FindBalanceAsync(c,tx,row.WarehouseId,row.VariantId,row.BatchId,ct).ConfigureAwait(false);
                        if(balance==null||balance.QuantityBaseScaled!=row.QuantityBaseScaled||balance.ValueScaled!=row.ValueScaled)return Fail<InventoryPostResult>("reversal_conflict","تغير المخزون بعد الحركة؛ سجّل تصحيحًا جديدًا بدل عكسها.");
                        var tracked=await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM StockMovementUnits WHERE MovementId=@Id",new{row.Id},tx,ct)).ConfigureAwait(false);
                        if(tracked>0)return Fail<InventoryPostResult>("reversal_unsupported","عكس البضاعة المتسلسلة يحتاج تسوية جديدة تحدد أرقامها.");
                        var lot=await c.ExecuteScalarAsync<string>(Cmd("SELECT LotNumber FROM ProductBatches WHERE Id=@Id",new{Id=row.BatchId},tx,ct)).ConfigureAwait(false);
                        lines.Add(new InventoryLineInput{VariantId=row.VariantId,ProductUnitId=row.ProductUnitId,Quantity=(row.QuantityBaseScaled/1000000m).ToString("0.######",CultureInfo.InvariantCulture),LotNumber=lot});
                    }
                    var post=new InventoryPostCommand{Type="reversal",WarehouseId=doc.SourceWarehouseId,OperationKey=command.OperationKey,Reason=command.Reason,Notes=command.Notes,UserId=command.UserId,ReversesDocumentId=documentId,Lines=lines};
                    var result=await PostInTransactionAsync(c,tx,post,requestHash,null,ct).ConfigureAwait(false);
                    if(!result.Succeeded)return result;tx.Commit();return result;
                }
            }
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return Fail<InventoryPostResult>("reversal_conflict","تعذر عكس الحركة بعد تغير المخزون.");}
        }

        public async Task<bool> VerifyProjectionAsync(CancellationToken ct)
        {
            using(var c=Open())
            {
                var bad=await c.ExecuteScalarAsync<long>(new CommandDefinition(@"SELECT COUNT(*) FROM (
                    SELECT b.WarehouseId,b.VariantId,b.BatchId,b.QuantityBaseScaled,b.ValueScaled,
                    COALESCE((SELECT SUM(m.QuantityBaseScaled) FROM StockMovements m WHERE m.WarehouseId=b.WarehouseId AND m.VariantId=b.VariantId AND ((m.BatchId IS NULL AND b.BatchId IS NULL) OR m.BatchId=b.BatchId)),0) LedgerQuantity,
                    COALESCE((SELECT SUM(m.ValueScaled) FROM StockMovements m WHERE m.WarehouseId=b.WarehouseId AND m.VariantId=b.VariantId AND ((m.BatchId IS NULL AND b.BatchId IS NULL) OR m.BatchId=b.BatchId)),0) LedgerValue
                    FROM InventoryBalances b) x WHERE x.QuantityBaseScaled<>x.LedgerQuantity OR x.ValueScaled<>x.LedgerValue",cancellationToken:ct)).ConfigureAwait(false);
                var missing=await c.ExecuteScalarAsync<long>(new CommandDefinition(@"SELECT COUNT(*) FROM StockMovements m WHERE NOT EXISTS(SELECT 1 FROM InventoryBalances b WHERE b.WarehouseId=m.WarehouseId AND b.VariantId=m.VariantId AND ((b.BatchId IS NULL AND m.BatchId IS NULL) OR b.BatchId=m.BatchId))",cancellationToken:ct)).ConfigureAwait(false);
                return bad==0&&missing==0;
            }
        }

        public async Task<InventoryResult<InventoryRebuildResult>> RebuildProjectionAsync(long userId,CancellationToken ct)
        {
            var builder=new SQLiteConnectionStringBuilder(_connectionString);
            var folder=Path.Combine(Path.GetDirectoryName(builder.DataSource),"InventoryBackups");
            Directory.CreateDirectory(folder);
            var backupPath=Path.Combine(folder,"before-inventory-rebuild-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")+".db");
            using(var source=Open())
            {
                using(var backup=new SQLiteConnection("Data Source="+backupPath+";Version=3;Foreign Keys=True;"))
                {
                    backup.Open();source.BackupDatabase(backup,"main","main",-1,null,0);
                    var check=await backup.ExecuteScalarAsync<string>(new CommandDefinition("PRAGMA quick_check",cancellationToken:ct)).ConfigureAwait(false);
                    var fk=await backup.QueryAsync<string>(new CommandDefinition("PRAGMA foreign_key_check",cancellationToken:ct)).ConfigureAwait(false);
                    if(check!="ok"||fk.Any())return Fail<InventoryRebuildResult>("backup_invalid","فشل التحقق من النسخة الاحتياطية؛ لم يُعدل الرصيد.");
                }
                try
                {
                    using(var tx=source.BeginTransaction())
                    {
                        var badPhysical=await source.ExecuteScalarAsync<long>(Cmd(@"WITH keys AS (
                            SELECT WarehouseId,VariantId,BatchId FROM StockMovements
                            UNION SELECT WarehouseId,VariantId,BatchId FROM InventoryPhysicalUnits WHERE WarehouseId IS NOT NULL
                        ) SELECT COUNT(*) FROM keys k JOIN ProductVariants v ON v.Id=k.VariantId
                        JOIN ProductCapabilityAssignments a ON a.ProductId=v.ProductId AND a.CapabilityKey='serial-numbers' AND a.IsEnabled=1
                        WHERE COALESCE((SELECT SUM(m.QuantityBaseScaled) FROM StockMovements m WHERE m.WarehouseId=k.WarehouseId AND m.VariantId=k.VariantId AND ((m.BatchId IS NULL AND k.BatchId IS NULL) OR m.BatchId=k.BatchId)),0)
                        <>1000000*COALESCE((SELECT COUNT(*) FROM InventoryPhysicalUnits p WHERE p.WarehouseId=k.WarehouseId AND p.VariantId=k.VariantId AND ((p.BatchId IS NULL AND k.BatchId IS NULL) OR p.BatchId=k.BatchId)),0)",null,tx,ct)).ConfigureAwait(false);
                        if(badPhysical>0)return Fail<InventoryRebuildResult>("tracking_mismatch","الأرقام الفعلية لا تطابق دفتر المخزون. أوقف إعادة البناء وافحص السجل.");
                        var negative=await source.ExecuteScalarAsync<long>(Cmd(@"SELECT COUNT(*) FROM (SELECT SUM(QuantityBaseScaled) Q,SUM(ValueScaled) V FROM StockMovements GROUP BY WarehouseId,VariantId,BatchId) WHERE Q<0 OR V<0 OR (Q=0 AND V<>0)",null,tx,ct)).ConfigureAwait(false);
                        if(negative>0)return Fail<InventoryRebuildResult>("ledger_inconsistent","دفتر الحركات يحتوي قيمة أو رصيدًا غير صالح لإعادة البناء.");
                        var now=Now();
                        await source.ExecuteAsync(Cmd(@"UPDATE InventoryBalances SET
                            QuantityBaseScaled=COALESCE((SELECT SUM(m.QuantityBaseScaled) FROM StockMovements m WHERE m.WarehouseId=InventoryBalances.WarehouseId AND m.VariantId=InventoryBalances.VariantId AND ((m.BatchId IS NULL AND InventoryBalances.BatchId IS NULL) OR m.BatchId=InventoryBalances.BatchId)),0),
                            ValueScaled=COALESCE((SELECT SUM(m.ValueScaled) FROM StockMovements m WHERE m.WarehouseId=InventoryBalances.WarehouseId AND m.VariantId=InventoryBalances.VariantId AND ((m.BatchId IS NULL AND InventoryBalances.BatchId IS NULL) OR m.BatchId=InventoryBalances.BatchId)),0),
                            Version=Version+1,UpdatedUtc=@Now",new{Now=now},tx,ct)).ConfigureAwait(false);
                        await source.ExecuteAsync(Cmd(@"INSERT INTO InventoryBalances(WarehouseId,VariantId,BatchId,QuantityBaseScaled,ValueScaled,Version,UpdatedUtc)
                            SELECT m.WarehouseId,m.VariantId,m.BatchId,SUM(m.QuantityBaseScaled),SUM(m.ValueScaled),1,@Now
                            FROM StockMovements m
                            WHERE NOT EXISTS(SELECT 1 FROM InventoryBalances b WHERE b.WarehouseId=m.WarehouseId AND b.VariantId=m.VariantId AND ((b.BatchId IS NULL AND m.BatchId IS NULL) OR b.BatchId=m.BatchId))
                            GROUP BY m.WarehouseId,m.VariantId,m.BatchId",new{Now=now},tx,ct)).ConfigureAwait(false);
                        var remaining=await source.ExecuteScalarAsync<long>(Cmd(@"SELECT COUNT(*) FROM InventoryBalances b WHERE b.QuantityBaseScaled<>COALESCE((SELECT SUM(m.QuantityBaseScaled) FROM StockMovements m WHERE m.WarehouseId=b.WarehouseId AND m.VariantId=b.VariantId AND ((m.BatchId IS NULL AND b.BatchId IS NULL) OR m.BatchId=b.BatchId)),0)
                            OR b.ValueScaled<>COALESCE((SELECT SUM(m.ValueScaled) FROM StockMovements m WHERE m.WarehouseId=b.WarehouseId AND m.VariantId=b.VariantId AND ((m.BatchId IS NULL AND b.BatchId IS NULL) OR m.BatchId=b.BatchId)),0)",null,tx,ct)).ConfigureAwait(false);
                        if(remaining>0)return Fail<InventoryRebuildResult>("rebuild_failed","فشل التحقق من إسقاط الأرصدة؛ لم تُحفظ تغييرات.");
                        var defaultId=await source.ExecuteScalarAsync<long>(Cmd("SELECT Id FROM Warehouses WHERE IsDefault=1 LIMIT 1",null,tx,ct)).ConfigureAwait(false);
                        var operationKey="rebuild-"+Guid.NewGuid().ToString("N");
                        await source.ExecuteAsync(Cmd("INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,Notes,CreatedByUserId,CreatedUtc,PostedUtc,OperationKey,RequestHash) VALUES('projection_rebuild','draft',@WarehouseId,'إعادة بناء إسقاط الأرصدة',@BackupPath,@UserId,@Now,NULL,@OperationKey,@OperationKey)",new{WarehouseId=defaultId,BackupPath=backupPath,UserId=userId,Now=now,OperationKey=operationKey},tx,ct)).ConfigureAwait(false);
                        var documentId=await source.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                        await source.ExecuteAsync(Cmd("UPDATE InventoryDocuments SET Status='posted',PostedUtc=@Now WHERE Id=@Id",new{Now=now,Id=documentId},tx,ct)).ConfigureAwait(false);
                        await source.ExecuteAsync(Cmd("INSERT INTO InventoryAudit(DocumentId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@UserId,'projection_rebuild',@Now,@BackupPath)",new{Id=documentId,UserId=userId,Now=now,BackupPath=backupPath},tx,ct)).ConfigureAwait(false);
                        var rows=await source.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM InventoryBalances",null,tx,ct)).ConfigureAwait(false);
                        tx.Commit();return InventoryResult<InventoryRebuildResult>.Ok(new InventoryRebuildResult{DocumentId=documentId,BackupPath=backupPath,BalanceRows=rows});
                    }
                }
                catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint||e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked)
                { return Fail<InventoryRebuildResult>("rebuild_failed","تعذرت إعادة البناء بأمان؛ لم تُحفظ تغييرات."); }
            }
        }
    }
}


