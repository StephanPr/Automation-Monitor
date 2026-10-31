using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// Where the local settings live and what the files are called. Names are fixed constants:
    /// nothing here is ever composed from a workflow name, a solution name or any other provider
    /// text, which is what keeps a malicious or merely odd name from becoming a path.
    /// </summary>
    public sealed class SettingsLayout
    {
        /// <summary>The folder created under the host's settings directory.</summary>
        public const string DirectoryName = "AutomationMonitor";

        /// <summary>The one settings file. Watchlists and settings share it so a save cannot leave the two halves out of step.</summary>
        public const string DataFileName = "watchlists.json";

        private const string BackupSuffix = ".bak";
        private const string PendingSuffix = ".new";
        private const string QuarantinePrefix = ".corrupt-";
        private const string DisplacedPrefix = ".displaced-";

        public SettingsLayout(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException(
                    "A settings directory is required. The plugin host supplies it; nothing here guesses a location.",
                    nameof(directoryPath));
            }

            string trimmed = directoryPath.Trim();
            if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException("The settings directory path contains characters a path cannot hold.", nameof(directoryPath));
            }

            DirectoryPath = trimmed;
        }

        /// <summary>The settings directory, supplied by the host from a trusted root.</summary>
        public string DirectoryPath { get; }

        public string WatchlistFilePath
        {
            get { return Path.Combine(DirectoryPath, DataFileName); }
        }

        /// <summary>Full path for a file name in this layout. Refuses a name that could escape the directory.</summary>
        public string PathOf(string fileName)
        {
            if (!IsSafeFileName(fileName))
            {
                throw new ArgumentException("Not a settings file name: " + fileName, nameof(fileName));
            }

            return Path.Combine(DirectoryPath, fileName);
        }

        public static string BackupOf(string fileName)
        {
            return fileName + BackupSuffix;
        }

        /// <summary>The scratch name a save writes before moving it into place.</summary>
        public static string PendingOf(string fileName)
        {
            return fileName + PendingSuffix;
        }

        /// <summary>
        /// The name an unusable file is preserved under. Timestamped so two corrupt generations stay
        /// two files, and derived from the clock the caller supplies so it is deterministic in a test.
        /// </summary>
        public static string QuarantineOf(string fileName, DateTimeOffset timestampUtc)
        {
            return Stamped(fileName, QuarantinePrefix, timestampUtc);
        }

        /// <summary>
        /// The name a readable copy is kept under when a deliberate action - a reset, an override -
        /// pushes it out of the way. Nothing that was understood is preserved as if it were damage, and
        /// neither this nor a quarantined name is ever a recovery candidate: a reset has to stay reset.
        /// </summary>
        public static string DisplacedOf(string fileName, DateTimeOffset timestampUtc)
        {
            return Stamped(fileName, DisplacedPrefix, timestampUtc);
        }

        private static string Stamped(string fileName, string infix, DateTimeOffset timestampUtc)
        {
            string stem = fileName;
            string extension = string.Empty;
            int dot = fileName.LastIndexOf('.');
            if (dot > 0)
            {
                stem = fileName.Substring(0, dot);
                extension = fileName.Substring(dot);
            }

            return stem
                + infix
                + timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture)
                + extension;
        }

        /// <summary>
        /// Whether a name may be used inside the settings directory: a plain file name, no separators,
        /// no relative segments, nothing that a file system would reject.
        /// </summary>
        public static bool IsSafeFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string text = fileName!.Trim();
            if (text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            if (text.IndexOf(Path.DirectorySeparatorChar) >= 0
                || text.IndexOf(Path.AltDirectorySeparatorChar) >= 0
                || text.IndexOf(':') >= 0)
            {
                return false;
            }

            return text != "." && text != ".." && !text.Contains("..");
        }

        /// <summary>Every settings file the store recognises, including the scratch and backup names.</summary>
        public static bool IsKnownSettingsFile(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string text = fileName!.Trim();
            return string.Equals(text, DataFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, BackupOf(DataFileName), StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, PendingOf(DataFileName), StringComparison.OrdinalIgnoreCase)
                || (text.StartsWith(DataFileName.Substring(0, DataFileName.Length - 5), StringComparison.OrdinalIgnoreCase)
                    && (text.Contains(QuarantinePrefix) || text.Contains(DisplacedPrefix)));
        }

        public override string ToString()
        {
            return DirectoryPath;
        }
    }
}
