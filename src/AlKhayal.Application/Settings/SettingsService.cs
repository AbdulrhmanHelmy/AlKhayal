using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Settings
{
    public sealed class SettingsService
    {
        private static readonly HashSet<string> DateFormats = new HashSet<string>(StringComparer.Ordinal) { "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd" };
        private static readonly HashSet<string> TimeFormats = new HashSet<string>(StringComparer.Ordinal) { "HH:mm", "hh:mm tt" };
        private readonly ISettingsStore _store;
        private readonly IDatabaseInformationProvider _database;
        private readonly SemaphoreSlim _companyLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _generalLock = new SemaphoreSlim(1, 1);
        private CompanyProfile _companyCache;
        private GeneralSettings _generalCache;

        public SettingsService(ISettingsStore store, IDatabaseInformationProvider database) { _store = store; _database = database; }

        public async Task<CompanyProfile> GetCompanyAsync(CancellationToken ct)
        {
            if (_companyCache != null) return _companyCache;
            await _companyLock.WaitAsync(ct).ConfigureAwait(false);
            try { return _companyCache ?? (_companyCache = await _store.GetCompanyAsync(ct).ConfigureAwait(false)); }
            finally { _companyLock.Release(); }
        }

        public async Task<GeneralSettings> GetGeneralAsync(CancellationToken ct)
        {
            if (_generalCache != null) return _generalCache;
            await _generalLock.WaitAsync(ct).ConfigureAwait(false);
            try { return _generalCache ?? (_generalCache = await _store.GetGeneralAsync(ct).ConfigureAwait(false)); }
            finally { _generalLock.Release(); }
        }

        public Task<DatabaseInformation> GetDatabaseInformationAsync(CancellationToken ct) { return _database.GetAsync(ct); }

        public async Task<SettingsResult<CompanyProfile>> UpdateCompanyAsync(UpdateCompanyProfileCommand command, CancellationToken ct)
        {
            var error = ValidateCompany(command);
            if (error != null) return SettingsResult<CompanyProfile>.Fail("validation", error);
            NormalizeCompany(command);
            var value = await _store.UpdateCompanyAsync(command, ct).ConfigureAwait(false);
            _companyCache = value;
            return SettingsResult<CompanyProfile>.Ok(value);
        }

        public async Task<SettingsResult<GeneralSettings>> UpdateGeneralAsync(UpdateGeneralSettingsCommand command, CancellationToken ct)
        {
            var error = ValidateGeneral(command);
            if (error != null) return SettingsResult<GeneralSettings>.Fail("validation", error);
            NormalizeGeneral(command);
            var value = await _store.UpdateGeneralAsync(command, ct).ConfigureAwait(false);
            _generalCache = value;
            return SettingsResult<GeneralSettings>.Ok(value);
        }

        private static string ValidateCompany(UpdateCompanyProfileCommand value)
        {
            if (value == null) return "بيانات المنشأة مطلوبة.";
            if (string.IsNullOrWhiteSpace(value.BusinessName)) return "اسم المنشأة مطلوب.";
            if (value.BusinessName.Trim().Length > 160) return "اسم المنشأة أطول من الحد المسموح.";
            if (!string.IsNullOrWhiteSpace(value.Email)) { try { var ignored = new MailAddress(value.Email.Trim()); } catch (FormatException) { return "البريد الإلكتروني غير صالح."; } }
            if (TooLong(value.DisplayName, 160) || TooLong(value.Phone, 40) || TooLong(value.Email, 254) || TooLong(value.Address, 500) || TooLong(value.TaxRegistrationNumber, 80) || TooLong(value.CommercialRegistrationNumber, 80) || TooLong(value.FooterText, 500)) return "أحد الحقول أطول من الحد المسموح.";
            return null;
        }

        private static string ValidateGeneral(UpdateGeneralSettingsCommand value)
        {
            if (value == null) return "الإعدادات العامة مطلوبة.";
            if (value.LanguageCode != "ar") return "العربية هي اللغة المتاحة حاليًا.";
            if (!DateFormats.Contains(value.DateFormat)) return "تنسيق التاريخ غير مدعوم.";
            if (!TimeFormats.Contains(value.TimeFormat)) return "تنسيق الوقت غير مدعوم.";
            if (string.IsNullOrWhiteSpace(value.CurrencyCode) || value.CurrencyCode.Trim().Length != 3) return "رمز العملة يجب أن يتكون من ثلاثة أحرف.";
            if (string.IsNullOrWhiteSpace(value.CurrencySymbol) || value.CurrencySymbol.Trim().Length > 12) return "رمز عرض العملة مطلوب وبحد أقصى 12 حرفًا.";
            if (value.DecimalPrecision != 2) return "عرض المبالغ المالية ثابت على خانتين عشريتين.";
            if (value.DefaultTaxRate < 0 || value.DefaultTaxRate > 100) return "نسبة الضريبة يجب أن تكون بين صفر و100.";
            if (!value.TaxEnabled && value.PricesIncludeTax) return "لا يمكن اعتبار الأسعار شاملة للضريبة عند تعطيل الضريبة.";
            return null;
        }

        private static void NormalizeCompany(UpdateCompanyProfileCommand value)
        {
            value.BusinessName = value.BusinessName.Trim(); value.DisplayName = Clean(value.DisplayName); value.Phone = Clean(value.Phone); value.Email = Clean(value.Email); value.Address = Clean(value.Address); value.TaxRegistrationNumber = Clean(value.TaxRegistrationNumber); value.CommercialRegistrationNumber = Clean(value.CommercialRegistrationNumber); value.FooterText = Clean(value.FooterText);
        }
        private static void NormalizeGeneral(UpdateGeneralSettingsCommand value) { value.CurrencyCode = value.CurrencyCode.Trim().ToUpperInvariant(); value.CurrencySymbol = value.CurrencySymbol.Trim(); if (!value.TaxEnabled) value.DefaultTaxRate = 0; }
        private static string Clean(string value) { return string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        private static bool TooLong(string value, int maximum) { return value != null && value.Trim().Length > maximum; }
    }
}
