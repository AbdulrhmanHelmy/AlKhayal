using System;

namespace AlKhayal.Application.Inventory
{
    /// <summary>Scale-4 money and scale-6 quantity policy, independent of storage.</summary>
    public static class InventoryCosting
    {
        public static long ReceiptValue(long selectedUnitCost,long enteredQuantityScaled)
        {
            if(selectedUnitCost<0||enteredQuantityScaled<=0)throw new ArgumentOutOfRangeException();
            return Round((decimal)selectedUnitCost*enteredQuantityScaled/1000000m);
        }

        public static long BaseUnitPrice(long selectedUnitPrice,long numerator,long denominator)
        {
            if(selectedUnitPrice<0||numerator<=0||denominator<=0)throw new ArgumentOutOfRangeException();
            return Round((decimal)selectedUnitPrice*denominator/numerator);
        }

        public static long AllocateLayer(long originalQuantity,long originalValue,long remainingQuantity,long remainingValue,long quantity)
        {
            if(originalQuantity<=0||originalValue<0||remainingQuantity<=0||remainingQuantity>originalQuantity||remainingValue<0||remainingValue>originalValue||quantity<=0||quantity>remainingQuantity)throw new ArgumentOutOfRangeException();
            if(quantity==remainingQuantity)return remainingValue;
            var consumed=checked(originalQuantity-remainingQuantity);
            return checked(Round((decimal)originalValue*checked(consumed+quantity)/originalQuantity)-(originalValue-remainingValue));
        }

        private static long Round(decimal value){return checked((long)Math.Round(value,0,MidpointRounding.AwayFromZero));}
    }
}
