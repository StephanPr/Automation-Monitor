using System;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// The one mechanism that answers "which Dataverse organization is this connection inside".
    /// Every consumer - discovery, the saved-watchlist scope, the settings profile, the environment
    /// column - asks this class rather than reading a host property of its own, so there is exactly
    /// one place where a scope is authored and exactly one reason a scope can be missing.
    ///
    /// <para>
    /// Three rules make it safe to call from anywhere. <b>One read per connection, not per record:</b>
    /// a resolved organization is cached, because fifty-nine flows do not need fifty-nine identity
    /// round trips. <b>A failure is never cached as an answer:</b> a refused or unreadable read is
    /// returned honestly and retried by the next action, so a transient fault cannot pin a whole
    /// session to an unverified scope. <b>A new connection drops everything:</b> <see cref="Bind"/>
    /// and <see cref="Invalidate"/> discard the cached organization, so an environment switch can
    /// never be answered with the previous environment's identity.
    /// </para>
    /// </summary>
    public sealed class ConnectedOrganizationResolver
    {
        private readonly object gate = new object();
        private readonly IOrganizationIdentitySource source;
        private readonly IClock clock;
        private ConnectedOrganization cached = ConnectedOrganization.NotAttempted;
        private string? boundToken;

        public ConnectedOrganizationResolver(IOrganizationIdentitySource source, IClock? clock = null)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.clock = clock ?? new SystemClock();
        }

        /// <summary>How many identity reads this resolver has actually issued. Tests pin the caching rule with it.</summary>
        public int Reads { get; private set; }

        /// <summary>The connection whose identity is cached; null when none is bound.</summary>
        public string? ConnectionToken
        {
            get
            {
                lock (gate)
                {
                    return boundToken;
                }
            }
        }

        /// <summary>What is known right now, without asking anything: <see cref="ConnectedOrganization.NotAttempted"/>
        /// until a read has run for the bound connection.</summary>
        public ConnectedOrganization Current
        {
            get
            {
                lock (gate)
                {
                    return cached;
                }
            }
        }

        /// <summary>
        /// Bind the resolver to a connection. A different token drops the cached organization, because
        /// an identity read against one environment says nothing about the next one. Binding the same
        /// token again keeps the cache, which is what keeps repeated actions free of round trips.
        /// </summary>
        public void Bind(string? connectionToken)
        {
            string? token = string.IsNullOrWhiteSpace(connectionToken) ? null : connectionToken!.Trim();

            lock (gate)
            {
                if (string.Equals(boundToken, token, StringComparison.Ordinal))
                {
                    return;
                }

                boundToken = token;
                cached = ConnectedOrganization.NotAttempted;
            }
        }

        /// <summary>
        /// Ask the connection which organization it is, or hand back the one this connection already
        /// answered with. A failure is returned as an unresolved organization - never as an empty
        /// scope presented as a fact - and is not cached, so the next action can try again.
        /// </summary>
        public ConnectedOrganization Resolve(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                if (cached.IsResolved)
                {
                    return cached;
                }

                OrganizationIdentityRead read = source.Read(cancellationToken);
                Reads++;
                cached = ConnectedOrganization.From(read, clock.UtcNow);
                return cached;
            }
        }

        /// <summary>
        /// Attach the connection's display context to what is known. Display text costs nothing to
        /// obtain, so this never triggers a read; if no read has happened yet it stays not attempted,
        /// because a friendly name beside no verified organization would read as a claim.
        /// </summary>
        public ConnectedOrganization CurrentWith(string? displayName)
        {
            ConnectedOrganization current = Current;
            return current.State == ConnectedOrganizationState.NotAttempted
                ? current
                : current.WithDisplayName(displayName);
        }

        /// <summary>
        /// Forget everything: the next <see cref="Resolve"/> must ask the connection again. Called when
        /// the host reports the connection changed or closed, and by any action that must not trust a
        /// read it did not make itself.
        /// </summary>
        public void Invalidate()
        {
            lock (gate)
            {
                cached = ConnectedOrganization.NotAttempted;
            }
        }
    }
}
