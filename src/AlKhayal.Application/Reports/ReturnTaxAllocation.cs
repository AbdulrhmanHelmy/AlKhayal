using System;
using System.Collections.Generic;

namespace AlKhayal.Application.Reports
{
    // Reporting only: inputs are immutable sale snapshots and returns in event order.
    public sealed class ReturnAllocationEvent
    {
        public long QuantityScaled { get; set; }
        public long GrossScaled { get; set; }
    }

    public sealed class ReturnAllocationPart
    {
        public long TaxScaled { get; set; }
        public long RevenueScaled { get; set; }
        public long LineDiscountScaled { get; set; }
        public long InvoiceDiscountScaled { get; set; }
    }

    public static class ReturnTaxAllocation
    {
        // A missing snapshot or inconsistent history must never be reported as zero tax.
        public static bool TryAllocate(long soldQuantity, long? originalGross, long? originalTax,
            long? lineDiscount, long? invoiceDiscount, IReadOnlyList<ReturnAllocationEvent> returns,
            out IReadOnlyList<ReturnAllocationPart> parts)
        {
            parts = null;
            if (soldQuantity <= 0 || !originalGross.HasValue || !originalTax.HasValue ||
                !lineDiscount.HasValue || !invoiceDiscount.HasValue || returns == null ||
                originalGross < 0 || originalTax < 0 || originalTax > originalGross ||
                lineDiscount < 0 || invoiceDiscount < 0) return false;
            try
            {
                long quantity = 0, gross = 0, tax = 0, line = 0, invoice = 0;
                var result = new List<ReturnAllocationPart>();
                foreach (var item in returns)
                {
                    if (item == null || item.QuantityScaled <= 0 || item.GrossScaled <= 0) return false;
                    quantity = checked(quantity + item.QuantityScaled);
                    gross = checked(gross + item.GrossScaled);
                    if (quantity > soldQuantity || gross > originalGross ||
                        (quantity == soldQuantity && gross != originalGross)) return false;
                    // Bound the cumulative tax by both the actual gross returned and the
                    // remaining original revenue, so neither component can be over-reversed.
                    long desired = Proportion(originalTax.Value, quantity, soldQuantity);
                    long minimum = Math.Max(tax, gross - (originalGross.Value - originalTax.Value));
                    long maximum = Math.Min(originalTax.Value, checked(tax + item.GrossScaled));
                    if (minimum > maximum) return false;
                    long nextTax = Math.Max(minimum, Math.Min(maximum, desired));
                    long nextLine = Proportion(lineDiscount.Value, quantity, soldQuantity);
                    long nextInvoice = Proportion(invoiceDiscount.Value, quantity, soldQuantity);
                    result.Add(new ReturnAllocationPart { TaxScaled = nextTax - tax,
                        RevenueScaled = item.GrossScaled - (nextTax - tax),
                        LineDiscountScaled = nextLine - line, InvoiceDiscountScaled = nextInvoice - invoice });
                    tax = nextTax; line = nextLine; invoice = nextInvoice;
                }
                parts = result;
                return true;
            }
            catch (OverflowException) { return false; }
        }

        private static long Proportion(long total, long quantity, long soldQuantity)
        {
            if (quantity == soldQuantity) return total;
            return checked((long)decimal.Floor((decimal)total * quantity / soldQuantity));
        }
    }
}
