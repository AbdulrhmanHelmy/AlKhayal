using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Settings
{
    public interface ISettingsStore
    {
        Task<CompanyProfile> GetCompanyAsync(CancellationToken ct);
        Task<GeneralSettings> GetGeneralAsync(CancellationToken ct);
        Task<CompanyProfile> UpdateCompanyAsync(UpdateCompanyProfileCommand command, CancellationToken ct);
        Task<GeneralSettings> UpdateGeneralAsync(UpdateGeneralSettingsCommand command, CancellationToken ct);
    }

    public interface IDatabaseInformationProvider
    {
        Task<DatabaseInformation> GetAsync(CancellationToken ct);
    }
}
