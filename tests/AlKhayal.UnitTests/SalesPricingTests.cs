using System;
using AlKhayal.Application.Sales;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class SalesPricingTests
    {
        [Theory]
        [InlineData(2000000,3,1,6000000)]
        [InlineData(33333,3,1,100000)]
        [InlineData(100000,1,3,33300)]
        [InlineData(100050,1,1,100100)]
        [InlineData(0,12,1,0)]
        public void Selected_unit_price_rounds_once_to_piastres(long price,long numerator,long denominator,long expected)
        { Assert.Equal(expected,SalesPricing.SelectedUnitPrice(price,numerator,denominator)); }

        [Fact]
        public void Invalid_conversions_and_overflow_are_rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>SalesPricing.SelectedUnitPrice(-1,1,1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>SalesPricing.SelectedUnitPrice(100,0,1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>SalesPricing.SelectedUnitPrice(100,1,0));
            Assert.Throws<OverflowException>(()=>SalesPricing.SelectedUnitPrice(long.MaxValue,2,1));
        }
    }
}
