using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// What an environment scope means for the saved watchlists, and when the catalogue behind them has
    /// to be read again.
    ///
    /// <para>
    /// A host connection may report no environment id at all, so the empty string is a legitimate scope
    /// rather than a missing value - <see cref="WatchlistCatalog.ForEnvironment"/> deliberately shows
    /// unscoped lists everywhere so a list built while disconnected cannot vanish. That is exactly why
    /// "not read yet" must never be spelled as an empty string: to a scope comparison the two states look
    /// identical and mean opposite things, and conflating them leaves a saved file visible on disk but
    /// absent from the UI. The read state is therefore a nullable value held by the caller and this is the
    /// one rule that decides whether a read is due.
    /// </para>
    /// </summary>
    public static class WatchlistScope
    {
        /// <summary>Trim the value and treat a connection that reported no id as the unscoped scope.</summary>
        public static string Normalize(string? environmentId)
        {
            return (environmentId ?? string.Empty).Trim();
        }

        /// <summary>True when the connection gave no environment id, so lists are not scoped to one.</summary>
        public static bool IsUnscoped(string? environmentId)
        {
            return Normalize(environmentId).Length == 0;
        }

        /// <summary>
        /// Whether the saved catalogue must be read again before it is shown. Three states force a read: it
        /// has never happened in this session, the last read was refused (the operator may have fixed the
        /// file by hand), or the connection now names a different environment. Anything else is a repeat
        /// read that doubles disk work for no new information.
        /// </summary>
        public static bool NeedsRead(string? lastReadScope, string? environmentId, bool blocksSave)
        {
            if (blocksSave || lastReadScope == null)
            {
                return true;
            }

            return !string.Equals(Normalize(lastReadScope), Normalize(environmentId), StringComparison.Ordinal);
        }
    }
}
