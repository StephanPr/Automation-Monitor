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
        /// <summary>
        /// Placeholder shown when there is no list to describe.
        ///
        /// <para>
        /// Wording revised in C6, with the product reason: watchlists can now be saved from the interface,
        /// so this line must no longer imply that nothing is ever written - saved lists may already sit on
        /// disk for this environment. What it honestly states is only about the current state: no list is
        /// selected, so nothing is being monitored yet.
        /// </para>
        /// </summary>
        public static string Empty
        {
            get { return "No watchlist selected. Nothing is being monitored yet."; }
        }

        public static string Describe(Watchlist? watchlist)
        {
            if (watchlist == null)
            {
                return Empty;
            }

            StringBuilder text = new StringBuilder();
            text.Append("Watchlist \"").Append(watchlist.Name).Append("\" - ")
                .Append(watchlist.MonitoredFlowCount.ToString(CultureInfo.InvariantCulture)).Append(" flow(s) selected of ")
                .Append(watchlist.Members.Count.ToString(CultureInfo.InvariantCulture)).Append(" member(s) - ")
                .Append(WatchlistMembershipModes.Describe(watchlist.Mode)).Append(" - ")
                .Append(SourceStatus.Label(watchlist.State));

            if (watchlist.SourceSolutionIds.Count > 0)
            {
                text.Append(" - from ").Append(watchlist.SourceSolutionIds.Count.ToString(CultureInfo.InvariantCulture)).Append(" solution(s)");
            }

            text.Append(" - membership checked ")
                .Append(watchlist.LastMembershipRefreshUtc.HasValue
                    ? watchlist.LastMembershipRefreshUtc.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)
                    : "never");

            if (!string.IsNullOrWhiteSpace(watchlist.CoverageDescription))
            {
                text.Append(" - ").Append(watchlist.CoverageDescription);
            }

            return text.ToString();
        }
    }
}
