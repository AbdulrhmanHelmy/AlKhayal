using AlKhayal.Application.Finance;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class MoneyPolicyTests
    {
        [Theory]
        [InlineData("0", 0L)]
        [InlineData("0.01", 100L)]
        [InlineData("0.10", 1000L)]
        [InlineData("0.99", 9900L)]
        [InlineData("922337203685477.58", 9223372036854775800L)]
        public void User_amounts_accept_at_most_two_decimals(string text, long expected)
        { long actual; Assert.True(MoneyPolicy.TryParseUserAmount(text, false, out actual)); Assert.Equal(expected, actual); }

        [Theory]
        [InlineData("0.001")]
        [InlineData("1.2345")]
        [InlineData("1.2300")]
        [InlineData("-0.01")]
        [InlineData("1,000.00")]
        public void Invalid_user_amounts_are_rejected(string text)
        { long ignored; Assert.False(MoneyPolicy.TryParseUserAmount(text, false, out ignored)); }

        [Fact]
        public void Decimal_contract_values_retain_and_enforce_the_entered_scale()
        {
            Assert.True(MoneyPolicy.HasAtMostTwoDecimals(1.23m));
            Assert.False(MoneyPolicy.HasAtMostTwoDecimals(1.230m));
        }

        [Fact]
        public void Finalized_amounts_round_half_piastre_away_from_zero()
        {
            Assert.Equal(100, MoneyPolicy.RoundInternalToPiastre(50));
            Assert.Equal(-100, MoneyPolicy.RoundInternalToPiastre(-50));
            Assert.Equal(100, MoneyPolicy.FinalizeQuantityAmount(500000, 100));
            Assert.Equal(100, MoneyPolicy.FinalizeQuantityAmount(125000, 800));
            Assert.Equal("12.35", MoneyPolicy.FormatForUser(123450));
        }
    }
}
