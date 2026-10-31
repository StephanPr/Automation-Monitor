using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// The persistence shape of everything the plugin stores locally. These are deliberately not the
    /// domain models: the file format is a contract that has to stay stable across releases while
    /// <c>Models/</c> is free to change. Every timestamp here is a string because the framework
    /// serializer renders <see cref="DateTimeOffset"/> as a legacy <c>/Date(ms)/</c> object rather
    /// than ISO 8601, and <see cref="SettingsFormat"/> converts between the two.
    /// </summary>
    [DataContract(Name = "settings", Namespace = "")]
    public sealed class SettingsDocument
    {
        /// <summary>
        /// Required. A document without it, or with a value this build cannot handle, is refused
        /// rather than partially understood.
        /// </summary>
        [DataMember(Name = "schemaVersion", IsRequired = true, Order = 0)]
        public int SchemaVersion { get; set; }

        /// <summary>Which payload this file carries, so a wrong file is named instead of merged.</summary>
        [DataMember(Name = "kind", Order = 1)]
        public string? Kind { get; set; }

        /// <summary>When this file was written, ISO 8601 UTC. Diagnostic only; nothing compares to it.</summary>
        [DataMember(Name = "savedAtUtc", Order = 2)]
        public string? SavedAtUtc { get; set; }

        /// <summary>The product name that wrote the file. Diagnostic, and never trusted as authority.</summary>
        [DataMember(Name = "writtenBy", Order = 3)]
        public string? WrittenBy { get; set; }

        [DataMember(Name = "app", Order = 4)]
        public ApplicationSettingsDocument? App { get; set; }

        [DataMember(Name = "watchlists", Order = 5)]
        public WatchlistFileDocument? Watchlists { get; set; }
    }

    /// <summary>Global settings: the defaults at the bottom of the inheritance chain.</summary>
    [DataContract(Name = "app", Namespace = "")]
    public sealed class ApplicationSettingsDocument
    {
        [DataMember(Name = "activeEnvironmentId", Order = 0)]
        public string? ActiveEnvironmentId { get; set; }

        [DataMember(Name = "defaultAutoRefresh", Order = 1)]
        public string? DefaultAutoRefresh { get; set; }

        [DataMember(Name = "defaultDateRange", Order = 2)]
        public string? DefaultDateRange { get; set; }

        [DataMember(Name = "defaultShowExcludedMembers", Order = 3)]
        public bool? DefaultShowExcludedMembers { get; set; }

        [DataMember(Name = "defaultThresholds", Order = 4)]
        public DurationThresholdsDocument? DefaultThresholds { get; set; }

        [DataMember(Name = "environments", Order = 5)]
        public List<EnvironmentProfileDocument>? Environments { get; set; }
    }

    /// <summary>One environment's stored selection and overrides.</summary>
    [DataContract(Name = "environment", Namespace = "")]
    public sealed class EnvironmentProfileDocument
    {
        [DataMember(Name = "environmentId", Order = 0)]
        public string? EnvironmentId { get; set; }

        [DataMember(Name = "activeWatchlistId", Order = 1)]
        public string? ActiveWatchlistId { get; set; }

        [DataMember(Name = "autoRefresh", Order = 2)]
        public string? AutoRefresh { get; set; }

        [DataMember(Name = "dateRange", Order = 3)]
        public string? DateRange { get; set; }

        [DataMember(Name = "lastUsedUtc", Order = 4)]
        public string? LastUsedUtc { get; set; }
    }

    /// <summary>Duration thresholds as stored. Null means "not set at this level", never zero.</summary>
    [DataContract(Name = "thresholds", Namespace = "")]
    public sealed class DurationThresholdsDocument
    {
        [DataMember(Name = "slowCompletedSeconds", Order = 0)]
        public long? SlowCompletedSeconds { get; set; }

        [DataMember(Name = "maximumRunningSeconds", Order = 1)]
        public long? MaximumRunningSeconds { get; set; }
    }

    /// <summary>The watchlist file payload.</summary>
    [DataContract(Name = "watchlistFile", Namespace = "")]
    public sealed class WatchlistFileDocument
    {
        /// <summary>Selection not tied to one environment. Per-environment selections live in <c>app</c>.</summary>
        [DataMember(Name = "activeWatchlistId", Order = 0)]
        public string? ActiveWatchlistId { get; set; }

        [DataMember(Name = "items", Order = 1)]
        public List<WatchlistDocument>? Items { get; set; }
    }

    /// <summary>One saved watchlist.</summary>
    [DataContract(Name = "watchlist", Namespace = "")]
    public sealed class WatchlistDocument
    {
        /// <summary>Stable id. Never derived from the name, and never re-generated on rename.</summary>
        [DataMember(Name = "id", IsRequired = true, Order = 0)]
        public string? Id { get; set; }

        [DataMember(Name = "name", Order = 1)]
        public string? Name { get; set; }

        [DataMember(Name = "description", Order = 2)]
        public string? Description { get; set; }

        /// <summary>Environment scope. Empty means the list is not tied to one environment.</summary>
        [DataMember(Name = "environmentId", Order = 3)]
        public string? EnvironmentId { get; set; }

        /// <summary><c>snapshot</c>, <c>dynamic</c> or <c>manual</c>. Decides what a refresh may change.</summary>
        [DataMember(Name = "membershipMode", Order = 4)]
        public string? MembershipMode { get; set; }

        [DataMember(Name = "createdAtUtc", Order = 5)]
        public string? CreatedAtUtc { get; set; }

        [DataMember(Name = "modifiedAtUtc", Order = 6)]
        public string? ModifiedAtUtc { get; set; }

        [DataMember(Name = "lastMembershipRefreshUtc", Order = 7)]
        public string? LastMembershipRefreshUtc { get; set; }

        /// <summary>Solutions a dynamic list reconciles against, in the order they were added.</summary>
        [DataMember(Name = "sourceSolutionIds", Order = 8)]
        public List<string>? SourceSolutionIds { get; set; }

        [DataMember(Name = "members", Order = 9)]
        public List<WatchlistMemberDocument>? Members { get; set; }

        /// <summary>Coverage of the discovery that last filled this list, so a partial read cannot heal itself.</summary>
        [DataMember(Name = "coverageState", Order = 10)]
        public string? CoverageState { get; set; }

        [DataMember(Name = "coverageNotes", Order = 11)]
        public List<string>? CoverageNotes { get; set; }

        [DataMember(Name = "preferences", Order = 12)]
        public WatchlistPreferencesDocument? Preferences { get; set; }
    }

    /// <summary>
    /// One stored member. Workflow identity is written as its three parts - provider, scope and the
    /// provider's own id - instead of one canonical string, because a provider id may itself contain
    /// the separator and a stored identity must never be reconstructed by splitting text.
    /// </summary>
    [DataContract(Name = "member", Namespace = "")]
    public sealed class WatchlistMemberDocument
    {
        [DataMember(Name = "provider", Order = 0)]
        public string? Provider { get; set; }

        [DataMember(Name = "scopeId", Order = 1)]
        public string? ScopeId { get; set; }

        [DataMember(Name = "workflowId", Order = 2)]
        public string? WorkflowId { get; set; }

        /// <summary>Display name only. It never takes part in identity or de-duplication.</summary>
        [DataMember(Name = "name", Order = 3)]
        public string? Name { get; set; }

        /// <summary>Stored enablement state, so the list is still informative while disconnected.</summary>
        [DataMember(Name = "state", Order = 4)]
        public string? State { get; set; }

        /// <summary>Raw provider state code, kept beside the normalized value for diagnosis.</summary>
        [DataMember(Name = "stateCode", Order = 5)]
        public int? StateCode { get; set; }

        /// <summary>Null means the file did not say; the loader treats that as included and reports it.</summary>
        [DataMember(Name = "included", Order = 6)]
        public bool? Included { get; set; }

        [DataMember(Name = "addedAtUtc", Order = 7)]
        public string? AddedAtUtc { get; set; }

        [DataMember(Name = "lastSeenUtc", Order = 8)]
        public string? LastSeenUtc { get; set; }

        /// <summary><c>solution</c> and/or <c>manual</c>: why this flow is in the list.</summary>
        [DataMember(Name = "origins", Order = 9)]
        public List<string>? Origins { get; set; }

        /// <summary>Which solution components produced this member, for reconciliation.</summary>
        [DataMember(Name = "solutionIds", Order = 10)]
        public List<string>? SolutionIds { get; set; }
    }

    /// <summary>Watchlist-level preferences. Null means "inherit", not "off" or "empty".</summary>
    [DataContract(Name = "preferences", Namespace = "")]
    public sealed class WatchlistPreferencesDocument
    {
        [DataMember(Name = "autoRefresh", Order = 0)]
        public string? AutoRefresh { get; set; }

        [DataMember(Name = "dateRange", Order = 1)]
        public string? DateRange { get; set; }

        [DataMember(Name = "showExcludedMembers", Order = 2)]
        public bool? ShowExcludedMembers { get; set; }

        [DataMember(Name = "thresholds", Order = 3)]
        public DurationThresholdsDocument? Thresholds { get; set; }
    }
}
