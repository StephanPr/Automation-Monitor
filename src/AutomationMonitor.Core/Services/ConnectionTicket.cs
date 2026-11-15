using System;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// What one asynchronous action started against: the connection it was launched from and the
    /// organization scope that action ran under. Immutable, and the only evidence a callback has that
    /// its result still belongs to what the operator is looking at.
    ///
    /// <para>
    /// Both halves matter. The connection half answers "is this the same connection"; the scope half
    /// answers "which organization may these rows be saved, matched and labelled as". A result applied
    /// after a switch would otherwise be stored under an environment the operator never asked about,
    /// which is an identity defect rather than a stale display - so a callback that finds its ticket
    /// is no longer current discards the result instead of re-labelling it.
    /// </para>
    /// </summary>
    public sealed class ConnectionTicket
    {
        private readonly long epoch;
        private readonly string? connectionToken;

        internal ConnectionTicket(long epoch, string? connectionToken, ConnectedOrganization organization)
        {
            this.epoch = epoch;
            this.connectionToken = connectionToken;
            Organization = organization ?? ConnectedOrganization.NotAttempted;
        }

        /// <summary>False when the action was launched with no connection at all; such a ticket is never current.</summary>
        public bool IsBound
        {
            get { return connectionToken != null && connectionToken.Length > 0; }
        }

        /// <summary>
        /// Which connection round this ticket belongs to. Internal: only <see cref="ConnectionContext"/>
        /// may compare it, so nobody outside can decide staleness by reading a counter and guessing.
        /// </summary>
        internal long Epoch
        {
            get { return epoch; }
        }

        /// <summary>The captured connection identity, for <see cref="ConnectionContext"/> to compare.</summary>
        internal string? ConnectionToken
        {
            get { return connectionToken; }
        }

        /// <summary>The connection's organization as this action knew it. Unverified until a read resolved it.</summary>
        public ConnectedOrganization Organization { get; }

        /// <summary>The scope to key this action's rows and saved lists by - empty when unverified.</summary>
        public string ScopeText
        {
            get { return Organization.ScopeText; }
        }

        /// <summary>Same connection, same scope resolution, for a result the worker produced.</summary>
        internal ConnectionTicket WithOrganization(ConnectedOrganization organization)
        {
            return new ConnectionTicket(epoch, connectionToken, organization);
        }
    }
}
