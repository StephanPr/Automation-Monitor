using System;
using System.Collections.Generic;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// What a settings load or save actually achieved. The distinction is the product: a missing file
    /// is a first run, a corrupt file is a diagnosis, a future version is a refusal, and "no
    /// watchlists" is only ever the boring one of those. Collapsing them would let a broken file look
    /// like an empty one and get overwritten.
    /// </summary>
    public enum SettingsOutcome
    {
        /// <summary>Nothing to load: this build starts from defaults and nothing was written.</summary>
        CreatedDefault = 0,

        /// <summary>The file was read, its version understood and its content mapped.</summary>
        Loaded = 1,

        /// <summary>The file was unusable but a backup or pending write was, so settings were recovered.</summary>
        Recovered = 2,

        /// <summary>The file is present but cannot be parsed. Preserved, never silently overwritten.</summary>
        RejectedCorrupt = 3,

        /// <summary>The file uses a newer schema. Refused whole rather than partially applied.</summary>
        RejectedFutureVersion = 4,

        /// <summary>The file is older than this build supports. Refused, with the range stated.</summary>
        RejectedOldVersion = 5,

        /// <summary>The file or directory could not be read: permissions, read-only, missing drive.</summary>
        RejectedUnreadable = 6,

        /// <summary>The document was written atomically, keeping the previous copy when there was one.</summary>
        Saved = 7,

        /// <summary>The operation failed and the existing file is unchanged.</summary>
        Failed = 8
    }

    /// <summary>Outcome of reading or writing one settings document.</summary>
    public sealed class SettingsResult
    {
        private SettingsResult(
            SettingsOutcome outcome,
            SettingsDocument? document,
            string fileName,
            string reason,
            string? backupFileName,
            string? recoveredFromFileName,
            IEnumerable<string>? notes)
        {
            Outcome = outcome;
            Document = document;
            FileName = fileName ?? string.Empty;
            Reason = reason ?? string.Empty;
            BackupFileName = backupFileName;
            RecoveredFromFileName = recoveredFromFileName;
            List<string> list = new List<string>();
            if (notes != null)
            {
                list.AddRange(notes);
            }

            Notes = list;
        }

        public SettingsOutcome Outcome { get; }

        /// <summary>The document, present for the readable outcomes and null for every refusal.</summary>
        public SettingsDocument? Document { get; }

        /// <summary>The file this result is about - a name, not a path, so a message cannot leak the profile location.</summary>
        public string FileName { get; }

        /// <summary>Sanitized explanation for a failure or refusal; empty when the operation succeeded.</summary>
        public string Reason { get; }

        /// <summary>The backup a save kept, when there was something to keep.</summary>
        public string? BackupFileName { get; }

        /// <summary>Which file recovery read, so the message can name it.</summary>
        public string? RecoveredFromFileName { get; }

        /// <summary>Anything else worth telling the operator: leftovers, unknown fields, cleanups.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Settings may be used. A recovered load counts, but stays labelled as recovered.</summary>
        public bool IsUsable
        {
            get
            {
                return Outcome == SettingsOutcome.Loaded
                    || Outcome == SettingsOutcome.Recovered
                    || Outcome == SettingsOutcome.CreatedDefault
                    || Outcome == SettingsOutcome.Saved;
            }
        }

        /// <summary>True when an existing file was refused, so a later save must preserve it first.</summary>
        public bool IsPreservedRefusal
        {
            get
            {
                return Outcome == SettingsOutcome.RejectedCorrupt
                    || Outcome == SettingsOutcome.RejectedFutureVersion
                    || Outcome == SettingsOutcome.RejectedOldVersion
                    || Outcome == SettingsOutcome.RejectedUnreadable;
            }
        }

        public static SettingsResult CreatedDefault(string fileName, IEnumerable<string>? notes = null)
        {
            return new SettingsResult(SettingsOutcome.CreatedDefault, null, fileName, string.Empty, null, null, notes);
        }

        public static SettingsResult Loaded(SettingsDocument document, string fileName, IEnumerable<string>? notes = null)
        {
            return new SettingsResult(SettingsOutcome.Loaded, document, fileName, string.Empty, null, null, notes);
        }

        public static SettingsResult Recovered(SettingsDocument document, string fileName, string recoveredFrom, IEnumerable<string>? notes = null)
        {
            return new SettingsResult(SettingsOutcome.Recovered, document, fileName, string.Empty, null, recoveredFrom, notes);
        }

        public static SettingsResult Rejected(SettingsOutcome outcome, string fileName, string reason, IEnumerable<string>? notes = null)
        {
            return new SettingsResult(outcome, null, fileName, reason, null, null, notes);
        }

        public static SettingsResult Saved(string fileName, string? backupFileName, IEnumerable<string>? notes = null)
        {
            return new SettingsResult(SettingsOutcome.Saved, null, fileName, string.Empty, backupFileName, null, notes);
        }

        public static SettingsResult Failed(string fileName, string reason, IEnumerable<string>? notes = null)
        {
            return new SettingsResult(SettingsOutcome.Failed, null, fileName, reason, null, null, notes);
        }

        public override string ToString()
        {
            return SettingsStatus.Describe(this);
        }
    }
}
