using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AlKhayal.Application.Catalog
{
    // Pure validation for the future ledger intake command. It never registers stock.
    public static class TrackingIdentityRules
    {
        public static bool TryNormalizeBatch(string value, out string normalized)
        {
            normalized = Normalize(value);
            return normalized != null && normalized.Length <= 80;
        }

        public static bool TryNormalizeSerial(string value, out string normalized)
        {
            normalized = Normalize(value);
            return normalized != null && normalized.Length <= 120;
        }

        public static bool TryNormalizeImei(string value, out string normalized)
        {
            normalized = value == null ? null : value.Trim();
            if (normalized == null || normalized.Length != 15 || normalized.Any(c => c < '0' || c > '9')) return false;
            var sum = 0;
            for (var i = 0; i < 15; i++)
            {
                var digit = normalized[i] - '0';
                if (i % 2 == 1) { digit *= 2; if (digit > 9) digit -= 9; }
                sum += digit;
            }
            return sum % 10 == 0;
        }

        public static bool TryParseExpirationDate(string value, out DateTime date)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                && date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == value;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var result = value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
            return result.Any(char.IsControl) ? null : result;
        }
    }
}
