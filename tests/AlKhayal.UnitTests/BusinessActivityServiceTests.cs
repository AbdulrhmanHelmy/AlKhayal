using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.BusinessActivities;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class BusinessActivityServiceTests
    {
        [Fact]
        public void Single_and_multiple_activity_recommendations_are_deterministic_and_track_sources()
        {
            var service = new BusinessActivityService(new MemoryStore());
            var clothing = service.Recommend(new[] { "clothing" });
            Assert.Contains(clothing, item => item.CapabilityKey == "sizes" && item.ActivitySources.SequenceEqual(new[] { "clothing" }));
            var combined = service.Recommend(new[] { "clothing", "footwear" });
            Assert.Equal(combined.Count, combined.Select(item => item.CapabilityKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            var sizes = Assert.Single(combined, item => item.CapabilityKey == "sizes");
            Assert.Equal(new[] { "clothing", "footwear" }, sizes.ActivitySources);
        }

        [Fact]
        public async Task Saving_changes_primary_and_preserves_administrator_overrides()
        {
            var store = new MemoryStore();
            store.Configuration.Add(new CapabilityConfiguration { CapabilityKey = "sizes", IsEnabled = false, OverrideState = "administrator" });
            var service = new BusinessActivityService(store);
            var saved = await service.SaveAsync(new SaveBusinessActivitiesCommand { ActivityKeys = new[] { "clothing", "footwear" }, PrimaryActivityKey = "clothing", ChangedByUserId = 7, ApplyRecommendations = true }, CancellationToken.None);
            Assert.True(saved.Succeeded);
            Assert.Equal("clothing", saved.Value.PrimaryActivityKey);
            Assert.False(Assert.Single(store.Configuration, item => item.CapabilityKey == "sizes").IsEnabled);
            saved = await service.SaveAsync(new SaveBusinessActivitiesCommand { ActivityKeys = new[] { "footwear" }, PrimaryActivityKey = "footwear", ChangedByUserId = 7 }, CancellationToken.None);
            Assert.True(saved.Succeeded);
            Assert.Equal("footwear", saved.Value.PrimaryActivityKey);
            Assert.Contains(store.Configuration, item => item.CapabilityKey == "sizes");
        }

        [Fact]
        public async Task Invalid_activity_and_primary_are_rejected()
        {
            var service = new BusinessActivityService(new MemoryStore());
            Assert.Equal("invalid_activity", (await service.SaveAsync(new SaveBusinessActivitiesCommand { ActivityKeys = new[] { "unknown" }, PrimaryActivityKey = "unknown" }, CancellationToken.None)).ErrorCode);
            Assert.Equal("invalid_primary_activity", (await service.SaveAsync(new SaveBusinessActivitiesCommand { ActivityKeys = new[] { "clothing" }, PrimaryActivityKey = "grocery" }, CancellationToken.None)).ErrorCode);
        }

        private sealed class MemoryStore : IBusinessActivityStore
        {
            public readonly List<SelectedBusinessActivity> Selected = new List<SelectedBusinessActivity>();
            public readonly List<CapabilityConfiguration> Configuration = new List<CapabilityConfiguration>();
            public Task<IReadOnlyList<SelectedBusinessActivity>> GetSelectedAsync(CancellationToken cancellationToken) { return Task.FromResult((IReadOnlyList<SelectedBusinessActivity>)Selected.ToList()); }
            public Task SaveSelectedAsync(IReadOnlyList<string> keys, string primary, long userId, CancellationToken cancellationToken) { Selected.Clear(); Selected.AddRange(keys.Select(key => new SelectedBusinessActivity { ActivityKey = key, IsPrimary = key == primary, SelectedUtc = DateTime.UtcNow })); return Task.CompletedTask; }
            public Task<IReadOnlyList<CapabilityConfiguration>> GetCapabilityConfigurationAsync(CancellationToken cancellationToken) { return Task.FromResult((IReadOnlyList<CapabilityConfiguration>)Configuration.ToList()); }
            public Task ApplyRecommendationsAsync(IReadOnlyList<CapabilityRecommendation> recommendations, long userId, CancellationToken cancellationToken)
            {
                foreach (var recommendation in recommendations)
                {
                    var current = Configuration.FirstOrDefault(item => item.CapabilityKey == recommendation.CapabilityKey);
                    if (current == null) Configuration.Add(new CapabilityConfiguration { CapabilityKey = recommendation.CapabilityKey, IsEnabled = true, OverrideState = "recommended" });
                    else if (current.OverrideState != "administrator") current.IsEnabled = true;
                }
                return Task.CompletedTask;
            }
        }
    }
}
