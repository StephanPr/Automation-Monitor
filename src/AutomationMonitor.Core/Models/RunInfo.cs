using System;

namespace AutomationMonitor.Core.Models
{
    public enum RunStatus
    {
        Unknown = 0,
        Running = 1,
        Succeeded = 2,
        Failed = 3,
        Cancelled = 4,
        Skipped = 5,
        TimedOut = 6
    }

    // Platform-neutral model used by Power Automate and Logic Apps providers.
    public sealed class RunInfo
    {
        public string Provider { get; set; } = string.Empty;
        public string WorkflowId { get; set; } = string.Empty;
        public string WorkflowName { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public RunStatus Status { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset? EndedAtUtc { get; set; }
        public string? ParentRunId { get; set; }
        public string? CorrelationId { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
