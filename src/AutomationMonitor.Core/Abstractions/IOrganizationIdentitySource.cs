using System;
using System.Threading;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Abstractions
{
    /// <summary>
    /// The host's answer to one question: which Dataverse organization is the connection it already
    /// opened authenticated against? The plugin implements this over a single read-only message and
    /// the core decides what the answer means - which scope may be built from it, and what must be
    /// reported when there is no answer.
    ///
    /// <para>
    /// One method, deliberately, and one implementation: <see cref="ConnectedOrganizationResolver"/>
    /// is the only consumer, so every feature that needs the connected environment asks the same
    /// source and shares its cache. A second path that reads an identifier itself - the connection
    /// record's environment id, or the connector's client - is how one build came to key flows by an
    /// identifier the monitoring path could not resolve.
    /// </para>
    /// </summary>
    public interface IOrganizationIdentitySource
    {
        /// <summary>
        /// Ask the connected service which organization it is. The read is a round trip, so this is
        /// called from a worker, never from a UI thread, and cancellation must stop it promptly.
        /// A refused request is an answer about the request, not an empty identifier: report it as
        /// <see cref="OrganizationIdentityRead.Failed(ProviderResultState, string)"/>.
        /// </summary>
        OrganizationIdentityRead Read(CancellationToken cancellationToken);
    }
}
