using System;

namespace AlKhayal.Application.Settings
{
    public class CompanyProfile
    {
        public string BusinessName { get; set; }
        public string DisplayName { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string TaxRegistrationNumber { get; set; }
        public string CommercialRegistrationNumber { get; set; }
        public string FooterText { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public class GeneralSettings
    {
        public string LanguageCode { get; set; }
        public string DateFormat { get; set; }
        public string TimeFormat { get; set; }
        public bool UseGroupSeparators { get; set; }
        public string CurrencyCode { get; set; }
        public string CurrencySymbol { get; set; }
        public int DecimalPrecision { get; set; }
        public bool TaxEnabled { get; set; }
        public decimal DefaultTaxRate { get; set; }
        public bool PricesIncludeTax { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public sealed class DatabaseInformation
    {
        public string DatabasePath { get; set; }
        public long SizeBytes { get; set; }
        public int AppliedMigrationCount { get; set; }
        public string LatestMigration { get; set; }
        public string JournalMode { get; set; }
        public bool ForeignKeysEnabled { get; set; }
    }

    public sealed class UpdateCompanyProfileCommand : CompanyProfile { public long ChangedByUserId { get; set; } }
    public sealed class UpdateGeneralSettingsCommand : GeneralSettings { public long ChangedByUserId { get; set; } }

    public sealed class SettingsResult<T>
    {
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public T Value { get; set; }
        public static SettingsResult<T> Ok(T value) { return new SettingsResult<T> { Succeeded = true, Value = value }; }
        public static SettingsResult<T> Fail(string code, string message) { return new SettingsResult<T> { ErrorCode = code, ErrorMessage = message }; }
    }
}
