using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// What a solution's component rows contained, including the rows that were not cloud flows.
    /// The skipped counts are part of the result on purpose: an empty grid must be able to say
    /// "this solution has three classic workflows and no cloud flows" rather than look like a
    /// failed or empty read.
    /// </summary>
    public sealed class CloudFlowSelectionResult
    {
        internal CloudFlowSelectionResult(
            IReadOnlyList<WorkflowReference> flows,
            int rowsRead,
            int duplicateCount,
            int nonCloudCount,
            int unknownCategoryCount,
            int missingIdCount)
        {
            Flows = flows;
            RowsRead = rowsRead;
            DuplicateCount = duplicateCount;
            NonCloudCount = nonCloudCount;
            UnknownCategoryCount = unknownCategoryCount;
            MissingIdCount = missingIdCount;
        }

        /// <summary>Unique cloud flows, ordered by name for a stable grid.</summary>
        public IReadOnlyList<WorkflowReference> Flows { get; }

        /// <summary>Rows examined before filtering and de-duplication.</summary>
        public int RowsRead { get; }

        /// <summary>Cloud-flow rows removed because the same workflow id was seen already.</summary>
        public int DuplicateCount { get; }

        /// <summary>Rows excluded because they are classic workflows, dialogs, rules or BPFs.</summary>
        public int NonCloudCount { get; }

        /// <summary>Rows whose category was absent; excluded, but counted so nothing is hidden.</summary>
        public int UnknownCategoryCount { get; }

        /// <summary>Rows with no usable provider id; excluded because they cannot carry identity.</summary>
        public int MissingIdCount { get; }

        /// <summary>True when the solution had workflow components but none of them were cloud flows.</summary>
        public bool HasWorkflowComponentsWithoutCloudFlows
        {
            get { return Flows.Count == 0 && RowsRead > 0; }
        }

        /// <summary>
        /// Single coverage sentence shared by every view, so a filtered-away count cannot be
        /// described differently in two places.
        /// </summary>
        public string DescribeCoverage()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Flows.Count.ToString(CultureInfo.InvariantCulture)).Append(" cloud flow(s)");

            Append(text, "classic workflow(s) skipped", NonCloudCount);
            Append(text, "row(s) with unknown category skipped", UnknownCategoryCount);
            Append(text, "duplicate(s) removed", DuplicateCount);
            Append(text, "row(s) without an id skipped", MissingIdCount);

            return text.ToString();
        }

        private static void Append(StringBuilder text, string label, int count)
        {
            if (count > 0)
            {
                text.Append(" - ")
                    .Append(count.ToString(CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(label);
            }
        }
    }
}
