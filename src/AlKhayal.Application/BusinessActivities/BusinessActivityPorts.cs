using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.BusinessActivities
{
    public interface IBusinessActivityStore
    {
        Task<IReadOnlyList<SelectedBusinessActivity>> GetSelectedAsync(CancellationToken cancellationToken);
        Task SaveSelectedAsync(IReadOnlyList<string> activityKeys, string primaryActivityKey, long changedByUserId, CancellationToken cancellationToken);
        Task<IReadOnlyList<CapabilityConfiguration>> GetCapabilityConfigurationAsync(CancellationToken cancellationToken);
        Task ApplyRecommendationsAsync(IReadOnlyList<CapabilityRecommendation> recommendations, long changedByUserId, CancellationToken cancellationToken);
    }

    public interface IFeatureRecommendationApplier
    {
        Task ApplyAsync(IReadOnlyList<CapabilityRecommendation> recommendations, long changedByUserId, CancellationToken cancellationToken);
    }
}
