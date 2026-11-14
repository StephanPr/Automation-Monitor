using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AutomationMonitor.Core.Persistence;

namespace AutomationMonitor.Plugin.Storage
{
    /// <summary>
    /// The settings directory on a real disk: the <c>System.IO</c> half of <see cref="ISettingsFileAccess"/>
    /// and nothing else. What is stored, in what order, and what an interrupted write means are all decided
    /// by <see cref="SettingsFileStore"/>; this class performs the primitive and reports honestly when the
    /// file system will not allow it. It never throws where the store asked a question, because the store's
    /// answer to a failed primitive is a message for the operator while an exception would be a crash.
    ///
    /// <para>
    /// Three file-system realities shape the body, none of them visible from the fake the core is tested
    /// against: <see cref="File.Move(string, string)"/> does not replace an existing target on .NET
    /// Framework, while the core's save moves the current file onto the previous backup on every save after
    /// the first; a file can be held for a moment by a search indexer, a scanner or a roaming agent, so
    /// reads share access rather than turning a momentary lock into unreadable settings; and a name the
    /// directory will not accept is refused before anything is touched, so no caller can reach outside the
    /// settings folder.
    /// </para>
    /// </summary>
    public sealed class DiskSettingsFileAccess : ISettingsFileAccess
    {
        private const string UnsafeName = "is not a plain file name, so nothing on disk was touched";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly string _directoryPath;

        public DiskSettingsFileAccess(SettingsLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            _directoryPath = layout.DirectoryPath;
        }

        public string DirectoryPath
        {
            get { return _directoryPath; }
        }

        public bool DirectoryExists()
        {
            return Directory.Exists(_directoryPath);
        }

        public bool TryCreateDirectory(out string? reason)
        {
            reason = null;
            try
            {
                // Idempotent, and creates parents, which is what a first save into a settings directory
                // that does not exist yet needs. A read-only or roaming profile arrives as a reason.
                Directory.CreateDirectory(_directoryPath);
                return true;
            }
            catch (Exception error) when (IsFileSystemFailure(error))
            {
                reason = "the settings folder could not be created: " + Detail(error);
                return false;
            }
        }

        public bool FileExists(string fileName)
        {
            if (!IsSafe(fileName))
            {
                return false;
            }

            return File.Exists(Place(fileName));
        }

        public bool TryRead(string fileName, out string? content, out string? reason)
        {
            content = null;
            reason = null;
            if (!IsSafe(fileName))
            {
                reason = Quote(fileName) + " " + UnsafeName;
                return false;
            }

            string path = Place(fileName);
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    content = reader.ReadToEnd();
                }

                return true;
            }
            catch (FileNotFoundException)
            {
                // A missing file is not damage. The store checks existence first, so reaching this line
                // means the file went away in between, which is still answered the way the seam asks.
                reason = "no such file: " + fileName;
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                reason = "no such file: " + fileName;
                return false;
            }
            catch (Exception error) when (IsFileSystemFailure(error))
            {
                reason = "the file " + Quote(fileName) + " could not be read: " + Detail(error);
                return false;
            }
        }

        public bool TryWrite(string fileName, string content, out string? reason)
        {
            reason = null;
            if (!IsSafe(fileName))
            {
                reason = Quote(fileName) + " " + UnsafeName;
                return false;
            }

            if (content == null)
            {
                reason = "no content was given for " + Quote(fileName) + ", so nothing was written";
                return false;
            }

            string path = Place(fileName);
            try
            {
                // Whole-file, exclusive, no byte-order mark: the file is complete before it is closed, and
                // the store's write-a-pending-name-then-move discipline stays the only atomicity there is.
                // Truncating an existing name is fine here because the store never writes live content.
                using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, Utf8NoBom))
                {
                    writer.Write(content);
                }

                return true;
            }
            catch (Exception error) when (IsFileSystemFailure(error))
            {
                reason = "writing " + Quote(fileName) + " failed: " + Detail(error);
                return false;
            }
        }

        public bool TryMove(string sourceFileName, string targetFileName, out string? reason)
        {
            reason = null;
            if (!IsSafe(sourceFileName))
            {
                reason = Quote(sourceFileName) + " " + UnsafeName;
                return false;
            }

            if (!IsSafe(targetFileName))
            {
                reason = Quote(targetFileName) + " " + UnsafeName;
                return false;
            }

            string source = Place(sourceFileName);
            string target = Place(targetFileName);
            try
            {
                if (!File.Exists(source))
                {
                    reason = "no such file: " + sourceFileName;
                    return false;
                }

                if (File.Exists(target))
                {
                    // File.Move on .NET Framework refuses an existing target, and the store's save moves
                    // the current file onto the previous backup on every save after the first, so
                    // replacing is the behaviour this seam documents. The name replaced is always an older
                    // generation, never the newest content, and a read-only backup is refused by name
                    // rather than with an opaque access error.
                    if ((File.GetAttributes(target) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        reason = Quote(targetFileName) + " is read-only, so it was not replaced. Nothing was changed.";
                        return false;
                    }

                    File.Delete(target);
                }

                File.Move(source, target);
                return true;
            }
            catch (Exception error) when (IsFileSystemFailure(error))
            {
                reason = "moving " + Quote(sourceFileName) + " onto " + Quote(targetFileName) + " failed: "
                    + Detail(error) + WhereTheContentIs(sourceFileName, targetFileName);
                return false;
            }
        }
        /// <summary>
        /// Which name holds the content after a move could not be completed. The store's notes are the
        /// operator's only guide when a save fails halfway, so a refusal that cannot say where the data
        /// ended up would be worse than no refusal at all.
        /// </summary>
        private string WhereTheContentIs(string sourceFileName, string targetFileName)
        {
            bool sourcePresent = File.Exists(Place(sourceFileName));
            bool targetPresent = File.Exists(Place(targetFileName));

            if (sourcePresent && targetPresent)
            {
                return " Both files are still present, so nothing was lost.";
            }

            if (sourcePresent)
            {
                return " " + Quote(sourceFileName) + " still holds its content.";
            }

            if (targetPresent)
            {
                return " " + Quote(targetFileName) + " now holds the content.";
            }

            return " Neither name is present now, so check the settings folder before saving again.";
        }

        public bool TryDelete(string fileName, out string? reason)
        {
            reason = null;
            if (!IsSafe(fileName))
            {
                reason = Quote(fileName) + " " + UnsafeName;
                return false;
            }

            string path = Place(fileName);
            if (!File.Exists(path))
            {
                // File.Delete stays silent about a missing file. The seam reports it, because the store
                // uses this to clean up and needs to know whether the name it asked about was really there.
                reason = "no such file: " + fileName;
                return false;
            }

            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception error) when (IsFileSystemFailure(error))
            {
                reason = "deleting " + Quote(fileName) + " failed: " + Detail(error);
                return false;
            }
        }

        public IReadOnlyList<string> ListFileNames()
        {
            try
            {
                string[] found = Directory.GetFiles(_directoryPath);
                List<string> names = new List<string>(found.Length);
                foreach (string file in found)
                {
                    string name = Path.GetFileName(file);
                    if (IsSafe(name))
                    {
                        names.Add(name);
                    }
                }

                return names;
            }
            catch (Exception error) when (IsFileSystemFailure(error))
            {
                // This list only ever drives cleanup of leftovers, and the seam has no channel for a
                // reason. "Nothing to clean up" is the honest reading of a folder that cannot be listed -
                // never an exception thrown into the middle of a save that was otherwise working.
                return new List<string>();
            }
        }

        private string Place(string fileName)
        {
            return Path.Combine(_directoryPath, fileName!.Trim());
        }

        private static bool IsSafe(string? fileName)
        {
            return SettingsLayout.IsSafeFileName(fileName);
        }

        private static string Quote(string? fileName)
        {
            return "\"" + (fileName ?? string.Empty).Trim() + "\"";
        }

        /// <summary>
        /// The failures a file system produces. Anything else is a bug in this build and is allowed to
        /// reach the caller instead of being reported as a refused primitive.
        /// </summary>
        private static bool IsFileSystemFailure(Exception error)
        {
            return error is IOException
                || error is UnauthorizedAccessException
                || error is ArgumentException
                || error is NotSupportedException
                || error is System.Security.SecurityException;
        }

        private static string Detail(Exception error)
        {
            string message = (error.Message ?? string.Empty).Trim().TrimEnd('.');
            return message.Length == 0 ? error.GetType().Name : message;
        }
    }
}
