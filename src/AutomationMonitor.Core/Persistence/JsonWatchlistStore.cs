using System;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// The JSON watchlist store: <see cref="SettingsFileStore"/> for the file discipline,
    /// <see cref="SettingsMap"/> for the format, and nothing else. It remembers what the last read
    /// concluded, because that is what decides whether a save may touch the file on disk - the rule
    /// "never silently overwrite settings this build cannot read" lives here so no caller can forget it.
    /// </summary>
    public sealed class JsonWatchlistStore : IWatchlistStore
    {
        private readonly SettingsFileStore _storage;
        private readonly IClock _clock;
        private bool _refuseOverwrite;

        public JsonWatchlistStore(SettingsFileStore storage, IClock clock)
        {
            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            _storage = storage;
            _clock = clock ?? new SystemClock();
        }

        public string Description
        {
            get
            {
                return SettingsLayout.DataFileName + " in " + _storage.Layout.DirectoryPath + " via " + _storage.EngineName;
            }
        }

        /// <summary>The directory the file lives in, for a status line that tells the operator where to look.</summary>
        public string DirectoryPath
        {
            get { return _storage.Layout.DirectoryPath; }
        }

        public WatchlistStoreResult Read()
        {
            SettingsResult loaded = _storage.Load();

            if (!loaded.IsUsable || loaded.Document == null)
            {
                // A refusal is remembered so a later save cannot quietly replace what is on disk.
                _refuseOverwrite = loaded.IsPreservedRefusal;
                return loaded.Outcome == SettingsOutcome.CreatedDefault
                    ? WatchlistStoreResult.From(loaded, WatchlistCatalog.Empty)
                    : WatchlistStoreResult.Refused(loaded.Outcome, loaded.Reason, loaded.Notes);
            }

            if (loaded.Outcome == SettingsOutcome.CreatedDefault)
            {
                _refuseOverwrite = false;
                return WatchlistStoreResult.From(loaded, WatchlistCatalog.Empty);
            }

            WatchlistCatalog catalog;
            string? problem;
            System.Collections.Generic.IReadOnlyList<string> notes;
            if (!SettingsMap.TryToCatalog(loaded.Document, out catalog, out problem, out notes))
            {
                // Valid JSON whose content this build cannot honour is still a refusal: writing back
                // what was understood would silently delete what was not.
                _refuseOverwrite = true;
                return WatchlistStoreResult.Refused(SettingsOutcome.RejectedCorrupt, problem, loaded.Notes, notes);
            }

            _refuseOverwrite = false;
            return WatchlistStoreResult.From(loaded, catalog, loaded.Notes, notes);
        }

        public WatchlistStoreResult Write(WatchlistCatalog catalog)
        {
            if (_refuseOverwrite)
            {
                return WatchlistStoreResult.Refused(
                    SettingsOutcome.Failed,
                    "The saved file is not something this version can read, so nothing was written over it. "
                        + "Reload to try again, or reset the saved watchlists if you mean to replace them.");
            }

            return Save(catalog);
        }

        public WatchlistStoreResult ReplaceUnreadable(WatchlistCatalog catalog)
        {
            return Save(catalog);
        }

        public WatchlistStoreResult Clear()
        {
            SettingsResult deleted = _storage.Delete();
            if (deleted.IsUsable)
            {
                _refuseOverwrite = false;
            }

            return WatchlistStoreResult.From(deleted, WatchlistCatalog.Empty, deleted.Notes);
        }

        private WatchlistStoreResult Save(WatchlistCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            SettingsResult saved = _storage.Save(SettingsMap.ToDocument(catalog, _clock.UtcNow));
            if (saved.IsUsable)
            {
                // The file now matches what is in memory, so the next save is not writing over anything
                // this build could not read.
                _refuseOverwrite = false;
            }

            // The storage notes carry the file discipline's own account - whether the previous copy was
            // backed up, restored, or left in limbo. Dropping them would report a risky save as a plain
            // success, which is exactly the silence this store exists to avoid.
            return WatchlistStoreResult.From(saved, catalog, saved.Notes);
        }
    }
}
