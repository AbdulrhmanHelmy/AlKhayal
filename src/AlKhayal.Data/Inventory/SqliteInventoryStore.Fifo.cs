using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Inventory;
using Dapper;

namespace AlKhayal.Data.Inventory
{
    public sealed partial class SqliteInventoryStore
    {
        private sealed class CostLayer
        {
            public long Id { get; set; }
            public long WarehouseId { get; set; }
            public long VariantId { get; set; }
            public long? BatchId { get; set; }
            public long? OriginMovementId { get; set; }
            public string AcquiredUtc { get; set; }
            public string Basis { get; set; }
            public long QuantityScaled { get; set; }
            public long ValueScaled { get; set; }
            public long? UnitCostScaled { get; set; }
            public long RemainingQuantityScaled { get; set; }
            public long RemainingValueScaled { get; set; }
        }
        private sealed class CostTake { public CostLayer Layer; public long Quantity; public long Value; }

        // Quantity and total value are authoritative. Cumulative rounding makes a layer's
        // total cost independent of the sizes of successive partial withdrawals.
        private static long PortionValue(CostLayer layer,long quantity)
        {
            return InventoryCosting.AllocateLayer(layer.QuantityScaled,layer.ValueScaled,layer.RemainingQuantityScaled,layer.RemainingValueScaled,quantity);
        }

        private static async Task<InventoryResult<List<CostTake>>> PlanFifoAsync(SQLiteConnection c,SQLiteTransaction tx,long warehouseId,long variantId,long? batchId,long quantity,BalanceState balance,CancellationToken ct)
        {
            var layers=(await c.QueryAsync<CostLayer>(Cmd(@"SELECT * FROM InventoryCostLayers WHERE WarehouseId=@WarehouseId AND VariantId=@VariantId AND BatchId IS @BatchId AND RemainingQuantityScaled>0
                ORDER BY OriginMovementId,Id",new{WarehouseId=warehouseId,VariantId=variantId,BatchId=batchId},tx,ct)).ConfigureAwait(false)).ToList();
            if(layers.Any(x=>x.Basis=="unresolved"))return Fail<List<CostTake>>("unresolved_cost_basis","تكلفة الرصيد القديم غير مؤكدة. اطلب من المسؤول توثيق أساس التكلفة قبل الصرف أو النقل.");
            long available=0,total=0;
            foreach(var layer in layers){available=checked(available+layer.RemainingQuantityScaled);total=checked(total+layer.RemainingValueScaled);}
            if(balance==null||available!=balance.QuantityBaseScaled||total!=balance.ValueScaled)return Fail<List<CostTake>>("fifo_integrity","أرصدة التكلفة لا تطابق حركة المخزون. يلزم فحص البيانات قبل الحفظ.");
            var takes=new List<CostTake>();
            foreach(var layer in layers)
            {
                if(quantity==0)break;
                var take=Math.Min(quantity,layer.RemainingQuantityScaled);
                takes.Add(new CostTake{Layer=layer,Quantity=take,Value=PortionValue(layer,take)});
                quantity-=take;
            }
            if(quantity!=0)return Fail<List<CostTake>>("insufficient_cost_layers","لا توجد طبقات تكلفة كافية لهذه الكمية.");
            return InventoryResult<List<CostTake>>.Ok(takes);
        }

        private static async Task<long> AddLayerAsync(SQLiteConnection c,SQLiteTransaction tx,long warehouseId,long variantId,long? batchId,long? movementId,long? parentId,long? originId,string acquired,string basis,long quantity,long value,long? unitCost,CancellationToken ct)
        {
            await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryCostLayers(WarehouseId,VariantId,BatchId,SourceMovementId,ParentLayerId,OriginMovementId,AcquiredUtc,Basis,QuantityScaled,ValueScaled,UnitCostScaled,RemainingQuantityScaled,RemainingValueScaled)
                VALUES(@WarehouseId,@VariantId,@BatchId,@MovementId,@ParentId,@OriginId,@Acquired,@Basis,@Quantity,@Value,@Cost,@Quantity,@Value)",new{WarehouseId=warehouseId,VariantId=variantId,BatchId=batchId,MovementId=movementId,ParentId=parentId,OriginId=originId,Acquired=acquired,Basis=basis,Quantity=quantity,Value=value,Cost=unitCost},tx,ct)).ConfigureAwait(false);
            return await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
        }

        private static async Task ConsumeFifoAsync(SQLiteConnection c,SQLiteTransaction tx,long movementId,IReadOnlyList<CostTake> takes,CancellationToken ct)
        {
            foreach(var take in takes)
            {
                var changed=await c.ExecuteAsync(Cmd(@"UPDATE InventoryCostLayers SET RemainingQuantityScaled=RemainingQuantityScaled-@Quantity,RemainingValueScaled=RemainingValueScaled-@Value
                    WHERE Id=@Id AND RemainingQuantityScaled=@ExpectedQuantity AND RemainingValueScaled=@ExpectedValue",new{take.Quantity,take.Value,take.Layer.Id,ExpectedQuantity=take.Layer.RemainingQuantityScaled,ExpectedValue=take.Layer.RemainingValueScaled},tx,ct)).ConfigureAwait(false);
                if(changed!=1)throw new SQLiteException(SQLiteErrorCode.Constraint,"fifo_concurrency");
                await c.ExecuteAsync(Cmd("INSERT INTO InventoryCostAllocations(MovementId,LayerId,QuantityScaled,ValueScaled) VALUES(@MovementId,@LayerId,@Quantity,@Value)",new{MovementId=movementId,LayerId=take.Layer.Id,take.Quantity,take.Value},tx,ct)).ConfigureAwait(false);
            }
        }

        // A documented opening basis is deliberately not relabeled as historical FIFO.
        // This conservative path accepts only a basis consistent with the untouched ledger.
        public async Task<InventoryResult<long>> ReconcileCostAsync(InventoryCostReconciliationCommand command,string requestHash,CancellationToken ct)
        {
            try
            {
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var replay=await c.QuerySingleOrDefaultAsync<Reconciliation>(Cmd("SELECT ReplacementLayerId,RequestHash FROM InventoryCostReconciliations WHERE OperationKey=@OperationKey",new{command.OperationKey},tx,ct)).ConfigureAwait(false);
                    if(replay!=null)return replay.RequestHash==requestHash?InventoryResult<long>.Ok(replay.ReplacementLayerId):Fail<long>("operation_key_conflict","هذه المحاولة محفوظة ببيانات مختلفة.");
                    var old=await c.QuerySingleOrDefaultAsync<CostLayer>(Cmd("SELECT * FROM InventoryCostLayers WHERE Id=@Id",new{Id=command.LayerId},tx,ct)).ConfigureAwait(false);
                    if(old==null||old.Basis!="unresolved"||old.RemainingQuantityScaled==0)return Fail<long>("invalid_cost_basis","الرصيد لا يحتاج إلى توثيق تكلفة أو سبق توثيقه.");
                    var balance=await FindBalanceAsync(c,tx,old.WarehouseId,old.VariantId,old.BatchId,ct).ConfigureAwait(false);
                    if(balance==null||balance.Version!=command.ExpectedVersion)return Fail<long>("stale_count","تغير الرصيد. حدّث الصفحة قبل توثيق التكلفة.");
                    long cost;
                    if(!InventoryService.TryScale(command.UnitCost,4,out cost)||string.IsNullOrWhiteSpace(command.Evidence))return Fail<long>("invalid_cost_basis","أدخل التكلفة ومصدر إثباتها.");
                    var value=InventoryCosting.ReceiptValue(cost,old.RemainingQuantityScaled);
                    if(value!=old.RemainingValueScaled)return Fail<long>("legacy_value_mismatch","التكلفة الموثقة لا تطابق القيمة التاريخية. لم تتغير البيانات؛ يلزم تسوية تقييم موثقة منفصلة بواسطة مختص.");
                    var now=Now();
                    var replacement=await AddLayerAsync(c,tx,old.WarehouseId,old.VariantId,old.BatchId,null,old.Id,null,old.AcquiredUtc,"reconciled_opening",old.RemainingQuantityScaled,value,cost,ct).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("UPDATE InventoryCostLayers SET RemainingQuantityScaled=0,RemainingValueScaled=0 WHERE Id=@Id",new{old.Id},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryCostReconciliations(LegacyLayerId,ReplacementLayerId,Evidence,ActorUserId,CreatedUtc,OperationKey,RequestHash)
                        VALUES(@LayerId,@Replacement,@Evidence,@UserId,@Now,@OperationKey,@Hash)",new{command.LayerId,Replacement=replacement,command.Evidence,command.UserId,Now=now,command.OperationKey,Hash=requestHash},tx,ct)).ConfigureAwait(false);
                    // Invalidate a count snapshot when its valuation basis changes.
                    await c.ExecuteAsync(Cmd("UPDATE InventoryBalances SET Version=Version+1 WHERE Id=@Id",new{balance.Id},tx,ct)).ConfigureAwait(false);
                    tx.Commit();return InventoryResult<long>.Ok(replacement);
                }
            }
            catch(OverflowException){return Fail<long>("numeric_overflow","التكلفة تتجاوز الحد المسموح.");}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return Fail<long>("database_busy","المخزون مشغول. أعد المحاولة.");}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return Fail<long>("inventory_conflict","تعارض توثيق التكلفة. حدّث الأرصدة.");}
        }
        private sealed class Reconciliation { public long ReplacementLayerId{get;set;} public string RequestHash{get;set;} }
    }
}
