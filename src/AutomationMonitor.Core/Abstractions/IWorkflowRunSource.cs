using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Abstractions
{
    /// <summary>
    /// Run-history seam for any automation platform. Like <see cref="ISolutionAwareFlowSource"/> it
    /// is synchronous on purpose: the XrmToolBox host already runs provider work on a
    /// <c>WorkAsync</c> worker thread, and a second thread hop would add deadlock risk without
    /// buying anything.
    ///
    /// <para>
    /// An implementation returns <see cref="WorkflowRun"/> records - never provider-native rows -
    /// and reports coverage through <see cref="ProviderResult{T}"/>. "No runs in this window",
    /// "not permitted", "table not available" and "only part of the pages were read" are four
    /// different answers and must stay four different states. Every call accepts a cancellation
    /// token and stops promptly: closing the plugin cancels the refresh.
    /// </para>
    /// </summary>
    public interface IWorkflowRunSource
    {
        ProviderKind Provider { get; }

        /// <summary>
        /// Environment, workspace or resource group the rows came from; null when the connection
        /// cannot report one. It is the scope half of every run identity, so an unscoped read stays
        /// a distinct key and never merges with a scoped one.
        /// </summary>
        string? ScopeId { get; }

        /// <summary>
        /// Runs of one workflow inside one <see cref="MonitoringQuery"/> window. The query's window
        /// and status filter are applied by the provider where the source supports it and are
        /// re-applied by the query service, so a source that ignores a criterion cannot widen the
        /// answer. Rows the source could not give an id for are counted and reported, not invented.
        /// </summary>
        ProviderResult<IReadOnlyList<WorkflowRun>> GetRuns(WorkflowKey workflow, MonitoringQuery query, CancellationToken cancellationToken);
    }
}
