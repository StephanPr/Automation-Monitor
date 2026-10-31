using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>One store operation: the catalog as it now stands plus the truth about how it got there.</summary>
    public sealed class WatchlistStoreResult
    {
        private WatchlistStoreResult(
            SettingsOutcome outcome,
            WatchlistCatalog catalog,
            string? reason,
            string? backupFileName,
            string? recoveredFromFileName,
            IEnumerable<string>? notes)
        {
            Outcome = outcome;
            Catalog = catalog ?? WatchlistCatalog.Empty;
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

        /// <summary>The catalog. Always present: empty for a first run or a refusal, never null.</summary>
        public WatchlistCatalog Catalog { get; }

        /// <summary>Sanitized reason for a refusal or failure; empty when the operation succeeded.</summary>
        public string Reason { get; }

        public string? BackupFileName { get; }

        public string? RecoveredFromFileName { get; }

        /// <summary>Field-level notes: what was skipped, what was repaired, what was preserved.</summary>
        public IReadOnlyList<string> Notes { get; }

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

        /// <summary>An existing file was refused and its content was left in place or preserved.</summary>
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

        public static WatchlistStoreResult From(
            SettingsResult storage,
            WatchlistCatalog catalog,
            IEnumerable<string>? firstNotes = null,
            IEnumerable<string>? secondNotes = null)
        {
            return new WatchlistStoreResult(
                storage.Outcome,
                catalog,
                storage.Reason,
                storage.BackupFileName,
                storage.RecoveredFromFileName,
                Combine(firstNotes, secondNotes));
        }

        public static WatchlistStoreResult Refused(
            SettingsOutcome outcome,
            string? reason,
            IEnumerable<string>? firstNotes = null,
            IEnumerable<string>? secondNotes = null)
        {
            return new WatchlistStoreResult(outcome, WatchlistCatalog.Empty, reason, null, null, Combine(firstNotes, secondNotes));
        }

        private static List<string> Combine(IEnumerable<string>? first, IEnumerable<string>? second)
        {
            List<string> notes = new List<string>();
            if (first != null)
            {
                notes.AddRange(first);
            }

            if (second != null)
            {
                notes.AddRange(second);
            }

            return notes;
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("Saved watchlists - ")
                .Append(SettingsStatus.Label(Outcome))
                .Append(" (")
                .Append(Catalog.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" list(s))");

            if (!string.IsNullOrWhiteSpace(Reason))
            {
                text.Append(" - ").Append(Reason.Trim());
            }

            if (!string.IsNullOrEmpty(RecoveredFromFileName))
            {
                text.Append(" - content taken from ").Append(RecoveredFromFileName);
            }

            if (!string.IsNullOrEmpty(BackupFileName))
            {
                text.Append(" - previous copy kept as ").Append(BackupFileName);
            }

            foreach (string note in Notes)
            {
                text.Append(" - ").Append(note);
            }

            return text.ToString();
        }

        public override string ToString()
        {
            return Describe();
        }
    }
}
