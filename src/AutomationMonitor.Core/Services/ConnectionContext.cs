using System;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Tracks which connection the plugin is on, so an asynchronous action can tell whether the one it
    /// started against is still the one the operator is looking at. The plugin captures a
    /// <see cref="ConnectionTicket"/> on the UI thread before launching work and asks this class whether
    /// that ticket is still current before it publishes, saves or re-labels anything.
    ///
    /// <para>
    /// Two things make a captured ticket stale: the connection identity changing (a switch to another
    /// environment, which also bumps the epoch so a ticket from before it can never come back into
    /// favour) and an explicit <see cref="Invalidate"/>, which is what a disconnect or a host-reported
    /// connection update does. Neither depends on a worker noticing in time - the check happens at the
    /// moment the result would be applied, which is the last point where discarding it is still honest.
    /// </para>
    /// </summary>
    public sealed class ConnectionContext
    {
        private readonly object gate = new object();
        private long epoch = -1;
        private string? token;

        /// <summary>False when no connection is active, which is the state after a disconnect.</summary>
        public bool IsConnected
        {
            get
            {
                lock (gate)
                {
                    return token != null && token.Length > 0;
                }
            }
        }

        /// <summary>
        /// Record the connection an action is launching against. A different identity from the last one
        /// seen bumps the epoch: a ticket captured before the switch must not be revived by a later
        /// switch back to the same environment.
        /// </summary>
        public ConnectionTicket Capture(string? connectionToken, ConnectedOrganization organization)
        {
            string? identity = string.IsNullOrWhiteSpace(connectionToken) ? null : connectionToken!.Trim();

            lock (gate)
            {
                if (!string.Equals(token, identity, StringComparison.Ordinal))
                {
                    epoch++;
                    token = identity;
                }

                return new ConnectionTicket(epoch, token, organization);
            }
        }

        /// <summary>
        /// Whether this ticket still names the active connection. An unbound ticket (launched with no
        /// service) is never current, so a read that could not have reached anything cannot be applied.
        /// </summary>
        public bool IsCurrent(ConnectionTicket? ticket)
        {
            if (ticket == null || !ticket.IsBound)
            {
                return false;
            }

            lock (gate)
            {
                return ticket.Epoch == epoch && string.Equals(ticket.ConnectionToken, token, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// The connection changed or closed. Every ticket captured before this call is stale, even if
        /// the operator switches back to the environment they started in: what was read before is no
        /// longer evidence about what is connected now.
        /// </summary>
        public void Invalidate()
        {
            lock (gate)
            {
                epoch++;
                token = null;
            }
        }

        /// <summary>
        /// The one wording for a discarded result, shared by every action so a stale read cannot be
        /// dressed up as an empty answer. It names what was thrown away and what to do next, and it
        /// never implies the environment had nothing to show.
        /// </summary>
        public static string DescribeDiscard(string action)
        {
            return "The " + action + " finished after this plugin moved to a different connection, so its results were"
                + " discarded and nothing was applied or saved. Run it again to read the environment that is connected now.";
        }
    }
}
