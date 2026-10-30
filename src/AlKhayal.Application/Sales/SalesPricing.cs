using System;
using AlKhayal.Application.Finance;

namespace AlKhayal.Application.Sales
{
    public static class SalesPricing
    {
        // Catalog variant prices are per base unit. Final sale inputs are piastres.
        public static long SelectedUnitPrice(long basePriceScaled, long numerator, long denominator)
        {
            if (basePriceScaled < 0 || numerator <= 0 || denominator <= 0) throw new ArgumentOutOfRangeException();
            return checked((long)decimal.Round((decimal)basePriceScaled * numerator / denominator /
                MoneyPolicy.PiastreInInternalUnits, 0, MidpointRounding.AwayFromZero) * MoneyPolicy.PiastreInInternalUnits);
        }
    }
}
