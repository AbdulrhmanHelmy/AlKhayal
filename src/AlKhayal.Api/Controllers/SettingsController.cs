using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Settings;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/settings")]
    public sealed class SettingsController : ApiController
    {
        private const string CookieName = "alkhayal_session";
        private readonly SettingsService _settings;
        private readonly AuthService _auth;
        public SettingsController(SettingsService settings, AuthService auth) { _settings = settings; _auth = auth; }

        [HttpGet, Route("company")]
        public async Task<IHttpActionResult> Company(CancellationToken ct)
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            return Ok(ApiEnvelope<CompanyProfile>.Ok(await _settings.GetCompanyAsync(ct).ConfigureAwait(false)));
        }

        [HttpPut, Route("company")]
        public async Task<IHttpActionResult> Company(UpdateCompanyProfileRequest request, CancellationToken ct)
        {
            var check = await Administrator(ct, true).ConfigureAwait(false); if (check.Error != null) return check.Error;
            var result = await _settings.UpdateCompanyAsync(request == null ? null : request.ToCommand(check.User.Id), ct).ConfigureAwait(false);
            return Result(result);
        }

        [HttpGet, Route("general")]
        public async Task<IHttpActionResult> General(CancellationToken ct)
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            return Ok(ApiEnvelope<GeneralSettings>.Ok(await _settings.GetGeneralAsync(ct).ConfigureAwait(false)));
        }

        [HttpPut, Route("general")]
        public async Task<IHttpActionResult> General(UpdateGeneralSettingsRequest request, CancellationToken ct)
        {
            var check = await Administrator(ct, true).ConfigureAwait(false); if (check.Error != null) return check.Error;
            var result = await _settings.UpdateGeneralAsync(request == null ? null : request.ToCommand(check.User.Id), ct).ConfigureAwait(false);
            return Result(result);
        }

        [HttpGet, Route("database-info")]
        public async Task<IHttpActionResult> DatabaseInfo(CancellationToken ct)
        {
            var check = await Administrator(ct, false).ConfigureAwait(false); if (check.Error != null) return check.Error;
            return Ok(ApiEnvelope<DatabaseInformation>.Ok(await _settings.GetDatabaseInformationAsync(ct).ConfigureAwait(false)));
        }

        private IHttpActionResult Result<T>(SettingsResult<T> result) { return result.Succeeded ? Ok(ApiEnvelope<T>.Ok(result.Value)) : Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail(result.ErrorCode, result.ErrorMessage)); }
        private async Task<AuthCheck> Administrator(CancellationToken ct, bool csrf)
        {
            var user = await CurrentUser(ct).ConfigureAwait(false);
            if (user == null) return new AuthCheck { Error = UnauthorizedResult() };
            if (user.Role != UserRoles.Administrator) return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("forbidden", "تعديل الإعدادات متاح لمسؤول النظام فقط.")) };
            if (csrf && !await _auth.ValidateCsrfAsync(ReadToken(), Header("X-CSRF-Token"), ct).ConfigureAwait(false)) return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى.")) };
            return new AuthCheck { User = user };
        }
        private Task<UserIdentity> CurrentUser(CancellationToken ct) { return _auth.GetCurrentUserAsync(ReadToken(), ct); }
        private IHttpActionResult UnauthorizedResult() { return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولاً.")); }
        private string ReadToken() { IEnumerable<string> values; if (!Request.Headers.TryGetValues("Cookie", out values)) return null; foreach (var header in values) foreach (var part in header.Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == CookieName) return pair[1]; } return null; }
        private string Header(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
        private sealed class AuthCheck { public UserIdentity User { get; set; } public IHttpActionResult Error { get; set; } }
    }

    public sealed class UpdateCompanyProfileRequest
    {
        public string BusinessName { get; set; } public string DisplayName { get; set; } public string Phone { get; set; } public string Email { get; set; } public string Address { get; set; } public string TaxRegistrationNumber { get; set; } public string CommercialRegistrationNumber { get; set; } public string FooterText { get; set; }
        public UpdateCompanyProfileCommand ToCommand(long userId) { return new UpdateCompanyProfileCommand { BusinessName=BusinessName, DisplayName=DisplayName, Phone=Phone, Email=Email, Address=Address, TaxRegistrationNumber=TaxRegistrationNumber, CommercialRegistrationNumber=CommercialRegistrationNumber, FooterText=FooterText, ChangedByUserId=userId }; }
    }

    public sealed class UpdateGeneralSettingsRequest
    {
        public string LanguageCode { get; set; } public string DateFormat { get; set; } public string TimeFormat { get; set; } public bool UseGroupSeparators { get; set; } public string CurrencyCode { get; set; } public string CurrencySymbol { get; set; } public int DecimalPrecision { get; set; } public bool TaxEnabled { get; set; } public decimal DefaultTaxRate { get; set; } public bool PricesIncludeTax { get; set; }
        public UpdateGeneralSettingsCommand ToCommand(long userId) { return new UpdateGeneralSettingsCommand { LanguageCode=LanguageCode, DateFormat=DateFormat, TimeFormat=TimeFormat, UseGroupSeparators=UseGroupSeparators, CurrencyCode=CurrencyCode, CurrencySymbol=CurrencySymbol, DecimalPrecision=DecimalPrecision, TaxEnabled=TaxEnabled, DefaultTaxRate=DefaultTaxRate, PricesIncludeTax=PricesIncludeTax, ChangedByUserId=userId }; }
    }
}
