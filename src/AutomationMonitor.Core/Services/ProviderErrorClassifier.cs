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
    /// back. A mapping or cast failure happens after the answer arrived, and a query rejected for an
    /// unknown column was answered too - so <see cref="DefectKindOf"/> separates all three and
    /// <see cref="AdviceFor(ProviderResultState, DefectKind)"/> refuses to send the operator off to check
    /// a connection that is working.
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
        /// The platform's words for "a column in your query is not in this table". Dataverse validates
        /// every projected column against the table's metadata and refuses the <em>whole</em> query when
        /// one name is unknown, so this is a defect in the query this build wrote - not an outage, and
        /// not a capability the environment happens to lack. The wording a real environment returned on
        /// 2026-11-09 was "The 'workflow' entity doesn't contain attribute with Name = 'primarycategory'
        /// and NameMapping = 'Logical'"; matching the fragment rather than the apostrophe word covers
        /// either spelling. Markers stay lowercase because only the haystack is normalised.
        /// </summary>
        private static readonly string[] RejectedAttributeMarkers =
        {
            "contain attribute with name",
            "contain attribute",
            "no attribute defined by the name"
        };

        /// <summary>
        /// The platform's words for "a value in your filter is not the type this column takes". Like a
        /// projected column the table does not have, this is the environment answering a request this build
        /// wrote badly: it refused the whole query, so no id was read and nothing about membership was
        /// established. Recorded from the first live workflow-identity read on 2026-11-15: "Condition for
        /// attribute 'workflow.workflowid': expected argument(s) of type 'System.Guid' but received
        /// 'System.Guid[]'" - a Guid[] handed to a params object[] filter value. Markers stay lowercase
        /// because only the haystack is normalised.
        /// </summary>
        private static readonly string[] RejectedQueryMarkers =
        {
            "expected argument(s) of type",
            "condition for attribute"
        };

        /// <summary>
        /// Who caused a failure. The coverage state says what the operator received; this says whether to
        /// blame the environment or this build, and a rejected query must not share one bool with a value
        /// this tool failed to read - the advice an operator needs is different in each case.
        /// </summary>
        public enum DefectKind
        {
            /// <summary>The environment failed; the ordinary coverage advice is the honest one.</summary>
            None,

            /// <summary>A value arrived in a shape this build cannot read, or a cast inside this tool failed.</summary>
            Mapping,

            /// <summary>Dataverse refused this build's query because a projected column does not exist in this environment.</summary>
            RejectedAttribute,

            /// <summary>
            /// Dataverse refused this build's query because a filter value was not the type the column takes.
            /// The request never became a read, so every id the read was to ask about is still unanswered.
            /// </summary>
            QueryConstruction
        }

        /// <summary>
        /// True when the failure came from inside this tool rather than from the environment. A local
        /// defect still maps to <see cref="ProviderResultState.Unsupported"/> - no new coverage state is
        /// invented for a bug - but the wording must say so, and "check the connection" must never be
        /// shown for it. Prefer <see cref="DefectKindOf"/> when the advice depends on which defect it was.
        /// </summary>
        public static bool IsLocalDefect(string? exceptionTypeName, string? message)
        {
            return DefectKindOf(exceptionTypeName, message) != DefectKind.None;
        }

        /// <summary>
        /// Which failure this is. A cast or mapping problem means an answer arrived that this build could
        /// not read; a rejected attribute means this build asked for a column this environment does not
        /// have; anything else is the environment's own failure. Both local kinds outrank every provider
        /// marker, because the alternative is sending the operator to check something that is working.
        /// </summary>
        public static DefectKind DefectKindOf(string? exceptionTypeName, string? message)
        {
            string haystack = Haystack(exceptionTypeName, message);

            foreach (string marker in LocalDefectMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return DefectKind.Mapping;
                }
            }

            foreach (string marker in RejectedAttributeMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return DefectKind.RejectedAttribute;
                }
            }

            // Same family, different sentence to fix: the columns were right and a filter value was not.
            foreach (string marker in RejectedQueryMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return DefectKind.QueryConstruction;
                }
            }

            return DefectKind.None;
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

            // The environment answered this one - it answered "no such column in the query you sent".
            // It is Unsupported (nothing came back) but never Unavailable, whose advice blames the link.
            foreach (string marker in RejectedAttributeMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return ProviderResultState.Unsupported;
                }
            }

            // And the same for "no such value in the query you sent": answered, refused, nothing read.
            // Classifying it as Unavailable is what once told an operator the environment had not answered
            // when the environment had in fact said this build's filter was unusable.
            foreach (string marker in RejectedQueryMarkers)
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
        /// Prefer the <see cref="DefectKind"/> overload, which can tell a rejected query apart from a
        /// misread value.
        /// </summary>
        public static string AdviceFor(ProviderResultState state, bool isLocalDefect)
        {
            return AdviceFor(state, isLocalDefect ? DefectKind.Mapping : DefectKind.None);
        }

        /// <summary>
        /// The advice for a coverage state plus the defect that produced it. Only the local kinds change
        /// the wording, and both of them say the same two things: Automation Monitor is the part that has
        /// to change, and the connection is not it. Sending an operator to re-check a working connection
        /// is how a bug in this build ends up blamed on their environment.
        /// </summary>
        public static string AdviceFor(ProviderResultState state, DefectKind defect)
        {
            switch (defect)
            {
                case DefectKind.Mapping:
                    return "Automation Monitor could not read a value this environment did return, so the connection and the permissions are not the problem. The column named below is not the shape this build expects; retrying will not help.";
                case DefectKind.RejectedAttribute:
                    return "Automation Monitor asked this environment for a column its table does not contain, so Dataverse rejected the whole query and no rows were read. The connection and the permissions are not the problem: this build's column list has to be corrected, and retrying will not help.";
                case DefectKind.QueryConstruction:
                    return "Automation Monitor sent this environment a query whose filter value was not the type the column takes, so Dataverse rejected the whole query and no workflow id was read at all. The connection and the permissions are not the problem: this build's query has to be corrected, and retrying will not help. Nothing this read reported can be treated as a missing workflow.";
                default:
                    break;
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
