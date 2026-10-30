using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// One row exactly as it was read from a source's workflow catalogue, before any
    /// normalization. It lives in the core so that categorisation, de-duplication and
    /// watchlist construction are testable without the Dataverse SDK. Raw provider values are
    /// kept verbatim here and carried onto <see cref="WorkflowReference"/>: normalization is
    /// lossy and diagnosis needs the original.
    /// </summary>
    public sealed class RawWorkflowRecord
    {
        /// <summary>Provider's stable workflow id. Null or blank makes the row unusable.</summary>
        public string? Id { get; set; }

        /// <summary>Display name. Presentation and search only; never identity.</summary>
        public string? Name { get; set; }

        /// <summary>Raw workflow category as stored by the source (5 = modern cloud flow).</summary>
        public int? RawCategory { get; set; }

        /// <summary>Raw state code (Dataverse: 0 disabled, 1 enabled).</summary>
        public int? RawStateCode { get; set; }

        /// <summary>Raw status code; source-specific detail behind the state.</summary>
        public int? RawStatusCode { get; set; }

        /// <summary>Raw primary category, when the source exposes it.</summary>
        public int? RawPrimaryCategory { get; set; }

        /// <summary>Raw secondary category, when the source exposes it.</summary>
        public int? RawSecondaryCategory { get; set; }

        /// <summary>Optional description text from the source.</summary>
        public string? Description { get; set; }

        /// <summary>Creation time in UTC, when the source reported it.</summary>
        public DateTimeOffset? CreatedOnUtc { get; set; }

        /// <summary>Last-modified time in UTC, when the source reported it.</summary>
        public DateTimeOffset? ModifiedOnUtc { get; set; }
    }
}
