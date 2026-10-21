using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Features
{
    public interface IFeatureStore
    {
        Task<IReadOnlyList<StoredFeatureState>> GetModuleStatesAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<StoredFeatureState>> GetCapabilityStatesAsync(CancellationToken cancellationToken);
        Task SetStateAsync(string kind, string key, bool enabled, long changedByUserId, CancellationToken cancellationToken);
        Task ApplyRecommendationsAsync(IReadOnlyList<string> moduleKeys, IReadOnlyList<string> capabilityKeys, long changedByUserId, CancellationToken cancellationToken);
    }
}

