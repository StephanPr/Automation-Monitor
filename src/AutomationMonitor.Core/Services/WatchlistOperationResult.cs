using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Why a watchlist operation did or did not happen. A saved watchlist is data the operator owns,
    /// so every mutation reports its outcome as a value instead of throwing or returning a bare bool:
    /// "not found", "name already used here" and "the file could not be read" must not look alike.
    /// </summary>
    public enum WatchlistOperationOutcome
    {
        /// <summary>The change was made in memory. It reaches disk when the catalog is saved.</summary>
        Applied = 0,

        /// <summary>Nothing changed: the requested state already is the current state.</summary>
        NoChange = 1,

        /// <summary>No watchlist has that id. It was deleted, or the caller holds a stale reference.</summary>
        NotFound = 2,

        /// <summary>Another watchlist in the same environment already has that name.</summary>
        DuplicateName = 3,

        /// <summary>The name is empty, all whitespace, or too long to be usable in a list.</summary>
        InvalidName = 4,

        /// <summary>The flow belongs to a different environment than this watchlist.</summary>
        EnvironmentMismatch = 5,

        /// <summary>The caller's copy is older than the stored one, so applying it would undo a change.</summary>
        Conflict = 6,

        /// <summary>Membership cannot change in this mode: a manual list is owned by the operator.</summary>
        NotAllowedInMode = 7,

        /// <summary>Nothing could be saved because the file on disk is not readable by this version.</summary>
        StorageRefused = 8,

        /// <summary>The store failed for another reason (permissions, read-only, disk).</summary>
        StorageFailed = 9
    }

    /// <summary>The result of one watchlist mutation, with the catalog as it now stands.</summary>
    public sealed class WatchlistOperationResult
    {
        private WatchlistOperationResult(
            WatchlistOperationOutcome outcome,
            WatchlistCatalog catalog,
            Watchlist? watchlist,
            string? reason,
            IEnumerable<string>? notes)
        {
            Outcome = outcome;
            Catalog = catalog ?? WatchlistCatalog.Empty;
            Watchlist = watchlist;
            Reason = reason ?? string.Empty;
            List<string> list = new List<string>();
            if (notes != null)
            {
                list.AddRange(notes);
            }

            Notes = list;
        }

        public WatchlistOperationOutcome Outcome { get; }

        /// <summary>The catalog after the attempt, changed or not, so a caller can rebind without reloading.</summary>
        public WatchlistCatalog Catalog { get; }

        /// <summary>The watchlist affected, when the operation concerns one.</summary>
        public Watchlist? Watchlist { get; }

        /// <summary>Sanitized explanation for anything other than <see cref="WatchlistOperationOutcome.Applied"/>.</summary>
        public string Reason { get; }

        /// <summary>Extra statements from the store or the reconciliation, kept with the outcome.</summary>
        public IReadOnlyList<string> Notes { get; }

        public bool IsApplied
        {
            get { return Outcome == WatchlistOperationOutcome.Applied; }
        }

        public static WatchlistOperationResult Applied(WatchlistCatalog catalog, Watchlist? watchlist, string? reason = null, IEnumerable<string>? notes = null)
        {
            return new WatchlistOperationResult(WatchlistOperationOutcome.Applied, catalog, watchlist, reason, notes);
        }

        public static WatchlistOperationResult Unchanged(WatchlistCatalog catalog, Watchlist? watchlist, string? reason = null)
        {
            return new WatchlistOperationResult(WatchlistOperationOutcome.NoChange, catalog, watchlist, reason, null);
        }

        public static WatchlistOperationResult Refused(WatchlistOperationOutcome outcome, WatchlistCatalog catalog, Watchlist? watchlist, string? reason, IEnumerable<string>? notes = null)
        {
            if (outcome == WatchlistOperationOutcome.Applied)
            {
                throw new ArgumentException("An outcome that refuses a change cannot report itself as applied.", nameof(outcome));
            }

            return new WatchlistOperationResult(outcome, catalog, watchlist, reason, notes);
        }

        /// <summary>One honest line for a status bar. Never a bare "OK".</summary>
        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            switch (Outcome)
            {
                case WatchlistOperationOutcome.Applied:
                    text.Append("Changed");
                    break;
                case WatchlistOperationOutcome.NoChange:
                    text.Append("No change");
                    break;
                case WatchlistOperationOutcome.NotFound:
                    text.Append("That watchlist is no longer in the list");
                    break;
                case WatchlistOperationOutcome.DuplicateName:
                    text.Append("That name is already used in this environment");
                    break;
                case WatchlistOperationOutcome.InvalidName:
                    text.Append("That name cannot be used");
                    break;
                case WatchlistOperationOutcome.EnvironmentMismatch:
                    text.Append("That flow belongs to another environment");
                    break;
                case WatchlistOperationOutcome.Conflict:
                    text.Append("This watchlist changed since you last saw it");
                    break;
                case WatchlistOperationOutcome.NotAllowedInMode:
                    text.Append("This watchlist's membership mode does not allow that");
                    break;
                case WatchlistOperationOutcome.StorageRefused:
                    text.Append("NOTHING WAS SAVED - the settings file could not be read");
                    break;
                default:
                    text.Append("NOTHING WAS SAVED");
                    break;
            }

            if (Watchlist != null)
            {
                text.Append(" - ").Append(Watchlist.Name).Append(" (")
                    .Append(Watchlist.Members.Count.ToString(CultureInfo.InvariantCulture)).Append(" member(s))");
            }

            if (!string.IsNullOrWhiteSpace(Reason))
            {
                text.Append(" - ").Append(Reason.Trim());
            }

            foreach (string note in Notes)
            {
                text.Append(" - ").Append(note);
            }

            return text.ToString();
        }

        public override string ToString()
        {
            return Describe();
        }
    }
}
