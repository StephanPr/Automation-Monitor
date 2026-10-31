using System.Threading;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// The raw Dataverse run-row seam - the run counterpart of <see cref="IDataverseRowSource"/>.
    /// It is a separate interface on purpose: the discovery fake every existing test uses stays
    /// valid, and a run read can be faked without a test opting into solution discovery.
    ///
    /// <para>
    /// Same rules as the workflow seam: read-only, cancellation-honouring, transport errors thrown
    /// for the classifier to turn into a state, and never a page that claims
    /// <see cref="Page{T}.MoreRecords"/> without advancing. A <c>null</c> page means the source had
    /// no further page; an empty page with more records is the stalled-continuation case the shared
    /// reader already stops on.
    /// </para>
    /// </summary>
    public interface IDataverseRunRowSource
    {
        /// <summary>
        /// Environment the rows are read from, used as the scope half of every run identity. Null
        /// when the connection cannot report one; unscoped identities never merge with scoped ones.
        /// </summary>
        string? EnvironmentId { get; }

        /// <summary>
        /// One page of <c>flowrun</c> rows for the criteria's workflow and window. The criteria is
        /// applied by the source where it can be; the core re-applies the query afterwards, so a
        /// source that ignored a filter cannot widen the operator's request.
        /// </summary>
        Page<RawRunRecord>? ReadFlowRuns(RunQueryCriteria criteria, PageRequest request, CancellationToken cancellationToken);
    }
}
