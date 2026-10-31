using System;
using System.Collections.Generic;
using System.Globalization;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Counters over a set of normalized runs. The counts are mutually exclusive because a run has
    /// exactly one normalized status, and the failure rate states its own denominator: a rate
    /// without a basis is how a partial read starts looking like a statistic.
    /// </summary>
    public sealed class RunSummary
    {
        private RunSummary(
            int totalRuns,
            int running,
            int succeeded,
            int failed,
            int cancelled,
            int skipped,
            int timedOut,
            int unknown,
            int distinctWorkflows,
            bool isPartial)
        {
            TotalRuns = totalRuns;
            Running = running;
            Succeeded = succeeded;
            Failed = failed;
            Cancelled = cancelled;
            Skipped = skipped;
            TimedOut = timedOut;
            Unknown = unknown;
            DistinctWorkflows = distinctWorkflows;
            IsPartial = isPartial;
        }

        public int TotalRuns { get; }

        /// <summary>Not finished. Excluded from the failure-rate denominator.</summary>
        public int Running { get; }

        public int Succeeded { get; }

        public int Failed { get; }

        public int Cancelled { get; }

        public int Skipped { get; }

        public int TimedOut { get; }

        /// <summary>
        /// Runs whose provider status could not be placed in a group. They stay in the totals and
        /// are never folded into Succeeded or Failed: a large value here means the source sent
        /// statuses this build does not know, which is a coverage problem, not a health problem.
        /// </summary>
        public int Unknown { get; }

        /// <summary>Workflows represented, counted on provider-scoped identity, never on a name.</summary>
        public int DistinctWorkflows { get; }

        /// <summary>True when any contributing read was partial, unauthorized, unavailable or stale.</summary>
        public bool IsPartial { get; }

        /// <summary>Terminal runs: the only honest denominator for a failure rate.</summary>
        public int CompletedRuns { get { return Succeeded + Failed + Cancelled + Skipped + TimedOut; } }

        /// <summary>Problem runs: failed plus timed out.</summary>
        public int Unsuccessful { get { return Failed + TimedOut; } }

        /// <summary>
        /// Unsuccessful divided by <see cref="CompletedRuns"/>, or null when nothing has completed.
        /// Running and Unknown runs are not in the denominator, and a null rate must read as "no
        /// completed runs", never as zero.
        /// </summary>
        public double? FailureRate
        {
            get
            {
                int completed = CompletedRuns;
                return completed == 0 ? (double?)null : (double)Unsuccessful / completed;
            }
        }

        /// <summary>The denominator in words, for a card footer or an export header.</summary>
        public string FailureRateBasis
        {
            get
            {
                return FailureRate.HasValue
                    ? "over " + CompletedRuns.ToString(CultureInfo.InvariantCulture) + " completed run(s)"
                    : "no completed runs in this window";
            }
        }


        /// <summary>
        /// Count a set of runs. <paramref name="isPartial"/> carries the coverage of the reads those
        /// runs came from: aggregation must not wash the qualifier out, so a summary built from
        /// partial data stays labelled partial all the way to the card and the export.
        /// </summary>
        public static RunSummary FromRuns(IEnumerable<WorkflowRun>? runs, bool isPartial)
        {
            int running = 0;
            int succeeded = 0;
            int failed = 0;
            int cancelled = 0;
            int skipped = 0;
            int timedOut = 0;
            int unknown = 0;
            int total = 0;
            HashSet<string> workflows = new HashSet<string>(StringComparer.Ordinal);

            if (runs != null)
            {
                foreach (WorkflowRun run in runs)
                {
                    if (run == null)
                    {
                        continue;
                    }

                    total++;
                    workflows.Add(run.WorkflowKey.Canonical);
                    switch (run.Status)
                    {
                        case RunStatus.Running:
                            running++;
                            break;
                        case RunStatus.Succeeded:
                            succeeded++;
                            break;
                        case RunStatus.Failed:
                            failed++;
                            break;
                        case RunStatus.Cancelled:
                            cancelled++;
                            break;
                        case RunStatus.Skipped:
                            skipped++;
                            break;
                        case RunStatus.TimedOut:
                            timedOut++;
                            break;
                        default:
                            unknown++;
                            break;
                    }
                }
            }

            return new RunSummary(total, running, succeeded, failed, cancelled, skipped, timedOut, unknown, workflows.Count, isPartial);
        }

        public override string ToString()
        {
            string text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} run(s) over {1} workflow(s) - {2} ok, {3} failed, {4} timeout, {5} cancelled, {6} skipped, {7} running, {8} unknown; failure rate {9} [{10}]",
                TotalRuns,
                DistinctWorkflows,
                Succeeded,
                Failed,
                TimedOut,
                Cancelled,
                Skipped,
                Running,
                Unknown,
                FailureRate.HasValue ? FailureRate.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a",
                FailureRateBasis);

            return IsPartial ? text + " (PARTIAL)" : text;
        }
    }
}

