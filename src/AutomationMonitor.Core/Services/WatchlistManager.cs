using System;
using System.Collections.Generic;
using System.Globalization;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Owns the in-memory catalog of saved watchlists: create, rename, duplicate, delete, select,
    /// membership edits, and the only two paths to disk. Validation lives here rather than in a view so
    /// the rules cannot be bypassed by a second caller, and every mutation answers with a
    /// <see cref="WatchlistOperationResult"/> instead of throwing.
    ///
    /// <para>
    /// Saving is deliberately separate from changing: a change is in memory until <see cref="Save"/> is
    /// called, and <see cref="Save"/> refuses while the last read of the file was a refusal. That pair of
    /// rules is what keeps "I edited the list" from becoming "I replaced the file I could not read".
    /// </para>
    /// </summary>
    public sealed partial class WatchlistManager
    {
        /// <summary>The longest watchlist name accepted. Longer is a paste mistake, not a name.</summary>
        public const int MaximumNameLength = 100;

        /// <summary>The longest description accepted.</summary>
        public const int MaximumDescriptionLength = 1000;

        private readonly IWatchlistStore _store;
        private readonly IClock _clock;
        private readonly IIdGenerator _ids;
        private readonly ScopeResolutionSupport _scopeResolution;

        private WatchlistCatalog _catalog = WatchlistCatalog.Empty;
        private WatchlistStoreResult? _lastStoreResult;
        private bool _hasUnsavedChanges;
        private bool _blocksSave;

        public WatchlistManager(IWatchlistStore store, IClock clock, IIdGenerator ids, ScopeResolutionSupport? scopeResolution = null)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _store = store;
            _clock = clock ?? new SystemClock();
            _ids = ids ?? new GuidIdGenerator();

            // The shipped default defers re-keying a stored list. A caller passes something else only when this
            // build really can resolve a qualified connected scope - see ScopeResolutionSupport.
            _scopeResolution = scopeResolution ?? ScopeResolutionSupport.Shipped;
        }

        /// <summary>
        /// Whether this build may reassign a stored scope. The manager asks it before applying an association,
        /// so the rule that keeps a saved list reachable lives in one place and not in whichever view offers
        /// the button.
        /// </summary>
        public ScopeResolutionSupport ScopeResolution
        {
            get { return _scopeResolution; }
        }

        /// <summary>The catalog as it now stands, saved or not.</summary>
        public WatchlistCatalog Catalog
        {
            get { return _catalog; }
        }

        /// <summary>What the store last reported, including the reason for a refusal.</summary>
        public WatchlistStoreResult? LastStoreResult
        {
            get { return _lastStoreResult; }
        }

        /// <summary>True when something has changed in memory that is not on disk yet.</summary>
        public bool HasUnsavedChanges
        {
            get { return _hasUnsavedChanges; }
        }

        /// <summary>
        /// True while the saved file is not fully understood by this build: it could not be read at all, or it
        /// was read with members left out because their content has no meaning here. Saving stays blocked so the
        /// only ways forward are a reload or an explicit replace, both of which the operator chose.
        /// </summary>
        public bool BlocksSave
        {
            get { return _blocksSave; }
        }

        public string StoreDescription
        {
            get { return _store.Description; }
        }

        /// <summary>The time every stored timestamp in this manager comes from.</summary>
        public IClock Clock
        {
            get { return _clock; }
        }

        /// <summary>Read the catalog from disk. Never writes, and never clears unsaved edits silently.</summary>
        public WatchlistStoreResult Load()
        {
            WatchlistStoreResult read = _store.Read();
            _lastStoreResult = read;
            _catalog = read.Catalog;

            // A file this build could not read at all, or could read only in part, blocks saving for the same
            // reason: writing from memory would replace content that memory does not hold.
            _blocksSave = read.IsPreservedRefusal || _catalog.HasUnrepresentedMembers;
            _hasUnsavedChanges = false;
            return read;
        }

        /// <summary>Write the catalog. Refused while the file on disk could not be read.</summary>
        public WatchlistStoreResult Save()
        {
            return Save(false);
        }

        /// <summary>
        /// Write the catalog, optionally over a file this build could not read. The explicit override is
        /// for the confirmed "reset and replace" action only; the unusable file is still preserved by the
        /// store rather than deleted.
        /// </summary>
        public WatchlistStoreResult Save(bool replaceUnreadable)
        {
            WatchlistStoreResult written = replaceUnreadable ? _store.ReplaceUnreadable(_catalog) : _store.Write(_catalog);
            _lastStoreResult = written;
            if (written.IsUsable)
            {
                _blocksSave = false;
                _hasUnsavedChanges = false;
            }

            return written;
        }

        /// <summary>
        /// Drop the stored file and start from defaults. The caller must have asked the operator first:
        /// this is the one operation that removes saved watchlists, and the backup stays on disk.
        /// </summary>
        public WatchlistStoreResult ResetSaved()
        {
            WatchlistStoreResult cleared = _store.Clear();
            _lastStoreResult = cleared;
            if (cleared.IsUsable)
            {
                _catalog = WatchlistCatalog.Empty;
                _blocksSave = false;
                _hasUnsavedChanges = false;
            }

            return cleared;
        }

        /// <summary>Record what reconciliation produced, keeping the manager as the only writer.</summary>
        public WatchlistOperationResult Apply(Watchlist updated, string? reason = null, IEnumerable<string>? notes = null)
        {
            if (updated == null)
            {
                throw new ArgumentNullException(nameof(updated));
            }

            return Replace(updated, reason, notes);
        }

        /// <summary>The lists usable in one environment, in stored order.</summary>
        public IReadOnlyList<Watchlist> Lists(string? environmentId)
        {
            return _catalog.ForEnvironment(environmentId);
        }

        public Watchlist? Find(string? id)
        {
            return _catalog.Find(id);
        }

        /// <summary>The list this environment last selected, or null when nothing valid is stored.</summary>
        public Watchlist? ActiveWatchlist(string? environmentId)
        {
            return _catalog.ActiveWatchlistFor(environmentId);
        }

        /// <summary>The preferences that apply to one list in one environment, with their sources.</summary>
        public EffectivePreferences PreferencesFor(string? id, string? environmentId)
        {
            return PreferenceResolver.Resolve(_catalog.Settings, Find(id), environmentId);
        }

        /// <summary>
        /// A name for a new list that does not clash in this environment. Numbered rather than silent,
        /// because an operator who pastes a solution name twice gets two lists they can still tell apart.
        /// </summary>
        public string SuggestName(string baseName, string? environmentId)
        {
            string clean;
            string? reason;
            if (!TryValidateName(baseName, out clean, out reason))
            {
                clean = "New watchlist";
            }

            string candidate = clean;
            for (int attempt = 2; attempt < 100; attempt++)
            {
                if (_catalog.FindSameName(candidate, environmentId) == null)
                {
                    return candidate;
                }

                candidate = clean + " (" + attempt.ToString(CultureInfo.InvariantCulture) + ")";
            }

            return candidate;
        }

        /// <summary>Create an empty list. Membership comes later, from discovery or by hand.</summary>
        public WatchlistOperationResult Create(
            string name,
            WatchlistMembershipMode mode,
            string? environmentId,
            string? description = null,
            bool selectNow = true)
        {
            if (mode == WatchlistMembershipMode.DynamicSolution)
            {
                return WatchlistOperationResult.Refused(
                    WatchlistOperationOutcome.NotAllowedInMode,
                    _catalog,
                    null,
                    "A dynamic watchlist needs the solution it follows: build it from a solution import instead of creating it empty.");
            }

            string clean;
            string? reason;
            if (!TryValidateName(name, out clean, out reason))
            {
                return WatchlistOperationResult.Refused(WatchlistOperationOutcome.InvalidName, _catalog, null, reason);
            }

            if (_catalog.FindSameName(clean, environmentId) != null)
            {
                return WatchlistOperationResult.Refused(WatchlistOperationOutcome.DuplicateName, _catalog, null, DescribeClash(clean));
            }

            WatchlistBuilder builder = new WatchlistBuilder(_ids.NewId(), clean, mode, description, environmentId);
            builder.SetCoverage(ProviderResultState.Complete, new string[] { "created empty: nothing has been discovered into this list yet" });

            Watchlist created = builder.Build().WithModifiedAt(_clock.UtcNow);
            WatchlistCatalog next = _catalog.WithWatchlist(created);
            if (selectNow)
            {
                next = next.WithActiveWatchlist(created.Id, environmentId, _clock.UtcNow);
            }

            return Commit(next, created, "created as " + WatchlistMembershipModes.Describe(mode));
        }

        public WatchlistOperationResult Rename(string id, string name, DateTimeOffset? expectedModifiedAtUtc = null)
        {
            Watchlist? current = FindRequired(id, out WatchlistOperationResult? failure);
            if (current == null)
            {
                return failure!;
            }

            WatchlistOperationResult? stale = CheckStale(current, expectedModifiedAtUtc);
            if (stale != null)
            {
                return stale;
            }

            string clean;
            string? reason;
            if (!TryValidateName(name, out clean, out reason))
            {
                return WatchlistOperationResult.Refused(WatchlistOperationOutcome.InvalidName, _catalog, current, reason);
            }

            if (string.Equals(clean, current.Name, StringComparison.Ordinal))
            {
                return WatchlistOperationResult.Unchanged(_catalog, current, "that is already its name");
            }

            Watchlist? clash = _catalog.FindSameName(clean, current.EnvironmentId);
            if (clash != null && !string.Equals(clash.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            {
                return WatchlistOperationResult.Refused(WatchlistOperationOutcome.DuplicateName, _catalog, current, DescribeClash(clean));
            }

            // Identity does not move with the name: the id is what membership, selection and any later
            // cache key refer to, so renaming cannot orphan anything.
            return Commit(_catalog, current.WithName(clean).WithModifiedAt(_clock.UtcNow), "renamed");
        }

        /// <summary>
        /// Store what the editor form holds: membership mode, description and preferences in one
        /// mutation, because a half-saved form is a surprise the operator cannot see.
        /// </summary>
        public WatchlistOperationResult UpdateSettings(
            string id,
            WatchlistMembershipMode? mode,
            string? description,
            WatchlistPreferences? preferences,
            DateTimeOffset? expectedModifiedAtUtc = null)
        {
            Watchlist? current = FindRequired(id, out WatchlistOperationResult? failure);
            if (current == null)
            {
                return failure!;
            }

            WatchlistOperationResult? stale = CheckStale(current, expectedModifiedAtUtc);
            if (stale != null)
            {
                return stale;
            }

            Watchlist next = current;
            if (mode.HasValue && next.Mode != mode.Value)
            {
                if (mode.Value == WatchlistMembershipMode.DynamicSolution && next.SourceSolutionIds.Count == 0)
                {
                    return WatchlistOperationResult.Refused(
                        WatchlistOperationOutcome.NotAllowedInMode,
                        _catalog,
                        current,
                        "There is no solution to follow yet: import flows from a solution first, then make the list dynamic.");
                }

                next = next.WithMode(mode.Value);
            }

            if (description != null)
            {
                string clean = description.Trim();
                if (clean.Length > MaximumDescriptionLength)
                {
                    clean = clean.Substring(0, MaximumDescriptionLength);
                }

                next = next.WithDescription(clean);
            }

            if (preferences != null)
            {
                next = next.WithPreferences(preferences);
            }

            if (ReferenceEquals(next, current))
            {
                return WatchlistOperationResult.Unchanged(_catalog, current, "nothing in the settings changed");
            }

            return Commit(_catalog, next.WithModifiedAt(_clock.UtcNow), next.Preferences.Describe());
        }
    }
}
