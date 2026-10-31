using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Turns one <see cref="MonitoringQuery"/> into one merged, de-duplicated, honestly labelled
    /// set of runs across many workflows. This is the only merge rule in the product: the grid, the
    /// counters, correlation and any later export all read its output, so a second merge path
    /// appearing elsewhere is a defect, not a feature.
    ///
    /// <para>
    /// Workflows are read <strong>sequentially on the caller's thread</strong>. The host hands
    /// provider work to one <c>WorkAsync</c> worker and the connected <c>IOrganizationService</c>
    /// belongs to that one connection, so fanning parallel reads over it would trade a correctness
    /// guarantee for latency the operator cannot see. The seam stays one call per workflow, so a
    /// later refresh coordinator can run a bounded number of these calls without changing any merge
    /// rule written here.
    /// </para>
    /// </summary>
    public sealed class RunQueryService
    {
        private readonly IWorkflowRunSource source;
        private readonly IClock clock;

        public RunQueryService(IWorkflowRunSource source, IClock? clock)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.clock = clock ?? new SystemClock();
        }

        /// <summary>The provider this service reads through.</summary>
        public ProviderKind Provider { get { return source.Provider; } }

        /// <summary>
        /// Read every requested workflow. It does not throw for a source problem: one unauthorized
        /// or unavailable workflow marks its own coverage and leaves the rows already gathered from
        /// the others intact. Cancellation stops the remaining reads and returns what arrived,
        /// flagged - a cancelled refresh that shows nothing is a failed interaction even when it is
        /// accurate.
        /// </summary>
        public RunQueryResult Query(MonitoringQuery query, CancellationToken cancellationToken)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            Dictionary<string, WorkflowRun> merged = new Dictionary<string, WorkflowRun>(StringComparer.Ordinal);
            List<WorkflowCoverage> coverage = new List<WorkflowCoverage>(query.Workflows.Count);
            int rowsReceived = 0;
            int duplicates = 0;
            int conflicts = 0;
            int windowExcluded = 0;
            int statusExcluded = 0;
            int undated = 0;
            bool wasCancelled = false;

            for (int index = 0; index < query.Workflows.Count; index++)
            {
                WorkflowKey workflow = query.Workflows[index];

                if (cancellationToken.IsCancellationRequested)
                {
                    wasCancelled = true;
                    coverage.Add(CancelledCoverage(workflow));
                    continue;
                }

                // Never hand a workflow to a provider that cannot serve it: this build has a run
                // provider for Power Automate only, and asking the Dataverse source about a Logic
                // App would produce an empty answer where the honest answer is "not supported".
                if (!ProviderKinds.IsImplemented(workflow.Provider))
                {
                    coverage.Add(new WorkflowCoverage(
                        workflow,
                        ProviderResultState.Unsupported,
                        0,
                        0,
                        0,
                        clock.UtcNow,
                        "No run provider for " + ProviderKinds.Prefix(workflow.Provider)
                            + " exists in this build, so this workflow contributes nothing to the result."));
                    continue;
                }

                ProviderResult<IReadOnlyList<WorkflowRun>> read;
                try
                {
                    read = source.GetRuns(workflow, query, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    wasCancelled = true;
                    coverage.Add(CancelledCoverage(workflow));
                    continue;
                }

                IReadOnlyList<WorkflowRun>? received = read.Value;
                int returned = 0;
                if (received != null)
                {
                    for (int i = 0; i < received.Count; i++)
                    {
                        WorkflowRun? run = received[i];
                        if (run == null)
                        {
                            continue;
                        }

                        returned++;

                        if (!query.MatchesStatus(run.Status))
                        {
                            statusExcluded++;
                            continue;
                        }

                        if (run.StartedAtUtc == null)
                        {
                            // No instant to place it by: kept, counted, never guessed into the window.
                            undated++;
                        }
                        else if (!query.MatchesWindow(run.StartedAtUtc))
                        {
                            windowExcluded++;
                            continue;
                        }

                        WorkflowRun? existing;
                        if (!merged.TryGetValue(run.Key.Canonical, out existing))
                        {
                            merged[run.Key.Canonical] = run;
                            continue;
                        }

                        duplicates++;
                        if (IsConflict(existing, run))
                        {
                            conflicts++;
                        }

                        if (Prefers(existing, run))
                        {
                            merged[run.Key.Canonical] = run;
                        }
                    }
                }

                rowsReceived += read.ItemsRead;
                coverage.Add(new WorkflowCoverage(
                    workflow,
                    read.State,
                    read.ItemsRead,
                    returned,
                    read.PagesRead,
                    read.CheckedAtUtc,
                    read.Message));
            }

            return Finish(query, merged, coverage, rowsReceived, duplicates, windowExcluded, statusExcluded, undated, conflicts, wasCancelled, clock.UtcNow);
        }

        /// <summary>
        /// Collapse repeated observations of one run identity into a single row - the rule any later
        /// reconciliation step reuses. Duplicates are decided on <see cref="RunKey.Canonical"/>
        /// only: the same run seen twice is one run, whatever its name now says.
        /// </summary>
        public static IReadOnlyList<WorkflowRun> MergeRuns(IEnumerable<WorkflowRun>? observed)
        {
            Dictionary<string, WorkflowRun> merged = new Dictionary<string, WorkflowRun>(StringComparer.Ordinal);
            if (observed != null)
            {
                foreach (WorkflowRun run in observed)
                {
                    if (run == null)
                    {
                        continue;
                    }

                    WorkflowRun? existing;
                    if (!merged.TryGetValue(run.Key.Canonical, out existing) || Prefers(existing, run))
                    {
                        merged[run.Key.Canonical] = run;
                    }
                }
            }

            List<WorkflowRun> ordered = new List<WorkflowRun>(merged.Values);
            ordered.Sort(CompareForDisplay);
            return ordered;
        }

        private static RunQueryResult Finish(
            MonitoringQuery query,
            Dictionary<string, WorkflowRun> merged,
            List<WorkflowCoverage> coverage,
            int rowsReceived,
            int duplicates,
            int windowExcluded,
            int statusExcluded,
            int undated,
            int conflicts,
            bool wasCancelled,
            DateTimeOffset checkedAtUtc)
        {
            List<WorkflowRun> ordered = new List<WorkflowRun>(merged.Values);
            ordered.Sort(CompareForDisplay);

            int truncated = 0;
            if (ordered.Count > query.MaxRows)
            {
                truncated = ordered.Count - query.MaxRows;
                ordered = new List<WorkflowRun>(ordered.GetRange(0, query.MaxRows));
            }

            return new RunQueryResult(
                query,
                ordered,
                coverage,
                rowsReceived,
                duplicates,
                windowExcluded,
                statusExcluded,
                undated,
                truncated,
                conflicts,
                wasCancelled,
                checkedAtUtc);
        }

        /// <summary>
        /// Which of two observations of one run to display. A terminal outcome supersedes a
        /// non-terminal one, so a poll that still said "Running" cannot hide a finished run. After
        /// that the later observation wins, because it is the one the source was last asked about;
        /// a tie keeps the row already in front of the operator so a refresh cannot flicker.
        /// </summary>
        private static bool Prefers(WorkflowRun current, WorkflowRun candidate)
        {
            bool currentTerminal = RunStatusRules.IsTerminal(current.Status);
            bool candidateTerminal = RunStatusRules.IsTerminal(candidate.Status);
            if (currentTerminal != candidateTerminal)
            {
                return candidateTerminal;
            }

            return candidate.CheckedAtUtc > current.CheckedAtUtc;
        }

        /// <summary>
        /// Two terminal observations that disagree about the outcome. Both facts are kept visible
        /// through the count: which one the source really means is not something a merge may invent.
        /// </summary>
        private static bool IsConflict(WorkflowRun current, WorkflowRun candidate)
        {
            return RunStatusRules.IsTerminal(current.Status)
                && RunStatusRules.IsTerminal(candidate.Status)
                && current.Status != candidate.Status;
        }

        private WorkflowCoverage CancelledCoverage(WorkflowKey workflow)
        {
            return new WorkflowCoverage(
                workflow,
                ProviderResultState.Unavailable,
                0,
                0,
                0,
                clock.UtcNow,
                "The refresh was cancelled before this workflow was read, so its runs are missing from this result.");
        }

        /// <summary>
        /// Newest start first, undated rows last so they cannot impersonate recent activity. Ties
        /// break on the canonical key, which keeps a grid's order stable between refreshes.
        /// </summary>
        private static int CompareForDisplay(WorkflowRun? left, WorkflowRun? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            if (left.StartedAtUtc.HasValue != right.StartedAtUtc.HasValue)
            {
                return left.StartedAtUtc.HasValue ? -1 : 1;
            }

            if (left.StartedAtUtc.HasValue)
            {
                int byStart = right.StartedAtUtc!.Value.CompareTo(left.StartedAtUtc!.Value);
                if (byStart != 0)
                {
                    return byStart;
                }
            }

            int byWorkflow = string.Compare(left.WorkflowKey.Canonical, right.WorkflowKey.Canonical, StringComparison.Ordinal);
            if (byWorkflow != 0)
            {
                return byWorkflow;
            }

            return string.Compare(left.Key.Canonical, right.Key.Canonical, StringComparison.Ordinal);
        }
    }
}

