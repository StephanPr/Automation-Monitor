namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// The shared run-outcome group every provider is normalized into. Values and numbering are
    /// unchanged from the M0 scaffold so existing counters keep working.
    ///
    /// <para>
    /// This is a <em>group</em>, not a claim that the products mean the same thing: the provider's
    /// own status text is always kept beside it on <see cref="WorkflowRun.RawStatus"/>. Anything
    /// unrecognised or absent is <see cref="Unknown"/>, which is surfaced and counted - never
    /// treated as success and never dropped.
    /// </para>
    /// </summary>
    public enum RunStatus
    {
        /// <summary>Absent or unrecognised provider status. Visible, never counted as success.</summary>
        Unknown = 0,

        /// <summary>Not finished: Running, Waiting, Suspended or Paused.</summary>
        Running = 1,

        /// <summary>Completed successfully.</summary>
        Succeeded = 2,

        /// <summary>An unsuccessful outcome; the raw cause stays visible.</summary>
        Failed = 3,

        /// <summary>Terminated by a user or the platform; distinct from failed.</summary>
        Cancelled = 4,

        /// <summary>Not executed; separate from failures unless a rule includes it.</summary>
        Skipped = 5,

        /// <summary>Ran out of time. Kept separate from <see cref="Failed"/> because the raw value is.</summary>
        TimedOut = 6
    }

    /// <summary>
    /// Classification of a normalized status. Grouping decisions live here, once, so no view or
    /// counter re-invents what counts as terminal or as a problem run.
    /// </summary>
    public static class RunStatusRules
    {
        /// <summary>True when the run has finished and will not change again.</summary>
        public static bool IsTerminal(RunStatus status)
        {
            switch (status)
            {
                case RunStatus.Succeeded:
                case RunStatus.Failed:
                case RunStatus.Cancelled:
                case RunStatus.Skipped:
                case RunStatus.TimedOut:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when the outcome is a problem run: failed or timed out. Cancelled and Skipped are
        /// deliberately excluded - they are terminal but not failures - and Unknown is never
        /// promoted into a problem without evidence.
        /// </summary>
        public static bool IsUnsuccessful(RunStatus status)
        {
            return status == RunStatus.Failed || status == RunStatus.TimedOut;
        }

        /// <summary>Short label for grids and exports. Unknown shows the raw value it came from.</summary>
        public static string Label(RunStatus status, string? rawStatus)
        {
            if (status != RunStatus.Unknown)
            {
                return status.ToString();
            }

            return string.IsNullOrWhiteSpace(rawStatus)
                ? "Unknown"
                : "Unknown (" + rawStatus!.Trim() + ")";
        }
    }

    /// <summary>Which instant pair a duration was derived from, so a caller can see its basis.</summary>
    public enum RunDurationBasis
    {
        /// <summary>Neither the source reported a duration nor could one be derived: still running or unknown.</summary>
        NotAvailable = 0,

        /// <summary>Derived from the stored UTC start and end instants.</summary>
        Measured = 1,

        /// <summary>Taken from the provider's own duration field.</summary>
        ProviderReported = 2
    }
}

