using System;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using Microsoft.Xrm.Sdk;

namespace AutomationMonitor.Plugin.Connectivity
{
    /// <summary>
    /// The plugin's implementation of the core's organization-identity seam: one
    /// <see cref="DataverseOrganizationIdentity"/> read against the service the host already connected, reduced
    /// to the core's value type. It exists so the core owns the only copy of the rule "a Dataverse organization
    /// id becomes a <c>dataverse-organization</c> scope, and anything else is reported as unresolved" - an
    /// adapter that decided that for itself would be a second identity mechanism wearing the host's clothes.
    /// </summary>
    public sealed class DataverseOrganizationIdentitySource : IOrganizationIdentitySource
    {
        private readonly IOrganizationService service;

        public DataverseOrganizationIdentitySource(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public OrganizationIdentityRead Read(CancellationToken cancellationToken)
        {
            DataverseOrganizationIdentity identity = DataverseOrganizationIdentity.Read(service, cancellationToken);
            Guid? organizationId = identity.OrganizationId;

            if (identity.Succeeded && organizationId.HasValue && organizationId.Value != Guid.Empty)
            {
                return OrganizationIdentityRead.Identified(organizationId.Value);
            }

            // An unreadable response is this build's problem, not the environment's silence; the state the
            // classifier chose for the failure travels with it so the advice line can name the right cause.
            return OrganizationIdentityRead.Failed(
                identity.FailureState ?? ProviderResultState.Unavailable,
                identity.Succeeded ? "the response named no organization" : identity.FailureKind);
        }
    }
}
