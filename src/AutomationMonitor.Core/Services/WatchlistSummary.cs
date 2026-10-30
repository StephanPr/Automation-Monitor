using System;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// The one wording for "what is in this watchlist and how complete is it". Views and any
    /// future export must use it so an excluded or partially-discovered membership cannot be
    /// described as a plain count in one place and something else in another.
    /// </summary>
    public static class WatchlistSummary
    {
        /// <summary>Placeholder shown before the first discovery has produced a watchlist.</summary>
        public static string Empty
        {
            get { return "No watchlist built yet. Nothing is written to disk in this milestone."; }
        }

        public static string Describe(Watchlist? watchlist)
        {
            if (watchlist == null)
            {
                return Empty;
            }

            StringBuilder text = new StringBuilder();
            text.Append("Watchlist \"").Append(watchlist.Name).Append("\" - ")
                .Append(watchlist.MonitoredFlowCount.ToString(CultureInfo.InvariantCulture)).Append(" flow(s) monitored of ")
                .Append(watchlist.Members.Count.ToString(CultureInfo.InvariantCulture)).Append(" member(s) - ")
                .Append(SourceStatus.Label(watchlist.State));

            if (watchlist.SourceSolutionIds.Count > 0)
            {
                text.Append(" - from ").Append(watchlist.SourceSolutionIds.Count.ToString(CultureInfo.InvariantCulture)).Append(" solution(s)");
            }

            if (!string.IsNullOrWhiteSpace(watchlist.CoverageDescription))
            {
                text.Append(" - ").Append(watchlist.CoverageDescription);
            }

            return text.ToString();
        }
    }
}
