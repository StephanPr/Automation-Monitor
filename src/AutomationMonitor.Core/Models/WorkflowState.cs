using System;

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

    /// <summary>
    /// The stored and displayed form of a <see cref="WorkflowState"/>. A saved watchlist records the
    /// state it last saw so the list stays informative while disconnected; an unrecognised token is
    /// <see cref="WorkflowState.Unknown"/> and reported, never guessed into "enabled".
    /// </summary>
    public static class WorkflowStates
    {
        public static string Token(WorkflowState state)
        {
            switch (state)
            {
                case WorkflowState.Enabled:
                    return "enabled";
                case WorkflowState.Disabled:
                    return "disabled";
                default:
                    return "unknown";
            }
        }

        public static bool TryParse(string? token, out WorkflowState state)
        {
            state = WorkflowState.Unknown;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string text = token!.Trim().ToLowerInvariant();
            if (string.Equals(text, "enabled", StringComparison.Ordinal))
            {
                state = WorkflowState.Enabled;
                return true;
            }

            if (string.Equals(text, "disabled", StringComparison.Ordinal))
            {
                state = WorkflowState.Disabled;
                return true;
            }

            return string.Equals(text, "unknown", StringComparison.Ordinal);
        }
    }
}
