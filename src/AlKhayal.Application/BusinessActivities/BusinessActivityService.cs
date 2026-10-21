using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.BusinessActivities
{
    public sealed class BusinessActivityService
    {
        private readonly IBusinessActivityStore _store;
        private readonly IFeatureRecommendationApplier _featureRecommendations;
        public BusinessActivityService(IBusinessActivityStore store, IFeatureRecommendationApplier featureRecommendations = null) { _store = store; _featureRecommendations = featureRecommendations; }

        public IReadOnlyList<BusinessActivityDefinition> ListSupported()
        {
            return BusinessActivityRegistry.All.Where(item => item.IsActive).OrderBy(item => item.DefaultPriority).ToList();
        }

        public async Task<BusinessActivitySelection> GetSelectedAsync(CancellationToken ct)
        {
            var selected = await _store.GetSelectedAsync(ct).ConfigureAwait(false);
            return new BusinessActivitySelection
            {
                IsConfigured = selected.Count > 0,
                ActivityKeys = selected.Select(item => item.ActivityKey).ToList(),
                PrimaryActivityKey = selected.Where(item => item.IsPrimary).Select(item => item.ActivityKey).FirstOrDefault()
            };
        }

        public IReadOnlyList<CapabilityRecommendation> Recommend(IEnumerable<string> activityKeys)
        {
            string validationError;
            var definitions = NormalizeAndValidate(activityKeys, false, out validationError);
            if (validationError != null) throw new ArgumentException(validationError, "activityKeys");
            var sources = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var activity in definitions)
                foreach (var capability in activity.SuggestedCapabilities)
                {
                    List<string> activitySources;
                    if (!sources.TryGetValue(capability, out activitySources)) sources[capability] = activitySources = new List<string>();
                    if (!activitySources.Contains(activity.Key)) activitySources.Add(activity.Key);
                }
            return sources.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new CapabilityRecommendation
            {
                CapabilityKey = pair.Key,
                SuggestedEnabled = true,
                ActivitySources = pair.Value,
                Reason = "موصى بها للأنشطة: " + string.Join("، ", pair.Value.Select(key => BusinessActivityRegistry.Find(key).ArabicName))
            }).ToList();
        }

        public async Task<RecommendationComparison> CompareAsync(IEnumerable<string> activityKeys, CancellationToken ct)
        {
            var recommendations = Recommend(activityKeys);
            var configured = await _store.GetCapabilityConfigurationAsync(ct).ConfigureAwait(false);
            var recommendedKeys = new HashSet<string>(recommendations.Select(item => item.CapabilityKey), StringComparer.OrdinalIgnoreCase);
            var currentRecommended = new HashSet<string>(configured.Where(item => item.OverrideState == "recommended").Select(item => item.CapabilityKey), StringComparer.OrdinalIgnoreCase);
            return new RecommendationComparison
            {
                Recommendations = recommendations,
                NewlyRecommended = recommendedKeys.Where(key => !currentRecommended.Contains(key)).OrderBy(key => key).ToList(),
                NoLongerRecommended = currentRecommended.Where(key => !recommendedKeys.Contains(key)).OrderBy(key => key).ToList(),
                AdministratorOverrides = configured.Where(item => item.OverrideState == "administrator").Select(item => item.CapabilityKey).OrderBy(key => key).ToList()
            };
        }

        public async Task<BusinessActivityResult<BusinessActivitySelection>> SaveAsync(SaveBusinessActivitiesCommand command, CancellationToken ct)
        {
            if (command == null) return BusinessActivityResult<BusinessActivitySelection>.Fail("validation", "بيانات النشاط مطلوبة.");
            string validationError;
            var definitions = NormalizeAndValidate(command.ActivityKeys, true, out validationError);
            if (validationError != null) return BusinessActivityResult<BusinessActivitySelection>.Fail("invalid_activity", validationError);
            var keys = definitions.Select(item => item.Key).ToList();
            var primary = BusinessActivityRegistry.Find(command.PrimaryActivityKey);
            if (primary == null || !keys.Contains(primary.Key)) return BusinessActivityResult<BusinessActivitySelection>.Fail("invalid_primary_activity", "يجب اختيار نشاط رئيسي من الأنشطة المحددة.");
            await _store.SaveSelectedAsync(keys, primary.Key, command.ChangedByUserId, ct).ConfigureAwait(false);
            if (command.ApplyRecommendations)
            {
                var recommendations = Recommend(keys);
                if (_featureRecommendations != null) await _featureRecommendations.ApplyAsync(recommendations, command.ChangedByUserId, ct).ConfigureAwait(false);
                else await _store.ApplyRecommendationsAsync(recommendations, command.ChangedByUserId, ct).ConfigureAwait(false);
            }
            return BusinessActivityResult<BusinessActivitySelection>.Ok(await GetSelectedAsync(ct).ConfigureAwait(false));
        }

        private static IReadOnlyList<BusinessActivityDefinition> NormalizeAndValidate(IEnumerable<string> keys, bool requireAny, out string error)
        {
            error = null;
            var normalized = (keys ?? Enumerable.Empty<string>()).Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (requireAny && normalized.Count == 0) { error = "اختر نشاطًا تجاريًا واحدًا على الأقل."; return new List<BusinessActivityDefinition>(); }
            var result = new List<BusinessActivityDefinition>();
            foreach (var key in normalized)
            {
                var definition = BusinessActivityRegistry.Find(key);
                if (definition == null || !definition.IsActive) { error = "نوع النشاط غير معروف: " + key; return new List<BusinessActivityDefinition>(); }
                result.Add(definition);
            }
            return result;
        }
    }
}
