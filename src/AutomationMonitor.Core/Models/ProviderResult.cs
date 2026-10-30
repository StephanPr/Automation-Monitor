using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// A collection plus the truth about how it was collected. Every provider read returns one
    /// of these so that "no rows", "not allowed", "broken" and "incomplete" stay distinguishable
    /// all the way into the UI.
    /// </summary>
    public sealed class ProviderResult<T>
    {
        private ProviderResult(
            T? value,
            ProviderResultState state,
            int pagesRead,
            int itemsRead,
            bool isTruncated,
            DateTimeOffset checkedAtUtc,
            string? message)
        {
            Value = value;
            State = state;
            PagesRead = pagesRead;
            ItemsRead = itemsRead;
            IsTruncated = isTruncated;
            CheckedAtUtc = checkedAtUtc;
            Message = message;
        }

        /// <summary>The collected value. Null only for non-success states.</summary>
        public T? Value { get; }

        public ProviderResultState State { get; }

        /// <summary>How many source pages were consumed to build this result.</summary>
        public int PagesRead { get; }

        /// <summary>Rows the source returned before filtering and de-duplication.</summary>
        public int ItemsRead { get; }

        /// <summary>True when a configured ceiling stopped the read early.</summary>
        public bool IsTruncated { get; }

        /// <summary>When the source was last contacted. Separate from how fresh the source data is.</summary>
        public DateTimeOffset CheckedAtUtc { get; }

        /// <summary>Sanitized, actionable text for a degraded state; never a payload, token or URL.</summary>
        public string? Message { get; }

        /// <summary>Rows are present and may be displayed, with the state shown alongside.</summary>
        public bool IsUsable
        {
            get { return State == ProviderResultState.Complete || State == ProviderResultState.Partial; }
        }

        public static ProviderResult<T> Complete(T value, int pagesRead, int itemsRead, DateTimeOffset checkedAtUtc)
        {
            return new ProviderResult<T>(value, ProviderResultState.Complete, pagesRead, itemsRead, false, checkedAtUtc, null);
        }

        public static ProviderResult<T> Partial(T value, int pagesRead, int itemsRead, DateTimeOffset checkedAtUtc, string? message)
        {
            return new ProviderResult<T>(value, ProviderResultState.Partial, pagesRead, itemsRead, true, checkedAtUtc, message);
        }

        public static ProviderResult<T> Unauthorized(int pagesRead, DateTimeOffset checkedAtUtc, string? message)
        {
            return new ProviderResult<T>(default(T), ProviderResultState.Unauthorized, pagesRead, 0, false, checkedAtUtc, message);
        }

        public static ProviderResult<T> Unavailable(int pagesRead, DateTimeOffset checkedAtUtc, string? message)
        {
            return new ProviderResult<T>(default(T), ProviderResultState.Unavailable, pagesRead, 0, false, checkedAtUtc, message);
        }

        public static ProviderResult<T> Unsupported(DateTimeOffset checkedAtUtc, string? message)
        {
            return new ProviderResult<T>(default(T), ProviderResultState.Unsupported, 0, 0, false, checkedAtUtc, message);
        }

        public static ProviderResult<T> Stale(T value, int pagesRead, int itemsRead, DateTimeOffset checkedAtUtc, string? message)
        {
            return new ProviderResult<T>(value, ProviderResultState.Stale, pagesRead, itemsRead, false, checkedAtUtc, message);
        }
    }
}
