using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Abstractions
{
    /// <summary>
    /// Discovery seam for a source that organises cloud flows by solution (Dataverse today,
    /// Logic Apps / APIM later). Methods are synchronous on purpose: the XrmToolBox host already
    /// runs this on a worker thread via WorkAsync, and a second thread hop would only add
    /// deadlock risk. Every implementation must accept cancellation and must report coverage
    /// through <see cref="ProviderResult{T}"/> instead of returning an empty list on failure.
    /// </summary>
    public interface ISolutionAwareFlowSource
    {
        ProviderKind Provider { get; }

        /// <summary>
        /// Solutions visible to the current connection. A solution-less environment is a complete
        /// empty result; a permission problem is <see cref="ProviderResultState.Unauthorized"/>.
        /// </summary>
        ProviderResult<IReadOnlyList<SolutionReference>> GetSolutions(CancellationToken cancellationToken);

        /// <summary>
        /// The cloud flows belonging to one solution, de-duplicated and normalized. The result
        /// state reflects the weakest page read, not the row count.
        /// </summary>
        ProviderResult<IReadOnlyList<WorkflowReference>> GetCloudFlows(string solutionId, CancellationToken cancellationToken);
    }
}
