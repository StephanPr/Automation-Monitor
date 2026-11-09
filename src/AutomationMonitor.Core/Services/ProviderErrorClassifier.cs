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
    ///
    /// <para>
    /// "The environment did not answer" is only true when a request left the process and did not come
    /// back. A mapping or cast failure happens after the answer arrived, so <see cref="IsLocalDefect"/>
    /// separates it, and <see cref="AdviceFor(ProviderResultState, bool)"/> refuses to send the operator
    /// off to check a connection that is working.
    /// </para>
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

        /// <summary>
        /// Failure shapes that mean this tool could not read an answer the environment really gave: the
        /// mapping diagnostic this core throws, the raw cast and conversion failures it used to produce,
        /// and the wording that diagnostic starts with (so a wrapped one is still recognised). They
        /// outrank every other marker, because the honest alternative is telling the operator the
        /// environment did not answer when it plainly did. The type names are taken from the types so a
        /// renamed or misspelled literal cannot quietly switch this detection off.
        /// </summary>
        private static readonly string[] LocalDefectMarkers =
        {
            nameof(Providers.Dataverse.DataverseMappingException).ToLowerInvariant(),
            nameof(InvalidCastException).ToLowerInvariant(),
            nameof(OverflowException).ToLowerInvariant(),
            nameof(FormatException).ToLowerInvariant(),
            "automation monitor could not read"
        };

        /// <summary>
        /// True when the failure came from inside this tool rather than from the environment. A local
        /// defect still maps to <see cref="ProviderResultState.Unsupported"/> - no new coverage state is
        /// invented for a bug - but the wording must say so, and "check the connection" must never be
        /// shown for it.
        /// </summary>
        public static bool IsLocalDefect(string? exceptionTypeName, string? message)
        {
            string haystack = Haystack(exceptionTypeName, message);
            foreach (string marker in LocalDefectMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static ProviderResultState Classify(string? exceptionTypeName, string? message)
        {
            string haystack = Haystack(exceptionTypeName, message);

            foreach (string marker in LocalDefectMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return ProviderResultState.Unsupported;
                }
            }

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

        private static string Haystack(string? exceptionTypeName, string? message)
        {
            return ((exceptionTypeName ?? string.Empty) + " " + (message ?? string.Empty)).ToLowerInvariant();
        }

        /// <summary>What the operator should do about it, without echoing a payload.</summary>
        public static string AdviceFor(ProviderResultState state)
        {
            return AdviceFor(state, false);
        }

        /// <summary>
        /// The same advice table, with one distinction: a defect in this tool is not the environment
        /// lacking a table and not a connection problem, and retrying cannot fix it. Say which it is.
        /// </summary>
        public static string AdviceFor(ProviderResultState state, bool isLocalDefect)
        {
            if (isLocalDefect)
            {
                return "Automation Monitor could not read a value this environment did return, so the connection and the permissions are not the problem. The column named below is not the shape this build expects; retrying will not help.";
            }

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
