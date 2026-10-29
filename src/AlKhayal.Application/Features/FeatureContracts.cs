using System;
using System.Collections.Generic;

namespace AlKhayal.Application.Features
{
    public sealed class FeatureStateConflictException : Exception
    {
        public FeatureStateConflictException(string message) : base(message) { }
    }
    public static class FeatureKinds
    {
        public const string Module = "module";
        public const string Capability = "capability";
    }

    public sealed class FeatureDefinition
    {
        public string Key { get; set; }
        public string Kind { get; set; }
        public string ArabicName { get; set; }
        public string Description { get; set; }
        public bool IsCore { get; set; }
        public bool DefaultEnabled { get; set; }
        public IReadOnlyList<string> Dependencies { get; set; }
    }

    public sealed class StoredFeatureState
    {
        public string Key { get; set; }
        public bool IsEnabled { get; set; }
        public string Source { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public sealed class EffectiveFeature
    {
        public string Key { get; set; }
        public string Kind { get; set; }
        public string ArabicName { get; set; }
        public string Description { get; set; }
        public bool IsCore { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsRecommended { get; set; }
        public string Source { get; set; }
        public IReadOnlyList<string> Dependencies { get; set; }
        public IReadOnlyList<string> RecommendedByActivities { get; set; }
    }

    public sealed class FeatureSnapshot
    {
        public IReadOnlyList<EffectiveFeature> Modules { get; set; }
        public IReadOnlyList<EffectiveFeature> Capabilities { get; set; }
        public DateTime GeneratedUtc { get; set; }
    }

    public sealed class SetFeatureCommand
    {
        public string Key { get; set; }
        public string Kind { get; set; }
        public bool IsEnabled { get; set; }
        public long ChangedByUserId { get; set; }
    }

    public sealed class ApplyFeatureRecommendationsCommand
    {
        public IReadOnlyList<string> ActivityKeys { get; set; }
        public long ChangedByUserId { get; set; }
    }

    public sealed class ProductCapabilityApplicability
    {
        public string CapabilityKey { get; set; }
        public bool IsGloballyEnabled { get; set; }
        public bool IsApplicableToProduct { get; set; }
        public string State { get { return !IsGloballyEnabled ? "globally-disabled" : IsApplicableToProduct ? "enabled-and-applicable" : "enabled-not-applicable"; } }
    }

    public sealed class FeatureResult<T>
    {
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public T Value { get; set; }
        public static FeatureResult<T> Ok(T value) { return new FeatureResult<T> { Succeeded = true, Value = value }; }
        public static FeatureResult<T> Fail(string code, string message) { return new FeatureResult<T> { ErrorCode = code, ErrorMessage = message }; }
    }
}
