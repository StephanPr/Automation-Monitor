using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Abstractions;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// Reads and writes the local settings file: the schema gate, the atomic write, the backup, the
    /// recovery candidates and the preservation of anything unusable. This is the only place that
    /// decides the order of file operations, so an interrupted write has exactly one answer instead of
    /// one per caller.
    ///
    /// <para>
    /// A save writes <c>watchlists.json.new</c>, moves the current file to <c>watchlists.json.bak</c>,
    /// then moves the pending file into place. The crash points are therefore a stale pending file
    /// (harmless, and a recovery candidate) or the original living only as the backup (recovered on the
    /// next load). Nothing is ever written straight over the operator's file, and a file this build
    /// cannot understand is moved aside under a timestamped name rather than replaced.
    /// </para>
    /// </summary>
    public sealed class SettingsFileStore
    {
        private readonly SettingsLayout _layout;
        private readonly ISettingsFileAccess _files;
        private readonly ISettingsSerializer _serializer;
        private readonly IClock _clock;

        public SettingsFileStore(SettingsLayout layout, ISettingsFileAccess files, ISettingsSerializer serializer, IClock clock)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (files == null)
            {
                throw new ArgumentNullException(nameof(files));
            }

            if (serializer == null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            _layout = layout;
            _files = files;
            _serializer = serializer;
            _clock = clock ?? new SystemClock();
        }

        public SettingsLayout Layout
        {
            get { return _layout; }
        }

        /// <summary>
        /// The time source every timestamp in this layer comes from. Exposed so the store above it
        /// stamps its own content with the same clock instead of reading the wall clock separately.
        /// </summary>
        public IClock Clock
        {
            get { return _clock; }
        }

        /// <summary>Which engine produces the text, for the status line and for diagnosis.</summary>
        public string EngineName
        {
            get { return _serializer.EngineName; }
        }

        /// <summary>
        /// Read the settings file. Never writes: a load that found nothing leaves the disk exactly as it
        /// was, so a first run and a failed run stay different until something is really saved.
        /// </summary>
        public SettingsResult Load()
        {
            string fileName = SettingsLayout.DataFileName;
            if (!_files.DirectoryExists())
            {
                return SettingsResult.CreatedDefault(fileName);
            }

            if (!_files.FileExists(fileName))
            {
                // The file name is missing but a copy may not be: the crash point between moving the
                // original to the backup and moving the pending write into place leaves exactly this
                // state, and treating it as a first run would lose the operator's saved lists.
                SettingsResult? orphaned = TryRecover(fileName, "the settings file is missing");
                return orphaned ?? SettingsResult.CreatedDefault(fileName);
            }

            string? content;
            string? readReason;
            if (!_files.TryRead(fileName, out content, out readReason))
            {
                return SettingsResult.Rejected(
                    SettingsOutcome.RejectedUnreadable,
                    fileName,
                    readReason ?? "The settings file could not be read, so nothing was loaded.");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                // An empty file is not "no settings": something truncated it and the previous copy may
                // still be good, so it is treated exactly like unparseable content.
                return RejectOrRecover(fileName, "The settings file is empty.");
            }

            SettingsDocument? document;
            string? parseReason;
            if (TryReadDocument(content!, out document, out parseReason))
            {
                return AcceptVersion(document!, fileName, SettingsOutcome.Loaded, null);
            }

            return RejectOrRecover(fileName, parseReason ?? "The settings file could not be understood.");
        }

        /// <summary>
        /// Write the document atomically, keeping the previous copy. On any failure the original name is
        /// restored from the backup made moments earlier, so a failed save cannot leave less data than it
        /// started with.
        /// </summary>
        public SettingsResult Save(SettingsDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            string fileName = SettingsLayout.DataFileName;
            string pendingName = SettingsLayout.PendingOf(fileName);
            string backupName = SettingsLayout.BackupOf(fileName);

            document.SchemaVersion = SettingsSchema.CurrentVersion;
            document.Kind = SettingsSchema.Kind;
            document.WrittenBy = SettingsSchema.WrittenBy;
            document.SavedAtUtc = SettingsFormat.Timestamp(_clock.UtcNow);

            string text;
            try
            {
                text = _serializer.Serialize(document);
            }
            catch (SettingsSerializationException error)
            {
                return SettingsResult.Failed(fileName, error.Reason);
            }

            string? directoryReason;
            if (!_files.DirectoryExists() && !_files.TryCreateDirectory(out directoryReason))
            {
                return SettingsResult.Failed(
                    fileName,
                    directoryReason ?? "The settings directory could not be created, so nothing was saved.");
            }

            string? writeReason;
            if (!_files.TryWrite(pendingName, text, out writeReason))
            {
                return SettingsResult.Failed(fileName, writeReason ?? "The new settings could not be written, so nothing changed.");
            }

            bool keptBackup = false;
            if (_files.FileExists(fileName))
            {
                string? backupReason;
                if (!_files.TryMove(fileName, backupName, out backupReason))
                {
                    string? discardReason;
                    _files.TryDelete(pendingName, out discardReason);
                    return SettingsResult.Failed(
                        fileName,
                        (backupReason ?? "The current settings file could not be backed up.") + " Nothing was changed.");
                }

                keptBackup = true;
            }

            string? moveReason;
            if (_files.TryMove(pendingName, fileName, out moveReason))
            {
                return SettingsResult.Saved(fileName, keptBackup ? backupName : null);
            }

            List<string> notes = new List<string>();
            string? cleanupReason;
            _files.TryDelete(pendingName, out cleanupReason);
            if (keptBackup)
            {
                string? restoreReason;
                if (_files.TryMove(backupName, fileName, out restoreReason))
                {
                    notes.Add("the previous copy was put back, so the saved watchlists are unchanged");
                }
                else
                {
                    notes.Add("the previous copy is still in " + backupName + " and can be restored by hand");
                }
            }

            return SettingsResult.Failed(
                fileName,
                (moveReason ?? "The new settings could not be put in place."),
                notes);
        }

        /// <summary>
        /// Remove the settings file, keeping any backup. Only an explicit operator action may call
        /// this, and the caller must have asked first: a reset is a data change, not a refresh.
        /// </summary>
        /// <summary>
        /// Remove the settings file. Only an explicit operator action may call this, and the caller
        /// must have asked first: a reset is a data change, not a refresh. The newest content always
        /// survives it, and nothing left on disk may resurrect what was just cleared.
        /// </summary>
        public SettingsResult Delete()
        {
            string fileName = SettingsLayout.DataFileName;
            if (!_files.FileExists(fileName))
            {
                return SettingsResult.CreatedDefault(fileName, new string[] { "there was no settings file to remove" });
            }

            List<string> notes = new List<string>();
            string backupName = SettingsLayout.BackupOf(fileName);
            string pendingName = SettingsLayout.PendingOf(fileName);
            string? reason;

            // The newest content is kept rather than shredded: losing the last saved watchlists to a
            // click is the same data loss as losing them to a crash. It deliberately does NOT go into the
            // backup slot, because recovery reads the backup - a reset must stay reset until the operator
            // saves something new.
            string removedName = SettingsLayout.DisplacedOf(fileName, _clock.UtcNow);
            if (!_files.TryMove(fileName, removedName, out reason))
            {
                return SettingsResult.Failed(
                    fileName,
                    (reason ?? "The settings file could not be moved aside.") + " Nothing was removed.");
            }

            notes.Add("the watchlists that were removed are kept as \"" + removedName + "\"");

            // Every remaining recovery candidate goes with it, kept under its own dated name so nothing is
            // destroyed while nothing is quietly restored either.
            foreach (string candidate in new[] { backupName, pendingName })
            {
                if (!_files.FileExists(candidate))
                {
                    continue;
                }

                string displacedName = SettingsLayout.DisplacedOf(candidate, _clock.UtcNow);
                string? displacedReason;
                if (_files.TryMove(candidate, displacedName, out displacedReason))
                {
                    notes.Add("the older copy \"" + candidate + "\" is kept as \"" + displacedName + "\" and will not come back on its own");
                }
                else
                {
                    notes.Add("the older copy \"" + candidate + "\" could not be moved aside"
                        + (string.IsNullOrWhiteSpace(displacedReason) ? "." : ": " + displacedReason!.Trim())
                        + " - a later load may still recover it, so remove it by hand before saving");
                }
            }

            return SettingsResult.Saved(fileName, removedName, notes);
        }

        private bool TryReadDocument(string content, out SettingsDocument? document, out string? reason)
        {
            document = null;
            reason = null;
            try
            {
                document = _serializer.Deserialize(content);
                return true;
            }
            catch (SettingsSerializationException error)
            {
                reason = error.Reason;
                return false;
            }
        }

        /// <summary>
        /// Apply the schema gate to a parsed document. A newer or too-old file is refused whole and
        /// stays on disk as it is; a readable older one is accepted in memory and labelled, with the
        /// rewrite happening only through a later save that also keeps a backup.
        /// </summary>
        private SettingsResult AcceptVersion(
            SettingsDocument document,
            string fileName,
            SettingsOutcome outcome,
            string? recoveredFrom,
            IEnumerable<string>? notes = null)
        {
            List<string> messages = new List<string>();
            if (notes != null)
            {
                messages.AddRange(notes);
            }

            if (document.SchemaVersion > SettingsSchema.CurrentVersion)
            {
                return SettingsResult.Rejected(
                    SettingsOutcome.RejectedFutureVersion,
                    fileName,
                    "This file was written by a newer version of Automation Monitor ("
                        + SettingsSchema.Describe(document.SchemaVersion)
                        + "). Its content was left untouched: upgrade the plugin rather than editing the file.",
                    messages);
            }

            if (document.SchemaVersion < SettingsSchema.OldestSupportedVersion)
            {
                return SettingsResult.Rejected(
                    SettingsOutcome.RejectedOldVersion,
                    fileName,
                    "This file uses a schema this version no longer reads ("
                        + SettingsSchema.Describe(document.SchemaVersion) + "). Its content was left untouched.",
                    messages);
            }

            if (SettingsSchema.NeedsMigration(document.SchemaVersion))
            {
                messages.Add("read " + SettingsSchema.Describe(document.SchemaVersion)
                    + "; it will be written in the current format the next time something is saved");
            }

            if (outcome == SettingsOutcome.Recovered)
            {
                return SettingsResult.Recovered(document, fileName, recoveredFrom ?? fileName, messages);
            }

            return SettingsResult.Loaded(document, fileName, messages);
        }

        /// <summary>
        /// The file is present but unusable, or missing. Look for a recovery candidate - the backup
        /// first, then a pending write that never got moved - and preserve whatever unusable file is
        /// there under a timestamped name, so the evidence outlives what the operator does next.
        /// Null means no candidate could be read.
        /// </summary>
        private SettingsResult? TryRecover(string fileName, string reason)
        {
            List<string> notes = new List<string>();
            string[] candidates = new[] { SettingsLayout.BackupOf(fileName), SettingsLayout.PendingOf(fileName) };

            foreach (string candidate in candidates)
            {
                if (!_files.FileExists(candidate))
                {
                    continue;
                }

                string? candidateContent;
                string? candidateReason;
                if (!_files.TryRead(candidate, out candidateContent, out candidateReason) || string.IsNullOrWhiteSpace(candidateContent))
                {
                    notes.Add(candidate + " could not be read either");
                    continue;
                }

                SettingsDocument? recovered;
                string? recoveredReason;
                // Judge the candidate's CONTENT, never its name: parsing "watchlists.json.bak" as JSON
                // fails, which would turn every recoverable file into a reported first run.
                if (!TryReadDocument(candidateContent!, out recovered, out recoveredReason) || recovered == null)
                {
                    notes.Add(candidate + " is unreadable too");
                    continue;
                }

                notes.Add("recovered because " + reason);
                PreserveUnusable(fileName, notes);
                return AcceptVersion(recovered!, fileName, SettingsOutcome.Recovered, candidate, notes);
            }

            return null;
        }

        private SettingsResult RejectOrRecover(string fileName, string reason)
        {
            SettingsResult? recovered = TryRecover(fileName, reason);
            if (recovered != null)
            {
                return recovered;
            }

            List<string> notes = new List<string>();
            PreserveUnusable(fileName, notes);
            return SettingsResult.Rejected(SettingsOutcome.RejectedCorrupt, fileName, reason, notes);
        }

        /// <summary>Move an unusable file aside and record what happened, in the note the operator sees.</summary>
        private void PreserveUnusable(string fileName, List<string> notes)
        {
            if (!_files.FileExists(fileName))
            {
                return;
            }

            string target = SettingsLayout.QuarantineOf(fileName, _clock.UtcNow);
            string? reason;
            if (_files.TryMove(fileName, target, out reason))
            {
                notes.Add("the unusable file was preserved as " + target);
                return;
            }

            notes.Add("the unusable file could not be moved aside, so copy " + _layout.PathOf(fileName) + " before saving anything");
        }
    }
}
