using System;
using System.Collections.Generic;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// The preferences that actually apply to a monitoring session, after inheritance, together with
    /// the level each value came from. The direction is the documented one -
    /// <c>global defaults -&gt; environment -&gt; watchlist</c>, most specific wins - and this is the
    /// only place it is implemented, so no view can pick a different winner.
    ///
    /// <para>
    /// This resolves <em>preferences</em>, not monitoring rules. Duration thresholds travel with it as
    /// configuration for M5's rule evaluator; nothing here decides that a run is slow, because adding
    /// that decision would create the second rule engine the plan forbids.
    /// </para>
    /// </summary>
    public sealed class EffectivePreferences
    {
        /// <summary>How the global level is named in a "where did this come from" message.</summary>
        public const string GlobalSource = "global default";

        public const string NoEnvironmentSource = "environment default";

        public const string WatchlistSource = "this watchlist";

        public EffectivePreferences(
            RefreshInterval autoRefresh,
            string autoRefreshSource,
            MonitoringDateRange dateRange,
            string dateRangeSource,
            bool showExcludedMembers,
            string showExcludedSource,
            DurationThresholds thresholds,
            string thresholdsSource)
        {
            AutoRefresh = autoRefresh;
            AutoRefreshSource = autoRefreshSource;
            DateRange = dateRange;
            DateRangeSource = dateRangeSource;
            ShowExcludedMembers = showExcludedMembers;
            ShowExcludedSource = showExcludedSource;
            Thresholds = thresholds ?? DurationThresholds.Empty;
            ThresholdsSource = thresholdsSource;
        }

        public RefreshInterval AutoRefresh { get; }

        public string AutoRefreshSource { get; }

        public MonitoringDateRange DateRange { get; }

        public string DateRangeSource { get; }

        public bool ShowExcludedMembers { get; }

        public string ShowExcludedSource { get; }

        public DurationThresholds Thresholds { get; }

        public string ThresholdsSource { get; }

        /// <summary>Whether a refresh timer should run at all once the coordinator exists.</summary>
        public bool IsAutoRefreshEnabled
        {
            get { return RefreshIntervals.IsAutoRefreshEnabled(AutoRefresh); }
        }

        /// <summary>Interval seconds that apply. Zero means "no timer", never "poll now".</summary>
        public int IntervalSeconds
        {
            get { return RefreshIntervals.Seconds(AutoRefresh); }
        }

        /// <summary>
        /// The window a session should query, ending at the caller's "now", limited to this list's
        /// included flows. The clock stays outside this type so a stored preference can be evaluated
        /// deterministically in a test.
        /// </summary>
        public MonitoringQuery BuildQuery(Watchlist? watchlist, DateTimeOffset endUtc, int maxRows = MonitoringQuery.DefaultMaxRows)
        {
            DateTimeOffset to = endUtc.ToUniversalTime();
            DateTimeOffset from = to.AddMinutes(-MonitoringDateRanges.Minutes(DateRange));

            List<WorkflowKey> keys = new List<WorkflowKey>();
            if (watchlist != null)
            {
                foreach (WorkflowReference flow in watchlist.IncludedFlows)
                {
                    keys.Add(flow.Key);
                }
            }

            return new MonitoringQuery(keys, from, to, null, maxRows);
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("refresh ").Append(RefreshIntervals.Label(AutoRefresh))
                .Append(" (").Append(AutoRefreshSource).Append(")")
                .Append(", window ").Append(MonitoringDateRanges.Label(DateRange))
                .Append(" (").Append(DateRangeSource).Append(")")
                .Append(", excluded members ").Append(ShowExcludedMembers ? "shown" : "hidden")
                .Append(" (").Append(ShowExcludedSource).Append(")")
                .Append(", ").Append(Thresholds.Describe()).Append(" (").Append(ThresholdsSource).Append(")");
            return text.ToString();
        }

        public override string ToString()
        {
            return Describe();
        }
    }

    /// <summary>Resolves the settings inheritance chain for one environment and watchlist.</summary>
    public static class PreferenceResolver
    {
        /// <summary>How the environment level is named in a message, using its id when one is known.</summary>
        public static string EnvironmentSource(string? environmentId)
        {
            return string.IsNullOrWhiteSpace(environmentId)
                ? EffectivePreferences.NoEnvironmentSource
                : "environment " + environmentId!.Trim() + " default";
        }

        /// <summary>
        /// Pick the value that applies. A null override at any level means "ask the level below", and
        /// the global settings are the last resort, so the answer is always a concrete value.
        /// </summary>
        public static EffectivePreferences Resolve(ApplicationSettings? settings, Watchlist? watchlist, string? environmentId)
        {
            ApplicationSettings global = settings ?? ApplicationSettings.Defaults;
            string environmentName = EnvironmentSource(environmentId);
            EnvironmentProfile? profile = global.ProfileFor(environmentId);
            WatchlistPreferences list = watchlist == null ? WatchlistPreferences.Inherited : watchlist.Preferences;

            RefreshInterval autoRefresh = global.DefaultAutoRefresh;
            string autoRefreshSource = EffectivePreferences.GlobalSource;
            if (profile != null && profile.AutoRefresh.HasValue)
            {
                autoRefresh = profile.AutoRefresh.Value;
                autoRefreshSource = environmentName;
            }

            if (list.AutoRefresh.HasValue)
            {
                autoRefresh = list.AutoRefresh.Value;
                autoRefreshSource = EffectivePreferences.WatchlistSource;
            }

            MonitoringDateRange dateRange = global.DefaultDateRange;
            string dateRangeSource = EffectivePreferences.GlobalSource;
            if (profile != null && profile.DateRange.HasValue)
            {
                dateRange = profile.DateRange.Value;
                dateRangeSource = environmentName;
            }

            if (list.DateRange.HasValue)
            {
                dateRange = list.DateRange.Value;
                dateRangeSource = EffectivePreferences.WatchlistSource;
            }

            bool showExcluded = global.DefaultShowExcludedMembers;
            string showExcludedSource = EffectivePreferences.GlobalSource;
            if (list.ShowExcludedMembers.HasValue)
            {
                showExcluded = list.ShowExcludedMembers.Value;
                showExcludedSource = EffectivePreferences.WatchlistSource;
            }

            DurationThresholds thresholds = global.DefaultThresholds;
            string thresholdsSource = EffectivePreferences.GlobalSource;
            if (!list.Thresholds.IsEmpty)
            {
                thresholds = list.Thresholds;
                thresholdsSource = EffectivePreferences.WatchlistSource;
            }

            return new EffectivePreferences(
                autoRefresh,
                autoRefreshSource,
                dateRange,
                dateRangeSource,
                showExcluded,
                showExcludedSource,
                thresholds,
                thresholdsSource);
        }
    }
}
