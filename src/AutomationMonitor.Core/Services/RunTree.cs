using System;
using System.Collections.Generic;
using System.Globalization;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// One node of a resolved execution tree: a run, the children the evidence attached to it, and
    /// how it came to sit where it sits. A node is a view over the runs of the current result set,
    /// never a copy that could drift from them.
    /// </summary>
    public sealed class RunTree
    {
        internal RunTree(WorkflowRun run, int depth, bool parentWasResolved, bool isInCycle)
        {
            Run = run;
            Depth = depth;
            ParentWasResolved = parentWasResolved;
            IsInCycle = isInCycle;
            Children = new List<RunTree>();
        }

        /// <summary>The execution this node represents.</summary>
        public WorkflowRun Run { get; }

        /// <summary>0 for a root; increases one level per resolved parent link.</summary>
        public int Depth { get; }

        /// <summary>Nodes whose evidence pointed at this run, in start-time order.</summary>
        public IList<RunTree> Children { get; }

        /// <summary>
        /// True when this run names a parent and that parent was found in the result set. False on
        /// a run that does name a parent means the parent exists but was not retrieved - a coverage
        /// gap, not an absence of a parent.
        /// </summary>
        public bool ParentWasResolved { get; }

        /// <summary>
        /// True when this run takes part in a parent cycle: the source claims links that cannot all
        /// be true, so the group is shown as broken evidence instead of being walked as a tree.
        /// </summary>
        public bool IsInCycle { get; }

        /// <summary>Descendants of this node, excluding the node itself. Counts runs, not actions.</summary>
        public int DescendantCount { get; internal set; }

        /// <summary>Total milliseconds of this run plus its descendants. Null when a member lacks one.</summary>
        public long? TotalDurationMs { get; internal set; }

        /// <summary>True when a duration in this subtree could not be stated, so the total is not a total.</summary>
        public bool HasDurationGap { get; internal set; }

        public override string ToString()
        {
            return new string(' ', Depth * 2) + Run.WorkflowName + " " + Run.StatusLabel
                + " (" + DescendantCount.ToString(CultureInfo.InvariantCulture) + " descendant(s))";
        }
    }
}
