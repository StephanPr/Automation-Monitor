using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// The seam for one narrow question: "do these workflow ids exist in the organization this
    /// connection is authenticated against?" It exists apart from <see cref="IDataverseRowSource"/>
    /// because that seam answers "what flows does this solution contain", which is a different query
    /// with a different projection, and because an identity read must be bounded in a way discovery is
    /// not: it is issued against a live tenant on an operator's explicit request, before anything is
    /// written to their file.
    ///
    /// <para>
    /// An implementation must be read-only (<c>RetrieveMultiple</c> and nothing else), must project only
    /// <see cref="DataverseSchema.WorkflowId"/>, must not ask for distinct rows (a distinct query carries
    /// no primary keys), must honour cancellation, must translate its own transport errors into
    /// exceptions for the caller to classify, and must set <see cref="RawWorkflowRecord.IdSource"/> from
    /// <see cref="WorkflowIdentity.TryResolve"/> rather than guessing which value is the id.
    /// </para>
    ///
    /// <para>
    /// This seam deliberately reports no environment id, unlike <see cref="IDataverseRowSource"/>. The
    /// connection's environment id and the Dataverse organization id are different kinds of identifier
    /// and must never be compared (docs/provider-capabilities.md), and this read's authority is the
    /// organization id the caller already holds. A property here would invite exactly that comparison.
    /// </para>
    /// </summary>
    public interface IWorkflowIdentityRowSource
    {
        /// <summary>
        /// One page of workflow rows whose id is one of <paramref name="workflowIds"/>. The caller never
        /// passes more than the probe's documented batch size, and every id is Guid-shaped.
        /// </summary>
        Page<RawWorkflowRecord>? ReadWorkflowsByIds(IReadOnlyList<string> workflowIds, PageRequest request, CancellationToken cancellationToken);
    }
}
