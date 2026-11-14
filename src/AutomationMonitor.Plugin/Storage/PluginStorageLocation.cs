using System;
using System.IO;
using AutomationMonitor.Core.Persistence;

namespace AutomationMonitor.Plugin.Storage
{
    /// <summary>
    /// Where this installation keeps its local files, and how that answer was reached. The monitoring core
    /// is handed a settings directory and refuses to invent one, so the plugin has to choose it. This is
    /// the only place that choice happens, and it is made from roots the host or the operator controls -
    /// never from a workflow name, an environment url or any other text that arrived from a provider.
    ///
    /// <para>
    /// The directory is a folder of its own under the host's settings directory. Being outside the
    /// <c>Plugins</c> folder the host scans is deliberate: watchlist files, backups and quarantined copies
    /// must never be loadable candidates, and saved watchlists should not vanish when the binaries are
    /// updated or removed.
    /// </para>
    /// </summary>
    public sealed class PluginStorageLocation
    {
        /// <summary>
        /// The variable an operator sets when this XrmToolBox keeps its storage somewhere other than the
        /// per-user default - the same override the installer honours. Its value is the storage root, so
        /// the settings directory is the <c>Settings</c> folder inside it.
        /// </summary>
        public const string RootOverrideVariable = "AUTOMATION_MONITOR_XTB_ROOT";

        /// <summary>The settings folder inside a storage root, matching the host's own layout.</summary>
        public const string SettingsFolderName = "Settings";

        private const string HostSource = "the settings directory reported by XrmToolBox";
        private const string OverrideSource = "the " + RootOverrideVariable + " override";

        private PluginStorageLocation(string directoryPath, string source)
        {
            DirectoryPath = directoryPath;
            Source = source;
        }

        /// <summary>The directory the settings files live in. Already this plugin's own subfolder.</summary>
        public string DirectoryPath { get; }

        /// <summary>Which trusted root produced the answer, so a status line can say where to look.</summary>
        public string Source { get; }

        /// <summary>The directory and its origin, for a message an operator can act on.</summary>
        public string Description
        {
            get { return DirectoryPath + " (" + Source + ")"; }
        }

        /// <summary>
        /// Choose the settings directory from what the host reports, or from the operator's override when
        /// the host did not answer. Everything else is a refusal with a reason: writing settings where the
        /// running host is not looking is worse than writing none, so there is no default path here and no
        /// guess at where XrmToolBox "probably" keeps its files.
        /// </summary>
        /// <param name="hostSettingsPath">The host's settings directory, if it reported one.</param>
        /// <param name="overrideRoot">The value of <see cref="RootOverrideVariable"/>, if set.</param>
        /// <param name="pluginsPath">The host's Plugins folder, used only to refuse a settings directory that would land inside it.</param>
        public static bool TryResolve(
            string? hostSettingsPath,
            string? overrideRoot,
            string? pluginsPath,
            out PluginStorageLocation? location,
            out string? reason)
        {
            location = null;
            reason = null;

            string settingsPath = (hostSettingsPath ?? string.Empty).Trim();
            if (settingsPath.Length > 0)
            {
                return TryAccept(settingsPath, HostSource, pluginsPath, out location, out reason);
            }

            string root = (overrideRoot ?? string.Empty).Trim();
            if (root.Length > 0)
            {
                return TryAccept(Path.Combine(root, SettingsFolderName), OverrideSource, pluginsPath, out location, out reason);
            }

            reason = "There is no settings directory to use: the XrmToolBox host did not report one and "
                + RootOverrideVariable + " is not set, so no watchlist was read and none can be saved.";
            return false;
        }
        private static bool TryAccept(
            string settingsPath,
            string source,
            string? pluginsPath,
            out PluginStorageLocation? location,
            out string? reason)
        {
            location = null;
            reason = null;

            string directory;
            try
            {
                directory = Path.Combine(settingsPath, SettingsLayout.DirectoryName);

                // The layout is what the store will be built from, and it rejects a path no folder can
                // hold. Building it here turns that into a refusal with a reason instead of an exception
                // thrown from the first thing the operator tries to do.
                directory = new SettingsLayout(directory).DirectoryPath;
            }
            catch (ArgumentException error)
            {
                reason = "The settings directory this build was given cannot be a folder: " + Detail(error);
                return false;
            }

            string plugins = (pluginsPath ?? string.Empty).Trim();
            if (plugins.Length > 0 && IsSameOrBelow(plugins, directory))
            {
                reason = "Watchlists cannot be kept inside the folder XrmToolBox loads plugins from, so this "
                    + "settings directory was refused: " + directory;
                return false;
            }

            location = new PluginStorageLocation(directory, source);
            return true;
        }

        /// <summary>
        /// Whether one path is the other or sits below it. Compared as spelled rather than through
        /// <see cref="Path.GetFullPath"/> on purpose: that call resolves short (8.3) names differently from
        /// the strings the host hands over, which is how a path check can disagree with itself. This is a
        /// safety refusal, not a security boundary, so spelling is enough to be useful.
        /// </summary>
        private static bool IsSameOrBelow(string ancestor, string path)
        {
            string left = Normalize(ancestor);
            string right = Normalize(path);
            if (left.Length == 0 || right.Length == 0)
            {
                return false;
            }

            return right.StartsWith(left + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(right, left, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            string text = (path ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return string.Empty;
            }

            text = text.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return text.TrimEnd(Path.DirectorySeparatorChar);
        }

        private static string Detail(Exception error)
        {
            string message = (error.Message ?? string.Empty).Trim().TrimEnd('.');
            return message.Length == 0 ? error.GetType().Name : message;
        }
    }
}
