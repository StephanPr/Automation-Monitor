using System;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Plugin.Storage
{
    /// <summary>
    /// Builds the watchlist persistence stack for one open plugin instance: the directory the host
    /// implies, the disk behind the seam, the JSON engine, and the manager a view will talk to.
    ///
    /// <para>
    /// Creating it changes nothing on disk. The settings directory is made by the first save and never by a
    /// load, so opening the plugin against a read-only or roaming profile leaves the operator's files
    /// untouched until they ask for something to be kept.
    /// </para>
    ///
    /// <para>
    /// This is also the only file that names the host's <c>Paths</c>, which is what keeps the directory
    /// rules in <see cref="PluginStorageLocation"/> and the file primitives in
    /// <see cref="DiskSettingsFileAccess"/> testable without a Windows host or a running XrmToolBox.
    /// </para>
    /// </summary>
    public sealed class WatchlistStorage
    {
        private WatchlistStorage(PluginStorageLocation location, JsonWatchlistStore store, WatchlistManager manager)
        {
            Location = location;
            Store = store;
            Manager = manager;
        }

        /// <summary>The directory in use and how it was chosen, for a status line that can be checked.</summary>
        public PluginStorageLocation Location { get; }

        /// <summary>The store the manager writes through. Exposed because a reset and an override go to it directly.</summary>
        public JsonWatchlistStore Store { get; }

        /// <summary>The watchlist operations, bound to the store above.</summary>
        public WatchlistManager Manager { get; }

        /// <summary>File, directory and engine in one sentence, straight from the store that will use them.</summary>
        public string Description
        {
            get { return Store.Description; }
        }

        /// <summary>
        /// Resolve the storage directory and build the stack, or explain why there is nothing to build it
        /// on. Nothing is read or written here: loading and saving stay the caller's decision.
        /// </summary>
        public static bool TryCreate(out WatchlistStorage? storage, out string? reason)
        {
            storage = null;

            if (!PluginStorageLocation.TryResolve(HostSettingsPath(), OverrideRoot(), HostPluginsPath(),
                    out PluginStorageLocation? location, out reason)
                || location == null)
            {
                return false;
            }

            SettingsLayout layout = new SettingsLayout(location.DirectoryPath);
            SystemClock clock = new SystemClock();

            // One store instance is shared with the manager deliberately: it is what remembers that the
            // last read of the file was a refusal, and that memory is what stops a save from quietly
            // replacing content this build could not read. A second instance would reset the rule.
            JsonWatchlistStore store = new JsonWatchlistStore(
                new SettingsFileStore(layout, new DiskSettingsFileAccess(layout), new DataContractJsonSettingsSerializer(), clock),
                clock);

            storage = new WatchlistStorage(location, store, new WatchlistManager(store, clock, new GuidIdGenerator()));
            return true;
        }

        /// <summary>
        /// The settings directory the running host reports, which is the only answer that follows an
        /// installation started against a non-default storage root. Reported as unavailable rather than
        /// guessed when the host has not set one: outside XrmToolBox the value is simply not there, and a
        /// caller without a directory refuses with a reason instead of writing somewhere plausible.
        /// </summary>
        private static string? HostSettingsPath()
        {
            try
            {
                return XrmToolBox.Extensibility.Paths.SettingsPath;
            }
            catch (Exception error) when (error is ArgumentException || error is NullReferenceException)
            {
                // ArgumentException is what a path combination with no root behind it raises; a host that
                // never initialised the value can do the same through a null reference. Neither is a
                // reason to crash the control over a missing folder name.
                return null;
            }
        }

        /// <summary>The Plugins folder, used only so a settings directory inside it can be refused.</summary>
        private static string? HostPluginsPath()
        {
            try
            {
                return XrmToolBox.Extensibility.Paths.PluginsPath;
            }
            catch (Exception error) when (error is ArgumentException || error is NullReferenceException)
            {
                return null;
            }
        }

        private static string? OverrideRoot()
        {
            try
            {
                return Environment.GetEnvironmentVariable(PluginStorageLocation.RootOverrideVariable);
            }
            catch (Exception error) when (
                error is System.Security.SecurityException
                || error is NotSupportedException
                || error is UnauthorizedAccessException)
            {
                // Reading one's own environment variables can be refused by policy. That leaves the host's
                // own settings path as the only root, which TryResolve already handles.
                return null;
            }
        }
    }
}
