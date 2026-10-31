using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// The cross-provider outcome vocabulary: provider status text in, normalized group out.
    ///
    /// <para>
    /// Both Power Automate (<c>flowrun.status</c>, a free-text String) and Logic Apps (an enum in
    /// its REST API) spell the same outcomes almost identically, so one table serves both and a
    /// later provider adds aliases here rather than inventing a second mapping. Providers keep
    /// their own status text on the run record: this table is a grouping, not a replacement.
    /// </para>
    /// </summary>
    public static class StatusNormalizer
    {
        private static readonly Dictionary<string, RunStatus> Map =
            new Dictionary<string, RunStatus>(StringComparer.OrdinalIgnoreCase)
            {
                // Finished cleanly.
                { "succeeded", RunStatus.Succeeded },
                { "passed", RunStatus.Succeeded },
                { "completed", RunStatus.Succeeded },

                // Not finished. Waiting and Suspended still consume quota and can fail later, so
                // they are not skipped and not terminal; they need operator attention.
                { "running", RunStatus.Running },
                { "inprogress", RunStatus.Running },
                { "executing", RunStatus.Running },
                { "waiting", RunStatus.Running },
                { "suspended", RunStatus.Running },
                { "paused", RunStatus.Running },

                // Problem outcomes. TimedOut stays distinct because the raw value does.
                { "failed", RunStatus.Failed },
                { "faulted", RunStatus.Failed },
                { "aborted", RunStatus.Failed },
                { "canceled", RunStatus.Failed },
                { "errored", RunStatus.Failed },
                { "timedout", RunStatus.TimedOut },
                { "timeout", RunStatus.TimedOut },

                // Stopped without being a failure.
                { "cancelled", RunStatus.Cancelled },
                { "skipped", RunStatus.Skipped },
                { "atomicsskipped", RunStatus.Skipped }
            };

        /// <summary>
        /// Group a provider status. Null, blank and unrecognised text are all
        /// <see cref="RunStatus.Unknown"/>: they are surfaced and counted, never coerced into a
        /// success and never dropped.
        /// </summary>
        public static RunStatus Normalize(string? providerStatus)
        {
            if (string.IsNullOrWhiteSpace(providerStatus))
            {
                return RunStatus.Unknown;
            }

            RunStatus status;
            return Map.TryGetValue(providerStatus!.Trim(), out status) ? status : RunStatus.Unknown;
        }

        /// <summary>
        /// The provider status texts that belong to one group, used to build a server-side filter
        /// from a normalized selection. Spelling variants are included so a filter cannot quietly
        /// miss a run the client would have counted as that group.
        /// </summary>
        public static IReadOnlyList<string> ProviderValuesFor(RunStatus status)
        {
            List<string> values = new List<string>();
            foreach (KeyValuePair<string, RunStatus> entry in Map)
            {
                if (entry.Value == status)
                {
                    values.Add(entry.Key);
                }
            }

            values.Sort(StringComparer.Ordinal);
            return values;
        }
    }
}
