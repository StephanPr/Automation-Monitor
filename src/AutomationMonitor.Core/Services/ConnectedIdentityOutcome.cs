using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// What one AM-42 measurement run found, as shapes plus one same-kind comparison.
    ///
    /// <para>
    /// Like <see cref="IdentityObservation"/> this type cannot carry an identifier: the evaluator receives the
    /// values as parameters and holds only what it derives. The exception information that survives is the
    /// failure's type name and its classified state, because the message of a connector or SDK error can carry
    /// a tenant url, an environment name or a correlation value.
    /// </para>
    /// </summary>
    public sealed class ConnectedIdentityOutcome
    {
        public ConnectedIdentityOutcome(
            IdentityObservation connectionEnvironmentId,
            IdentityObservation clientEnvironmentId,
            IdentityObservation organizationId,
            IdentityMatch environmentIdMatch,
            bool whoAmISucceeded,
            ProviderResultState? whoAmIFailureState,
            string? whoAmIFailureKind,
            DateTimeOffset checkedAtUtc)
        {
            ConnectionEnvironmentId = connectionEnvironmentId ?? throw new ArgumentNullException(nameof(connectionEnvironmentId));
            ClientEnvironmentId = clientEnvironmentId ?? throw new ArgumentNullException(nameof(clientEnvironmentId));
            OrganizationId = organizationId ?? throw new ArgumentNullException(nameof(organizationId));
            EnvironmentIdMatch = environmentIdMatch;
            WhoAmISucceeded = whoAmISucceeded;
            WhoAmIFailureState = whoAmIFailureState;
            WhoAmIFailureKind = string.IsNullOrWhiteSpace(whoAmIFailureKind) ? null : whoAmIFailureKind!.Trim();
            CheckedAtUtc = checkedAtUtc.ToUniversalTime();
        }

        /// <summary>The environment id the connection record carries.</summary>
        public IdentityObservation ConnectionEnvironmentId { get; }

        /// <summary>The environment id of the service client the host already connected.</summary>
        public IdentityObservation ClientEnvironmentId { get; }

        /// <summary>The organization id the WhoAmI request reported.</summary>
        public IdentityObservation OrganizationId { get; }

        /// <summary>The only comparison this probe makes: the two environment-id sources.</summary>
        public IdentityMatch EnvironmentIdMatch { get; }

        public bool WhoAmISucceeded { get; }

        /// <summary>How the WhoAmI request failed, as a coverage state; null when it did not fail.</summary>
        public ProviderResultState? WhoAmIFailureState { get; }

        /// <summary>The exception's type name only, never its message.</summary>
        public string? WhoAmIFailureKind { get; }

        public DateTimeOffset CheckedAtUtc { get; }

        /// <summary>Either environment-id source stated a usable identifier.</summary>
        public bool HasEnvironmentIdentity
        {
            get { return ConnectionEnvironmentId.IsUsable || ClientEnvironmentId.IsUsable; }
        }

        /// <summary>WhoAmI stated a usable organization identifier.</summary>
        public bool HasOrganizationIdentity
        {
            get { return OrganizationId.IsUsable; }
        }

        /// <summary>Every observation, in the order the report shows them.</summary>
        public IReadOnlyList<IdentityObservation> Observations
        {
            get { return new[] { ConnectionEnvironmentId, ClientEnvironmentId, OrganizationId }; }
        }

        /// <summary>
        /// How loudly the host should echo this run. A refused request is the operator's to fix, so it is the one
        /// Error; a run that found no usable identifier is a Warning because the question is still unanswered;
        /// and a run that read both kinds is plain information.
        /// </summary>
        public ProbeReportSeverity Severity
        {
            get
            {
                if (WhoAmIFailureState == ProviderResultState.Unauthorized)
                {
                    return ProbeReportSeverity.Error;
                }

                return HasEnvironmentIdentity && HasOrganizationIdentity
                    ? ProbeReportSeverity.Information
                    : ProbeReportSeverity.Warning;
            }
        }
    }
}
