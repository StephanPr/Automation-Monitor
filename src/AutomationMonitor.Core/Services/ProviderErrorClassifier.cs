using System;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Maps a failed Dataverse/HTTP read onto an honest <see cref="ProviderResultState"/>.
    /// Classification is text-based because the plugin catches exceptions as strings plus type
    /// names; it is deliberately conservative - only an explicit access complaint becomes
    /// Unauthorized, everything else stays Unavailable rather than being called "no data".
    /// The real-tenant spike recorded in docs/provider-capabilities.md should refine these
    /// markers, never the other way round.
    /// </summary>
    public static class ProviderErrorClassifier
    {
        private static readonly string[] UnauthorizedMarkers =
        {
            "privilege",
            "seclib",
            "insufficient access",
            "does not have",
            "is not authorized",
            "unauthorized",
            "permission",
            "403",
            "401",
            "cannot be granted"
        };

        private static readonly string[] UnsupportedMarkers =
        {
            "attribute does not exist",
            "does not exist by name",
            "no entity defined",
            "entity does not exist",
            "is not a valid attribute",
            "unsupported"
        };

        public static ProviderResultState Classify(string? exceptionTypeName, string? message)
        {
            string haystack = ((exceptionTypeName ?? string.Empty) + " " + (message ?? string.Empty)).ToLowerInvariant();

            foreach (string marker in UnsupportedMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return ProviderResultState.Unsupported;
                }
            }

            foreach (string marker in UnauthorizedMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return ProviderResultState.Unauthorized;
                }
            }

            return ProviderResultState.Unavailable;
        }

        /// <summary>What the operator should do about it, without echoing a payload.</summary>
        public static string AdviceFor(ProviderResultState state)
        {
            switch (state)
            {
                case ProviderResultState.Unauthorized:
                    return "The connected user is not allowed to read this data. Check the security role for the solution, solution component and process tables.";
                case ProviderResultState.Unsupported:
                    return "This environment does not expose the expected table or column, so discovery cannot run here.";
                case ProviderResultState.Unavailable:
                    return "The environment did not answer. Check the connection and try again.";
                case ProviderResultState.Partial:
                    return "Some pages were read and others were not. Treat the list as incomplete.";
                case ProviderResultState.Stale:
                    return "The data returned is older than the source can refresh.";
                default:
                    return string.Empty;
            }
        }
    }
}
