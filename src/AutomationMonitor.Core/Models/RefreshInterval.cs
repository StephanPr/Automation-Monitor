using System;
using System.Globalization;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// The auto-refresh interval an operator can choose. A closed set rather than a free-form
    /// number, so the interval that gets stored is always one the plugin can honour and no view
    /// can invent its own magic number. The refresh coordinator (M5) reads <see cref="RefreshIntervals.Seconds"/>;
    /// nothing in the monitoring core starts a timer with it today.
    /// </summary>
    public enum RefreshInterval
    {
        /// <summary>No auto-refresh. Only the operator's own Refresh action contacts a source.</summary>
        Off = 0,

        /// <summary>Thirty seconds. The fastest interval offered: it is already heavy on Dataverse.</summary>
        ThirtySeconds = 1,

        /// <summary>One minute.</summary>
        OneMinute = 2,

        /// <summary>Two minutes.</summary>
        TwoMinutes = 3,

        /// <summary>Five minutes. The default once auto-refresh is switched on.</summary>
        FiveMinutes = 4,

        /// <summary>Fifteen minutes.</summary>
        FifteenMinutes = 5,

        /// <summary>Thirty minutes.</summary>
        ThirtyMinutes = 6,

        /// <summary>One hour.</summary>
        OneHour = 7
    }

    /// <summary>
    /// The single mapping between a <see cref="RefreshInterval"/> and real time, its persistence
    /// token and its operator wording. Keeping the three together is what lets a stored interval
    /// survive a rename of either the enum member or the label.
    /// </summary>
    public static class RefreshIntervals
    {
        /// <summary>The token meaning "never poll" when no interval has been chosen at a level.</summary>
        public const string OffToken = "off";

        /// <summary>Seconds for an interval. Off is zero, meaning "no timer", not "poll constantly".</summary>
        public static int Seconds(RefreshInterval interval)
        {
            switch (interval)
            {
                case RefreshInterval.ThirtySeconds:
                    return 30;
                case RefreshInterval.OneMinute:
                    return 60;
                case RefreshInterval.TwoMinutes:
                    return 120;
                case RefreshInterval.FiveMinutes:
                    return 300;
                case RefreshInterval.FifteenMinutes:
                    return 900;
                case RefreshInterval.ThirtyMinutes:
                    return 1800;
                case RefreshInterval.OneHour:
                    return 3600;
                case RefreshInterval.Off:
                    return 0;
                default:
                    // An out-of-range value - a member a future build added, or a document that
                    // slipped past validation - must not become a fast timer.
                    return 0;
            }
        }

        public static TimeSpan Interval(RefreshInterval interval)
        {
            return TimeSpan.FromSeconds(Seconds(interval));
        }

        /// <summary>True when a timer should run at all.</summary>
        public static bool IsAutoRefreshEnabled(RefreshInterval interval)
        {
            return Seconds(interval) > 0;
        }

        /// <summary>
        /// Stable lower-case token written to the settings file. Never derived from
        /// <see cref="Label"/>: rewording what the operator sees must not change what is on disk.
        /// </summary>
        public static string Token(RefreshInterval interval)
        {
            switch (interval)
            {
                case RefreshInterval.ThirtySeconds:
                    return "30s";
                case RefreshInterval.OneMinute:
                    return "1m";
                case RefreshInterval.TwoMinutes:
                    return "2m";
                case RefreshInterval.FiveMinutes:
                    return "5m";
                case RefreshInterval.FifteenMinutes:
                    return "15m";
                case RefreshInterval.ThirtyMinutes:
                    return "30m";
                case RefreshInterval.OneHour:
                    return "1h";
                default:
                    return OffToken;
            }
        }

        /// <summary>
        /// Reads a stored token. An unknown or absent token yields <see cref="RefreshInterval.Off"/>
        /// and false, so the caller can say "that interval is not recognised" instead of quietly
        /// adopting a polling rate the operator never asked for.
        /// </summary>
        public static bool TryParse(string? token, out RefreshInterval interval)
        {
            interval = RefreshInterval.Off;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string text = token!.Trim().ToLowerInvariant();
            if (string.Equals(text, OffToken, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (RefreshInterval candidate in All())
            {
                if (candidate != RefreshInterval.Off && string.Equals(Token(candidate), text, StringComparison.Ordinal))
                {
                    interval = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Operator wording. Deliberately different from the persistence token.</summary>
        public static string Label(RefreshInterval interval)
        {
            switch (interval)
            {
                case RefreshInterval.ThirtySeconds:
                    return "every 30 seconds";
                case RefreshInterval.OneMinute:
                    return "every minute";
                case RefreshInterval.TwoMinutes:
                    return "every 2 minutes";
                case RefreshInterval.FiveMinutes:
                    return "every 5 minutes";
                case RefreshInterval.FifteenMinutes:
                    return "every 15 minutes";
                case RefreshInterval.ThirtyMinutes:
                    return "every 30 minutes";
                case RefreshInterval.OneHour:
                    return "every hour";
                default:
                    return "off";
            }
        }

        /// <summary>Every interval in the closed set, slowest first, for a selector.</summary>
        public static RefreshInterval[] All()
        {
            return new[]
            {
                RefreshInterval.Off,
                RefreshInterval.ThirtySeconds,
                RefreshInterval.OneMinute,
                RefreshInterval.TwoMinutes,
                RefreshInterval.FiveMinutes,
                RefreshInterval.FifteenMinutes,
                RefreshInterval.ThirtyMinutes,
                RefreshInterval.OneHour
            };
        }

        /// <summary>
        /// The slower of two intervals. Throttling must lengthen a wait and never shorten it, so
        /// this is the only safe way to combine a requested interval with a required minimum.
        /// </summary>
        public static RefreshInterval Slowest(RefreshInterval requested, RefreshInterval minimum)
        {
            return Seconds(requested) >= Seconds(minimum) ? requested : minimum;
        }

        /// <summary>One honest line for a status bar or a future export footer.</summary>
        public static string Describe(RefreshInterval interval)
        {
            int seconds = Seconds(interval);
            if (seconds == 0)
            {
                return "auto-refresh off";
            }

            return "auto-refresh " + Label(interval) + " (" + seconds.ToString(CultureInfo.InvariantCulture) + "s)";
        }
    }
}
