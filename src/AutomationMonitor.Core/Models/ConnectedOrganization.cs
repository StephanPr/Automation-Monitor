using System;
using AutomationMonitor.Core.Abstractions;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Whether this build knows which Dataverse organization the current connection is inside.
    /// Three states because two would collapse "not asked yet" into "asked and refused", and those
    /// license different sentences: one is a session that has not looked, the other is a read the
    /// operator may want to retry or report.
    /// </summary>
    public enum ConnectedOrganizationState
    {
        /// <summary>No identity read has been made for this connection yet.</summary>
        NotAttempted = 0,

        /// <summary>A completed read named an organization, so <see cref="ConnectedOrganization.Scope"/> is an identity.</summary>
        Resolved = 1,

        /// <summary>The read ran and did not name an organization. Nothing may be keyed by it.</summary>
        Failed = 2
    }

    /// <summary>
    /// What one connection's organization-identity read established: the qualified resource scope,
    /// the display context that goes beside it, or the honest reason there is neither.
    ///
    /// <para>
    /// This is the only place the connected environment exists as a value, so discovery, saved
    /// watchlists, the settings profile and the environment column all key off the same
    /// <see cref="ResourceScope"/> instead of each reading whichever host property looks like an
    /// identifier. <c>ConnectionDetail.EnvironmentId</c> is measured empty on the live connection
    /// (docs/provider-capabilities.md), so a build that keyed off it silently stored no scope at all;
    /// this value is built from the organization id a completed read returned.
    /// </para>
    ///
    /// <para>
    /// The two halves must not be confused. <see cref="Scope"/> is identity and goes into keys;
    /// <see cref="DisplayName"/> is host-supplied display context, mutable and not unique, and never
    /// enters a key, a merge, a cache key, a filter or a path. An unresolved read comes back with an
    /// unverified scope <em>and</em> says why: substituting the empty scope silently is what makes an
    /// unverified connection look scoped.
    /// </para>
    /// </summary>
    public sealed class ConnectedOrganization
    {
        /// <summary>The longest display text this build will echo back. Longer is a paste, not a name.</summary>
        public const int MaximumDisplayNameLength = 80;

        private ConnectedOrganization(
            ConnectedOrganizationState state,
            ResourceScope scope,
            string? displayName,
            ProviderResultState? failureState,
            string? failureKind,
            DateTimeOffset? checkedAtUtc)
        {
            State = state;
            Scope = scope ?? ResourceScope.Unverified;
            DisplayName = Clean(displayName);
            FailureState = failureState;
            FailureKind = failureKind;
            CheckedAtUtc = checkedAtUtc;
        }

        public ConnectedOrganizationState State { get; }

        /// <summary>
        /// The qualified scope of the connected organization, or <see cref="ResourceScope.Unverified"/>.
        /// Never a scope this build invented and never a preserved stored value.
        /// </summary>
        public ResourceScope Scope { get; }

        /// <summary>
        /// The connected organization's display context, sanitized for presentation. Null when the
        /// host did not supply one. Identity never reads this.
        /// </summary>
        public string? DisplayName { get; }

        /// <summary>How a failed read is classified, for the one shared source wording. Null otherwise.</summary>
        public ProviderResultState? FailureState { get; }

        /// <summary>The failure's type name only - provider text can carry a tenant url and is never kept.</summary>
        public string? FailureKind { get; }

        /// <summary>When the read ran. Null until one has run for this connection.</summary>
        public DateTimeOffset? CheckedAtUtc { get; }

        public bool IsResolved
        {
            get { return State == ConnectedOrganizationState.Resolved && Scope.IsQualified; }
        }

        /// <summary>
        /// The scope text a key or a stored field should carry for this connection: the qualified
        /// organization when it was read, and the empty scope when it was not - the same spelling an
        /// unverified stored member uses, so an unresolved connection cannot quietly qualify anything.
        /// </summary>
        public string ScopeText
        {
            get { return IsResolved ? Scope.Text : string.Empty; }
        }

        /// <summary>Nothing has been asked of this connection yet.</summary>
        public static ConnectedOrganization NotAttempted
        {
            get
            {
                return new ConnectedOrganization(
                    ConnectedOrganizationState.NotAttempted, ResourceScope.Unverified, null, null, null, null);
            }
        }

        /// <summary>
        /// Build the value a completed read earned. The scope is authored by <see cref="ResourceScope"/>,
        /// so an all-zero identifier cannot become an identity here even by accident.
        /// </summary>
        public static ConnectedOrganization For(Guid organizationId, string? displayName, DateTimeOffset checkedAtUtc)
        {
            return new ConnectedOrganization(
                ConnectedOrganizationState.Resolved,
                ResourceScope.ForDataverseOrganization(organizationId),
                displayName,
                null,
                null,
                checkedAtUtc.ToUniversalTime());
        }

        /// <summary>
        /// Map what the host's read answered. A read that did not name an organization comes back
        /// unverified with its state and type name, which is what lets a status line blame the right
        /// thing instead of showing an empty scope as though it were the answer.
        /// </summary>
        public static ConnectedOrganization From(OrganizationIdentityRead read, DateTimeOffset checkedAtUtc)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            DateTimeOffset checkedAt = checkedAtUtc.ToUniversalTime();
            if (read.Succeeded && read.OrganizationId.HasValue)
            {
                return For(read.OrganizationId.Value, null, checkedAt);
            }

            return new ConnectedOrganization(
                ConnectedOrganizationState.Failed,
                ResourceScope.Unverified,
                null,
                read.FailureState ?? ProviderResultState.Unavailable,
                read.FailureKind,
                checkedAt);
        }

        /// <summary>
        /// The same resolution beside the display context the host supplied for this connection.
        /// Separate because the read is a round trip and the display context is not: the cache stays
        /// about identity, and a caller with no display context simply does not ask.
        /// </summary>
        public ConnectedOrganization WithDisplayName(string? displayName)
        {
            string? clean = Clean(displayName);
            if (string.Equals(clean, DisplayName, StringComparison.Ordinal))
            {
                return this;
            }

            return new ConnectedOrganization(State, Scope, clean, FailureState, FailureKind, CheckedAtUtc);
        }

        /// <summary>True when this resolution names the same organization as <paramref name="other"/>.</summary>
        public bool IsSameOrganizationAs(ResourceScope? other)
        {
            return Scope.IsSameResourceAs(other);
        }

        public override string ToString()
        {
            return IsResolved ? Scope.Text : "unresolved (" + (FailureKind ?? State.ToString()) + ")";
        }

        /// <summary>
        /// Presentation hygiene for host-supplied display text: trimmed, control characters replaced,
        /// clamped. It stays data - a view puts the result in a cell or a label and never parses it.
        /// </summary>
        private static string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string text = value!.Trim();
            if (text.Length > MaximumDisplayNameLength)
            {
                text = text.Substring(0, MaximumDisplayNameLength);
            }

            char[] characters = text.ToCharArray();
            for (int index = 0; index < characters.Length; index++)
            {
                if (char.IsControl(characters[index]))
                {
                    characters[index] = ' ';
                }
            }

            text = new string(characters).Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
