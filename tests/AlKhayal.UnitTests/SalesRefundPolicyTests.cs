using AlKhayal.Application.Sales;
using AlKhayal.Application.Treasury;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class SalesRefundPolicyTests
    {
        [Fact]
        public void Credit_refund_caps_debt_at_zero_and_returns_the_cash_residual_exactly()
        {
            Assert.Equal((7000L,3000L),SalesRefundPolicy.CapDebtReduction(10000,3000));
            Assert.Equal((0L,10000L),SalesRefundPolicy.CapDebtReduction(10000,30000));
            Assert.Equal((10000L,0L),SalesRefundPolicy.CapDebtReduction(10000,0));
        }
        [Fact]
        public void Explicit_refund_overdraft_preserves_integer_bounds_and_default_withdrawals_still_reject_it()
        {
            Assert.Equal(-100,TreasuryRules.NextBalance(0,100,"withdraw",true));
            Assert.Equal(0,TreasuryRules.NextBalance(-100,100,"deposit"));
            Assert.Equal("insufficient_balance",Assert.Throws<TreasuryException>(()=>TreasuryRules.NextBalance(0,100,"withdraw")).Code);
            Assert.Equal("balance_overflow",Assert.Throws<TreasuryException>(()=>TreasuryRules.NextBalance(-9223372036854775800L,100,"withdraw",true)).Code);
        }
    }
}
