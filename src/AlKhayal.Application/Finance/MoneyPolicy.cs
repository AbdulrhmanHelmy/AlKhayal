using System;
using System.Globalization;

namespace AlKhayal.Application.Finance
{
    public static class MoneyPolicy
    {
        public const long InternalScale = 10000L;
        public const long PiastreInInternalUnits = 100L;

        public static bool TryParseUserAmount(string text, bool allowNegative, out long scaled)
        {
            scaled = 0;
            decimal value;
            var normalized = text == null ? null : text.Trim();
            var decimalPoint = normalized == null ? -1 : normalized.IndexOf('.');
            if (string.IsNullOrWhiteSpace(normalized) ||
                decimalPoint >= 0 && normalized.Length - decimalPoint - 1 > 2 ||
                !decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out value) ||
                !allowNegative && value < 0)
                return false;

            try
            {
                var piastres = checked(value * 100m);
                if (piastres != decimal.Truncate(piastres)) return false;
                scaled = checked(decimal.ToInt64(piastres) * PiastreInInternalUnits);
                return true;
            }
            catch (OverflowException) { return false; }
        }

        public static bool HasAtMostTwoDecimals(decimal value)
        {
            var scale = (decimal.GetBits(value)[3] >> 16) & 0x7F;
            if (scale > 2) return false;
            try
            {
                var internalValue = checked(value * InternalScale);
                return internalValue >= long.MinValue && internalValue <= long.MaxValue;
            }
            catch (OverflowException) { return false; }
        }

        public static long RoundInternalToPiastre(long scaled)
        {
            return checked(decimal.ToInt64(Math.Round(scaled / (decimal)PiastreInInternalUnits, 0,
                MidpointRounding.AwayFromZero)) * PiastreInInternalUnits);
        }

        public static long FinalizeQuantityAmount(long quantityScale6, long unitPriceScale4)
        {
            var internalValue = checked((decimal)quantityScale6 * unitPriceScale4 / 1000000m);
            return checked(decimal.ToInt64(Math.Round(internalValue / PiastreInInternalUnits, 0,
                MidpointRounding.AwayFromZero)) * PiastreInInternalUnits);
        }

        public static string FormatForUser(long scaled)
        {
            return (scaled / (decimal)InternalScale).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
