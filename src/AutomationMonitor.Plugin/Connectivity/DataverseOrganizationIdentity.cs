using System;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace AutomationMonitor.Plugin.Connectivity
{
    /// <summary>
    /// Reads the Dataverse organization the current connection is authenticated against, so a later action can
    /// name that organization as a scope. It executes one <see cref="WhoAmIRequest"/> on the service the host
    /// already connected - the same message AM-42 measured, which carries no target, no payload and no write
    /// semantics, and whose documented response is the caller's own business unit, organization and user ids.
    ///
    /// <para>
    /// This is the only place the organization identifier exists as a value, and it exists so that
    /// <see cref="AutomationMonitor.Core.Models.ResourceScope"/> can be authored from a real identifier.
    /// <see cref="DataverseConnectionIdentityProbe"/> deliberately cannot do this: its whole point is that its
    /// result types hold shapes and no value. Nothing here is logged, shown or stored - the label a person sees
    /// comes from <see cref="ResourceScopeLabel"/>, which prints a fingerprint.
    /// </para>
    ///
    /// <para>
    /// The value is never compared with <c>ConnectionDetail.EnvironmentId</c> or
    /// <c>CrmServiceClient.EnvironmentId</c>. A Power Platform environment id and a Dataverse organization id are
    /// different kinds of identifier that both happen to be GUIDs; AM-41 settled what each one means, and
    /// docs/provider-capabilities.md forbids treating one as the other.
    /// </para>
    /// </summary>
    public sealed class DataverseOrganizationIdentity
    {
        private DataverseOrganizationIdentity(Guid? organizationId, bool succeeded, ProviderResultState? failureState, string? failureKind)
        {
            OrganizationId = organizationId;
            Succeeded = succeeded;
            FailureState = failureState;
            FailureKind = failureKind;
        }

        /// <summary>The organization id, or null when nothing was obtained. Never a placeholder.</summary>
        public Guid? OrganizationId { get; }

        /// <summary>True only when the request came back with a response object this class could read.</summary>
        public bool Succeeded { get; }

        /// <summary>How a failure is classified, for the one shared source wording. Null when there was none.</summary>
        public ProviderResultState? FailureState { get; }

        /// <summary>The failure's type name only - provider text can carry a tenant url and is never kept.</summary>
        public string? FailureKind { get; }

        /// <summary>
        /// The qualified scope this connection belongs to, or an unverified scope when the organization could not
        /// be read. An unverified scope is what makes every downstream action refuse instead of guess.
        /// </summary>
        public ResourceScope Scope
        {
            get
            {
                return OrganizationId.HasValue
                    ? ResourceScope.ForDataverseOrganization(OrganizationId.Value)
                    : ResourceScope.Unverified;
            }
        }

        /// <summary>
        /// Run the read. Cancellation propagates; a refused request is an answer, reported as a state rather than
        /// thrown, because the caller still has to tell the operator what happened.
        /// </summary>
        public static DataverseOrganizationIdentity Read(IOrganizationService service, CancellationToken cancellationToken)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                WhoAmIResponse? response = service.Execute(new WhoAmIRequest()) as WhoAmIResponse;
                if (response != null)
                {
                    return new DataverseOrganizationIdentity(response.OrganizationId, true, null, null);
                }

                // This build's own wording, not provider text: the call returned something unreadable, which is a
                // defect here rather than an answer from the environment.
                return new DataverseOrganizationIdentity(null, false, ProviderResultState.Unavailable, "no response object returned");
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                return new DataverseOrganizationIdentity(
                    null,
                    false,
                    ProviderErrorClassifier.Classify(error.GetType().Name, error.Message),
                    error.GetType().Name);
            }
        }
    }
}
