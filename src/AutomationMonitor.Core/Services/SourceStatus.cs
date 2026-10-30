using System;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
     /// The one renderer for "what did this source actually give me". Every view must use it so a
     /// partial, unauthorized or stale read is described identically in the grid header, the
     /// status bar and (later) an export footer, instead of each control inventing its own
     /// optimistic wording.
    /// </summary>
    public static class SourceStatus
    {
        /// <summary>Short honest label for a coverage state. Never "OK".</summary>
        public static string Label(ProviderResultState state)
        {
            switch (state)
            {
                case ProviderResultState.Complete:
                    return "complete";
                case ProviderResultState.Partial:
                    return "PARTIAL";
                case ProviderResultState.Unauthorized:
                    return "NOT AUTHORIZED";
                case ProviderResultState.Unavailable:
                    return "UNAVAILABLE";
                case ProviderResultState.Unsupported:
                    return "NOT SUPPORTED";
                default:
                    return "STALE";
            }
        }

        /// <summary>
        /// One status line: source, coverage, size, when the source was last contacted, and the
        /// reason when the read was degraded. Times are UTC; time-zone display is a later
        /// presentation concern and must not change what is stored or compared.
        /// </summary>
        public static string Describe(
            string sourceLabel,
            ProviderResultState state,
            int itemsRead,
            int pagesRead,
            DateTimeOffset checkedAtUtc,
            string? message)
        {
            StringBuilder text = new StringBuilder();
            text.Append(string.IsNullOrWhiteSpace(sourceLabel) ? "Source" : sourceLabel.Trim())
                .Append(" - ")
                .Append(Label(state))
                .Append(" - ")
                .Append(itemsRead.ToString(CultureInfo.InvariantCulture))
                .Append(" row(s) over ")
                .Append(pagesRead.ToString(CultureInfo.InvariantCulture))
                .Append(" page(s) - checked ")
                .Append(checkedAtUtc.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(message))
            {
                text.Append(" - ").Append(message!.Trim());
            }

            return text.ToString();
        }

        /// <summary>Status line for a provider result, including the degraded reason.</summary>
        public static string Describe<T>(string sourceLabel, ProviderResult<T> result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            int items = result.IsUsable ? result.ItemsRead : 0;
            return Describe(sourceLabel, result.State, items, result.PagesRead, result.CheckedAtUtc, result.Message);
        }
    }
}
