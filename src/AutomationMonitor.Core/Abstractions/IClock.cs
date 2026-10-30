using System;

namespace AutomationMonitor.Core.Abstractions
{
    /// <summary>
    /// Injectable time source. Core never reads the wall clock directly, so "last checked",
    /// freshness and window arithmetic stay deterministic under test.
    /// </summary>
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }

    /// <summary>Real clock. Only the plugin host is allowed to use it.</summary>
    public sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow
        {
            get { return DateTimeOffset.UtcNow; }
        }
    }
}
