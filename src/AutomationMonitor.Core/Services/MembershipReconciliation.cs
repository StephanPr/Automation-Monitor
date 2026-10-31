using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// What one solution read said about the flows it owns. It is the input to reconciliation, and it
    /// carries its own coverage: the difference between "this solution has no cloud flows" and "this
    /// solution could not be read" is the whole safety of removing a member, so it can never be
    /// collapsed into a list of flows.
    /// </summary>
    public sealed class MembershipEvidence
    {
        public MembershipEvidence(
            string solutionId,
            string? solutionName,
            ProviderResultState state,
            IEnumerable<WorkflowReference>? flows,
            int pagesRead,
            int itemsRead,
            DateTimeOffset checkedAtUtc,
            string? message = null)
        {
            SolutionId = (solutionId ?? string.Empty).Trim();
            SolutionName = string.IsNullOrWhiteSpace(solutionName) ? SolutionId : solutionName!.Trim();
            State = state;
            List<WorkflowReference> list = new List<WorkflowReference>();
            if (flows != null)
            {
                foreach (WorkflowReference flow in flows)
                {
                    if (flow != null)
                    {
                        list.Add(flow);
                    }
                }
            }

            Flows = list;
            PagesRead = pagesRead;
            ItemsRead = itemsRead;
            CheckedAtUtc = checkedAtUtc.ToUniversalTime();
            Message = message;
        }

        public string SolutionId { get; }

        /// <summary>Display name for the report. Provenance is keyed on <see cref="SolutionId"/>.</summary>
        public string SolutionName { get; }

        public ProviderResultState State { get; }

        public IReadOnlyList<WorkflowReference> Flows { get; }

        public int PagesRead { get; }

        public int ItemsRead { get; }

        /// <summary>When this solution was read. UTC, and separate from how fresh its rows are.</summary>
        public DateTimeOffset CheckedAtUtc { get; }

        /// <summary>Sanitized reason for a degraded read.</summary>
        public string? Message { get; }

        /// <summary>Rows were delivered and may be used to add members.</summary>
        public bool IsUsable
        {
            get { return State == ProviderResultState.Complete || State == ProviderResultState.Partial; }
        }

        /// <summary>Every component was read, so a member missing from this evidence really left.</summary>
        public bool IsComplete
        {
            get { return State == ProviderResultState.Complete; }
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(SolutionName).Append(" - ").Append(SourceStatus.Label(State)).Append(" - ")
                .Append(Flows.Count.ToString(CultureInfo.InvariantCulture)).Append(" flow(s) of ")
                .Append(ItemsRead.ToString(CultureInfo.InvariantCulture)).Append(" row(s) over ")
                .Append(PagesRead.ToString(CultureInfo.InvariantCulture)).Append(" page(s) - checked ")
                .Append(CheckedAtUtc.ToString("u", CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(Message))
            {
                text.Append(" - ").Append(Message!.Trim());
            }

            return text.ToString();
        }

        public override string ToString()
        {
            return Describe();
        }
    }

    /// <summary>One membership line in a reconciliation report.</summary>
    public sealed class MembershipChange
    {
        public MembershipChange(MembershipChangeKind kind, WatchlistMember member, string because)
        {
            Kind = kind;
            Member = member ?? throw new ArgumentNullException(nameof(member));
            Because = because ?? string.Empty;
        }

        public MembershipChangeKind Kind { get; }

        public WatchlistMember Member { get; }

        /// <summary>Why, naming the evidence. Shown to the operator before anything is applied.</summary>
        public string Because { get; }

        public string Describe()
        {
            return Member.Workflow.Name + " [" + Member.Key.Canonical + "] - " + Because;
        }

        public override string ToString()
        {
            return Describe();
        }
    }

    /// <summary>The kind of membership change a reconciliation reports.</summary>
    public enum MembershipChangeKind
    {
        /// <summary>Newly discovered in a source solution and now monitored.</summary>
        Added = 0,

        /// <summary>Left its solution, and the read that proved it was complete.</summary>
        Removed = 1,

        /// <summary>Still a member, unchanged.</summary>
        Retained = 2,

        /// <summary>Still a member but its source could not be read, so nothing is known about it.</summary>
        Unresolved = 3,

        /// <summary>Still excluded because the operator said so.</summary>
        KeptExcluded = 4,

        /// <summary>A manual member: discovery can add provenance but never remove it.</summary>
        KeptManual = 5,

        /// <summary>Discovered, but this list's mode means it was not added.</summary>
        NotApplied = 6
    }

    /// <summary>
    /// What a reconciliation pass found, and whether it was applied. Changes are reported whether or not
    /// they were applied: an operator has to see what a refresh is about to do before it happens, and a
    /// pass that removed nothing because the read was incomplete must say so rather than look current.
    /// </summary>
    public sealed class MembershipReconciliationResult
    {
        private readonly Dictionary<MembershipChangeKind, int> _counts = new Dictionary<MembershipChangeKind, int>();

        public MembershipReconciliationResult(
            Watchlist before,
            Watchlist after,
            bool wasApplied,
            ProviderResultState state,
            DateTimeOffset checkedAtUtc,
            IEnumerable<MembershipChange>? changes,
            IEnumerable<string>? notes,
            IEnumerable<MembershipEvidence>? evidence)
        {
            Before = before ?? throw new ArgumentNullException(nameof(before));
            After = after ?? before;
            WasApplied = wasApplied;
            State = state;
            CheckedAtUtc = checkedAtUtc.ToUniversalTime();

            List<MembershipChange> list = new List<MembershipChange>();
            if (changes != null)
            {
                foreach (MembershipChange change in changes)
                {
                    if (change == null)
                    {
                        continue;
                    }

                    list.Add(change);
                    int seen;
                    _counts[change.Kind] = _counts.TryGetValue(change.Kind, out seen) ? seen + 1 : 1;
                }
            }

            Changes = list;

            List<string> messages = new List<string>();
            if (notes != null)
            {
                messages.AddRange(notes);
            }

            Notes = messages;

            List<MembershipEvidence> sources = new List<MembershipEvidence>();
            if (evidence != null)
            {
                sources.AddRange(evidence);
            }

            Evidence = sources;
        }

        /// <summary>The list as it was stored before this pass.</summary>
        public Watchlist Before { get; }

        /// <summary>The list as it now is - the same instance as <see cref="Before"/> when nothing was applied.</summary>
        public Watchlist After { get; }

        /// <summary>True when membership in <see cref="After"/> reflects this pass.</summary>
        public bool WasApplied { get; }

        /// <summary>The combined coverage of every source read in this pass.</summary>
        public ProviderResultState State { get; }

        /// <summary>When the sources were read. UTC.</summary>
        public DateTimeOffset CheckedAtUtc { get; }

        public IReadOnlyList<MembershipChange> Changes { get; }

        public IReadOnlyList<string> Notes { get; }

        /// <summary>Per-source statements, so the report can name which solution said what.</summary>
        public IReadOnlyList<MembershipEvidence> Evidence { get; }

        public int AddedCount { get { return Count(MembershipChangeKind.Added); } }

        public int RemovedCount { get { return Count(MembershipChangeKind.Removed); } }

        public int RetainedCount { get { return Count(MembershipChangeKind.Retained); } }

        /// <summary>Members whose source could not be read: not current, not gone, and never deleted.</summary>
        public int UnresolvedCount { get { return Count(MembershipChangeKind.Unresolved); } }

        public int ExcludedCount { get { return Count(MembershipChangeKind.KeptExcluded); } }

        public int ManualCount { get { return Count(MembershipChangeKind.KeptManual); } }

        public int NotAppliedCount { get { return Count(MembershipChangeKind.NotApplied); } }

        public bool HasChanges
        {
            get { return AddedCount != 0 || RemovedCount != 0; }
        }

        private int Count(MembershipChangeKind kind)
        {
            int count;
            return _counts.TryGetValue(kind, out count) ? count : 0;
        }

        /// <summary>The one-line summary every view and the log use for one reconciliation pass.</summary>
        public string DescribeSummary()
        {
            StringBuilder text = new StringBuilder();
            text.Append("Membership of \"").Append(Before.Name).Append("\" - ")
                .Append(AddedCount.ToString(CultureInfo.InvariantCulture)).Append(" added, ")
                .Append(RemovedCount.ToString(CultureInfo.InvariantCulture)).Append(" removed, ")
                .Append(RetainedCount.ToString(CultureInfo.InvariantCulture)).Append(" kept, ")
                .Append(UnresolvedCount.ToString(CultureInfo.InvariantCulture)).Append(" unresolved, ")
                .Append(ExcludedCount.ToString(CultureInfo.InvariantCulture)).Append(" still excluded, ")
                .Append(ManualCount.ToString(CultureInfo.InvariantCulture)).Append(" manual - ")
                .Append(SourceStatus.Label(State)).Append(" - checked ")
                .Append(CheckedAtUtc.ToString("u", CultureInfo.InvariantCulture))
                .Append(WasApplied ? " - applied" : " - not applied");

            foreach (string note in Notes)
            {
                text.Append(" - ").Append(note);
            }

            return text.ToString();
        }

        /// <summary>
        /// The report shown before anything is applied: one line per change. Built here rather than in a
        /// dialog so the preview and the applied result cannot drift apart.
        /// </summary>
        public string DescribeChanges()
        {
            if (Changes.Count == 0)
            {
                return "Nothing to report: this pass found no source flows and no stored members.";
            }

            StringBuilder text = new StringBuilder();
            foreach (MembershipChange change in Changes)
            {
                text.Append(Label(change.Kind)).Append(": ").Append(change.Describe()).AppendLine();
            }

            return text.ToString().TrimEnd();
        }

        public static string Label(MembershipChangeKind kind)
        {
            switch (kind)
            {
                case MembershipChangeKind.Added:
                    return "ADD";
                case MembershipChangeKind.Removed:
                    return "REMOVE";
                case MembershipChangeKind.Retained:
                    return "KEEP";
                case MembershipChangeKind.Unresolved:
                    return "UNRESOLVED";
                case MembershipChangeKind.KeptExcluded:
                    return "EXCLUDED";
                case MembershipChangeKind.KeptManual:
                    return "MANUAL";
                default:
                    return "NOT APPLIED";
            }
        }

        public override string ToString()
        {
            return DescribeSummary();
        }
    }
}
