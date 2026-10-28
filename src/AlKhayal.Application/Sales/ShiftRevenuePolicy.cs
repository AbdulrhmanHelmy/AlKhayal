using System;
using System.Text;

namespace AlKhayal.Application.Sales
{
    public static class ShiftRevenuePolicy
    {
        public const string AccountName = "إيراد الوردية";
        public const string AccountCode = "SHIFT-REVENUE";
        public static bool MatchesName(string name)
        {
            var normalized = (name ?? "").Normalize(NormalizationForm.FormKC).Trim()
                .Replace('إ', 'ا').Replace('أ', 'ا').Replace('آ', 'ا').Replace('ة', 'ه');
            return string.Equals(normalized, "ايراد الورديه", StringComparison.Ordinal);
        }
    }
}
