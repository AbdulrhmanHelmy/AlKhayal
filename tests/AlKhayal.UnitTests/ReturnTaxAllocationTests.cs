using System.Collections.Generic;
using AlKhayal.Application.Reports;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class ReturnTaxAllocationTests
    {
        [Fact]
        public void Repeated_returns_reconcile_saved_gross_and_final_residual()
        {
            IReadOnlyList<ReturnAllocationPart> parts;
            Assert.True(ReturnTaxAllocation.TryAllocate(3, 1001, 151, 11, 7,
                new[] { Event(1, 300), Event(1, 300), Event(1, 401) }, out parts));
            Assert.Equal(new long[] { 50, 50, 51 }, new[] { parts[0].TaxScaled, parts[1].TaxScaled, parts[2].TaxScaled });
            Assert.Equal(850, parts[0].RevenueScaled + parts[1].RevenueScaled + parts[2].RevenueScaled);
            Assert.Equal(11, parts[0].LineDiscountScaled + parts[1].LineDiscountScaled + parts[2].LineDiscountScaled);
            Assert.Equal(7, parts[0].InvoiceDiscountScaled + parts[1].InvoiceDiscountScaled + parts[2].InvoiceDiscountScaled);
        }

        [Fact]
        public void Saved_tax_and_actual_refund_bound_each_component()
        {
            IReadOnlyList<ReturnAllocationPart> parts;
            Assert.True(ReturnTaxAllocation.TryAllocate(2, 100, 80, 0, 0,
                new[] { Event(1, 10), Event(1, 90) }, out parts));
            Assert.Equal(10, parts[0].TaxScaled);
            Assert.Equal(70, parts[1].TaxScaled);
            Assert.Equal(20, parts[1].RevenueScaled);
            Assert.True(ReturnTaxAllocation.TryAllocate(2, 100, 0, 0, 0,
                new[] { Event(1, 50) }, out parts));
            Assert.Equal(0, parts[0].TaxScaled);
        }

        [Fact]
        public void Missing_or_inconsistent_history_is_incomplete()
        {
            IReadOnlyList<ReturnAllocationPart> parts;
            Assert.False(ReturnTaxAllocation.TryAllocate(1, 100, null, 0, 0, new[] { Event(1, 100) }, out parts));
            Assert.Null(parts);
            Assert.False(ReturnTaxAllocation.TryAllocate(1, 100, 20, 0, 0, new[] { Event(1, 99) }, out parts));
            Assert.False(ReturnTaxAllocation.TryAllocate(1, 100, 20, 0, 0, new[] { Event(2, 100) }, out parts));
        }

        private static ReturnAllocationEvent Event(long quantity, long gross)
        { return new ReturnAllocationEvent { QuantityScaled = quantity, GrossScaled = gross }; }
    }
}
