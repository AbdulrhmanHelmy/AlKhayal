using AlKhayal.Application.Suppliers;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class SupplierPaymentAllocatorTests
    {
        [Fact]
        public void Creation_priority_compares_instants_across_UTC_timestamp_precisions()
        {
            var rows=SupplierPaymentAllocator.Allocate(new[]{
                new SupplierAllocationInvoice{SupplierInvoiceId=1,RemainingScaled=100,PriorityDate="2026-11-01",CreatedUtc="2026-10-28T12:00:00.1000000Z"},
                new SupplierAllocationInvoice{SupplierInvoiceId=2,RemainingScaled=100,PriorityDate="2026-11-01",CreatedUtc="2026-10-28T12:00:00Z"}
            },150);
            Assert.Equal(2,rows[0].SupplierInvoiceId);Assert.Equal(100,rows[0].AmountScaled);
            Assert.Equal(1,rows[1].SupplierInvoiceId);Assert.Equal(50,rows[1].AmountScaled);
        }
    }
}
