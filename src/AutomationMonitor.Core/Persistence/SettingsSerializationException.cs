using System;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// A settings document could not be produced or understood. The reason is already sanitized: it
    /// names what is wrong with the format, never a payload, a token or a tenant url. The caller
    /// decides whether that means "first run", "corrupt" or "newer than this build" - this type never
    /// implies a file may be overwritten.
    /// </summary>
    public class SettingsSerializationException : Exception
    {
        public SettingsSerializationException(string reason)
            : base(reason)
        {
            Reason = reason;
        }

        public SettingsSerializationException(string reason, Exception inner)
            : base(reason, inner)
        {
            Reason = reason;
        }

        /// <summary>Short actionable statement of the format problem.</summary>
        public string Reason { get; }
    }
}
