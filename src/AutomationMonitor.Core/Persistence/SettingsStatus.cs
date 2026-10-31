using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// The one wording for what the local settings are doing, shared by every view and the log. A
    /// status never implies success: it names the file, the outcome and what happened to the data.
    /// This is the settings equivalent of <c>SourceStatus</c> - one renderer, so a refused or
    /// recovered file cannot be described as "loaded" by whichever control happens to show it.
    /// </summary>
    public static class SettingsStatus
    {
        public static string Label(SettingsOutcome outcome)
        {
            switch (outcome)
            {
                case SettingsOutcome.Loaded:
                    return "loaded";
                case SettingsOutcome.Recovered:
                    return "RECOVERED FROM A COPY";
                case SettingsOutcome.RejectedCorrupt:
                    return "CORRUPT AND PRESERVED";
                case SettingsOutcome.RejectedFutureVersion:
                    return "WRITTEN BY A NEWER VERSION";
                case SettingsOutcome.RejectedOldVersion:
                    return "TOO OLD FOR THIS VERSION";
                case SettingsOutcome.RejectedUnreadable:
                    return "NOT READABLE";
                case SettingsOutcome.Saved:
                    return "saved";
                case SettingsOutcome.Failed:
                    return "NOT SAVED";
                default:
                    return "first run";
            }
        }

        public static string Describe(SettingsResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            StringBuilder text = new StringBuilder();
            text.Append("Local settings ").Append(result.FileName).Append(" - ").Append(Label(result.Outcome));

            if (!string.IsNullOrWhiteSpace(result.Reason))
            {
                text.Append(" - ").Append(result.Reason.Trim());
            }

            if (!string.IsNullOrEmpty(result.RecoveredFromFileName))
            {
                text.Append(" - content taken from ").Append(result.RecoveredFromFileName);
            }

            if (!string.IsNullOrEmpty(result.BackupFileName))
            {
                text.Append(" - previous copy kept as ").Append(result.BackupFileName);
            }

            foreach (string note in result.Notes)
            {
                text.Append(" - ").Append(note);
            }

            return text.ToString();
        }

        /// <summary>
        /// What is loaded plus how trustworthy that load was, in one line. A catalog that came from a
        /// recovered or refused file is still described as such, so a view cannot present a fallback
        /// as the operator's saved lists.
        /// </summary>
        public static string DescribeCatalog(WatchlistCatalog catalog, SettingsResult? loadResult)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            StringBuilder text = new StringBuilder();
            text.Append(catalog.Count.ToString(CultureInfo.InvariantCulture)).Append(" saved watchlist(s)");

            if (loadResult != null)
            {
                text.Append(" - ").Append(Label(loadResult.Outcome));
                if (!string.IsNullOrWhiteSpace(loadResult.Reason))
                {
                    text.Append(" - ").Append(loadResult.Reason.Trim());
                }
            }

            return text.ToString();
        }
    }
}
