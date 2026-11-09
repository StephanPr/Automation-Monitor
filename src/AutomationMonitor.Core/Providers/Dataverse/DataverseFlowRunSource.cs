using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// Run history for solution-aware Power Automate cloud flows, read from the Dataverse
    /// <c>flowrun</c> table. All the decisions that matter - paging, identity, status grouping,
    /// duration basis, de-duplication and coverage - happen here against
    /// <see cref="IDataverseRunRowSource"/>, which the tests fake. The plugin supplies the
    /// Dataverse SDK glue and nothing else.
    ///
    /// <para>
    /// This provider reads and normalizes; it does not filter. The window and status criteria are
    /// sent to the source so the read can be narrowed, and <see cref="MonitoringQuery"/> is
    /// re-applied by <see cref="RunQueryService"/>, so a source that ignored a criterion cannot
    /// widen what the operator asked for. Coverage uses the shared
    /// <see cref="ProviderResultState"/> vocabulary: a table that is not enabled, a user who may
    /// not read it, and a window with no runs are three different answers.
    /// </para>
    /// </summary>
    public sealed class DataverseFlowRunSource : IWorkflowRunSource
    {
        /// <summary>Name recorded on every row this provider produces.</summary>
        public const string SourceName = "Dataverse FlowRun";

        private readonly IDataverseRunRowSource rows;
        private readonly IClock clock;

        public DataverseFlowRunSource(IDataverseRunRowSource rowSource, IClock? clock)
        {
            rows = rowSource ?? throw new ArgumentNullException(nameof(rowSource));
            this.clock = clock ?? new SystemClock();
        }

        public ProviderKind Provider { get { return ProviderKind.PowerAutomate; } }

        /// <summary>Environment of the underlying connection; null when it cannot be reported.</summary>
        public string? ScopeId { get { return rows.EnvironmentId; } }

        /// <summary>Contract form: the runs of one workflow inside one query window.</summary>
        public ProviderResult<IReadOnlyList<WorkflowRun>> GetRuns(WorkflowKey workflow, MonitoringQuery query, CancellationToken cancellationToken)
        {
            ProviderResult<RunReadResult> detailed = GetRunRead(workflow, null, query, cancellationToken);
            IReadOnlyList<WorkflowRun> runs = detailed.Value == null ? new List<WorkflowRun>() : detailed.Value.Runs;

            return Project(runs, detailed);
        }

        /// <summary>
        /// The rich read: the runs plus what the rows actually contained. Views must use this so
        /// skipped rows stay visible instead of becoming an unexplained short list.
        /// </summary>
        public ProviderResult<RunReadResult> GetRunRead(WorkflowKey workflow, string? workflowName, MonitoringQuery query, CancellationToken cancellationToken)
        {
            if (workflow == null)
            {
                throw new ArgumentNullException(nameof(workflow));
            }

            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            if (workflow.Provider != Provider)
            {
                return ProviderResult<RunReadResult>.Unsupported(
                    clock.UtcNow,
                    "This provider reads Power Automate runs only; the requested workflow belongs to "
                        + ProviderKinds.Prefix(workflow.Provider) + ".");
            }

            RunQueryCriteria criteria = RunQueryCriteria.From(query, workflow);

            try
            {
                PagedReadResult<RawRunRecord> read = PageReader.Read(
                    request => rows.ReadFlowRuns(criteria, request, cancellationToken),
                    criteria.PageSize,
                    query.MaxRows,
                    PageReader.DefaultMaxPages,
                    cancellationToken);

                RunReadResult result = Compose(read.Items, workflow, workflowName, read.State);
                DateTimeOffset checkedAtUtc = clock.UtcNow;

                if (read.IsComplete)
                {
                    return ProviderResult<RunReadResult>.Complete(result, read.PagesRead, read.Items.Count, checkedAtUtc);
                }

                return ProviderResult<RunReadResult>.Partial(
                    result,
                    read.PagesRead,
                    read.Items.Count,
                    checkedAtUtc,
                    DiagnosticsSanitizer.Describe("Only part of this flow's run history was read", read.StopReason));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                return Fail<RunReadResult>(error, 0);
            }
        }


        /// <summary>
        /// Normalize and de-duplicate the rows of one read. Duplicates collapse on the run identity
        /// - never on a name or a timestamp - and the first observation wins, so a repeat poll
        /// cannot overwrite what this same read already reported.
        /// </summary>
        private RunReadResult Compose(IReadOnlyList<RawRunRecord>? items, WorkflowKey workflow, string? workflowName, ProviderResultState coverageState)
        {
            DateTimeOffset checkedAtUtc = clock.UtcNow;
            List<WorkflowRun> runs = new List<WorkflowRun>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int rowsRead = 0;
            int duplicates = 0;
            int missingIdentity = 0;
            int foreign = 0;

            if (items != null)
            {
                foreach (RawRunRecord item in items)
                {
                    rowsRead++;

                    WorkflowRun? run;
                    RunRowDisposition disposition = DataverseRunNormalizer.TryToRun(
                        item, workflow, workflowName, SourceName, checkedAtUtc, coverageState, out run);

                    if (disposition == RunRowDisposition.MissingIdentity)
                    {
                        missingIdentity++;
                        continue;
                    }

                    if (disposition == RunRowDisposition.BelongsToAnotherWorkflow)
                    {
                        foreign++;
                        continue;
                    }

                    if (run == null || !seen.Add(run.Key.Canonical))
                    {
                        duplicates++;
                        continue;
                    }

                    runs.Add(run);
                }
            }

            return new RunReadResult(runs, rowsRead, duplicates, missingIdentity, foreign);
        }

        /// <summary>
        /// Project the rich read onto the contract without losing the state, the page count, the
        /// checked-at instant or the reason. A failed read stays failed here.
        /// </summary>
        private static ProviderResult<IReadOnlyList<WorkflowRun>> Project(IReadOnlyList<WorkflowRun> runs, ProviderResult<RunReadResult> source)
        {
            DateTimeOffset checkedAtUtc = source.CheckedAtUtc;

            switch (source.State)
            {
                case ProviderResultState.Complete:
                    return ProviderResult<IReadOnlyList<WorkflowRun>>.Complete(runs, source.PagesRead, source.ItemsRead, checkedAtUtc);
                case ProviderResultState.Partial:
                    return ProviderResult<IReadOnlyList<WorkflowRun>>.Partial(runs, source.PagesRead, source.ItemsRead, checkedAtUtc, source.Message);
                case ProviderResultState.Unauthorized:
                    return ProviderResult<IReadOnlyList<WorkflowRun>>.Unauthorized(source.PagesRead, checkedAtUtc, source.Message);
                case ProviderResultState.Unsupported:
                    return ProviderResult<IReadOnlyList<WorkflowRun>>.Unsupported(checkedAtUtc, source.Message);
                case ProviderResultState.Stale:
                    return ProviderResult<IReadOnlyList<WorkflowRun>>.Stale(runs, source.PagesRead, source.ItemsRead, checkedAtUtc, source.Message);
                default:
                    return ProviderResult<IReadOnlyList<WorkflowRun>>.Unavailable(source.PagesRead, checkedAtUtc, source.Message);
            }
        }

        /// <summary>
        /// Turn a thrown Dataverse error into an honest state plus an actionable, sanitized
        /// message, exactly as discovery does. A missing or disabled <c>flowrun</c> table is
        /// <see cref="ProviderResultState.Unsupported"/>, never an empty history.
        /// </summary>
        private ProviderResult<T> Fail<T>(Exception error, int pagesRead)
        {
            ProviderResultState state = ProviderErrorClassifier.Classify(error.GetType().Name, error.Message);
            ProviderErrorClassifier.DefectKind defect = ProviderErrorClassifier.DefectKindOf(error.GetType().Name, error.Message);
            string reason = DiagnosticsSanitizer.Sanitize(error.Message);
            string message = DiagnosticsSanitizer.Describe(RunReadAdviceFor(state, defect), reason);
            DateTimeOffset checkedAtUtc = clock.UtcNow;

            switch (state)
            {
                case ProviderResultState.Unauthorized:
                    return ProviderResult<T>.Unauthorized(pagesRead, checkedAtUtc, message);
                case ProviderResultState.Unsupported:
                    return ProviderResult<T>.Unsupported(checkedAtUtc, message);
                default:
                    return ProviderResult<T>.Unavailable(pagesRead, checkedAtUtc, message);
            }
        }

        /// <summary>
        /// Run-history wording. The shared classifier describes discovery permissions, while an
        /// absent run table is usually an ingestion or retention setting, and that difference is
        /// what makes an empty grid explainable instead of mysterious.
        /// </summary>
        private static string RunReadAdviceFor(ProviderResultState state, ProviderErrorClassifier.DefectKind defect)
        {
            // Either local kind: the run table is not missing and the connection is not down, so the
            // shared wording that names this build is the only honest one here.
            if (defect != ProviderErrorClassifier.DefectKind.None)
            {
                return ProviderErrorClassifier.AdviceFor(state, defect);
            }

            switch (state)
            {
                case ProviderResultState.Unauthorized:
                    return "The connected user may not read flow run rows, so their runs are missing from this view. Check the FlowRun read permission in the security role.";
                case ProviderResultState.Unsupported:
                    return "This environment does not expose the flow run table. Run history exists only for solution-aware flows, and only once run history ingestion is enabled.";
                case ProviderResultState.Unavailable:
                    return "The environment did not answer the run query. Check the connection and try again.";
                default:
                    return ProviderErrorClassifier.AdviceFor(state);
            }
        }

    }
}

