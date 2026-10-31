using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// The settings one environment contributes to the inheritance chain. The environment id is
    /// the same value a <see cref="WorkflowKey"/> uses as its scope, so a watchlist written for one
    /// environment can never become the active list of another.
    /// </summary>
    public sealed class EnvironmentProfile
    {
        public EnvironmentProfile(
            string? environmentId,
            string? activeWatchlistId,
            RefreshInterval? autoRefresh,
            MonitoringDateRange? dateRange,
            DateTimeOffset? lastUsedUtc)
        {
            EnvironmentId = (environmentId ?? string.Empty).Trim();
            ActiveWatchlistId = string.IsNullOrWhiteSpace(activeWatchlistId) ? null : activeWatchlistId!.Trim();
            AutoRefresh = autoRefresh;
            DateRange = dateRange;
            LastUsedUtc = lastUsedUtc;
        }

        /// <summary>Environment id, or empty for "not connected to a known environment".</summary>
        public string EnvironmentId { get; }

        /// <summary>The watchlist this environment had selected, or null when none was stored.</summary>
        public string? ActiveWatchlistId { get; }

        /// <summary>Environment-level auto-refresh override.</summary>
        public RefreshInterval? AutoRefresh { get; }

        /// <summary>Environment-level date-range override.</summary>
        public MonitoringDateRange? DateRange { get; }

        /// <summary>When this environment was last used with the tool. UTC; null when never.</summary>
        public DateTimeOffset? LastUsedUtc { get; }

        public EnvironmentProfile WithActiveWatchlist(string? activeWatchlistId)
        {
            return new EnvironmentProfile(EnvironmentId, activeWatchlistId, AutoRefresh, DateRange, LastUsedUtc);
        }

        public EnvironmentProfile WithOverrides(RefreshInterval? autoRefresh, MonitoringDateRange? dateRange)
        {
            return new EnvironmentProfile(EnvironmentId, ActiveWatchlistId, autoRefresh, dateRange, LastUsedUtc);
        }

        public EnvironmentProfile WithLastUsed(DateTimeOffset? lastUsedUtc)
        {
            return new EnvironmentProfile(EnvironmentId, ActiveWatchlistId, AutoRefresh, DateRange, lastUsedUtc);
        }

        public override string ToString()
        {
            return EnvironmentId.Length == 0 ? "no environment" : EnvironmentId;
        }
    }

    /// <summary>
    /// The plugin's global settings: what applies when nothing more specific is stored, plus the
    /// per-environment profiles. It carries no connection string, no token and no tenant url - the
    /// XrmToolBox connection owns those. An environment id is an identifier rather than a secret,
    /// and it is still only stored where per-environment scoping needs it.
    /// </summary>
    public sealed class ApplicationSettings
    {
        public ApplicationSettings(
            string? activeEnvironmentId,
            RefreshInterval defaultAutoRefresh,
            MonitoringDateRange defaultDateRange,
            bool defaultShowExcludedMembers,
            DurationThresholds? defaultThresholds,
            IEnumerable<EnvironmentProfile>? environments)
        {
            ActiveEnvironmentId = (activeEnvironmentId ?? string.Empty).Trim();
            DefaultAutoRefresh = defaultAutoRefresh;
            DefaultDateRange = defaultDateRange;
            DefaultShowExcludedMembers = defaultShowExcludedMembers;
            DefaultThresholds = defaultThresholds ?? DurationThresholds.Empty;

            List<EnvironmentProfile> profiles = new List<EnvironmentProfile>();
            if (environments != null)
            {
                foreach (EnvironmentProfile profile in environments)
                {
                    if (profile == null || Find(profiles, profile.EnvironmentId) != null)
                    {
                        continue;
                    }

                    profiles.Add(profile);
                }
            }

            Environments = profiles;
        }

        /// <summary>
        /// First-run settings: no auto-refresh (the plugin must never start polling by itself),
        /// a one-hour window, and excluded members visible so an operator can re-include a flow.
        /// </summary>
        public static ApplicationSettings Defaults
        {
            get { return new ApplicationSettings(null, RefreshInterval.Off, MonitoringDateRanges.Default, true, null, null); }
        }

        /// <summary>The environment the plugin last had a connection to. Empty when never connected.</summary>
        public string ActiveEnvironmentId { get; }

        public RefreshInterval DefaultAutoRefresh { get; }

        public MonitoringDateRange DefaultDateRange { get; }

        public bool DefaultShowExcludedMembers { get; }

        /// <summary>Bottom of the inheritance chain for duration thresholds.</summary>
        public DurationThresholds DefaultThresholds { get; }

        /// <summary>Per-environment profiles, de-duplicated by environment id.</summary>
        public IReadOnlyList<EnvironmentProfile> Environments { get; }

        public EnvironmentProfile? ProfileFor(string? environmentId)
        {
            return Find(Environments, environmentId);
        }

        /// <summary>The watchlist selected for one environment, or null when nothing is stored.</summary>
        public string? ActiveWatchlistIdFor(string? environmentId)
        {
            EnvironmentProfile? profile = ProfileFor(environmentId);
            return profile == null ? null : profile.ActiveWatchlistId;
        }

        public ApplicationSettings WithProfile(EnvironmentProfile profile, string? activeEnvironmentId = null)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            List<EnvironmentProfile> profiles = new List<EnvironmentProfile>(Environments);
            for (int i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i].EnvironmentId, profile.EnvironmentId, StringComparison.OrdinalIgnoreCase))
                {
                    profiles[i] = profile;
                    return Copy(profiles, activeEnvironmentId);
                }
            }

            profiles.Add(profile);
            return Copy(profiles, activeEnvironmentId);
        }

        /// <summary>Marks an environment as the one in use and records when it was touched.</summary>
        public ApplicationSettings WithActiveEnvironment(string? environmentId, DateTimeOffset usedAtUtc)
        {
            string scope = (environmentId ?? string.Empty).Trim();
            EnvironmentProfile profile = ProfileFor(scope) ?? new EnvironmentProfile(scope, null, null, null, null);
            return WithProfile(profile.WithLastUsed(usedAtUtc.ToUniversalTime()), scope);
        }

        /// <summary>
        /// Records which watchlist is selected for one environment. Only that environment's profile
        /// changes, which is what stops two environments from overwriting each other's selection.
        /// </summary>
        public ApplicationSettings WithActiveWatchlist(string? environmentId, string? watchlistId, DateTimeOffset usedAtUtc)
        {
            string scope = (environmentId ?? string.Empty).Trim();
            EnvironmentProfile profile = ProfileFor(scope) ?? new EnvironmentProfile(scope, null, null, null, null);
            return WithProfile(profile.WithActiveWatchlist(watchlistId).WithLastUsed(usedAtUtc.ToUniversalTime()));
        }

        public ApplicationSettings WithDefaults(
            RefreshInterval? defaultAutoRefresh,
            MonitoringDateRange? defaultDateRange,
            bool? defaultShowExcludedMembers,
            DurationThresholds? defaultThresholds)
        {
            return new ApplicationSettings(
                ActiveEnvironmentId,
                defaultAutoRefresh ?? DefaultAutoRefresh,
                defaultDateRange ?? DefaultDateRange,
                defaultShowExcludedMembers ?? DefaultShowExcludedMembers,
                defaultThresholds ?? DefaultThresholds,
                Environments);
        }

        /// <summary>
        /// Clears every environment selection still pointing at a watchlist that has been deleted.
        /// A stale id must not survive a delete: on the next restart it would either select nothing
        /// or, worse, match a newly created list that happened to reuse the id.
        /// </summary>
        public ApplicationSettings WithoutActiveWatchlist(string? watchlistId)
        {
            if (string.IsNullOrWhiteSpace(watchlistId))
            {
                return this;
            }

            List<EnvironmentProfile> profiles = new List<EnvironmentProfile>();
            bool changed = false;
            foreach (EnvironmentProfile profile in Environments)
            {
                if (string.Equals(profile.ActiveWatchlistId, watchlistId, StringComparison.OrdinalIgnoreCase))
                {
                    profiles.Add(profile.WithActiveWatchlist(null));
                    changed = true;
                }
                else
                {
                    profiles.Add(profile);
                }
            }

            if (!changed)
            {
                return this;
            }

            return new ApplicationSettings(
                ActiveEnvironmentId,
                DefaultAutoRefresh,
                DefaultDateRange,
                DefaultShowExcludedMembers,
                DefaultThresholds,
                profiles);
        }

        public string Describe()
        {
            return "defaults: " + RefreshIntervals.Describe(DefaultAutoRefresh)
                + ", window " + MonitoringDateRanges.Label(DefaultDateRange)
                + ", " + DefaultThresholds.Describe()
                + ", " + Environments.Count.ToString(CultureInfo.InvariantCulture) + " environment profile(s)";
        }

        private static EnvironmentProfile? Find(IEnumerable<EnvironmentProfile> profiles, string? environmentId)
        {
            string scope = (environmentId ?? string.Empty).Trim();
            foreach (EnvironmentProfile profile in profiles)
            {
                if (string.Equals(profile.EnvironmentId, scope, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return null;
        }

        private ApplicationSettings Copy(List<EnvironmentProfile> profiles, string? activeEnvironmentId)
        {
            return new ApplicationSettings(
                activeEnvironmentId ?? ActiveEnvironmentId,
                DefaultAutoRefresh,
                DefaultDateRange,
                DefaultShowExcludedMembers,
                DefaultThresholds,
                profiles);
        }
    }
}
