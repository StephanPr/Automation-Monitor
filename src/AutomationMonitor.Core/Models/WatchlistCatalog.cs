using System;
using System.Collections.Generic;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Everything the plugin keeps locally in one unit: the saved watchlists and the application
    /// settings that select between them. This is the object the persistence layer writes and reads,
    /// so an active selection can never be stored separately from the lists it points at.
    /// </summary>
    public sealed class WatchlistCatalog
    {
        public WatchlistCatalog(IEnumerable<Watchlist>? watchlists, string? activeWatchlistId, ApplicationSettings? settings)
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
        }

        /// <summary>No watchlists and first-run settings.</summary>
        public static WatchlistCatalog Empty
        {
            get { return new WatchlistCatalog(null, null, null); }
        }

        public IReadOnlyList<Watchlist> Watchlists { get; }

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
        /// </summary>
        public IReadOnlyList<Watchlist> ForEnvironment(string? environmentId)
        {
            string scope = (environmentId ?? string.Empty).Trim();
            List<Watchlist> visible = new List<Watchlist>();
            foreach (Watchlist watchlist in Watchlists)
            {
                if (watchlist.EnvironmentId.Length == 0
                    || scope.Length == 0
                    || string.Equals(watchlist.EnvironmentId, scope, StringComparison.OrdinalIgnoreCase))
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
        /// an operator picking from a dropdown cannot tell two identical names apart.
        /// </summary>
        public Watchlist? FindSameName(string? name, string? environmentId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string wanted = name!.Trim();
            string scope = (environmentId ?? string.Empty).Trim();
            foreach (Watchlist watchlist in Watchlists)
            {
                if (string.Equals(watchlist.Name, wanted, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(watchlist.EnvironmentId, scope, StringComparison.OrdinalIgnoreCase))
                {
                    return watchlist;
                }
            }

            return null;
        }

        public WatchlistCatalog WithWatchlists(IEnumerable<Watchlist> watchlists, string? activeWatchlistId = null)
        {
            return new WatchlistCatalog(watchlists, activeWatchlistId ?? ActiveWatchlistId, Settings);
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
                    return new WatchlistCatalog(lists, ActiveWatchlistId, Settings);
                }
            }

            lists.Add(watchlist);
            return new WatchlistCatalog(lists, ActiveWatchlistId, Settings);
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

            string? active = string.Equals(ActiveWatchlistId, id, StringComparison.OrdinalIgnoreCase) ? null : ActiveWatchlistId;
            return new WatchlistCatalog(lists, active, Settings.WithoutActiveWatchlist(id));
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

            return new WatchlistCatalog(Watchlists, id, settings);
        }

        public WatchlistCatalog WithSettings(ApplicationSettings settings)
        {
            return new WatchlistCatalog(Watchlists, ActiveWatchlistId, settings);
        }
    }
}
