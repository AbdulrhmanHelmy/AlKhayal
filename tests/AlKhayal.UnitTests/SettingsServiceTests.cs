using System;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Settings;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class SettingsServiceTests
    {
        [Fact]
        public async Task Defaults_are_cached_for_concurrent_reads_and_valid_updates_replace_cache()
        {
            var store = new FakeStore(); var service = new SettingsService(store, store);
            var reads = await Task.WhenAll(service.GetGeneralAsync(CancellationToken.None), service.GetGeneralAsync(CancellationToken.None), service.GetGeneralAsync(CancellationToken.None));
            Assert.Equal(1, store.GeneralReads); Assert.All(reads, item => Assert.Equal("EGP", item.CurrencyCode));
            var result = await service.UpdateGeneralAsync(ValidGeneral(), CancellationToken.None);
            Assert.True(result.Succeeded); Assert.Equal("USD", (await service.GetGeneralAsync(CancellationToken.None)).CurrencyCode); Assert.Equal(1, store.GeneralReads);
        }

        [Fact]
        public async Task Invalid_company_currency_tax_and_language_values_are_rejected_before_persistence()
        {
            var store = new FakeStore(); var service = new SettingsService(store, store);
            Assert.False((await service.UpdateCompanyAsync(new UpdateCompanyProfileCommand { BusinessName=" ", ChangedByUserId=1 }, CancellationToken.None)).Succeeded);
            var currency=ValidGeneral();currency.CurrencyCode="LE";Assert.False((await service.UpdateGeneralAsync(currency,CancellationToken.None)).Succeeded);
            var tax=ValidGeneral();tax.DefaultTaxRate=101;Assert.False((await service.UpdateGeneralAsync(tax,CancellationToken.None)).Succeeded);
            var language=ValidGeneral();language.LanguageCode="en";Assert.False((await service.UpdateGeneralAsync(language,CancellationToken.None)).Succeeded);
            var precision=ValidGeneral();precision.DecimalPrecision=4;Assert.False((await service.UpdateGeneralAsync(precision,CancellationToken.None)).Succeeded);
            Assert.Equal(0,store.GeneralWrites);Assert.Equal(0,store.CompanyWrites);
        }

        [Fact]
        public async Task Company_validation_accepts_arabic_optional_fields_and_rejects_invalid_email()
        {
            var store=new FakeStore();var service=new SettingsService(store,store);
            var result=await service.UpdateCompanyAsync(new UpdateCompanyProfileCommand{BusinessName=" مؤسسة الخيال ",DisplayName="الخيال",Email="info@example.com",ChangedByUserId=1},CancellationToken.None);
            Assert.True(result.Succeeded);Assert.Equal("مؤسسة الخيال",result.Value.BusinessName);
            Assert.False((await service.UpdateCompanyAsync(new UpdateCompanyProfileCommand{BusinessName="الخيال",Email="not-an-email",ChangedByUserId=1},CancellationToken.None)).Succeeded);
        }

        private static UpdateGeneralSettingsCommand ValidGeneral(){return new UpdateGeneralSettingsCommand{LanguageCode="ar",DateFormat="yyyy-MM-dd",TimeFormat="HH:mm",UseGroupSeparators=true,CurrencyCode="usd",CurrencySymbol="$",DecimalPrecision=2,TaxEnabled=true,DefaultTaxRate=14,PricesIncludeTax=true,ChangedByUserId=1};}
        private sealed class FakeStore:ISettingsStore,IDatabaseInformationProvider
        {
            public int GeneralReads,GeneralWrites,CompanyWrites;private GeneralSettings _general=new GeneralSettings{LanguageCode="ar",DateFormat="dd/MM/yyyy",TimeFormat="HH:mm",CurrencyCode="EGP",CurrencySymbol="ج.م",DecimalPrecision=2};private CompanyProfile _company=new CompanyProfile{BusinessName=""};
            public async Task<GeneralSettings> GetGeneralAsync(CancellationToken ct){Interlocked.Increment(ref GeneralReads);await Task.Delay(20,ct);return _general;}
            public Task<CompanyProfile> GetCompanyAsync(CancellationToken ct){return Task.FromResult(_company);}
            public Task<GeneralSettings> UpdateGeneralAsync(UpdateGeneralSettingsCommand value,CancellationToken ct){GeneralWrites++;_general=value;return Task.FromResult((GeneralSettings)value);}
            public Task<CompanyProfile> UpdateCompanyAsync(UpdateCompanyProfileCommand value,CancellationToken ct){CompanyWrites++;_company=value;return Task.FromResult((CompanyProfile)value);}
            public Task<DatabaseInformation> GetAsync(CancellationToken ct){return Task.FromResult(new DatabaseInformation());}
        }
    }
}
