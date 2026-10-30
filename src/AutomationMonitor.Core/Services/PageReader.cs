using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>Outcome of consuming every page a source offered, or reporting why it stopped.</summary>
    public sealed class PagedReadResult<T>
    {
        internal PagedReadResult(List<T> items, bool isComplete, bool isTruncated, int pagesRead, string? stopReason)
        {
            Items = items;
            IsComplete = isComplete;
            IsTruncated = isTruncated;
            PagesRead = pagesRead;
            StopReason = stopReason;
        }

        public IReadOnlyList<T> Items { get; }

        /// <summary>True only when the source stopped promising more pages.</summary>
        public bool IsComplete { get; }

        /// <summary>True when a ceiling stopped the read while more records were pending.</summary>
        public bool IsTruncated { get; }

        public int PagesRead { get; }

        /// <summary>Why reading stopped, for the partial-coverage message. Null when complete.</summary>
        public string? StopReason { get; }

        /// <summary>Complete when every page was consumed; otherwise partial and labelled as such.</summary>
        public ProviderResultState State
        {
            get { return IsComplete ? ProviderResultState.Complete : ProviderResultState.Partial; }
        }
    }

    /// <summary>
    /// The single paging rule shared by every provider: follow the source's own continuation
    /// signal until it stops, honour cancellation, and never pretend a stopped read is complete.
    /// </summary>
    public static class PageReader
    {
        /// <summary>Rows asked for per round trip. Matches the Dataverse page size used by the plugin.</summary>
        public const int DefaultPageSize = 250;

        /// <summary>Ceiling on collected rows so a runaway source cannot exhaust memory.</summary>
        public const int DefaultMaxItems = 20000;

        /// <summary>Ceiling on round trips, independent of row count, for sources that report no progress.</summary>
        public const int DefaultMaxPages = 400;

        public static PagedReadResult<T> Read<T>(
            Func<PageRequest, Page<T>?> fetchPage,
            int pageSize = DefaultPageSize,
            int maxItems = DefaultMaxItems,
            int maxPages = DefaultMaxPages,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (fetchPage == null)
            {
                throw new ArgumentNullException(nameof(fetchPage));
            }

            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be at least one row.");
            }

            if (maxItems < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems), "Maximum item count must be at least one.");
            }

            if (maxPages < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPages), "Maximum page count must be at least one.");
            }

            List<T> collected = new List<T>();
            string? cookie = null;
            int pageNumber = 1;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Page<T>? page = fetchPage(new PageRequest(pageNumber, cookie));
                if (page == null)
                {
                    return new PagedReadResult<T>(collected, false, true, pageNumber - 1, "The source returned no page.");
                }

                if (page.Items != null)
                {
                    collected.AddRange(page.Items);
                }

                if (!string.IsNullOrEmpty(page.Cookie))
                {
                    cookie = page.Cookie;
                }

                if (!page.MoreRecords)
                {
                    return new PagedReadResult<T>(collected, true, false, pageNumber, null);
                }

                if (collected.Count >= maxItems)
                {
                    return new PagedReadResult<T>(
                        collected,
                        false,
                        true,
                        pageNumber,
                        "Stopped at the " + maxItems.ToString(System.Globalization.CultureInfo.InvariantCulture) + " row ceiling with more records pending.");
                }

                if (pageNumber >= maxPages)
                {
                    return new PagedReadResult<T>(
                        collected,
                        false,
                        true,
                        pageNumber,
                        "Stopped after " + maxPages.ToString(System.Globalization.CultureInfo.InvariantCulture) + " pages with more records pending.");
                }

                if (page.Items == null || page.Items.Count == 0)
                {
                    // A source that claims more records while returning nothing would loop forever.
                    return new PagedReadResult<T>(collected, false, true, pageNumber, "The source reported more records but returned an empty page.");
                }

                pageNumber++;
            }
        }
    }
}
