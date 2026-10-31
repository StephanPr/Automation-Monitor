using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// One execution row exactly as a source returned it, before any normalization - the run
    /// counterpart of <see cref="RawWorkflowRecord"/>. Values a source did not send stay null, and
    /// the raw status text is kept verbatim because normalization is lossy.
    ///
    /// <para>
    /// It is deliberately not the normalized model: a provider must be able to report "the row
    /// carried no run id" or "the row is for a different flow than I asked for" without those
    /// facts becoming fields on the model the UI reads.
    /// </para>
    /// </summary>
    public sealed class RawRunRecord
    {
        /// <summary>The source's own record id for the run (Dataverse: the FlowRun row id).</summary>
        public string? RunRecordId { get; set; }

        /// <summary>
        /// The provider's run identifier. On Dataverse this is the Logic Apps run name in the
        /// <c>name</c> column, and it is what <see cref="ParentRunId"/> refers to.
        /// </summary>
        public string? RunId { get; set; }

        /// <summary>Workflow identifier the row carries. Blank or mismatched rows are reported, not merged.</summary>
        public string? WorkflowId { get; set; }

        public string? RawStatus { get; set; }

        public DateTimeOffset? StartedAtUtc { get; set; }

        public DateTimeOffset? EndedAtUtc { get; set; }

        /// <summary>Duration in milliseconds as the source reported it, or null when it did not.</summary>
        public long? ReportedDurationMs { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorText { get; set; }

        /// <summary>Parent run identifier exactly as reported. Never inferred.</summary>
        public string? ParentRunId { get; set; }

        /// <summary>Tracking id carried by the run, when the source has one.</summary>
        public string? CorrelationId { get; set; }

        /// <summary>Primary-run marker as a picklist or integer code.</summary>
        public int? PrimaryRunCode { get; set; }

        /// <summary>Primary-run marker when the source reports a real Boolean.</summary>
        public bool? PrimaryRunFlag { get; set; }

        public string? TriggerType { get; set; }
    }
}
