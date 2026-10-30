using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;

namespace AlKhayal.Application.Sales
{
    public sealed class SalesService
    {
        private readonly ISalesStore _store;
        public SalesService(ISalesStore store) { _store=store; }
        public async Task<SaleInvoice> CreateDraftAsync(SaleDraftCommand command,long userId,CancellationToken ct)
        { var amounts=Validate(command,userId); return await _store.CreateDraftAsync(command,amounts.subtotal,amounts.discount,amounts.tax,amounts.total,userId,ct).ConfigureAwait(false); }
        public async Task<SaleInvoice> UpdateDraftAsync(long id,SaleDraftCommand command,long userId,CancellationToken ct)
        { var amounts=Validate(command,userId); return await _store.UpdateDraftAsync(id,command,amounts.subtotal,amounts.discount,amounts.tax,amounts.total,userId,ct).ConfigureAwait(false); }
        public Task<SaleInvoice> GetAsync(long id,CancellationToken ct) { return _store.GetAsync(id,ct); }
        public Task<System.Collections.Generic.IReadOnlyList<SaleInvoice>> ListAsync(string search,string from,string to,CancellationToken ct) { ValidateDate(from);ValidateDate(to);if(from!=null&&to!=null&&string.CompareOrdinal(from,to)>0)throw new ArgumentException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");return _store.ListAsync(search,from,to,ct); }
        public async Task<SalePostResult> PostAsync(long id,string key,long userId,CancellationToken ct)
        { if(id<=0||userId<=0||string.IsNullOrWhiteSpace(key)||key.Length>120)return SalePostResult.Fail("invalid_operation","مفتاح العملية غير صالح.");var invoice=await _store.GetAsync(id,ct).ConfigureAwait(false);if(invoice==null)return null;return await _store.PostAsync(id,invoice.Version,key.Trim(),Hash(invoice,key.Trim(),userId),userId,ct).ConfigureAwait(false); }
        public Task<SalePaymentResult> AddPaymentAsync(long id,SalePaymentCommand command,long userId,CancellationToken ct)
        {
            long amount;DateTime date;
            if(id<=0||userId<=0||command==null||!MoneyPolicy.TryParseUserAmount(command.Amount,false,out amount)||amount<=0||string.IsNullOrWhiteSpace(command.OperationKey)||command.OperationKey.Length>120||!(command.Method=="cash"||command.Method=="card"||command.Method=="transfer")||!DateTime.TryParseExact(command.OccurredOn??DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))
                return Task.FromResult(new SalePaymentResult{ErrorCode="validation",ErrorMessage="أدخل مبلغًا موجبًا وطريقة دفع وتاريخًا صحيحًا."});
            command.OperationKey=command.OperationKey.Trim();command.OccurredOn=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);command.Amount=MoneyPolicy.FormatForUser(amount);
            return _store.AddPaymentAsync(id,command,userId,ct);
        }
        public Task<SaleReturnResult> ReturnAsync(long id,SaleReturnCommand command,long userId,CancellationToken ct)
        {
            if(id<=0||userId<=0||command==null||string.IsNullOrWhiteSpace(command.OperationKey)||command.OperationKey.Length>120||string.IsNullOrWhiteSpace(command.Reason)||command.Reason.Trim().Length<2||command.Reason.Trim().Length>240||command.Lines==null||command.Lines.Count==0||command.Lines.Any(x=>x==null||x.InvoiceLineId<=0))return Task.FromResult(new SaleReturnResult{ErrorCode="validation",ErrorMessage="أدخل سبب الإرجاع وبندًا واحدًا على الأقل ومفتاح عملية صالحًا."});
            command.OperationKey=command.OperationKey.Trim();command.Reason=command.Reason.Trim();return _store.ReturnAsync(id,command,userId,ct);
        }
        public Task<SaleCancellationResult> DeleteDraftAsync(long id,string key,long userId,CancellationToken ct)
        { if(id<=0||userId<=0||string.IsNullOrWhiteSpace(key)||key.Length>120)return Task.FromResult(new SaleCancellationResult{ErrorCode="validation",ErrorMessage="مفتاح العملية غير صالح."});return _store.DeleteDraftAsync(id,key.Trim(),userId,ct); }
        public Task<SaleCancellationResult> CancelAsync(long id,string key,string reason,long userId,CancellationToken ct)
        { if(id<=0||userId<=0||string.IsNullOrWhiteSpace(key)||key.Length>120||string.IsNullOrWhiteSpace(reason)||reason.Trim().Length<2||reason.Trim().Length>240)return Task.FromResult(new SaleCancellationResult{ErrorCode="validation",ErrorMessage="أدخل سبب إلغاء واضحًا ومفتاح عملية صالحًا."});return _store.CancelAsync(id,key.Trim(),reason.Trim(),userId,ct); }
        private static (long subtotal,long discount,long tax,long total) Validate(SaleDraftCommand c,long userId)
        {
            DateTime date;long discount;
            if(c==null||userId<=0||c.WarehouseId<=0||c.CustomerId.HasValue&&c.CustomerId<=0||c.PaymentMode!="cash"&&c.PaymentMode!="credit"||c.PaymentMode=="credit"&&!c.CustomerId.HasValue)throw new ArgumentException("اختر المخزن وطريقة الدفع الصحيحة؛ البيع الآجل يتطلب تحديد عميل.");
            if(c.PaymentAmount!=null||c.HasPaymentAllocations)throw new ArgumentException("الدفع الفوري يستخدم طريقة واحدة لكامل الفاتورة؛ لا ترسل مبالغ أو أجزاء دفع.");
            c.ImmediateMethod = c.ImmediateMethod ?? c.PaymentMode;
            if (c.PaymentMode == "credit" ? c.ImmediateMethod != "credit" || c.PaymentInstrumentId.HasValue :
                c.ImmediateMethod == "cash" ? c.PaymentInstrumentId.HasValue :
                (c.ImmediateMethod != "wallet" && c.ImmediateMethod != "card") || !c.PaymentInstrumentId.HasValue || c.PaymentInstrumentId <= 0)
                throw new ArgumentException("اختر طريقة دفع واحدة ووسيلة مناسبة لكامل الفاتورة.");
            ValidateDate(c.InvoiceDate,true);if(!DateTime.TryParseExact(c.InvoiceDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))throw new ArgumentException("تاريخ الفاتورة غير صحيح.");
            if(c.Lines==null||c.Lines.Count==0||c.Lines.Count>100)throw new ArgumentException("أضف صنفًا واحدًا على الأقل.");
            long subtotal=0;
            foreach(var l in c.Lines){long q,p,d;if(l==null||l.VariantId<=0||l.ProductUnitId<=0||!TryScale(l.Quantity,6,out q)||q<=0||!MoneyPolicy.TryParseUserAmount(l.UnitPrice,false,out p)||!MoneyPolicy.TryParseUserAmount(l.Discount??"0",false,out d))throw new ArgumentException("راجع الصنف والكمية والسعر والخصم.");var gross=MoneyPolicy.FinalizeQuantityAmount(q,p);if(d>gross)throw new ArgumentException("خصم البند أكبر من قيمته.");l.LineTotalScaled=gross-d;subtotal=checked(subtotal+l.LineTotalScaled);}
            if(c.Lines.Select(x=>new{ x.VariantId,x.ProductUnitId }).Distinct().Count()!=c.Lines.Count)throw new ArgumentException("لا تكرر الصنف والوحدة في بندين؛ اجمع الكمية في بند واحد.");
            if(!MoneyPolicy.TryParseUserAmount(c.Discount??"0",false,out discount)||discount>subtotal)throw new ArgumentException("خصم الفاتورة غير صالح أو أكبر من الإجمالي.");
            if(subtotal-discount<=0)throw new ArgumentException("يجب أن يكون إجمالي البيع بعد الخصومات أكبر من صفر.");
            // Tax is read from persisted settings during posting. Draft totals are recalculated by the store.
            return(subtotal,discount,0,subtotal-discount);
        }
        private static bool TryScale(string text,int places,out long scaled){scaled=0;decimal value;if(string.IsNullOrWhiteSpace(text)||!decimal.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value)||value<=0)return false;try{var result=value*(places==6?1000000m:10000m);if(result!=decimal.Truncate(result))return false;scaled=checked((long)result);return true;}catch{return false;}}
        private static void ValidateDate(string value,bool required=false){DateTime date;if(string.IsNullOrWhiteSpace(value)){if(required)throw new ArgumentException("تاريخ الفاتورة غير صحيح.");return;}if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))throw new ArgumentException("تاريخ الفاتورة غير صحيح.");}
        private static string Hash(SaleInvoice invoice,string key,long userId)
        {
            var s=new StringBuilder("sale-post-v2|").Append(invoice.Id).Append('|').Append(invoice.SubtotalScaled).Append('|').Append(invoice.DiscountScaled).Append('|').Append(invoice.TaxScaled).Append('|').Append(invoice.TotalScaled).Append('|').Append(invoice.WarehouseId).Append('|').Append(invoice.CustomerId).Append('|').Append(invoice.PaymentMode).Append('|').Append(key).Append('|').Append(userId);
            if (invoice.PaymentInstrumentId.HasValue) s.Append("|instrument:").Append(invoice.ImmediateMethod).Append(':').Append(invoice.PaymentInstrumentId.Value);
            foreach(var x in invoice.Lines.OrderBy(x=>x.Id))
            {
                s.Append('|').Append(x.Id).Append(':').Append(x.VariantId).Append(':').Append(x.ProductUnitId).Append(':').Append(x.Quantity).Append(':').Append(x.UnitPrice).Append(':').Append(x.Discount).Append(':').Append(x.BatchLotNumber).Append(':').Append(x.ExpirationDate);
                foreach(var unit in (x.PhysicalUnits??new System.Collections.Generic.List<SaleTrackingUnit>()).OrderBy(u=>u.Serial,StringComparer.OrdinalIgnoreCase))s.Append(':').Append(unit.Serial).Append('=').Append(string.Join(",",unit.Imeis.OrderBy(i=>i,StringComparer.OrdinalIgnoreCase)));
            }
            using(var sha=SHA256.Create())return "v2:"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s.ToString()))).Replace("-","");
        }
    }
}
