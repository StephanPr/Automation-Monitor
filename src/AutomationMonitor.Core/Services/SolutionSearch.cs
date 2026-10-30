using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Filtering for the solution selector. Search is a core decision so every view offers the
    /// same behaviour: match on display name or publisher name, case-insensitively, and an empty
    /// query means "everything", never "nothing".
    /// </summary>
    public static class SolutionSearch
    {
        public static IReadOnlyList<SolutionReference> Filter(IEnumerable<SolutionReference>? solutions, string? query)
        {
            List<SolutionReference> matches = new List<SolutionReference>();
            if (solutions == null)
            {
                return matches;
            }

            string needle = query == null ? string.Empty : query.Trim();
            foreach (SolutionReference solution in solutions)
            {
                if (solution == null)
                {
                    continue;
                }

                if (needle.Length == 0
                    || Contains(solution.DisplayName, needle)
                    || Contains(solution.UniqueName, needle))
                {
                    matches.Add(solution);
                }
            }

            matches.Sort(delegate(SolutionReference left, SolutionReference right)
            {
                int byName = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
                if (byName != 0)
                {
                    return byName;
                }

                return string.Compare(left.UniqueName, right.UniqueName, StringComparison.OrdinalIgnoreCase);
            });

            return matches;
        }

        /// <summary>
        /// Selector text for one solution. Built in one place so the managed flag and version are
        /// never dropped by a view that hand-rolls its own label.
        /// </summary>
        public static string FormatLabel(SolutionReference solution)
        {
            if (solution == null)
            {
                return string.Empty;
            }

            System.Text.StringBuilder label = new System.Text.StringBuilder();
            label.Append(solution.DisplayName);
            if (!string.IsNullOrEmpty(solution.UniqueName))
            {
                label.Append(" (").Append(solution.UniqueName).Append(')');
            }

            if (!string.IsNullOrEmpty(solution.Version))
            {
                label.Append(" - v").Append(solution.Version);
            }

            if (solution.IsManaged)
            {
                label.Append(" - managed");
            }

            return label.ToString();
        }

        private static bool Contains(string? haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack)
                && haystack!.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
