using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// Solution-aware cloud flow discovery over Dataverse. All the decisions that matter -
    /// paging, de-duplication, categorisation, coverage and error classification - happen here
    /// against <see cref="IDataverseRowSource"/>, which the tests can fake. The plugin supplies
    /// the Dataverse SDK glue and nothing else.
    /// </summary>
    public sealed class DataverseSolutionFlowSource : ISolutionAwareFlowSource
    {
        private readonly IDataverseRowSource rows;
        private readonly IClock clock;

        public DataverseSolutionFlowSource(IDataverseRowSource rowSource, IClock? clock)
        {
            rows = rowSource ?? throw new ArgumentNullException(nameof(rowSource));
            this.clock = clock ?? new SystemClock();
        }

        public ProviderKind Provider
        {
            get { return ProviderKind.PowerAutomate; }
        }

        /// <summary>Environment of the underlying connection; null when it cannot be reported.</summary>
        public string? EnvironmentId
        {
            get { return rows.EnvironmentId; }
        }

        public ProviderResult<IReadOnlyList<SolutionReference>> GetSolutions(CancellationToken cancellationToken)
        {
            try
            {
                PagedReadResult<SolutionReference> read = PageReader.Read(
                    request => rows.ReadSolutions(request, cancellationToken));

                // Sorting is a core decision so every view orders the selector the same way.
                IReadOnlyList<SolutionReference> solutions = SolutionSearch.Filter(read.Items, null);
                DateTimeOffset checkedAtUtc = clock.UtcNow;

                if (read.IsComplete)
                {
                    return ProviderResult<IReadOnlyList<SolutionReference>>.Complete(
                        solutions, read.PagesRead, read.Items.Count, checkedAtUtc);
                }

                return ProviderResult<IReadOnlyList<SolutionReference>>.Partial(
                    solutions,
                    read.PagesRead,
                    read.Items.Count,
                    checkedAtUtc,
                    DiagnosticsSanitizer.Describe("Only part of the solution list was read", read.StopReason));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                return Fail<IReadOnlyList<SolutionReference>>(error, 0);
            }
        }
        /// <summary>
        /// The rich discovery result: the selected flows plus what the solution's rows actually
        /// contained. Views must use this so the excluded-row counts stay visible.
        /// </summary>
        public ProviderResult<CloudFlowSelectionResult> GetSolutionFlows(string solutionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(solutionId))
            {
                throw new ArgumentException("A solution id is required to discover its flows.", nameof(solutionId));
            }

            try
            {
                PagedReadResult<RawWorkflowRecord> read = PageReader.Read(
                    request => rows.ReadSolutionWorkflows(solutionId, request, cancellationToken));

                CloudFlowSelectionResult selection = DataverseCloudFlowSelector.Select(read.Items, rows.EnvironmentId);
                DateTimeOffset checkedAtUtc = clock.UtcNow;

                if (read.IsComplete)
                {
                    return ProviderResult<CloudFlowSelectionResult>.Complete(
                        selection, read.PagesRead, read.Items.Count, checkedAtUtc);
                }

                return ProviderResult<CloudFlowSelectionResult>.Partial(
                    selection,
                    read.PagesRead,
                    read.Items.Count,
                    checkedAtUtc,
                    DiagnosticsSanitizer.Describe("Only part of this solution's flows were read", read.StopReason));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                return Fail<CloudFlowSelectionResult>(error, 0);
            }
        }

        public ProviderResult<IReadOnlyList<WorkflowReference>> GetCloudFlows(string solutionId, CancellationToken cancellationToken)
        {
            return Project(GetSolutionFlows(solutionId, cancellationToken));
        }

        /// <summary>
        /// Project the rich result onto the generic monitoring contract without losing the state,
        /// the page count, the checked-at instant or the reason. A failed read stays failed here.
        /// </summary>
        private static ProviderResult<IReadOnlyList<WorkflowReference>> Project(ProviderResult<CloudFlowSelectionResult> source)
        {
            IReadOnlyList<WorkflowReference> flows = source.Value == null
                ? new List<WorkflowReference>()
                : source.Value.Flows;
            DateTimeOffset checkedAtUtc = source.CheckedAtUtc;

            switch (source.State)
            {
                case ProviderResultState.Complete:
                    return ProviderResult<IReadOnlyList<WorkflowReference>>.Complete(flows, source.PagesRead, source.ItemsRead, checkedAtUtc);
                case ProviderResultState.Partial:
                    return ProviderResult<IReadOnlyList<WorkflowReference>>.Partial(flows, source.PagesRead, source.ItemsRead, checkedAtUtc, source.Message);
                case ProviderResultState.Unauthorized:
                    return ProviderResult<IReadOnlyList<WorkflowReference>>.Unauthorized(source.PagesRead, checkedAtUtc, source.Message);
                case ProviderResultState.Unsupported:
                    return ProviderResult<IReadOnlyList<WorkflowReference>>.Unsupported(checkedAtUtc, source.Message);
                case ProviderResultState.Stale:
                    return ProviderResult<IReadOnlyList<WorkflowReference>>.Stale(flows, source.PagesRead, source.ItemsRead, checkedAtUtc, source.Message);
                default:
                    return ProviderResult<IReadOnlyList<WorkflowReference>>.Unavailable(source.PagesRead, checkedAtUtc, source.Message);
            }
        }

        /// <summary>
        /// Turn a thrown Dataverse error into an honest state plus an actionable, sanitized
        /// message. The exception object never escapes, and the message never carries a url,
        /// token or payload.
        /// </summary>
        private ProviderResult<T> Fail<T>(Exception error, int pagesRead)
        {
            ProviderResultState state = ProviderErrorClassifier.Classify(error.GetType().Name, error.Message);
            bool local = ProviderErrorClassifier.IsLocalDefect(error.GetType().Name, error.Message);
            string reason = DiagnosticsSanitizer.Sanitize(error.Message);
            string message = DiagnosticsSanitizer.Describe(ProviderErrorClassifier.AdviceFor(state, local), reason);
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
    }
}
