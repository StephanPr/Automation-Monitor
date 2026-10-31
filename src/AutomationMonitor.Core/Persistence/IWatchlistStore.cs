using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// The store the rest of the product uses: saved watchlists and settings in, catalog out. This is
    /// the seam <see cref="Services.WatchlistManager"/> depends on, so neither the manager nor any view
    /// learns which serializer or file layout is in use, and swapping the JSON engine later touches
    /// neither of them.
    /// </summary>
    public interface IWatchlistStore
    {
        /// <summary>Where and how the data is kept, for the status line. Never a token or a payload.</summary>
        string Description { get; }

        /// <summary>Read the catalog. A refusal yields an empty catalog plus the reason, never a silent default.</summary>
        WatchlistStoreResult Read();

        /// <summary>
        /// Persist the catalog atomically, keeping the previous copy. Refuses to write over a file this
        /// build could not read until the caller says it means it, via <see cref="ReplaceUnreadable"/>.
        /// </summary>
        WatchlistStoreResult Write(WatchlistCatalog catalog);

        /// <summary>
        /// Write over a file that was refused on load. Only an explicit, confirmed operator action may
        /// call this, and the unusable file is preserved rather than deleted.
        /// </summary>
        WatchlistStoreResult ReplaceUnreadable(WatchlistCatalog catalog);

        /// <summary>Remove the store's file, keeping backups. An explicit operator reset, nothing else.</summary>
        WatchlistStoreResult Clear();
    }
}
