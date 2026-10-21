using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.BusinessActivities;
using AlKhayal.Application.Features;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class FeatureServiceTests
    {
        [Fact]
        public async Task Optional_module_can_be_enabled_disabled_and_restored_while_core_is_protected()
        {
            var store = new MemoryStore(); var service = new FeatureService(store, store);
            var defaults = await service.GetEffectiveAsync(CancellationToken.None);
            Assert.True(defaults.Modules.Single(item => item.Key == "sales").IsEnabled);
            Assert.False(defaults.Modules.Single(item => item.Key == "installments").IsEnabled);
            Assert.True((await service.SetAsync(Command("installments", FeatureKinds.Module, true), CancellationToken.None)).Succeeded);
            Assert.True((await service.GetEffectiveAsync(CancellationToken.None)).Modules.Single(item => item.Key == "installments").IsEnabled);
            Assert.True((await service.SetAsync(Command("installments", FeatureKinds.Module, false), CancellationToken.None)).Succeeded);
            Assert.True((await service.SetAsync(Command("installments", FeatureKinds.Module, true), CancellationToken.None)).Succeeded);
            Assert.Equal("core_feature", (await service.SetAsync(Command("sales", FeatureKinds.Module, false), CancellationToken.None)).ErrorCode);
            Assert.Equal("unknown_feature", (await service.SetAsync(Command("missing", FeatureKinds.Module, true), CancellationToken.None)).ErrorCode);
        }

        [Fact]
        public async Task Dependencies_are_validated_in_both_directions()
        {
            var store = new MemoryStore(); var service = new FeatureService(store, store);
            Assert.Equal("missing_dependency", (await service.SetAsync(Command("imei-tracking", FeatureKinds.Capability, true), CancellationToken.None)).ErrorCode);
            Assert.True((await service.SetAsync(Command("serial-numbers", FeatureKinds.Capability, true), CancellationToken.None)).Succeeded);
            Assert.True((await service.SetAsync(Command("imei-tracking", FeatureKinds.Capability, true), CancellationToken.None)).Succeeded);
            Assert.Equal("dependent_feature", (await service.SetAsync(Command("serial-numbers", FeatureKinds.Capability, false), CancellationToken.None)).ErrorCode);
        }

        [Fact]
        public async Task Multiple_activity_recommendations_enable_modules_and_capabilities_but_preserve_admin_overrides()
        {
            var store = new MemoryStore(); store.Selected.Add(Activity("clothing", true)); store.Selected.Add(Activity("mobile-phones", false));
            store.Capabilities.Add(new StoredFeatureState { Key = "sizes", IsEnabled = false, Source = "administrator" });
            var service = new FeatureService(store, store);
            var recommendations = new BusinessActivityService(store).Recommend(store.Selected.Select(item => item.ActivityKey));
            await service.ApplyAsync(recommendations, 7, CancellationToken.None);
            var snapshot = await service.GetEffectiveAsync(CancellationToken.None);
            Assert.False(snapshot.Capabilities.Single(item => item.Key == "sizes").IsEnabled);
            Assert.True(snapshot.Capabilities.Single(item => item.Key == "serial-numbers").IsEnabled);
            Assert.True(snapshot.Modules.Single(item => item.Key == "maintenance-management").IsEnabled);
            store.Selected.RemoveAll(item => item.ActivityKey == "mobile-phones");
            Assert.True((await service.GetEffectiveAsync(CancellationToken.None)).Modules.Single(item => item.Key == "maintenance-management").IsEnabled);
        }

        [Fact]
        public void Product_capability_contract_distinguishes_all_three_states()
        {
            Assert.Equal("globally-disabled", new ProductCapabilityApplicability { IsGloballyEnabled = false, IsApplicableToProduct = true }.State);
            Assert.Equal("enabled-not-applicable", new ProductCapabilityApplicability { IsGloballyEnabled = true, IsApplicableToProduct = false }.State);
            Assert.Equal("enabled-and-applicable", new ProductCapabilityApplicability { IsGloballyEnabled = true, IsApplicableToProduct = true }.State);
        }

        private static SetFeatureCommand Command(string key, string kind, bool enabled) { return new SetFeatureCommand { Key = key, Kind = kind, IsEnabled = enabled, ChangedByUserId = 7 }; }
        private static SelectedBusinessActivity Activity(string key, bool primary) { return new SelectedBusinessActivity { ActivityKey = key, IsPrimary = primary, SelectedUtc = DateTime.UtcNow }; }

        private sealed class MemoryStore : IFeatureStore, IBusinessActivityStore
        {
            public readonly List<StoredFeatureState> Modules = new List<StoredFeatureState>();
            public readonly List<StoredFeatureState> Capabilities = new List<StoredFeatureState>();
            public readonly List<SelectedBusinessActivity> Selected = new List<SelectedBusinessActivity>();
            public Task<IReadOnlyList<StoredFeatureState>> GetModuleStatesAsync(CancellationToken ct) { return Result(Modules); }
            public Task<IReadOnlyList<StoredFeatureState>> GetCapabilityStatesAsync(CancellationToken ct) { return Result(Capabilities); }
            public Task SetStateAsync(string kind, string key, bool enabled, long userId, CancellationToken ct) { Set(kind == FeatureKinds.Module ? Modules : Capabilities, key, enabled, "administrator"); return Task.CompletedTask; }
            public Task ApplyRecommendationsAsync(IReadOnlyList<string> moduleKeys, IReadOnlyList<string> capabilityKeys, long userId, CancellationToken ct) { foreach (var key in moduleKeys) Recommend(Modules, key); foreach (var key in capabilityKeys) Recommend(Capabilities, key); return Task.CompletedTask; }
            public Task<IReadOnlyList<SelectedBusinessActivity>> GetSelectedAsync(CancellationToken ct) { return Task.FromResult((IReadOnlyList<SelectedBusinessActivity>)Selected.ToList()); }
            public Task SaveSelectedAsync(IReadOnlyList<string> keys, string primary, long userId, CancellationToken ct) { return Task.CompletedTask; }
            public Task<IReadOnlyList<CapabilityConfiguration>> GetCapabilityConfigurationAsync(CancellationToken ct) { return Task.FromResult((IReadOnlyList<CapabilityConfiguration>)Capabilities.Select(item => new CapabilityConfiguration { CapabilityKey = item.Key, IsEnabled = item.IsEnabled, OverrideState = item.Source }).ToList()); }
            Task IBusinessActivityStore.ApplyRecommendationsAsync(IReadOnlyList<CapabilityRecommendation> values, long userId, CancellationToken ct) { return Task.CompletedTask; }
            private static Task<IReadOnlyList<StoredFeatureState>> Result(List<StoredFeatureState> values) { return Task.FromResult((IReadOnlyList<StoredFeatureState>)values.ToList()); }
            private static void Recommend(List<StoredFeatureState> values, string key) { var current = values.FirstOrDefault(item => item.Key == key); if (current == null) values.Add(new StoredFeatureState { Key = key, IsEnabled = true, Source = "recommended" }); else if (current.Source != "administrator") current.IsEnabled = true; }
            private static void Set(List<StoredFeatureState> values, string key, bool enabled, string source) { var current = values.FirstOrDefault(item => item.Key == key); if (current == null) values.Add(new StoredFeatureState { Key = key, IsEnabled = enabled, Source = source }); else { current.IsEnabled = enabled; current.Source = source; } }
        }
    }
}

