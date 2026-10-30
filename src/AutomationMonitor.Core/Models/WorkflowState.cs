namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Normalized enablement state of a workflow. The raw provider state code is always
    /// kept alongside it on <see cref="WorkflowReference"/> because normalization is lossy.
    /// </summary>
    public enum WorkflowState
    {
        /// <summary>The source did not report a state, or reported one we do not recognise.</summary>
        Unknown = 0,

        /// <summary>The flow is enabled and can be triggered.</summary>
        Enabled = 1,

        /// <summary>The flow is disabled or stopped.</summary>
        Disabled = 2
    }
}
