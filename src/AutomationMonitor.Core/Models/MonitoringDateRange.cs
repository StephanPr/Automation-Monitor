using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// The recent-time preset an operator picks instead of typing two date/time boxes. Stored as
    /// configuration, so it is a name in a closed set and not a number of minutes in a file.
    /// It carries no instant of its own: the window is always computed against the caller's clock,
    /// which keeps the value testable and free of a hidden <c>DateTime.Now</c>.
    /// </summary>
    public enum MonitoringDateRange
    {
        /// <summary>Fifteen minutes.</summary>
        LastFifteenMinutes = 0,

        /// <summary>One hour. The default for a monitoring session.</summary>
        LastHour = 1,

        /// <summary>Four hours.</summary>
        LastFourHours = 2,

        /// <summary>One day.</summary>
        LastDay = 3,

        /// <summary>Seven days. Beyond this belongs to the historical report, not the monitor.</summary>
        LastSevenDays = 4
    }

    /// <summary>Conversion between a <see cref="MonitoringDateRange"/> and its window.</summary>
    public static class MonitoringDateRanges
    {
        /// <summary>The range a session starts with when nothing has been stored.</summary>
        public const MonitoringDateRange Default = MonitoringDateRange.LastHour;

        public static int Minutes(MonitoringDateRange range)
        {
            switch (range)
            {
                case MonitoringDateRange.LastFifteenMinutes:
                    return 15;
                case MonitoringDateRange.LastHour:
                    return 60;
                case MonitoringDateRange.LastFourHours:
                    return 240;
                case MonitoringDateRange.LastDay:
                    return 1440;
                case MonitoringDateRange.LastSevenDays:
                    return 10080;
                default:
                    return 60;
            }
        }

        /// <summary>Stable persistence token, never derived from the operator wording.</summary>
        public static string Token(MonitoringDateRange range)
        {
            switch (range)
            {
                case MonitoringDateRange.LastFifteenMinutes:
                    return "15m";
                case MonitoringDateRange.LastFourHours:
                    return "4h";
                case MonitoringDateRange.LastDay:
                    return "24h";
                case MonitoringDateRange.LastSevenDays:
                    return "7d";
                default:
                    return "1h";
            }
        }

        /// <summary>
        /// Reads a stored token. Unknown or absent yields <see cref="Default"/> and false, so the
        /// caller can say "that range is not recognised" instead of adopting a window silently.
        /// </summary>
        public static bool TryParse(string? token, out MonitoringDateRange range)
        {
            range = Default;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string text = token!.Trim().ToLowerInvariant();
            foreach (MonitoringDateRange candidate in All())
            {
                if (string.Equals(Token(candidate), text, StringComparison.Ordinal))
                {
                    range = candidate;
                    return true;
                }
            }

            return false;
        }

        public static string Label(MonitoringDateRange range)
        {
            switch (range)
            {
                case MonitoringDateRange.LastFifteenMinutes:
                    return "last 15 minutes";
                case MonitoringDateRange.LastFourHours:
                    return "last 4 hours";
                case MonitoringDateRange.LastDay:
                    return "last 24 hours";
                case MonitoringDateRange.LastSevenDays:
                    return "last 7 days";
                default:
                    return "last hour";
            }
        }

        public static MonitoringDateRange[] All()
        {
            return new[]
            {
                MonitoringDateRange.LastFifteenMinutes,
                MonitoringDateRange.LastHour,
                MonitoringDateRange.LastFourHours,
                MonitoringDateRange.LastDay,
                MonitoringDateRange.LastSevenDays
            };
        }

        /// <summary>
        /// The half-open UTC window <c>[endUtc - range, endUtc)</c> for this preset. The caller
        /// supplies the end instant (the clock reading it took for "now") so the same stored
        /// preference can be evaluated deterministically in a test.
        /// </summary>
        public static MonitoringQuery BuildQuery(MonitoringDateRange range, DateTimeOffset endUtc, int maxRows = MonitoringQuery.DefaultMaxRows)
        {
            DateTimeOffset to = endUtc.ToUniversalTime();
            DateTimeOffset from = to.AddMinutes(-Minutes(range));
            return new MonitoringQuery(null, from, to, null, maxRows);
        }
    }
}
