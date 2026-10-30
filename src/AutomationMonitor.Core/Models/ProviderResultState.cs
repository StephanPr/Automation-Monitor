namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// How much of a requested collection the source actually delivered. This travels with the
    /// data all the way to the grid and any future export, so aggregation can never wash out
    /// the qualifier. A result is only <see cref="Complete"/> when every page was consumed.
    /// </summary>
    public enum ProviderResultState
    {
        /// <summary>All requested pages were read successfully. May still be zero rows.</summary>
        Complete = 0,

        /// <summary>Some rows were read, but the collection is known to be incomplete.</summary>
        Partial = 1,

        /// <summary>The connection is not permitted to read this source. Never an empty list.</summary>
        Unauthorized = 2,

        /// <summary>The source could not be reached or failed (timeout, 5xx, closed connection).</summary>
        Unavailable = 3,

        /// <summary>The source does not offer this capability at all (e.g. stateless Logic Apps).</summary>
        Unsupported = 4,

        /// <summary>Data was read but is older than the source can make it.</summary>
        Stale = 5
    }
}
