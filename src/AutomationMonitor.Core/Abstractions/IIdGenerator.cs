using System;

namespace AutomationMonitor.Core.Abstractions
{
    /// <summary>
    /// Source of locally generated identifiers. An abstraction because a watchlist id must be
    /// stable, unique and - unlike a name - never derived from text a provider or an operator can
    /// change. Tests supply their own generator so a saved document is reproducible.
    /// </summary>
    public interface IIdGenerator
    {
        /// <summary>A new identifier, lower-case and safe to embed in a JSON document or a file name.</summary>
        string NewId();
    }

    /// <summary>
    /// Real identifier source. Only the plugin host is allowed to use it: core services take an
    /// <see cref="IIdGenerator"/> so a created watchlist's id is deterministic under test.
    /// </summary>
    public sealed class GuidIdGenerator : IIdGenerator
    {
        public string NewId()
        {
            return Guid.NewGuid().ToString("D").ToLowerInvariant();
        }
    }
}
