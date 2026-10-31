using System;
using System.Globalization;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// Where the local settings format stands. The gate is deliberately blunt: this build knows one
    /// version, a newer file is refused rather than half-applied, and the same rules state what a
    /// future release must do when it adds a version.
    /// </summary>
    public static class SettingsSchema
    {
        /// <summary>The only schema this build writes.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// The oldest schema that may still be read. Raising <see cref="CurrentVersion"/> means a
        /// migration step has been added that reads older files, validates them, and writes the new
        /// version only on the next save - never a destructive in-place rewrite.
        /// </summary>
        public const int OldestSupportedVersion = 1;

        /// <summary>Discriminator written in <c>kind</c> so a foreign file is named, not merged.</summary>
        public const string Kind = "watchlist-store";

        /// <summary>Product stamp for diagnosis. Never authority for what a file contains.</summary>
        public const string WrittenBy = "Automation Monitor for XrmToolBox";

        public static bool IsCurrent(int version)
        {
            return version == CurrentVersion;
        }

        /// <summary>
        /// Whether this build may interpret a document at all. A future version is refused because
        /// ignoring fields this build does not know and then saving would drop them, which is data
        /// loss wearing an "OK" label.
        /// </summary>
        public static bool IsReadable(int version)
        {
            return version >= OldestSupportedVersion && version <= CurrentVersion;
        }

        /// <summary>True when a readable document is older than what this build writes.</summary>
        public static bool NeedsMigration(int version)
        {
            return version >= OldestSupportedVersion && version < CurrentVersion;
        }

        public static string Describe(int version)
        {
            return "schema version " + version.ToString(CultureInfo.InvariantCulture)
                + " (this build writes " + CurrentVersion.ToString(CultureInfo.InvariantCulture)
                + " and reads from " + OldestSupportedVersion.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }
}
