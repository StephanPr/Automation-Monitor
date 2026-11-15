using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using McTools.Xrm.Connection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace AutomationMonitor.Plugin.Connectivity
{
    /// <summary>
    /// The connection this plugin is on, and the Dataverse organization that connection has been verified
    /// against. It is the plugin's only route from host connection state to a resource scope: discovery, the
    /// saved-watchlist scope, the settings profile and the environment column all key off the same value, and
    /// only here is it decided whether that value exists.
    ///
    /// <para>
    /// <b>What it never reads.</b> Not <c>ConnectionDetail.EnvironmentId</c> - measured empty on the live
    /// connection, which is why every stored member ended up unscoped - and not
    /// <c>ConnectionDetail.ServiceClient</c>, whose getter can construct a client or start a sign-in (see
    /// <see cref="DataverseConnectionIdentityProbe"/>). The organization comes from one
    /// <see cref="WhoAmIRequest"/> on the service the host already connected, and a Dataverse organization id
    /// is never presented as a Power Platform environment id.
    /// </para>
    ///
    /// <para>
    /// <b>One read per connection.</b> The organization is resolved at most once per connection and cached, so
    /// fifty-nine discovered flows cost no extra identity round trips. A failure is never cached as an answer,
    /// so the next action may still succeed. Switching or closing the connection drops the cache and makes every
    /// ticket captured earlier obsolete, which is what lets a callback discard a result instead of saving it
    /// under an environment the operator never asked about.
    /// </para>
    /// </summary>
    public sealed class ConnectedDataverseEnvironment
    {
        private readonly object gate = new object();
        private readonly ConnectionContext contexts = new ConnectionContext();
        private readonly IClock clock;

        private ConnectedOrganizationResolver? resolver;
        private string? boundToken;
        private string? displayName;

        public ConnectedDataverseEnvironment(IClock? clock = null)
        {
            this.clock = clock ?? new SystemClock();
        }

        /// <summary>Identity round trips this connection has cost. Reported so a claim of caching is checkable.</summary>
        public int IdentityReads
        {
            get
            {
                lock (gate)
                {
                    return resolver == null ? 0 : resolver.Reads;
                }
            }
        }

        /// <summary>
        /// What is known about the connected organization without asking anything: unverified until a read has
        /// run. UI-thread code that only labels state reads this; code that needs a scope works through
        /// <see cref="BeginOperation"/> on a worker.
        /// </summary>
        public ConnectedOrganization Current
        {
            get
            {
                lock (gate)
                {
                    return resolver == null ? ConnectedOrganization.NotAttempted : resolver.CurrentWith(displayName);
                }
            }
        }

        /// <summary>
        /// Start one asynchronous action: capture the service, the connection detail and the organization
        /// scope as they are at this instant, on the UI thread. The worker uses only what comes back, so a
        /// connection change underneath it cannot make the action read a service it never started against.
        /// </summary>
        public DataverseOperation BeginOperation(IOrganizationService? service, ConnectionDetail? detail)
        {
            if (service == null)
            {
                // Nothing is connected. An unbound ticket can never be current, so no callback may apply it.
                lock (gate)
                {
                    resolver = null;
                    boundToken = null;
                    displayName = null;
                }

                return new DataverseOperation(contexts.Capture(null, ConnectedOrganization.NotAttempted), null, null, null);
            }

            string token = ConnectionToken(service, detail);
            string? display = DisplayContext(detail);

            lock (gate)
            {
                if (resolver == null || !string.Equals(boundToken, token, StringComparison.Ordinal))
                {
                    // A different connection: the previous organization says nothing about this one, so the
                    // scope is read again rather than carried over, and the display context is replaced.
                    resolver = new ConnectedOrganizationResolver(new DataverseOrganizationIdentitySource(service), clock);
                    boundToken = token;
                }

                displayName = display;
                return new DataverseOperation(contexts.Capture(token, resolver.CurrentWith(display)), service, resolver, display);
            }
        }

        /// <summary>
        /// Whether the connection an action started against is still the one in force. A callback that gets
        /// false discards the result: it is not re-labelled, not saved, and not shown as the new environment's.
        /// </summary>
        public bool IsCurrent(ConnectionTicket? ticket)
        {
            lock (gate)
            {
                return contexts.IsCurrent(ticket);
            }
        }

        /// <summary>
        /// The connection changed or closed. Every ticket captured before this is obsolete and the cached
        /// organization belongs to a connection that no longer exists, so both are dropped.
        /// </summary>
        public void Invalidate()
        {
            lock (gate)
            {
                resolver = null;
                boundToken = null;
                displayName = null;
                contexts.Invalidate();
            }
        }

        /// <summary>
        /// Identity of one live connection, for change detection only. It combines the service instance the
        /// host connected with the saved-connection record and the organization address, because a switch must
        /// be visible even when the operator returns to a connection used earlier in the session. Never
        /// displayed, never logged, never stored: it addresses a connection, not a resource, and no part of it
        /// may be treated as resource identity.
        /// </summary>
        private static string ConnectionToken(IOrganizationService service, ConnectionDetail? detail)
        {
            int instance = RuntimeHelpers.GetHashCode(service);
            string connectionId = detail != null && detail.ConnectionId.HasValue
                ? detail.ConnectionId.Value.ToString("N", CultureInfo.InvariantCulture)
                : "no-connection-id";
            string address = detail == null
                ? string.Empty
                : (detail.WebApplicationUrl ?? string.Empty).Trim().ToLowerInvariant();

            return "svc:" + instance.ToString("X8", CultureInfo.InvariantCulture) + "|conn:" + connectionId + "|addr:" + address;
        }

        /// <summary>
        /// The organization's display context, for presentation only: the friendly name the host reports,
        /// otherwise the URL name, otherwise nothing. Never a key, never a filter, never stored - the identity
        /// of an environment is its qualified scope, and this text is whatever an administrator last typed.
        /// </summary>
        private static string? DisplayContext(ConnectionDetail? detail)
        {
            if (detail == null)
            {
                return null;
            }

            string friendly = (detail.OrganizationFriendlyName ?? string.Empty).Trim();
            if (friendly.Length > 0)
            {
                return friendly;
            }

            string urlName = (detail.OrganizationUrlName ?? string.Empty).Trim();
            return urlName.Length > 0 ? urlName : null;
        }
    }
}
