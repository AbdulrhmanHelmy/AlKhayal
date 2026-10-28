using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace AlKhayal.Application.Suppliers
{
    public sealed class SupplierAllocationInvoice
    {
        public long? SupplierInvoiceId { get; set; }
        public long? PurchaseInvoiceId { get; set; }
        public long RemainingScaled { get; set; }
        public string PriorityDate { get; set; }
        public string CreatedUtc { get; set; }
    }
    public sealed class SupplierPaymentAllocation
    {
        public long? SupplierInvoiceId { get; set; }
        public long? PurchaseInvoiceId { get; set; }
        public long AmountScaled { get; set; }
    }
    public static class SupplierPaymentAllocator
    {
        public static IReadOnlyList<SupplierPaymentAllocation> Allocate(IEnumerable<SupplierAllocationInvoice> invoices,long amount)
        {
            var result=new List<SupplierPaymentAllocation>();
            foreach(var invoice in invoices.Where(x=>x.RemainingScaled>0).OrderBy(x=>x.PriorityDate,StringComparer.Ordinal).ThenBy(x=>CreatedTime(x.CreatedUtc)).ThenBy(x=>x.SupplierInvoiceId??x.PurchaseInvoiceId).ThenBy(x=>x.SupplierInvoiceId.HasValue?0:1))
            {
                if(amount<=0)break;
                var allocated=Math.Min(amount,invoice.RemainingScaled);
                result.Add(new SupplierPaymentAllocation{SupplierInvoiceId=invoice.SupplierInvoiceId,PurchaseInvoiceId=invoice.PurchaseInvoiceId,AmountScaled=allocated});amount-=allocated;
            }
            return result;
        }
        private static DateTimeOffset CreatedTime(string text)
        {
            DateTimeOffset value;
            if(!DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out value))
                throw new InvalidOperationException("تاريخ إنشاء إحدى الفواتير غير صالح. راجع بياناتها قبل توزيع السداد.");
            return value;
        }
    }
}
