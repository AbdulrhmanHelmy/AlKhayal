using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Features;

namespace AlKhayal.Application.Catalog
{
    public sealed class CatalogFeatureReader : ICatalogFeatureReader
    {
        private readonly FeatureService _features;
        public CatalogFeatureReader(FeatureService features) { _features = features; }

        public async Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(CancellationToken ct)
        {
            var snapshot = await _features.GetEffectiveAsync(ct).ConfigureAwait(false);
            return snapshot.Modules.Concat(snapshot.Capabilities)
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().IsEnabled, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<bool> IsFeatureEnabledAsync(string featureKey, CancellationToken ct)
        {
            var states = await GetEffectiveFeaturesAsync(ct).ConfigureAwait(false);
            return states.TryGetValue(featureKey, out var enabled) && enabled;
        }
    }
}
