using System;
using System.Collections.Generic;

namespace AlKhayal.Application.BusinessActivities
{
    public sealed class BusinessActivityDefinition
    {
        public string Key { get; set; }
        public string ArabicName { get; set; }
        public string Description { get; set; }
        public int DefaultPriority { get; set; }
        public bool IsActive { get; set; }
        public IReadOnlyList<string> SuggestedCapabilities { get; set; }
    }

    public sealed class SelectedBusinessActivity
    {
        public string ActivityKey { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime SelectedUtc { get; set; }
    }

    public sealed class SaveBusinessActivitiesCommand
    {
        public IReadOnlyList<string> ActivityKeys { get; set; }
        public string PrimaryActivityKey { get; set; }
        public long ChangedByUserId { get; set; }
        public bool ApplyRecommendations { get; set; }
    }

    public sealed class CapabilityRecommendation
    {
        public string CapabilityKey { get; set; }
        public bool SuggestedEnabled { get; set; }
        public string Reason { get; set; }
        public IReadOnlyList<string> ActivitySources { get; set; }
    }

    public sealed class CapabilityConfiguration
    {
        public string CapabilityKey { get; set; }
        public bool IsEnabled { get; set; }
        public string OverrideState { get; set; }
    }

    public sealed class RecommendationComparison
    {
        public IReadOnlyList<CapabilityRecommendation> Recommendations { get; set; }
        public IReadOnlyList<string> NewlyRecommended { get; set; }
        public IReadOnlyList<string> NoLongerRecommended { get; set; }
        public IReadOnlyList<string> AdministratorOverrides { get; set; }
    }

    public sealed class BusinessActivitySelection
    {
        public bool IsConfigured { get; set; }
        public string PrimaryActivityKey { get; set; }
        public IReadOnlyList<string> ActivityKeys { get; set; }
    }

    public sealed class BusinessActivityResult<T>
    {
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public T Value { get; set; }
        public static BusinessActivityResult<T> Ok(T value) { return new BusinessActivityResult<T> { Succeeded = true, Value = value }; }
        public static BusinessActivityResult<T> Fail(string code, string message) { return new BusinessActivityResult<T> { ErrorCode = code, ErrorMessage = message }; }
    }
}
