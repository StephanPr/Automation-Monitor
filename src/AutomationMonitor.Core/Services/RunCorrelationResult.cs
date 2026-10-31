using System;
using System.Collections.Generic;
using System.Globalization;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// A parent/child forest over one result set. Every retrieved run appears exactly once, so a
    /// tree can never hide a row: links that could not be resolved and links that contradict each
    /// other both surface as roots that carry their own explanation.
    /// </summary>
    public sealed class RunCorrelationResult
    {
        internal RunCorrelationResult(
            IReadOnlyList<RunTree> roots,
            IReadOnlyDictionary<string, IReadOnlyList<WorkflowRun>> childrenByParentRunId,
            IReadOnlyList<string> unresolvedParentRunIds,
            IReadOnlyList<string> ambiguousRunIds,
            IReadOnlyList<string> cyclicRunIds,
            int linkedRunCount)
        {
            Roots = roots;
            ChildrenByParentRunId = childrenByParentRunId;
            UnresolvedParentRunIds = unresolvedParentRunIds;
            AmbiguousRunIds = ambiguousRunIds;
            CyclicRunIds = cyclicRunIds;
            LinkedRunCount = linkedRunCount;
        }

        /// <summary>Runs with no resolvable parent, plus the unresolved and cyclic groups.</summary>
        public IReadOnlyList<RunTree> Roots { get; }

        /// <summary>Children keyed on the parent's run id. The parent reference is the only link source.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<WorkflowRun>> ChildrenByParentRunId { get; }

        /// <summary>
        /// Parent ids some run pointed at that no retrieved run carries. Expected when a parent ran
        /// outside the window, belongs to a flow that was not requested, or was never ingested;
        /// never replaced by a synthetic parent.
        /// </summary>
        public IReadOnlyList<string> UnresolvedParentRunIds { get; }

        /// <summary>
        /// Run ids that more than one retrieved run claims. A parent reference to such an id is not
        /// evidence of which run was meant, so those children stay unlinked instead of guessing.
        /// </summary>
        public IReadOnlyList<string> AmbiguousRunIds { get; }

        /// <summary>Run ids taking part in a parent cycle.</summary>
        public IReadOnlyList<string> CyclicRunIds { get; }

        /// <summary>Runs whose parent link resolved to another run in this result set.</summary>
        public int LinkedRunCount { get; }

        /// <summary>Number of top-level nodes, including the cyclic group shown as roots.</summary>
        public int RootRunCount { get { return Roots.Count; } }

        /// <summary>
        /// True when no run named a parent at all. A view should then say the source reported no
        /// hierarchy rather than render one flat tree and let it look like a result.
        /// </summary>
        public bool ReportsNoHierarchy
        {
            get { return LinkedRunCount == 0 && UnresolvedParentRunIds.Count == 0 && CyclicRunIds.Count == 0; }
        }

        /// <summary>
        /// Coverage sentence for the details pane and any export footer. Counts are run counts: a
        /// transaction or action total cannot be derived from run rows and is never implied here.
        /// </summary>
        public string DescribeCoverage()
        {
            return RootRunCount.ToString(CultureInfo.InvariantCulture) + " root run(s), "
                + LinkedRunCount.ToString(CultureInfo.InvariantCulture) + " linked child run(s), "
                + UnresolvedParentRunIds.Count.ToString(CultureInfo.InvariantCulture) + " unresolved parent id(s), "
                + CyclicRunIds.Count.ToString(CultureInfo.InvariantCulture) + " run(s) in parent cycles, "
                + AmbiguousRunIds.Count.ToString(CultureInfo.InvariantCulture) + " ambiguous run id(s)";
        }
    }
}
