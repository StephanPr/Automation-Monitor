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

        /// <summary>
        /// The store's own line for a read, plus the one thing a per-environment view must add: how many of
        /// the stored lists this environment actually gets to see. An empty drop-down has to read as "0 of
        /// the 3 saved lists are for this environment" rather than as "you have nothing saved", and a
        /// connection that reported no environment id has to say so out loud, because per-environment
        /// isolation is not in force for it. This is the only place that wording exists, so no view can
        /// present a scoped or unscoped read as a different one.
        /// </summary>
        public static string DescribeForEnvironment(WatchlistStoreResult result, string? environmentId)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            string scope = WatchlistScope.Normalize(environmentId);
            int visible = result.Catalog.ForEnvironment(scope).Count;
            StringBuilder text = new StringBuilder(result.Describe());

            if (result.IsUsable)
            {
                text.Append(" - ")
                    .Append(visible.ToString(CultureInfo.InvariantCulture))
                    .Append(" of ")
                    .Append(result.Catalog.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(" saved list(s) shown here");

                if (visible == 0 && result.Catalog.Count > 0)
                {
                    text.Append(" - they were saved for other environments");
                }
            }

            if (scope.Length == 0)
            {
                text.Append(" - the connection reported no environment id, so these saved lists are not environment-scoped");
            }

            return text.ToString();
        }
    }
}
