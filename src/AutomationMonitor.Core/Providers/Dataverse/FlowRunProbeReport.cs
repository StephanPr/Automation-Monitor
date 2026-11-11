using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// How loudly a probe result should be reported by the host. Decided in core, because the answer depends
    /// on what the environment did, not on which control happened to run the read.
    ///
    /// <para>
    /// The levels are deliberately meaningful rather than generous. XrmToolBox writes plugin log entries to
    /// <c>&lt;storage&gt;\Logs\AutomationMonitor.Plugin.log</c> only at Warning or above unless the operator
    /// raises its log level, so promoting everything to Warning would be gaming the log instead of reporting
    /// the result. AM-36 made the result readable on screen; this enum only decides how loudly the log
    /// echoes it.
    /// </para>
    /// </summary>
    public enum ProbeReportSeverity
    {
        /// <summary>The environment answered and the answer needs no action - including a clean empty read.</summary>
        Information,

        /// <summary>The experiment could not be completed as designed, or its two reads contradict each other.</summary>
        Warning,

        /// <summary>The environment refused to be read; access has to be fixed before the question can be asked.</summary>
        Error
    }

    /// <summary>
    /// The copyable diagnostic block the plugin shows after a probe run (AM-36).
    ///
    /// <para>
    /// It exists because the log was never a reliable hand-back path: the host drops Information-level plugin
    /// entries at its default log level, so the decisive result of a probe the maintainer ran was invisible.
    /// This builder needs no host, no logger and no settings - the text is a pure function of the outcome,
    /// which is what makes it reachable however XrmToolBox is configured.
    /// </para>
    ///
    /// <para>
    /// Same privacy rule as <see cref="FlowRunProbeOutcome.Describe"/>: counts, column names, CLR type names,
    /// state codes and UTC timestamps - never a flow id, a run id, an environment name or url, or provider
    /// message text. The per-read lines are delegated to <see cref="FlowRunProbeDiagnostics.Describe"/> so
    /// there is one wording rule for a read rather than two that can drift apart.
    /// </para>
    /// </summary>
    public static class FlowRunProbeReport
    {
        /// <summary>The first line, so a pasted block is recognisable even out of context.</summary>
        public const string Heading = "Run-history identity probe - diagnostic result";

        /// <summary>What the operator is asked to do, plus an explicit statement of what is not on the block.</summary>
        public const string CopyInstruction =
            "Copy this whole block back to the maintainer. It carries no flow id, no run id, no environment name or url and no provider message.";

        /// <summary>Builds the whole result. The caller shows the text; it decides nothing and interprets nothing.</summary>
        public static string Build(FlowRunProbeOutcome outcome)
        {
            if (outcome == null)
            {
                return Join(
                    Heading,
                    "Result: the probe returned no result object at all, so nothing was measured and no column is ruled out.",
                    CopyInstruction);
            }

            StringBuilder text = new StringBuilder();
            Append(text, Heading);
            Append(text, "Read at (UTC): " + Timestamp(outcome.WindowToUtc));
            Append(text, "Window queried: " + WindowText(outcome));
            Append(text, "Rows asked for: at most "
                + FlowRunProbeDiagnostics.RowCeiling.ToString(CultureInfo.InvariantCulture)
                + " per query, newest first - a sample, not a read of the whole window");
            Append(text, "Identity asked for: "
                + (outcome.RequestedIdentityIsGuid ? "guid-shaped" : "not guid-shaped"));
            Append(text, string.Empty);
            Append(text, "Counts:");
            Append(text, "  Query A - " + QueryLine(outcome.StringFilter));
            Append(text, "  Query B - " + QueryLine(outcome.LookupFilter));
            Append(text, "  Same run returned by both queries: "
                + outcome.OverlapCount.ToString(CultureInfo.InvariantCulture));
            Append(text, string.Empty);
            Append(text, "Verdict: " + outcome.VerdictLabel);
            Append(text, "What it means: " + outcome.Explanation);
            Append(text, "Failures: " + FailuresOf(outcome));
            Append(text, string.Empty);
            Append(text, "Structural detail (safe to share):");
            Append(text, "  " + FlowRunProbeDiagnostics.Describe(outcome.StringFilter));
            Append(text, "  " + FlowRunProbeDiagnostics.Describe(outcome.LookupFilter));
            Append(text, string.Empty);
            Append(text, CopyInstruction);

            return text.ToString();
        }

        /// <summary>
        /// One query's answer as a count line: what it filtered on, how much came back, and the state the
        /// read is in. A refused or unsent query carries no row count, because "we got no answer" and "the
        /// answer was zero rows" are different facts.
        /// </summary>
        private static string QueryLine(FlowRunProbeRead read)
        {
            if (read == null)
            {
                return "no result object at all";
            }

            string head = Safe(read.FilterColumn) + ", " + Safe(read.FilterKind) + " filter";
            if (!read.WasAttempted || read.Failure != null)
            {
                return head + ": " + read.StateLabel + " - no row count, because this query has no answer";
            }

            return head + ": "
                + read.Rows.ToString(CultureInfo.InvariantCulture) + " row(s), "
                + read.PagesRead.ToString(CultureInfo.InvariantCulture) + " page(s), "
                + FlowRunProbeDiagnostics.CountDistinct(read.RunIds).ToString(CultureInfo.InvariantCulture)
                + " distinct run id(s)"
                + (read.CeilingReached ? " (stopped at the row ceiling, so this is a sample)" : string.Empty)
                + " - " + read.StateLabel;
        }

        /// <summary>Every way this run failed to be a clean pair of answers, or an explicit "none".</summary>
        private static string FailuresOf(FlowRunProbeOutcome outcome)
        {
            List<string> parts = new List<string>();
            AddFailure(parts, "A", outcome.StringFilter);
            AddFailure(parts, "B", outcome.LookupFilter);

            return parts.Count == 0
                ? "none - both queries were sent and the environment answered"
                : string.Join("; ", parts.ToArray());
        }

        private static void AddFailure(List<string> parts, string name, FlowRunProbeRead read)
        {
            if (read == null || (read.WasAttempted && read.Failure == null))
            {
                return;
            }

            // The reason is either this build's own not-sent text or the exception's type name. Provider
            // message text was dropped where the read was caught, so there is nothing tenant-owned to leak.
            string detail = read.StateLabel;
            string? reason = read.WasAttempted ? read.FailureKind : read.NotAttemptedReason;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                detail += " (" + Safe(reason) + ")";
            }

            parts.Add("query " + name + " " + detail);
        }

        private static string WindowText(FlowRunProbeOutcome outcome)
        {
            if (outcome.WindowFromUtc == null || outcome.WindowToUtc == null)
            {
                return "not recorded by this run";
            }

            DateTimeOffset from = outcome.WindowFromUtc.Value;
            DateTimeOffset to = outcome.WindowToUtc.Value;
            string days = (to - from).TotalDays.ToString("0.##", CultureInfo.InvariantCulture);

            return Timestamp(from) + " to " + Timestamp(to) + " UTC (" + days
                + " days, widened by the usual server-skew allowance before the query was sent)";
        }

        private static string Timestamp(DateTimeOffset? value)
        {
            return value == null
                ? "not recorded"
                : value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
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

        /// <summary>Column names come from the platform, so they are sanitized like any other provider text.</summary>
        private static string Safe(string? value)
        {
            string cleaned = DiagnosticsSanitizer.Sanitize(value);
            return cleaned.Length == 0 ? "not reported" : cleaned;
        }
    }
}
