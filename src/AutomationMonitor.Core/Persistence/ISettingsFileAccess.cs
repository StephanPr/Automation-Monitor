using System;
using System.Collections.Generic;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// The file primitives the settings store needs. The monitoring core owns every decision about
    /// what is stored, in what order and what happens when a write is interrupted; this seam only
    /// performs the primitive operation the host's file system allows. Keeping it this small is what
    /// lets atomic write, backup, rollback and recovery be tested on Linux with a fake, while the
    /// plugin contributes 60 lines of <c>System.IO</c>.
    ///
    /// <para>
    /// Methods take a <em>file name</em>, never a path, and a name containing a separator or
    /// <c>..</c> must be refused: no caller can escape the settings directory, and provider text can
    /// never become a path.
    /// </para>
    /// </summary>
    public interface ISettingsFileAccess
    {
        /// <summary>
        /// The settings directory, for messages. Built by the host from a trusted root only - never
        /// from provider or operator text - and never used to read anything outside itself.
        /// </summary>
        string DirectoryPath { get; }

        bool DirectoryExists();

        /// <summary>Create the settings directory, including parents. A read-only or inaccessible root is a reason, not an exception.</summary>
        bool TryCreateDirectory(out string? reason);

        bool FileExists(string fileName);

        /// <summary>Read whole text. Missing is not an error: content stays null and the result is true only when the file was read.</summary>
        bool TryRead(string fileName, out string? content, out string? reason);

        /// <summary>Replace one file's content. Atomicity is the store's job, by writing a pending name and moving it.</summary>
        bool TryWrite(string fileName, string content, out string? reason);

        /// <summary>Move a file, replacing the target if it exists.</summary>
        bool TryMove(string sourceFileName, string targetFileName, out string? reason);

        bool TryDelete(string fileName, out string? reason);

        /// <summary>Names of the files present, used to find backups and leftovers to clean up.</summary>
        IReadOnlyList<string> ListFileNames();
    }
}
