using System.Threading;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// The raw Dataverse read seam. It is deliberately expressed in core types only: the plugin
    /// implements it with <c>IOrganizationService</c>, and the tests can implement it with canned
    /// pages, so the paging, selection and error behaviour is verifiable without a tenant.
    ///
    /// <para>
    /// Implementations must be read-only, must honour cancellation, must translate their own
    /// transport errors into exceptions (the reader classifies them), and must never return a
    /// page that claims <see cref="Page{T}.MoreRecords"/> without also advancing.
    /// </para>
    /// </summary>
    public interface IDataverseRowSource
    {
        /// <summary>
        /// Environment the rows were read from, used as the scope half of every workflow
        /// identity. Null when the connection cannot report one: the reader then produces
        /// unscoped identities and never merges them with scoped ones.
        /// </summary>
        string? EnvironmentId { get; }

        /// <summary>One page of solutions visible to the current connection.</summary>
        Page<SolutionReference>? ReadSolutions(PageRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// One page of workflow catalogue rows that belong to the given solution. Rows are
        /// returned unfiltered: classic workflows and rows with odd categories must reach the
        /// selector so that they can be counted and reported, not silently dropped here.
        /// </summary>
        Page<RawWorkflowRecord>? ReadSolutionWorkflows(string solutionId, PageRequest request, CancellationToken cancellationToken);
    }
}
