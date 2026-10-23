using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Features;

namespace AlKhayal.Application.Inventory
{
    public sealed class InventoryService
    {
        private readonly IInventoryStore _store;
        private readonly FeatureService _features;
        public InventoryService(IInventoryStore store,FeatureService features) { _store=store; _features=features; }

        public Task<IReadOnlyList<WarehouseRecord>> WarehousesAsync(CancellationToken ct) { return _store.WarehousesAsync(ct); }
        public Task<IReadOnlyList<InventoryBalanceRecord>> BalancesAsync(long? warehouseId,long? productId,CancellationToken ct) { return _store.BalancesAsync(warehouseId,productId,ct); }
        public Task<InventoryPage<InventoryMovementRecord>> MovementsAsync(long? warehouseId,long? productId,string type,string reference,string from,string to,int page,int pageSize,CancellationToken ct)
        { return _store.MovementsAsync(warehouseId,productId,type,reference,from,to,Math.Max(1,page),Math.Min(100,Math.Max(1,pageSize)),ct); }
        public Task<bool> VerifyProjectionAsync(CancellationToken ct) { return _store.VerifyProjectionAsync(ct); }
        public Task<InventoryResult<InventoryRebuildResult>> RebuildProjectionAsync(long userId,CancellationToken ct) { return _store.RebuildProjectionAsync(userId,ct); }

        public Task<InventoryResult<long>> ReconcileCostAsync(InventoryCostReconciliationCommand command,CancellationToken ct)
        {
            long cost;
            if(command==null||string.IsNullOrWhiteSpace(command.OperationKey)||command.OperationKey.Length>120||command.LayerId<=0||command.UserId<=0||!TryScale(command.UnitCost,4,out cost)||string.IsNullOrWhiteSpace(command.Evidence)||command.Evidence.Length>2000)
                return Task.FromResult(InventoryResult<long>.Fail("invalid_cost_basis","أدخل تكلفة موثقة ومصدر إثبات التكلفة."));
            var sb=new StringBuilder("reconcile|");AppendCanonical(sb,command.LayerId);AppendCanonical(sb,command.ExpectedVersion);AppendCanonical(sb,command.UnitCost);AppendCanonical(sb,command.Evidence);AppendCanonical(sb,command.UserId);
            return _store.ReconcileCostAsync(command,"v2:"+Hash(sb.ToString()),ct);
        }

        public async Task<InventoryResult<WarehouseRecord>> SaveWarehouseAsync(WarehouseCommand command,CancellationToken ct)
        {
            if(command==null||string.IsNullOrWhiteSpace(command.Name)||command.Name.Trim().Length>120||(!string.IsNullOrWhiteSpace(command.Code)&&command.Code.Trim().Length>32))
                return InventoryResult<WarehouseRecord>.Fail("invalid_warehouse","أدخل اسم مخزن صحيحًا.");
            var enabled=await MultipleWarehousesAsync(ct).ConfigureAwait(false);
            return await _store.SaveWarehouseAsync(command,enabled,ct).ConfigureAwait(false);
        }

        public async Task<InventoryResult<InventoryPostResult>> PostAsync(InventoryPostCommand command,CancellationToken ct)
        {
            var error=ValidatePost(command);
            if(error!=null)return error;
            command.ReversesDocumentId=null;
            if(command.Type=="transfer"&&!await MultipleWarehousesAsync(ct).ConfigureAwait(false))
                return InventoryResult<InventoryPostResult>.Fail("feature_disabled","فعّل تعدد المخازن قبل النقل.");
            return await _store.PostAsync(command,CanonicalHashPost(command),ct).ConfigureAwait(false);
        }

        public Task<InventoryResult<InventoryCountRecord>> CreateCountAsync(long warehouseId,long userId,IReadOnlyList<long> variantIds,CancellationToken ct)
        { return _store.CreateCountAsync(warehouseId,userId,variantIds,ct); }
        public Task<IReadOnlyList<InventoryCountRecord>> CountsAsync(long warehouseId,CancellationToken ct) { return _store.CountsAsync(warehouseId,ct); }
        public Task<InventoryResult<InventoryPostResult>> PostCountAsync(long countId,InventoryCountPostCommand command,CancellationToken ct)
        {
            if(command==null||string.IsNullOrWhiteSpace(command.OperationKey)||command.Lines==null||command.Lines.Any(x=>x==null))
                return Task.FromResult(InventoryResult<InventoryPostResult>.Fail("invalid_count","راجع كميات الجرد ثم أعد الحفظ."));
            if(string.IsNullOrWhiteSpace(command.Reason))command.Reason="جرد المخزون";
            return _store.PostCountAsync(countId,command,CanonicalHashCount(countId,command),ct);
        }
        public Task<InventoryResult<InventoryCountRecord>> GetCountAsync(long countId,CancellationToken ct) { return _store.GetCountAsync(countId,ct); }
        public Task<InventoryResult<InventoryCountRecord>> CancelCountAsync(long countId,long userId,CancellationToken ct) { return _store.CancelCountAsync(countId,userId,ct); }
        public Task<InventoryResult<InventoryTrackingRecord>> TrackingAsync(string identifier,CancellationToken ct)
        {
            if(string.IsNullOrWhiteSpace(identifier))return Task.FromResult(InventoryResult<InventoryTrackingRecord>.Fail("invalid_identifier","أدخل الرقم التسلسلي أو IMEI."));
            return _store.TrackingAsync(identifier,ct);
        }
        public Task<InventoryResult<InventoryPostResult>> ReverseAsync(long documentId,InventoryPostCommand command,CancellationToken ct)
        {
            if(command==null||string.IsNullOrWhiteSpace(command.OperationKey)||string.IsNullOrWhiteSpace(command.Reason))
                return Task.FromResult(InventoryResult<InventoryPostResult>.Fail("invalid_reversal","أدخل سبب الإلغاء ومفتاح العملية."));
            return _store.ReverseAsync(documentId,command,CanonicalHashReverse(command,documentId),ct);
        }

        private async Task<bool> MultipleWarehousesAsync(CancellationToken ct)
        { var snapshot=await _features.GetEffectiveAsync(ct).ConfigureAwait(false);return snapshot.Modules.Any(f=>f.Key=="multiple-warehouses"&&f.IsEnabled); }

        private static InventoryResult<InventoryPostResult> ValidatePost(InventoryPostCommand command)
        {
            if(command==null||string.IsNullOrWhiteSpace(command.OperationKey)||command.OperationKey.Length>120||command.WarehouseId<=0||command.UserId<=0||command.Lines==null||command.Lines.Count==0||command.Lines.Count>100)
                return InventoryResult<InventoryPostResult>.Fail("invalid_operation","أكمل بيانات الحركة ثم أعد الحفظ.");
            if(!new[]{"opening","manual_receipt","manual_issue","adjustment","transfer"}.Contains(command.Type))
                return InventoryResult<InventoryPostResult>.Fail("invalid_type","نوع حركة المخزون غير صالح.");
            if(command.Type=="transfer"&&(!command.DestinationWarehouseId.HasValue||command.DestinationWarehouseId==command.WarehouseId))
                return InventoryResult<InventoryPostResult>.Fail("invalid_destination","اختر مخزنًا آخر للوجهة.");
            if(string.IsNullOrWhiteSpace(command.Reason))command.Reason=command.Type=="manual_receipt"?"إضافة بضاعة":command.Type=="manual_issue"?"إخراج بضاعة":command.Type=="transfer"?"نقل بضاعة":command.Type=="adjustment"?"تسوية المخزون":"رصيد افتتاحي";
            foreach(var line in command.Lines)
            {
                long quantity;
                if(line==null||line.VariantId<=0||line.ProductUnitId<=0||!TryScale(line.Quantity,6,out quantity)||quantity==0)
                    return InventoryResult<InventoryPostResult>.Fail("invalid_quantity","أدخل كمية صحيحة بدقة لا تتجاوز ست خانات عشرية.");
                if(command.Type!="adjustment"&&quantity<0)return InventoryResult<InventoryPostResult>.Fail("invalid_quantity","يجب أن تكون الكمية موجبة.");
                if(line.UnitCost!=null&&!TryScale(line.UnitCost,4,out _))return InventoryResult<InventoryPostResult>.Fail("invalid_cost","تكلفة الوحدة غير صالحة.");
                if(line.UnitCost==null&&(command.Type=="opening"||command.Type=="manual_receipt"||command.Type=="adjustment"&&quantity>0))return InventoryResult<InventoryPostResult>.Fail("cost_required","أدخل سعر الشراء الفعلي للبضاعة المضافة.");
                if(command.Type=="manual_receipt"&&!TryScale(line.SalePrice,4,out _))return InventoryResult<InventoryPostResult>.Fail("sale_price_required","أدخل سعر البيع الجديد بدقة لا تتجاوز أربع خانات عشرية.");
                if(line.PhysicalUnits!=null&&line.PhysicalUnits.Any(x=>x==null))return InventoryResult<InventoryPostResult>.Fail("invalid_identifier","بيانات الأرقام الفعلية غير صالحة.");
            }
            return null;
        }

        public static bool TryScale(string value,int places,out long scaled)
        {
            scaled=0;decimal parsed;
            if(string.IsNullOrWhiteSpace(value)||!decimal.TryParse(value,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out parsed))return false;
            try { var factor=places==6?1000000m:10000m;var product=parsed*factor;if(product!=decimal.Truncate(product)||product<0&&places==4)return false;scaled=checked((long)product);return true; }
            catch(OverflowException){return false;}
        }

        private static string CanonicalHashPost(InventoryPostCommand command)
        {
            var sb=new StringBuilder();sb.Append("post|");AppendPostPayload(sb,command,null);
            return "v2:"+Hash(sb.ToString());
        }
        private static string CanonicalHashReverse(InventoryPostCommand command,long documentId)
        {
            var sb=new StringBuilder();sb.Append("reverse|");AppendPostPayload(sb,command,documentId);
            return "v2:"+Hash(sb.ToString());
        }
        private static string CanonicalHashCount(long countId,InventoryCountPostCommand command)
        {
            var sb=new StringBuilder();sb.Append("count|").Append(countId.ToString(CultureInfo.InvariantCulture)).Append("|");
            AppendCanonical(sb,command.Reason);AppendCanonical(sb,command.Notes);
            sb.Append(command.UserId.ToString(CultureInfo.InvariantCulture)).Append("|");
            AppendCanonical(sb,(command.Lines?.Count??0).ToString(CultureInfo.InvariantCulture));
            foreach(var l in command.Lines??new InventoryCountLine[0])
            {
                sb.Append("L|").Append(l.VariantId.ToString(CultureInfo.InvariantCulture)).Append("|");
                AppendCanonical(sb,l.BatchId);
                sb.Append(l.RecordedQuantityScaled.ToString(CultureInfo.InvariantCulture)).Append("|");
                AppendCanonical(sb,l.CountedQuantity);
                sb.Append(l.BalanceVersion.ToString(CultureInfo.InvariantCulture)).Append("|");
                AppendCanonical(sb,l.UnitCost);
                AppendCanonical(sb,(l.PhysicalUnits?.Count??0).ToString(CultureInfo.InvariantCulture));
                foreach(var u in l.PhysicalUnits??new InventoryUnitInput[0])
                {
                    sb.Append("U|");AppendCanonical(sb,u.Serial);
                    AppendCanonical(sb,(u.Imeis?.Count??0).ToString(CultureInfo.InvariantCulture));
                    foreach(var imei in u.Imeis??new string[0]){sb.Append("I|");AppendCanonical(sb,imei);}
                }
            }
            return "v2:"+Hash(sb.ToString());
        }
        private static string Hash(string input)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-","");
        }
        private static void AppendPostPayload(StringBuilder sb,InventoryPostCommand command,long? reversalDocumentId)
        {
            AppendCanonical(sb,command.Type);
            sb.Append(command.WarehouseId.ToString(CultureInfo.InvariantCulture)).Append("|");
            AppendCanonical(sb,command.DestinationWarehouseId);
            AppendCanonical(sb,command.Reason);AppendCanonical(sb,command.Notes);
            sb.Append(command.UserId.ToString(CultureInfo.InvariantCulture)).Append("|");
            AppendCanonical(sb,command.ReversesDocumentId);AppendCanonical(sb,reversalDocumentId);
            AppendCanonical(sb,(command.Lines?.Count??0).ToString(CultureInfo.InvariantCulture));
            foreach(var l in command.Lines??new InventoryLineInput[0])
            {
                sb.Append("L|").Append(l.VariantId.ToString(CultureInfo.InvariantCulture)).Append("|").Append(l.ProductUnitId.ToString(CultureInfo.InvariantCulture)).Append("|");
                AppendCanonical(sb,l.Quantity);AppendCanonical(sb,l.UnitCost);AppendCanonical(sb,l.LotNumber);AppendCanonical(sb,l.ExpirationDate);
                // Preserve v2 hashes of older non-retail operations. A supplied retail price
                // is an explicit extension and is always part of the receipt identity.
                if(l.SalePrice!=null){sb.Append("sale|");AppendCanonical(sb,l.SalePrice);}
                AppendCanonical(sb,l.ExpectedVersion);AppendCanonical(sb,l.ExpectedQuantity);
                AppendCanonical(sb,(l.PhysicalUnits?.Count??0).ToString(CultureInfo.InvariantCulture));
                foreach(var u in l.PhysicalUnits??new InventoryUnitInput[0])
                {
                    sb.Append("U|");AppendCanonical(sb,u.Serial);
                    AppendCanonical(sb,(u.Imeis?.Count??0).ToString(CultureInfo.InvariantCulture));
                    foreach(var imei in u.Imeis??new string[0]){sb.Append("I|");AppendCanonical(sb,imei);}
                }
            }
        }
        private static void AppendCanonical(StringBuilder sb,string value)
        {
            if(value==null)sb.Append("N|");
            else sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(":").Append(value).Append("|");
        }
        private static void AppendCanonical(StringBuilder sb,long? value)
        {
            if(value.HasValue)sb.Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append("|");
            else sb.Append("N|");
        }
    }
}
