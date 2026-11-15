using System;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Microsoft.Xrm.Sdk;

namespace AutomationMonitor.Plugin.Connectivity
{
    /// <summary>
    /// Everything one asynchronous action was launched against: the service and connection detail captured
    /// on the UI thread, the organization scope known at that instant, and the ticket a callback must still
    /// find current before it applies, saves or re-labels anything.
    ///
    /// <para>
    /// The point of carrying all four is the switch-and-disconnect race. <see cref="Resolve"/> runs on the
    /// worker against the service captured here rather than the host property, so an action can never end up
    /// reading an environment that appeared underneath it; and a callback that finds its ticket no longer
    /// current discards the result instead of storing it under the newly active environment.
    /// </para>
    /// </summary>
    public sealed class DataverseOperation
    {
        private readonly ConnectedOrganizationResolver? resolver;
        private ConnectedOrganization organization;

        internal DataverseOperation(
            ConnectionTicket ticket,
            IOrganizationService? service,
            ConnectedOrganizationResolver? resolver,
            string? displayName)
        {
            Ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
            Service = service;
            this.resolver = resolver;
            DisplayName = displayName;
            organization = ticket.Organization;
        }

        /// <summary>The captured connection identity. Ask the environment whether it is still current.</summary>
        public ConnectionTicket Ticket { get; }

        /// <summary>The service captured when the action started. Null when nothing was connected.</summary>
        public IOrganizationService? Service { get; }

        /// <summary>
        /// The captured service for a read that may only run when one exists. Throws rather than handing a
        /// caller a null to pass on, because the only way to reach it is an action that already needs a
        /// connection to mean anything.
        /// </summary>
        public IOrganizationService ConnectedService
        {
            get
            {
                IOrganizationService? service = Service;
                if (service == null)
                {
                    throw new InvalidOperationException("Connect to a Dataverse environment before using this tool.");
                }

                return service;
            }
        }

        /// <summary>The organization's display context, captured with the connection. Presentation only.</summary>
        public string? DisplayName { get; }

        /// <summary>What this action knows about the connected organization right now.</summary>
        public ConnectedOrganization Organization
        {
            get { return organization; }
        }

        /// <summary>
        /// The scope this action's rows and saved lists must use: the verified organization, or empty when it
        /// was never resolved. Empty is the same spelling a stored unverified member uses, which is what keeps
        /// an unresolved connection from quietly qualifying anything it touches.
        /// </summary>
        public string ScopeText
        {
            get { return organization.ScopeText; }
        }

        /// <summary>
        /// Resolve the connected organization on the worker. The read happens at most once per connection, so
        /// discovering fifty-nine flows costs one identity round trip rather than fifty-nine; a failure is
        /// returned honestly and tried again by the next action instead of being cached as an answer.
        /// </summary>
        public ConnectedOrganization Resolve(CancellationToken cancellationToken)
        {
            if (resolver == null)
            {
                organization = ConnectedOrganization.NotAttempted;
                return organization;
            }

            organization = resolver.Resolve(cancellationToken).WithDisplayName(DisplayName);
            return organization;
        }

        /// <summary>
        /// The gateway for this action, projected with the organization this action resolved. Discovery keys
        /// come from that value, so a newly found flow carries the verified organization and the same GUID in a
        /// second organization is a second identity. The <c>flowrun</c> filter is untouched by this: it filters
        /// on the workflow id, which is the only identity the run table was measured against.
        /// </summary>
        public DataverseGateway NewGateway(ConnectedOrganization organization)
        {
            IOrganizationService? service = Service;
            if (service == null)
            {
                throw new InvalidOperationException("Connect to a Dataverse environment before using this tool.");
            }

            return new DataverseGateway(service, organization.IsResolved ? organization.ScopeText : null);
        }
    }
}
