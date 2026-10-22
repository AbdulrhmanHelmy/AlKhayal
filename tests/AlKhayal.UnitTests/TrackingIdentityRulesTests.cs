using System;
using AlKhayal.Application.Catalog;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class TrackingIdentityRulesTests
    {
        [Fact]
        public void Serial_and_batch_normalize_without_registering_stock()
        {
            string normalized;
            Assert.True(TrackingIdentityRules.TryNormalizeSerial("  ab-١  ", out normalized));
            Assert.Equal("AB-١", normalized);
            Assert.True(TrackingIdentityRules.TryNormalizeBatch(" lot-7 ", out normalized));
            Assert.Equal("LOT-7", normalized);
            Assert.False(TrackingIdentityRules.TryNormalizeSerial("\u0001", out normalized));
        }

        [Fact]
        public void Imei_requires_exact_fifteen_ascii_digits_and_luhn()
        {
            string normalized;
            Assert.True(TrackingIdentityRules.TryNormalizeImei(" 490154203237518 ", out normalized));
            Assert.Equal("490154203237518", normalized);
            Assert.False(TrackingIdentityRules.TryNormalizeImei("490154203237519", out normalized));
            Assert.False(TrackingIdentityRules.TryNormalizeImei("49015420323751A", out normalized));
        }

        [Fact]
        public void Expiration_is_a_date_only_business_value()
        {
            DateTime date;
            Assert.True(TrackingIdentityRules.TryParseExpirationDate("2028-02-29", out date));
            Assert.False(TrackingIdentityRules.TryParseExpirationDate("2027-02-29", out date));
            Assert.False(TrackingIdentityRules.TryParseExpirationDate("2028-02-29T00:00:00Z", out date));
        }
    }
}
