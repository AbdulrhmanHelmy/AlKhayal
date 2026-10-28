using System;

namespace AlKhayal.Application.Sales
{
    public static class SalesRefundPolicy
    {
        public static (long Cash, long Credit) CapDebtReduction(long total, long customerDebt)
        {
            var credit = Math.Min(total, Math.Max(0, customerDebt));
            return (total - credit, credit);
        }
    }
}
