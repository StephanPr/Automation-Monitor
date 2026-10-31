using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// One workflow execution, normalized across providers: Power Automate today, Logic Apps
    /// Consumption and Standard later. This is the single record the grid, the counters,
    /// correlation and any future report all read - nothing may render provider-native rows
    /// instead.
    ///
    /// <para>
    /// Three rules hold regardless of what a source sends. Identity is <see cref="Key"/>
    /// (provider + environment + workflow + run id) and never the workflow name. Both the raw
    /// provider status and the normalized <see cref="Status"/> are kept, because normalization is
    /// lossy and diagnosis needs the original. And a value the source did not send stays
    /// <c>null</c>: a zero duration, a midnight start time and an empty correlation id are the
    /// classic fabricated values and are never produced here.
    /// </para>
    /// </summary>
    public sealed class WorkflowRun
    {
        /// <summary>
        /// Build one normalized run. The required arguments are the identity, the two status
        /// spellings, the timing and the observation facts: coverage and "when was the source
        /// asked" have no safe default, so they cannot be forgotten by omission.
        /// </summary>
        public WorkflowRun(
            RunKey key,
            RunStatus status,
            string? rawStatus,
            DateTimeOffset? startedAtUtc,
            DateTimeOffset? endedAtUtc,
            long? reportedDurationMs,
            string? dataSource,
            DateTimeOffset checkedAtUtc,
            ProviderResultState coverageState,
            string? runRecordId = null,
            string? workflowName = null,
            string? errorCode = null,
            string? errorSummary = null,
            string? parentRunId = null,
            string? correlationId = null,
            bool? isPrimaryRun = null,
            string? triggerType = null)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            Key = key;
            RawStatus = TrimToNull(rawStatus);
            Status = status;
            StartedAtUtc = ToUtc(startedAtUtc);
            EndedAtUtc = ToUtc(endedAtUtc);
            ReportedDurationMs = reportedDurationMs;
            DataSource = TrimToNull(dataSource);
            CheckedAtUtc = checkedAtUtc.ToUniversalTime();
            CoverageState = coverageState;
            RunRecordId = TrimToNull(runRecordId);
            WorkflowName = string.IsNullOrWhiteSpace(workflowName) ? "(unnamed workflow)" : workflowName!.Trim();
            ErrorCode = TrimToNull(errorCode);
            ErrorSummary = TrimToNull(errorSummary);
            ParentRunId = TrimToNull(parentRunId);
            CorrelationId = TrimToNull(correlationId);
            IsPrimaryRun = isPrimaryRun;
            TriggerType = TrimToNull(triggerType);

            MeasuredDurationMs = Measure(StartedAtUtc, EndedAtUtc);
            if (MeasuredDurationMs.HasValue)
            {
                DurationMs = MeasuredDurationMs;
                DurationBasis = RunDurationBasis.Measured;
            }
            else if (reportedDurationMs.HasValue && reportedDurationMs.Value >= 0)
            {
                DurationMs = reportedDurationMs;
                DurationBasis = RunDurationBasis.ProviderReported;
            }
            else
            {
                // Still running, or simply not reported. Never zero: that would read as "instant".
                DurationMs = null;
                DurationBasis = RunDurationBasis.NotAvailable;
            }
        }

        /// <summary>Run identity. Every merge and cache decision uses this, and only this.</summary>
        public RunKey Key { get; }

        public WorkflowKey WorkflowKey { get { return Key.Workflow; } }

        public ProviderKind Provider { get { return Key.Provider; } }

        public string EnvironmentId { get { return Key.EnvironmentId; } }

        public string WorkflowId { get { return Key.WorkflowId; } }

        /// <summary>Display name only. Excluded from identity because it is mutable and collides.</summary>
        public string WorkflowName { get; }

        /// <summary>The provider's run id, kept verbatim for portal deep links.</summary>
        public string RunId { get { return Key.RunId; } }

        /// <summary>
        /// The source's own record identifier when it differs from <see cref="RunId"/>. Dataverse
        /// keeps both: the FlowRun row id (a GUID) and the Logic Apps run name, and a deep link or
        /// a support ticket needs the one the owning service actually uses.
        /// </summary>
        public string? RunRecordId { get; }

        /// <summary>Start instant in UTC, or null when the source did not report one.</summary>
        public DateTimeOffset? StartedAtUtc { get; }

        /// <summary>End instant in UTC, or null while running or when not reported.</summary>
        public DateTimeOffset? EndedAtUtc { get; }

        /// <summary>Duration in milliseconds, or null when it cannot be honestly stated.</summary>
        public long? DurationMs { get; }

        /// <summary>How <see cref="DurationMs"/> was arrived at.</summary>
        public RunDurationBasis DurationBasis { get; }

        /// <summary>The duration the source itself reported, in milliseconds, kept for diagnosis.</summary>
        public long? ReportedDurationMs { get; }

        /// <summary>End minus start in milliseconds, when both instants exist and are consistent.</summary>
        public long? MeasuredDurationMs { get; }

        /// <summary>Raw provider status text, preserved because normalization is lossy.</summary>
        public string? RawStatus { get; }

        /// <summary>Normalized outcome group. <see cref="RunStatus.Unknown"/> is surfaced, not hidden.</summary>
        public RunStatus Status { get; }

        /// <summary>Source error code, when reported.</summary>
        public string? ErrorCode { get; }

        /// <summary>
        /// Source error description. Untrusted text: it is displayed and exported as data, never
        /// composed into a path, URL or command, and neutralised before a spreadsheet.
        /// </summary>
        public string? ErrorSummary { get; }

        /// <summary>
        /// The parent run's identifier exactly as the source reported it. The parent is not
        /// required to be present in the current result set, and an absent parent stays an
        /// unresolved link rather than becoming a fabricated one.
        /// </summary>
        public string? ParentRunId { get; }

        /// <summary>Client tracking / correlation id when the source carries one.</summary>
        public string? CorrelationId { get; }

        /// <summary>
        /// Whether the source marks this run as a primary (root) execution. Null when the source
        /// does not report it, which is a different fact from reporting "false".
        /// </summary>
        public bool? IsPrimaryRun { get; }

        /// <summary>Trigger kind as the source reports it (Automated, Scheduled, Manual, ...).</summary>
        public string? TriggerType { get; }

        /// <summary>Which source or API produced this record, e.g. <c>Dataverse FlowRun</c>.</summary>
        public string? DataSource { get; }

        /// <summary>When the source was queried. Deliberately not the same as how fresh its data is.</summary>
        public DateTimeOffset CheckedAtUtc { get; }

        /// <summary>
        /// Coverage of the read this row came from. It travels with the row so a summary or export
        /// built from partial data can never present itself as complete.
        /// </summary>
        public ProviderResultState CoverageState { get; }

        /// <summary>True while the run has not reached a terminal group.</summary>
        public bool IsRunning { get { return Status == RunStatus.Running; } }

        /// <summary>True when the source reported an end instant.</summary>
        public bool HasEnded { get { return EndedAtUtc.HasValue; } }

        /// <summary>True when the outcome is a problem run (failed or timed out).</summary>
        public bool IsUnsuccessful { get { return RunStatusRules.IsUnsuccessful(Status); } }

        /// <summary>True when the outcome is terminal and the source could state an end instant.</summary>
        public bool IsCompleteOutcome
        {
            get { return RunStatusRules.IsTerminal(Status) && EndedAtUtc.HasValue; }
        }

        /// <summary>
        /// True when timing cannot be trusted: an end before a start, a terminal outcome with no
        /// end instant, or an end instant with no start. Surfaced so a view can say "timing
        /// incomplete" instead of quietly showing a wrong duration.
        /// </summary>
        public bool HasTimingGap
        {
            get
            {
                if (StartedAtUtc == null && EndedAtUtc == null)
                {
                    return false;
                }

                if (StartedAtUtc == null || EndedAtUtc == null)
                {
                    return RunStatusRules.IsTerminal(Status) || (StartedAtUtc == null && EndedAtUtc != null);
                }

                return EndedAtUtc.Value < StartedAtUtc.Value;
            }
        }

        /// <summary>
        /// True when the source's own duration disagrees with the interval between its own
        /// timestamps. Reported rather than resolved silently: which value is right is a
        /// source-behaviour question whose answer differs between readers.
        /// </summary>
        public bool DurationConflictsWithTimestamps
        {
            get
            {
                return MeasuredDurationMs.HasValue
                    && ReportedDurationMs.HasValue
                    && MeasuredDurationMs.Value != ReportedDurationMs.Value;
            }
        }

        /// <summary>Status text, including the raw value when the group could not be decided.</summary>
        public string StatusLabel { get { return RunStatusRules.Label(Status, RawStatus); } }

        /// <summary>
        /// Elapsed milliseconds for a run that has not finished, measured against a caller-supplied
        /// instant. The model never reads the wall clock: a running duration is a calculation over
        /// two UTC instants, and keeping the instant injectable is what makes it testable and what
        /// stops "still running" from being reported as zero.
        /// </summary>
        public long? ElapsedMsAt(DateTimeOffset observedAtUtc)
        {
            if (StartedAtUtc == null || EndedAtUtc != null || Status != RunStatus.Running)
            {
                return null;
            }

            TimeSpan elapsed = observedAtUtc.ToUniversalTime() - StartedAtUtc.Value;
            return elapsed.TotalMilliseconds < 0 ? (long?)null : ConvertToMilliseconds(elapsed);
        }

        public override string ToString()
        {
            return WorkflowName + " " + StatusLabel + " " + RunId;
        }

        /// <summary>
        /// End minus start in whole milliseconds. A negative interval is corrupt source data, so it
        /// yields no duration rather than a nonsense number; <see cref="HasTimingGap"/> reports it.
        /// </summary>
        private static long? Measure(DateTimeOffset? start, DateTimeOffset? end)
        {
            if (start == null || end == null)
            {
                return null;
            }

            TimeSpan span = end.Value - start.Value;
            if (span.TotalMilliseconds < 0)
            {
                return null;
            }

            return ConvertToMilliseconds(span);
        }

        /// <summary>
        /// Whole milliseconds without the double-precision drift of <c>TotalMilliseconds</c> on
        /// large intervals: the tick count is exact and one millisecond is the finest unit any
        /// provider in scope here reports.
        /// </summary>
        private static long ConvertToMilliseconds(TimeSpan span)
        {
            long ticks = span.Ticks;
            if (ticks < 0 || ticks > long.MaxValue / TimeSpan.TicksPerMillisecond)
            {
                return long.MaxValue;
            }

            return ticks / TimeSpan.TicksPerMillisecond;
        }

        private static DateTimeOffset? ToUtc(DateTimeOffset? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime() : (DateTimeOffset?)null;
        }

        private static string? TrimToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }
    }
}

