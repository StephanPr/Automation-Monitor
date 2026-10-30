using System;
using System.Collections.Generic;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// How membership is decided. Only <see cref="Snapshot"/> is produced by this milestone;
    /// <see cref="DynamicSolution"/> exists so the shape does not change when M4 adds
    /// reconciliation, and so persistence is never inferred from an in-memory object.
    /// </summary>
    public enum WatchlistMembershipMode
    {
        /// <summary>Members are fixed until the operator edits or re-imports them.</summary>
        Snapshot = 0,

        /// <summary>Membership follows the source solution and is reconciled on explicit refresh.</summary>
        DynamicSolution = 1
    }

    /// <summary>Why a workflow is in the watchlist. Combinable: a flow can come from both.</summary>
    [Flags]
    public enum WatchlistMemberSource
    {
        None = 0,

        /// <summary>Reached the watchlist through a solution component.</summary>
        Solution = 1,

        /// <summary>Added by the operator directly; must survive a dynamic refresh.</summary>
        Manual = 2
    }

    /// <summary>One workflow in a watchlist, with its provenance and per-flow inclusion.</summary>
    public sealed class WatchlistMember
    {
        public WatchlistMember(
            WorkflowReference workflow,
            WatchlistMemberSource source,
            IEnumerable<string>? sourceSolutionIds,
            bool isIncluded)
        {
            if (workflow == null)
            {
                throw new ArgumentNullException(nameof(workflow));
            }

            Workflow = workflow;
            Source = source;
            List<string> ids = new List<string>();
            if (sourceSolutionIds != null)
            {
                foreach (string id in sourceSolutionIds)
                {
                    if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            SourceSolutionIds = ids;
            IsIncluded = isIncluded;
        }

        public WorkflowReference Workflow { get; }

        public WorkflowKey Key { get { return Workflow.Key; } }

        public WatchlistMemberSource Source { get; }

        /// <summary>Solution ids whose components produced this membership, for later reconciliation.</summary>
        public IReadOnlyList<string> SourceSolutionIds { get; }

        /// <summary>False means the operator excluded this flow; it stays visible, not deleted.</summary>
        public bool IsIncluded { get; }
    }

    /// <summary>
    /// An in-memory set of workflows to monitor. Deliberately has no file, repository or
    /// persistence behaviour: saved watchlists are M4 and will serialize this shape.
    /// </summary>
    public sealed class Watchlist
    {
        internal Watchlist(
            string id,
            string name,
            WatchlistMembershipMode mode,
            IEnumerable<string>? sourceSolutionIds,
            IEnumerable<WatchlistMember>? members,
            ProviderResultState state,
            IEnumerable<string>? coverageNotes)
        {
            Id = id;
            Name = name;
            Mode = mode;
            State = state;
            List<string> ids = new List<string>();
            if (sourceSolutionIds != null)
            {
                foreach (string solutionId in sourceSolutionIds)
                {
                    if (!string.IsNullOrWhiteSpace(solutionId) && !ids.Contains(solutionId))
                    {
                        ids.Add(solutionId);
                    }
                }
            }

            SourceSolutionIds = ids;
            List<WatchlistMember> list = new List<WatchlistMember>();
            if (members != null)
            {
                list.AddRange(members);
            }

            Members = list;
            List<string> notes = new List<string>();
            if (coverageNotes != null)
            {
                foreach (string note in coverageNotes)
                {
                    if (!string.IsNullOrWhiteSpace(note) && !notes.Contains(note))
                    {
                        notes.Add(note);
                    }
                }
            }

            CoverageNotes = notes;
        }

        /// <summary>In-memory identity. Not persisted and not derived from the name.</summary>
        public string Id { get; }

        public string Name { get; }

        public WatchlistMembershipMode Mode { get; }

        /// <summary>Solutions this watchlist was generated from, in the order they were added.</summary>
        public IReadOnlyList<string> SourceSolutionIds { get; }

        public IReadOnlyList<WatchlistMember> Members { get; }

        /// <summary>
        /// Coverage of the discovery that produced this membership. A partial or failed read
        /// stays labelled here so no later view can present the list as the full truth.
        /// </summary>
        public ProviderResultState State { get; }

        /// <summary>Per-source statements: excluded component types, ceilings, failures.</summary>
        public IReadOnlyList<string> CoverageNotes { get; }

        /// <summary>True only when every contributing source read completely.</summary>
        public bool HasFullCoverage { get { return State == ProviderResultState.Complete; } }

        /// <summary>All coverage notes as one line, for the status bar and future exports.</summary>
        public string CoverageDescription
        {
            get { return string.Join(" ", CoverageNotes); }
        }

        /// <summary>Flows that will actually be monitored (included members only).</summary>
        public int MonitoredFlowCount
        {
            get
            {
                int count = 0;
                foreach (WatchlistMember member in Members)
                {
                    if (member.IsIncluded)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public IEnumerable<WorkflowReference> IncludedFlows
        {
            get
            {
                foreach (WatchlistMember member in Members)
                {
                    if (member.IsIncluded)
                    {
                        yield return member.Workflow;
                    }
                }
            }
        }

        public WatchlistMember? FindMember(WorkflowKey? key)
        {
            if (key == null)
            {
                return null;
            }

            foreach (WatchlistMember member in Members)
            {
                if (member.Key.Equals(key))
                {
                    return member;
                }
            }

            return null;
        }
    }
}
