using System;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Abstractions
{
    /// <summary>
    /// What the host's organization-identity read answered. A separate value rather than an out
    /// parameter or a nullable <see cref="Guid"/>, because "the environment refused" and "the
    /// environment answered with nothing usable" are different sentences to show, and a bare null
    /// cannot tell them apart.
    ///
    /// <para>
    /// It carries the identifier and nothing else about the tenant. The failure keeps a state and a
    /// type name only: provider error text can hold a url or a tenant, so it is sanitized by the
    /// classifier before an operator sees it and never stored here.
    /// </para>
    /// </summary>
    public sealed class OrganizationIdentityRead
    {
        private OrganizationIdentityRead(bool succeeded, Guid? organizationId, ProviderResultState? failureState, string? failureKind)
        {
            Succeeded = succeeded;
            OrganizationId = organizationId;
            FailureState = failureState;
            FailureKind = failureKind;
        }

        /// <summary>True only when the read came back with an identifier this build can use.</summary>
        public bool Succeeded { get; }

        /// <summary>The organization id, or null. Never <see cref="Guid.Empty"/>: that is not an organization.</summary>
        public Guid? OrganizationId { get; }

        /// <summary>How a failure is classified, for the one shared source wording. Null on success.</summary>
        public ProviderResultState? FailureState { get; }

        /// <summary>The failure's type name only - never provider text, which can carry a url or a tenant.</summary>
        public string? FailureKind { get; }

        /// <summary>An identified organization. An all-zero id is refused here rather than upstream.</summary>
        public static OrganizationIdentityRead Identified(Guid organizationId)
        {
            if (organizationId == Guid.Empty)
            {
                throw new ArgumentException(
                    "An all-zero identifier does not name a Dataverse organization, so it cannot be an identity.",
                    nameof(organizationId));
            }

            return new OrganizationIdentityRead(true, organizationId, null, null);
        }

        /// <summary>
        /// The read did not produce an identifier. The state says what kind of failure it was, so the
        /// operator is pointed at the right thing: a refused query is this build's problem, a 401 is a
        /// permissions problem, and an unreadable response is neither.
        /// </summary>
        public static OrganizationIdentityRead Failed(ProviderResultState failureState, string? failureKind)
        {
            return new OrganizationIdentityRead(false, null, failureState, string.IsNullOrWhiteSpace(failureKind) ? null : failureKind!.Trim());
        }

        public override string ToString()
        {
            return Succeeded ? "identified" : "failed (" + FailureKind + ")";
        }
    }
}
