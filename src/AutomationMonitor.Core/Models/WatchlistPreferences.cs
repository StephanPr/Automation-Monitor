using System;
using System.Text;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// What one watchlist stores for the monitoring session it represents: refresh interval,
    /// default date range, membership display choice and duration-threshold overrides.
    ///
    /// <para>
    /// Every value is optional. Null means "inherit", which is what makes the documented
    /// inheritance <c>global defaults -&gt; environment -&gt; watchlist</c> representable: a level
    /// persists only what it really overrides, and <c>Services/PreferenceResolver.cs</c> is the
    /// only place the winner is picked.
    /// </para>
    /// </summary>
    public sealed class WatchlistPreferences
    {
        public WatchlistPreferences(RefreshInterval? autoRefresh, MonitoringDateRange? dateRange, bool? showExcludedMembers, DurationThresholds? thresholds)
        {
            AutoRefresh = autoRefresh;
            DateRange = dateRange;
            ShowExcludedMembers = showExcludedMembers;
            Thresholds = thresholds ?? DurationThresholds.Empty;
        }

        /// <summary>Nothing overridden: every preference comes from a level above.</summary>
        public static WatchlistPreferences Inherited
        {
            get { return new WatchlistPreferences(null, null, null, null); }
        }

        /// <summary>Auto-refresh override, or null to inherit. Off is a deliberate choice, not absence.</summary>
        public RefreshInterval? AutoRefresh { get; }

        /// <summary>Default window for a monitoring query, or null to inherit.</summary>
        public MonitoringDateRange? DateRange { get; }

        /// <summary>Whether the membership grid lists excluded flows, or null to inherit.</summary>
        public bool? ShowExcludedMembers { get; }

        /// <summary>Duration-threshold overrides. Never null; <see cref="DurationThresholds.Empty"/> means none.</summary>
        public DurationThresholds Thresholds { get; }

        /// <summary>True when this watchlist overrides nothing at all.</summary>
        public bool IsInherited
        {
            get { return AutoRefresh == null && DateRange == null && ShowExcludedMembers == null && Thresholds.IsEmpty; }
        }

        public WatchlistPreferences WithAutoRefresh(RefreshInterval? autoRefresh)
        {
            return new WatchlistPreferences(autoRefresh, DateRange, ShowExcludedMembers, Thresholds);
        }

        public WatchlistPreferences WithDateRange(MonitoringDateRange? dateRange)
        {
            return new WatchlistPreferences(AutoRefresh, dateRange, ShowExcludedMembers, Thresholds);
        }

        public WatchlistPreferences WithShowExcludedMembers(bool? showExcludedMembers)
        {
            return new WatchlistPreferences(AutoRefresh, DateRange, showExcludedMembers, Thresholds);
        }

        public WatchlistPreferences WithThresholds(DurationThresholds? thresholds)
        {
            return new WatchlistPreferences(AutoRefresh, DateRange, ShowExcludedMembers, thresholds);
        }

        public string Describe()
        {
            if (IsInherited)
            {
                return "preferences: all inherited";
            }

            StringBuilder text = new StringBuilder("preferences: ");
            text.Append("refresh ").Append(AutoRefresh.HasValue ? RefreshIntervals.Label(AutoRefresh.Value) : "inherited");
            text.Append(", range ").Append(DateRange.HasValue ? MonitoringDateRanges.Label(DateRange.Value) : "inherited");
            text.Append(", excluded members ").Append(ShowExcludedMembers.HasValue ? (ShowExcludedMembers.Value ? "shown" : "hidden") : "inherited");
            if (!Thresholds.IsEmpty)
            {
                text.Append(", ").Append(Thresholds.Describe());
            }

            return text.ToString();
        }

        public override string ToString()
        {
            return Describe();
        }
    }
}
