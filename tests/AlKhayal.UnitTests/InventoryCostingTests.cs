using System;
using AlKhayal.Application.Inventory;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class InventoryCostingTests
    {
        [Fact]
        public void Partial_layer_consumption_conserves_value_and_uses_cumulative_rounding()
        {
            long quantity=1000000,value=1,total=0;
            for(var i=0;i<10;i++)
            {
                var allocated=InventoryCosting.AllocateLayer(1000000,1,quantity,value,100000);
                total+=allocated;quantity-=100000;value-=allocated;
            }
            Assert.Equal(1,total);Assert.Equal(0,value);
            Assert.Equal(1,InventoryCosting.ReceiptValue(1,500000));
            Assert.Equal(0,InventoryCosting.ReceiptValue(0,1000000));
        }
        [Fact]
        public void Unit_prices_convert_once_and_unsafe_arithmetic_is_rejected()
        {
            Assert.Equal(100000,InventoryCosting.BaseUnitPrice(1200000,12,1));
            Assert.Equal(26667,InventoryCosting.BaseUnitPrice(40000,3,2));
            Assert.Throws<OverflowException>(()=>InventoryCosting.ReceiptValue(long.MaxValue,2000000));
            Assert.Throws<OverflowException>(()=>InventoryCosting.ReceiptValue(long.MaxValue,long.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(()=>InventoryCosting.AllocateLayer(100,10,50,5,51));
        }
    }
}
