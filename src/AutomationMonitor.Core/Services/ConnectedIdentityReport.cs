using System;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// The copyable block the plugin shows after an AM-42 identity read. Same contract as
    /// <see cref="Dataverse.FlowRunProbeReport"/>: pure function of the outcome, no host, no logger, and no line
    /// that could carry an identifier, a url, a connection name or tenant information.
    /// </summary>
    public static class ConnectedIdentityReport
    {
        /// <summary>The first line, so a pasted block is recognisable out of context.</summary>
        public const string Heading = "Connected environment identity - diagnostic result";

        /// <summary>What the operator is asked to do, and an explicit statement of what the block does not hold.</summary>
        public const string CopyInstruction =
            "Copy this whole block back to the maintainer. It carries no identifier value, no environment name or url, no connection name and no tenant information.";

        /// <summary>The one line every run ends with, so a report can never be read as a change that happened.</summary>
        public const string NothingWritten =
            "What this probe does not change: it reads and reports. No watchlist scope, stored member key or settings file is touched by it.";

        /// <summary>Stated on every block, because the temptation it prevents is a "these two differ" reading.</summary>
        public const string NotCompared =
            "Not compared: the Dataverse organization id and the Power Platform environment id are different identity kinds and are never treated as equal or as a mismatch.";

        public static string Build(ConnectedIdentityOutcome? outcome)
        {
            if (outcome == null)
            {
                return Join(Heading, "Result: the probe returned no result object at all, so nothing was measured.", CopyInstruction);
            }

            StringBuilder text = new StringBuilder();
            Append(text, Heading);
            Append(text, "Measured at: " + outcome.CheckedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");

            foreach (IdentityObservation observation in outcome.Observations)
            {
                Append(text, KindLabel(observation.Kind) + " from " + observation.Source + ": " + observation.Describe());
            }

            Append(text, "Comparison of the two environment-id sources: " + IdentityComparison.Describe(outcome.EnvironmentIdMatch));

            if (outcome.ClientEnvironmentId.Availability == IdentityAvailability.Unavailable)
            {
                Append(text, "Why that source was not read: the connected service was not the connector client type, so its environment id could only be obtained by creating a separate connection - which this tool never does.");
            }

            Append(text, "WhoAmI request: " + (outcome.WhoAmISucceeded ? "succeeded" : DescribeFailure(outcome)));
            Append(text, NotCompared);
            Append(text, "Verdict: " + ConnectedIdentityEvaluator.VerdictFor(outcome));

            if (outcome.WhoAmIFailureState != null)
            {
                Append(text, "What to do: " + ProviderErrorClassifier.AdviceFor(outcome.WhoAmIFailureState.Value));
            }

            Append(text, NothingWritten);
            Append(text, CopyInstruction);

            return text.ToString();
        }

        private static string KindLabel(IdentityKind kind)
        {
            return kind == IdentityKind.DataverseOrganizationId ? "Organization id" : "Environment id";
        }

        private static string DescribeFailure(ConnectedIdentityOutcome outcome)
        {
            if (outcome.WhoAmIFailureState == null && outcome.WhoAmIFailureKind == null)
            {
                return "did not complete, and no reason was reported";
            }

            string state = outcome.WhoAmIFailureState == null
                ? "did not complete"
                : ProviderResultStates.Token(outcome.WhoAmIFailureState.Value);

            // Only the exception's type name is offered, and it is sanitized like any other text before display.
            string? kind = outcome.WhoAmIFailureKind == null ? null : DiagnosticsSanitizer.Sanitize(outcome.WhoAmIFailureKind);

            return string.IsNullOrEmpty(kind) ? state : state + " (" + kind + ")";
        }

        private static void Append(StringBuilder text, string line)
        {
            text.Append(line).Append(Environment.NewLine);
        }

        private static string Join(params string[] lines)
        {
            StringBuilder text = new StringBuilder();
            foreach (string line in lines)
            {
                Append(text, line);
            }

            return text.ToString();
        }
    }
}
