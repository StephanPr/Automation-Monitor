using System;
using System.Collections.Generic;

namespace AutomationMonitor.Core.Models
{
    /// <summary>Which page a paged source reader should fetch, plus the previous continuation token.</summary>
    public sealed class PageRequest
    {
        public PageRequest(int pageNumber, string? cookie)
        {
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page numbers are one-based.");
            }

            PageNumber = pageNumber;
            Cookie = cookie;
        }

        public int PageNumber { get; }

        /// <summary>Opaque continuation token from the previous page; null when the source has none.</summary>
        public string? Cookie { get; }
    }

    /// <summary>
    /// One page of results from any paged source. <see cref="MoreRecords"/> is the source's own
    /// statement that another page exists; the reader trusts it and never guesses.
    /// </summary>
    public sealed class Page<T>
    {
        public Page(IReadOnlyList<T>? items, bool moreRecords, string? cookie)
        {
            Items = items ?? new T[0];
            MoreRecords = moreRecords;
            Cookie = cookie;
        }

        public IReadOnlyList<T> Items { get; }

        public bool MoreRecords { get; }

        public string? Cookie { get; }
    }
}
