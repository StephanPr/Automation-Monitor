using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// What one run read actually contained, including the rows that did not become runs. Like
    /// <see cref="CloudFlowSelectionResult"/> for discovery, the skipped counts are part of the
    /// result so a short list can be explained: "12 run(s) - 2 row(s) without a run id skipped"
    /// is a different fact from "12 runs happened".
    /// </summary>
    public sealed class RunReadResult
    {
        internal RunReadResult(
            IReadOnlyList<WorkflowRun> runs,
            int rowsRead,
            int duplicateCount,
            int missingIdentityCount,
            int foreignWorkflowCount)
        {
            Runs = runs;
            RowsRead = rowsRead;
            DuplicateCount = duplicateCount;
            MissingIdentityCount = missingIdentityCount;
            ForeignWorkflowCount = foreignWorkflowCount;
        }

        /// <summary>Unique normalized runs for the requested workflow, in the order they arrived.</summary>
        public IReadOnlyList<WorkflowRun> Runs { get; }

        /// <summary>Rows the source returned before de-duplication and identity checks.</summary>
        public int RowsRead { get; }

        /// <summary>Rows removed because this read already contained that run id.</summary>
        public int DuplicateCount { get; }

        /// <summary>Rows with neither a run name nor a row id: they cannot carry identity.</summary>
        public int MissingIdentityCount { get; }

        /// <summary>
        /// Rows naming a different flow than the one that was asked for. A non-zero count means the
        /// source did not honour the workflow filter, and is reported instead of merged.
        /// </summary>
        public int ForeignWorkflowCount { get; }

        /// <summary>Single coverage sentence shared by every view and any future export footer.</summary>
        public string DescribeCoverage()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Runs.Count.ToString(CultureInfo.InvariantCulture)).Append(" run(s)");

            Append(text, "duplicate(s) removed", DuplicateCount);
            Append(text, "row(s) without a run id skipped", MissingIdentityCount);
            Append(text, "row(s) for another flow skipped", ForeignWorkflowCount);

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
