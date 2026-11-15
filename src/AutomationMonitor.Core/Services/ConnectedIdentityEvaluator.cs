using System;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Turns the raw identity values one AM-42 read collected into a <see cref="ConnectedIdentityOutcome"/>.
    ///
    /// <para>
    /// This is the whole privacy boundary of the probe. The values enter as parameters and are used twice - to
    /// decide shape, and to decide whether the two environment-id sources agree - and nothing that outlives the
    /// call is derived from their content. There is no field, dictionary or log line anywhere downstream that
    /// could hold one, because the types on the other side of this method cannot express a value.
    /// </para>
    /// </summary>
    public static class ConnectedIdentityEvaluator
    {
        /// <summary>Fixed labels owned by this build; they name the API a value came from, not the value.</summary>
        public const string ConnectionSource = "the connection record (ConnectionDetail.EnvironmentId)";

        public const string ClientSource = "the connected service client (CrmServiceClient.EnvironmentId)";
        public const string WhoAmISource = "the WhoAmI request (WhoAmIResponse.OrganizationId)";

        /// <summary>
        /// Measure one run.
        /// </summary>
        /// <param name="clientReachable">
        /// False when the connector client could not be reached without creating a connection. That is a finding
        /// about this build's access, not an empty environment id, and it is reported as Unavailable.
        /// </param>
        /// <param name="whoAmISucceeded">False when the request was refused or returned no response object.</param>
        /// <param name="whoAmIFailureState">The classified state of that refusal, or null if there was none.</param>
        /// <param name="whoAmIFailureKind">The exception type name only. A message is never accepted here.</param>
        public static ConnectedIdentityOutcome Evaluate(
            string? connectionEnvironmentId,
            string? clientEnvironmentId,
            bool clientReachable,
            string? organizationId,
            bool whoAmISucceeded,
            ProviderResultState? whoAmIFailureState,
            string? whoAmIFailureKind,
            DateTimeOffset checkedAtUtc)
        {
            IdentityObservation connection = IdentityObservation.Observe(
                IdentityKind.PowerPlatformEnvironmentId, ConnectionSource, connectionEnvironmentId);

            IdentityObservation client = IdentityObservation.Observe(
                IdentityKind.PowerPlatformEnvironmentId, ClientSource, clientEnvironmentId, clientReachable);

            IdentityObservation organization = IdentityObservation.Observe(
                IdentityKind.DataverseOrganizationId, WhoAmISource, organizationId, whoAmISucceeded);

            // Two spellings of one GUID (dashed, braced, or bare) are the same identifier, so a comparison that
            // only looked at the text would report a false difference between two sources that agree.
            bool same = ValuesEqual(connectionEnvironmentId, clientEnvironmentId);

            return new ConnectedIdentityOutcome(
                connection,
                client,
                organization,
                IdentityComparison.Evaluate(connection, client, same),
                whoAmISucceeded,
                whoAmIFailureState,
                whoAmIFailureKind,
                checkedAtUtc);
        }

        /// <summary>
        /// What the run means, in one sentence. Deliberately careful: it reports which identifier exists, and it
        /// does not propose using the organization id as the environment id - that substitution is a design
        /// decision for a later checkpoint with its own migration answer.
        /// </summary>
        public static string VerdictFor(ConnectedIdentityOutcome outcome)
        {
            if (outcome == null)
            {
                return "nothing was measured, so no identifier is known";
            }

            if (outcome.HasEnvironmentIdentity && outcome.HasOrganizationIdentity)
            {
                return "both identifier kinds were obtained - a Power Platform environment id and a Dataverse "
                    + "organization id. They describe the same connection at two different levels and are kept apart";
            }

            if (outcome.HasEnvironmentIdentity)
            {
                return "a Power Platform environment id was obtained but the organization id was not, so the "
                    + "organization half of the question is still unanswered";
            }

            if (outcome.HasOrganizationIdentity)
            {
                return "no Power Platform environment id was available at runtime on this connection, while the "
                    + "Dataverse organization id was. Using an organization id as a watchlist scope is a later "
                    + "design decision with its own migration answer, not something this read performs";
            }

            return "no usable identifier of either kind was obtained, so nothing here may be used to scope a "
                + "watchlist and an existing unscoped list stays unscoped";
        }

        private static bool ValuesEqual(string? left, string? right)
        {
            string? a = Normalise(left);
            string? b = Normalise(right);
            if (a == null || b == null)
            {
                return false;
            }

            if (Guid.TryParse(a, out Guid first) && Guid.TryParse(b, out Guid second))
            {
                return first == second;
            }

            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string? Normalise(string? value)
        {
            // The ! is not an assumption: IsNullOrWhiteSpace already proved it non-null, but that guarantee is
            // not annotated in the netstandard2.0 reference assemblies this core compiles against.
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }
    }
}
