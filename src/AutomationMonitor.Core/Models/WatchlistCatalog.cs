using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Everything the plugin keeps locally in one unit: the saved watchlists and the application
    /// settings that select between them. This is the object the persistence layer writes and reads,
    /// so an active selection can never be stored separately from the lists it points at.
    /// </summary>
    public sealed class WatchlistCatalog
    {
        private readonly IReadOnlyList<string> _unrepresentedMembers;

        public WatchlistCatalog(
            IEnumerable<Watchlist>? watchlists,
            string? activeWatchlistId,
            ApplicationSettings? settings,
            IEnumerable<string>? unrepresentedMembers = null)
        {
            List<Watchlist> lists = new List<Watchlist>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (watchlists != null)
            {
                foreach (Watchlist watchlist in watchlists)
                {
                    // A duplicated id is a data defect: keeping the first is deterministic and keeps
                    // every later lookup honest instead of returning an arbitrary member.
                    if (watchlist == null || string.IsNullOrWhiteSpace(watchlist.Id) || !seen.Add(watchlist.Id))
                    {
                        continue;
                    }

                    lists.Add(watchlist);
                }
            }

            Watchlists = lists;
            ActiveWatchlistId = string.IsNullOrWhiteSpace(activeWatchlistId) ? null : activeWatchlistId!.Trim();
            Settings = settings ?? ApplicationSettings.Defaults;

            List<string> unreadable = new List<string>();
            if (unrepresentedMembers != null)
            {
                foreach (string note in unrepresentedMembers)
                {
                    if (!string.IsNullOrWhiteSpace(note) && !unreadable.Contains(note))
                    {
                        unreadable.Add(note!.Trim());
                    }
                }
            }

            _unrepresentedMembers = unreadable;
        }

        /// <summary>No watchlists and first-run settings.</summary>
        public static WatchlistCatalog Empty
        {
            get { return new WatchlistCatalog(null, null, null); }
        }

        public IReadOnlyList<Watchlist> Watchlists { get; }

        /// <summary>
        /// What the read had to leave out: one entry per stored member this build cannot represent, naming the
        /// list it belongs to and the value it could not interpret. Empty for anything built in memory.
        ///
        /// <para>
        /// This is the mark that blocks a save. One file holds every list, so writing any of them rewrites the
        /// lists that lost members and would delete what the read could not understand - the same data loss the
        /// unreadable-file refusal exists to prevent, arriving by a quieter route. The mark is therefore sticky
        /// through every mutation, including the removal of the affected list: deleting it from memory is not
        /// evidence that the file's copy may be discarded.
        /// </para>
        /// </summary>
        public IReadOnlyList<string> UnrepresentedMembers
        {
            get { return _unrepresentedMembers; }
        }

        public bool HasUnrepresentedMembers
        {
            get { return _unrepresentedMembers.Count > 0; }
        }

        /// <summary>The operator-facing account of the mark: what is missing, and the two ways out.</summary>
        public string DescribeUnrepresentedMembers()
        {
            if (!HasUnrepresentedMembers)
            {
                return "nothing is unreadable in the loaded watchlists";
            }

            StringBuilder text = new StringBuilder();
            text.Append(_unrepresentedMembers.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" saved member(s) are written in a form this version cannot read, so the saved file is not")
                .Append(" rewritten from what is in memory. Nothing has been lost: the file still holds them.");
            foreach (string note in _unrepresentedMembers)
            {
                text.Append(" - ").Append(note);
            }

            text.Append(" Open the file with a version that understands those values, or replace the saved")
                .Append(" watchlists deliberately if you mean to discard them.");
            return text.ToString();
        }

        /// <summary>
        /// Selection that is not tied to one environment. The per-environment selection in
        /// <see cref="ApplicationSettings"/> wins when it names a list that exists here.
        /// </summary>
        public string? ActiveWatchlistId { get; }

        public ApplicationSettings Settings { get; }

        public int Count { get { return Watchlists.Count; } }

        public Watchlist? Find(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            foreach (Watchlist watchlist in Watchlists)
            {
                if (string.Equals(watchlist.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return watchlist;
                }
            }

            return null;
        }

        /// <summary>
        /// The watchlists usable in one environment. A watchlist with no environment recorded is
        /// deliberately visible everywhere - lists built while disconnected must not vanish - while a
        /// list that names an environment must never appear in another one.
        ///
        /// <para>
        /// A list that names a resource is matched on what it names, not only on how the file spells it:
        /// AM-44.1 canonicalises a recognized scope, so a stored organization id in braces and upper case is
        /// the same organization as the connected one. Textual equality stays as its own arm, because a
        /// stored value this build cannot canonicalise - a bare legacy environment id, an unknown kind, a
        /// damaged identifier - must keep matching exactly what it says and nothing else. A preserved scope is
        /// never evidence of a match for a different spelling, and it never disappears while no environment is
        /// verified.
        /// </para>
        /// </summary>
        public IReadOnlyList<Watchlist> ForEnvironment(string? environmentId)
        {
            ResourceScope wanted = ResourceScope.Read(environmentId);
            string wantedText = (environmentId ?? string.Empty).Trim();
            List<Watchlist> visible = new List<Watchlist>();
            foreach (Watchlist watchlist in Watchlists)
            {
                ResourceScope stored = ResourceScope.Read(watchlist.EnvironmentId);
                string storedText = watchlist.EnvironmentId;
                if (stored.IsUnverified
                    || wanted.IsUnverified
                    || (storedText.Length > 0
                        && string.Equals(storedText, wantedText, StringComparison.OrdinalIgnoreCase))
                    || stored.IsSameResourceAs(wanted))
                {
                    visible.Add(watchlist);
                }
            }

            return visible;
        }

        /// <summary>
        /// The watchlist to monitor in one environment: that environment's own stored selection when
        /// it still names a visible list, otherwise the catalog-level selection, otherwise none. A
        /// stored id that no longer exists resolves to "none" rather than silently picking another
        /// list, because watching the wrong flows is worse than watching nothing.
        /// </summary>
        public Watchlist? ActiveWatchlistFor(string? environmentId)
        {
            IReadOnlyList<Watchlist> visible = ForEnvironment(environmentId);
            Watchlist? preferred = Find(Settings.ActiveWatchlistIdFor(environmentId));
            if (preferred != null && Contains(visible, preferred))
            {
                return preferred;
            }

            preferred = Find(ActiveWatchlistId);
            if (preferred != null && Contains(visible, preferred))
            {
                return preferred;
            }

            return null;
        }

        /// <summary>
        /// Identity, not value: a stored list is the same list only when it is the same object in
        /// this catalog. A rename produces a new instance with the same id, so comparison is by id.
        /// </summary>
        private static bool Contains(IReadOnlyList<Watchlist> watchlists, Watchlist candidate)
        {
            foreach (Watchlist watchlist in watchlists)
            {
                if (string.Equals(watchlist.Id, candidate.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when an id is already taken. Ids are compared case-insensitively.</summary>
        public bool ContainsId(string? id)
        {
            return Find(id) != null;
        }

        /// <summary>
        /// The existing list whose name would collide with a new one in the same environment scope.
        /// Two environments may each have a list called "Go-live"; one environment may not, because
        /// an operator picking from a dropdown cannot tell two identical names apart. "Same environment" is
        /// the shared scope rule (<see cref="ResourceScope.NamesSameEnvironment"/>), so a list whose stored
        /// scope spells the connected organization a different way is still the same environment and still
        /// clashes - the listing rule and the naming rule must not disagree, or association would let one
        /// organization hold two lists with one name.
        /// </summary>
        public Watchlist? FindSameName(string? name, string? environmentId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string wanted = name!.Trim();
            foreach (Watchlist watchlist in Watchlists)
            {
                if (string.Equals(watchlist.Name, wanted, StringComparison.OrdinalIgnoreCase)
                    && ResourceScope.NamesSameEnvironment(watchlist.EnvironmentId, environmentId))
                {
                    return watchlist;
                }
            }

            return null;
        }

        public WatchlistCatalog WithWatchlists(IEnumerable<Watchlist> watchlists, string? activeWatchlistId = null)
        {
            return Rebuild(watchlists, activeWatchlistId ?? ActiveWatchlistId, Settings);
        }

        /// <summary>Inserts or replaces one watchlist by id, keeping the stored order otherwise.</summary>
        public WatchlistCatalog WithWatchlist(Watchlist watchlist)
        {
            if (watchlist == null)
            {
                throw new ArgumentNullException(nameof(watchlist));
            }

            List<Watchlist> lists = new List<Watchlist>(Watchlists);
            for (int i = 0; i < lists.Count; i++)
            {
                if (string.Equals(lists[i].Id, watchlist.Id, StringComparison.OrdinalIgnoreCase))
                {
                    lists[i] = watchlist;
                    return Rebuild(lists, ActiveWatchlistId, Settings);
                }
            }

            lists.Add(watchlist);
            return Rebuild(lists, ActiveWatchlistId, Settings);
        }

        /// <summary>
        /// Removes one list and clears every selection that pointed at it, in the catalog and in all
        /// environment profiles. A stale id must not survive a delete: it would select nothing on the
        /// next restart, or match a new list that happened to reuse the id.
        /// </summary>
        public WatchlistCatalog Without(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return this;
            }

            List<Watchlist> lists = new List<Watchlist>();
            bool removed = false;
            foreach (Watchlist watchlist in Watchlists)
            {
                if (string.Equals(watchlist.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    removed = true;
                    continue;
                }

                lists.Add(watchlist);
            }

            if (!removed)
            {
                return this;
            }

            // The unreadable mark survives: dropping the list that carries it from memory does not make the
            // file's copy of those members safe to overwrite.
            string? active = string.Equals(ActiveWatchlistId, id, StringComparison.OrdinalIgnoreCase) ? null : ActiveWatchlistId;
            return Rebuild(lists, active, Settings.WithoutActiveWatchlist(id));
        }

        /// <summary>
        /// Selects the active watchlist. When an environment is supplied the selection is stored on
        /// that environment's profile as well, so reopening the tool against a different environment
        /// does not carry the choice over.
        /// </summary>
        public WatchlistCatalog WithActiveWatchlist(string? id, string? environmentId, DateTimeOffset? usedAtUtc)
        {
            ApplicationSettings settings = Settings;
            if (environmentId != null && usedAtUtc.HasValue)
            {
                settings = settings.WithActiveWatchlist(environmentId, id, usedAtUtc.Value);
            }

            return Rebuild(Watchlists, id, settings);
        }

        public WatchlistCatalog WithSettings(ApplicationSettings settings)
        {
            return Rebuild(Watchlists, ActiveWatchlistId, settings);
        }

        /// <summary>
        /// Every derived catalog is built here, so the unreadable mark cannot be dropped by a mutation that
        /// forgot to carry it. A copy that lost the mark would let a save rewrite a file it only half read.
        /// </summary>
        private WatchlistCatalog Rebuild(IEnumerable<Watchlist> watchlists, string? activeWatchlistId, ApplicationSettings settings)
        {
            return new WatchlistCatalog(watchlists, activeWatchlistId, settings, _unrepresentedMembers);
        }
    }
}
