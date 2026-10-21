using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.BusinessActivities;

namespace AlKhayal.Application.Features
{
    public sealed class FeatureService : IFeatureRecommendationApplier
    {
        private readonly IFeatureStore _store;
        private readonly IBusinessActivityStore _activities;
        public FeatureService(IFeatureStore store, IBusinessActivityStore activities) { _store = store; _activities = activities; }

        public IReadOnlyList<FeatureDefinition> ListAll() { return FeatureRegistry.All; }

        public async Task<FeatureSnapshot> GetEffectiveAsync(CancellationToken ct)
        {
            var modules = await _store.GetModuleStatesAsync(ct).ConfigureAwait(false);
            var capabilities = await _store.GetCapabilityStatesAsync(ct).ConfigureAwait(false);
            var selected = await _activities.GetSelectedAsync(ct).ConfigureAwait(false);
            var recommendations = new BusinessActivityService(_activities).Recommend(selected.Select(item => item.ActivityKey));
            var recommended = BuildRecommendationSources(recommendations);
            return new FeatureSnapshot
            {
                Modules = Resolve(FeatureKinds.Module, modules, recommended),
                Capabilities = Resolve(FeatureKinds.Capability, capabilities, recommended),
                GeneratedUtc = DateTime.UtcNow
            };
        }

        public async Task<FeatureSnapshot> PreviewAsync(IEnumerable<string> activityKeys, CancellationToken ct)
        {
            var recommendations = new BusinessActivityService(_activities).Recommend(activityKeys);
            var modules = await _store.GetModuleStatesAsync(ct).ConfigureAwait(false);
            var capabilities = await _store.GetCapabilityStatesAsync(ct).ConfigureAwait(false);
            var recommended = BuildRecommendationSources(recommendations);
            return new FeatureSnapshot { Modules = Resolve(FeatureKinds.Module, modules, recommended), Capabilities = Resolve(FeatureKinds.Capability, capabilities, recommended), GeneratedUtc = DateTime.UtcNow };
        }

        public async Task<FeatureResult<FeatureSnapshot>> SetAsync(SetFeatureCommand command, CancellationToken ct)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Key)) return FeatureResult<FeatureSnapshot>.Fail("validation", "مفتاح الميزة والحالة مطلوبان.");
            var definition = FeatureRegistry.Find(command.Key);
            if (definition == null || !string.Equals(definition.Kind, command.Kind, StringComparison.OrdinalIgnoreCase)) return FeatureResult<FeatureSnapshot>.Fail("unknown_feature", "الميزة المطلوبة غير معروفة.");
            if (definition.IsCore && !command.IsEnabled) return FeatureResult<FeatureSnapshot>.Fail("core_feature", "لا يمكن تعطيل وظيفة أساسية في النظام.");
            var snapshot = await GetEffectiveAsync(ct).ConfigureAwait(false);
            var all = snapshot.Modules.Concat(snapshot.Capabilities).ToList();
            if (command.IsEnabled)
            {
                var missing = definition.Dependencies.Where(key => !all.Any(item => item.Key == key && item.IsEnabled)).Select(Label).ToList();
                if (missing.Count > 0) return FeatureResult<FeatureSnapshot>.Fail("missing_dependency", "فعّل أولًا: " + string.Join("، ", missing));
            }
            else
            {
                var dependents = FeatureRegistry.All.Where(item => item.Dependencies.Contains(definition.Key) && all.Any(current => current.Key == item.Key && current.IsEnabled)).Select(item => item.ArabicName).ToList();
                if (dependents.Count > 0) return FeatureResult<FeatureSnapshot>.Fail("dependent_feature", "لا يمكن التعطيل لأن الميزات التالية تعتمد عليها: " + string.Join("، ", dependents));
            }
            await _store.SetStateAsync(definition.Kind, definition.Key, command.IsEnabled, command.ChangedByUserId, ct).ConfigureAwait(false);
            return FeatureResult<FeatureSnapshot>.Ok(await GetEffectiveAsync(ct).ConfigureAwait(false));
        }

        public async Task ApplyAsync(IReadOnlyList<CapabilityRecommendation> recommendations, long changedByUserId, CancellationToken ct)
        {
            var expanded = ExpandRecommendationKeys(recommendations.Select(item => item.CapabilityKey));
            await _store.ApplyRecommendationsAsync(expanded.Where(IsModule).ToList(), expanded.Where(key => !IsModule(key)).ToList(), changedByUserId, ct).ConfigureAwait(false);
        }

        public async Task<FeatureResult<FeatureSnapshot>> ApplyAsync(ApplyFeatureRecommendationsCommand command, CancellationToken ct)
        {
            if (command == null) return FeatureResult<FeatureSnapshot>.Fail("validation", "بيانات التوصيات مطلوبة.");
            IReadOnlyList<CapabilityRecommendation> recommendations;
            try { recommendations = new BusinessActivityService(_activities).Recommend(command.ActivityKeys ?? new string[0]); }
            catch (ArgumentException exception) { return FeatureResult<FeatureSnapshot>.Fail("invalid_activity", exception.Message); }
            await ApplyAsync(recommendations, command.ChangedByUserId, ct).ConfigureAwait(false);
            return FeatureResult<FeatureSnapshot>.Ok(await GetEffectiveAsync(ct).ConfigureAwait(false));
        }

        private static IReadOnlyList<EffectiveFeature> Resolve(string kind, IReadOnlyList<StoredFeatureState> stored, IDictionary<string, IReadOnlyList<string>> recommended)
        {
            return FeatureRegistry.All.Where(item => item.Kind == kind).Select(definition =>
            {
                var state = stored.FirstOrDefault(item => string.Equals(item.Key, definition.Key, StringComparison.OrdinalIgnoreCase));
                IReadOnlyList<string> sources;
                if (!recommended.TryGetValue(definition.Key, out sources)) sources = new string[0];
                return new EffectiveFeature { Key = definition.Key, Kind = definition.Kind, ArabicName = definition.ArabicName, Description = definition.Description, IsCore = definition.IsCore, IsEnabled = definition.IsCore || (state == null ? definition.DefaultEnabled : state.IsEnabled), Source = definition.IsCore ? "core" : state == null ? "default" : state.Source, IsRecommended = sources.Count > 0, Dependencies = definition.Dependencies, RecommendedByActivities = sources };
            }).ToList();
        }

        private static IDictionary<string, IReadOnlyList<string>> BuildRecommendationSources(IEnumerable<CapabilityRecommendation> recommendations)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in recommendations)
            {
                foreach (var key in ExpandRecommendationKeys(new[] { item.CapabilityKey })) result[key] = item.ActivitySources;
            }
            return result;
        }

        private static IReadOnlyList<string> ExpandRecommendationKeys(IEnumerable<string> keys)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                if (FeatureRegistry.Find(key) != null) result.Add(key);
                if (key == "barcode-labels") result.Add("barcode-label-printing");
            }
            bool changed;
            do
            {
                changed = false;
                foreach (var key in result.ToList())
                {
                    var definition = FeatureRegistry.Find(key);
                    if (definition == null) continue;
                    foreach (var dependency in definition.Dependencies) if (result.Add(dependency)) changed = true;
                }
            } while (changed);
            return result.OrderBy(key => key, StringComparer.Ordinal).ToList();
        }

        private static bool IsModule(string key) { var definition = FeatureRegistry.Find(key); return definition != null && definition.Kind == FeatureKinds.Module; }
        private static string Label(string key) { var item = FeatureRegistry.Find(key); return item == null ? key : item.ArabicName; }
    }
}
