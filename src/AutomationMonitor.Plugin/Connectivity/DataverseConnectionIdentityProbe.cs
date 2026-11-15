using System;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Tooling.Connector;

namespace AutomationMonitor.Plugin.Connectivity
{
    /// <summary>
    /// AM-42: asks the active connection which stable environment identifiers really exist, and reports them as
    /// shapes. It reads three things and nothing else - the environment id on the connection record, the
    /// environment id on the service client the host already connected, and the organization id the environment
    /// hands back for a <see cref="WhoAmIRequest"/>.
    ///
    /// <para>
    /// <b>Why <c>ConnectionDetail.ServiceClient</c> is never touched.</b> That property's getter is
    /// <c>GetCrmServiceClient()</c>. With a client cached it returns it; without one it builds a new
    /// <see cref="CrmServiceClient"/> from the stored connection string or calls the online / IFD / on-premises /
    /// OAuth / client-secret / key-vault connect path, and it throws for connections made through the SDK login
    /// control. Reading a property could therefore start a sign-in prompt, open a second connection, or throw -
    /// three things this tool must never do as a side effect of showing information. The connector's environment
    /// id is therefore obtained by type-testing the service the host already created and connected, and when that
    /// service is not a connector client the source is reported as Unavailable rather than reached for.
    /// </para>
    ///
    /// <para>
    /// <b>Read-only.</b> <see cref="IOrganizationService.RetrieveMultiple"/> stays the only call the monitoring
    /// path makes (see <see cref="DataverseGateway"/>). This class additionally calls
    /// <see cref="IOrganizationService.Execute"/> with one documented message, <see cref="WhoAmIRequest"/>, which
    /// carries no target, no payload and no write semantics: Microsoft documents its response as the caller's own
    /// business unit, organization and user identifiers. It cannot create, update, cancel or reconfigure
    /// anything, which is why it is allowed here and why no other message is.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing leaves this class.</b> The identifiers go to Core, which reduces them to shape plus one
    /// same-kind comparison and cannot store them; an exception's message is dropped where it is caught, as in
    /// <see cref="DataverseFlowRunProbe"/>, because connector text can carry a tenant url or an environment name.
    /// </para>
    /// </summary>
    public sealed class DataverseConnectionIdentityProbe
    {
        private readonly IOrganizationService service;
        private readonly string? connectionEnvironmentId;

        public DataverseConnectionIdentityProbe(IOrganizationService service, string? connectionEnvironmentId)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            this.service = service;
            this.connectionEnvironmentId = connectionEnvironmentId;
        }

        /// <summary>
        /// Run the read. The only failure that propagates is cancellation: a refused request is an answer this
        /// probe has to report, not one it may hide behind an exception.
        /// </summary>
        public ConnectedIdentityOutcome Run(DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
        {
            bool clientReachable;
            string? clientEnvironmentId = ReadClientEnvironmentId(out clientReachable);

            Guid? organizationId = null;
            bool whoAmISucceeded = false;
            ProviderResultState? failureState = null;
            string? failureKind = null;

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                WhoAmIResponse? response = service.Execute(new WhoAmIRequest()) as WhoAmIResponse;
                if (response != null)
                {
                    whoAmISucceeded = true;
                    organizationId = response.OrganizationId;
                }
                else
                {
                    // This build's own wording, not provider text: the call came back with something this class
                    // could not read, which is a defect here rather than an answer from the environment.
                    failureState = ProviderResultState.Unavailable;
                    failureKind = "no response object returned";
                }
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                failureState = ProviderErrorClassifier.Classify(error.GetType().Name, error.Message);
                failureKind = error.GetType().Name;
            }

            return ConnectedIdentityEvaluator.Evaluate(
                connectionEnvironmentId,
                clientEnvironmentId,
                clientReachable,
                organizationId == null ? null : organizationId.Value.ToString("D"),
                whoAmISucceeded,
                failureState,
                failureKind,
                checkedAtUtc);
        }

        /// <summary>
        /// Read the environment id off the client the host connected, by type test and never by constructing one.
        /// A source that could not be read, or a property that threw, is reported as unreachable: writing either
        /// up as "not stated" would claim the connection carries no environment id when nothing was learned.
        /// </summary>
        private string? ReadClientEnvironmentId(out bool reachable)
        {
            CrmServiceClient? client = service as CrmServiceClient;
            if (client == null)
            {
                reachable = false;
                return null;
            }

            try
            {
                reachable = true;
                return client.EnvironmentId;
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                reachable = false;
                return null;
            }
        }
    }
}
