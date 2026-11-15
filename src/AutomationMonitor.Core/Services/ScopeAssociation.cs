using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Whether this build may reassign a stored watchlist's scope. It is a value rather than an <c>if</c>
    /// inside the association path because the question is a property of the whole build, not of one
    /// method: re-keying a saved list to <c>dataverse-organization:&lt;guid&gt;</c> only leaves the list
    /// usable if the code that opens it, lists it, names it, holds its per-environment settings, reconciles
    /// its members and matches discovered flows against it understands that same scope.
    ///
    /// <para>
    /// <b>The shipped answer is <see cref="Resolvable"/>.</b> AM-44.3a made every one of those consumers
    /// resolve the connected organization from the same completed identity read; AM-44.3b closed the last two
    /// that still compared scope text - the name-clash check and the per-environment profile - behind one shared
    /// rule (<see cref="ResourceScope.NamesSameEnvironment"/>), and put a connection-context guard on the apply
    /// path so a confirmation cannot land against a different organization. The previous-build round trip proves
    /// the release before this one reads a re-keyed file back and re-saves it without loss. The maintainer reported
    /// the resolution working inside XrmToolBox (docs/roadmap.md, section 3, item 10) and asked for this action to be
    /// enabled; that report did not cover the connection-switch step or the scaling and Tab-order detail, which stay
    /// open as item 11.
    /// </para>
    ///
    /// <para>
    /// What that gate does not license is doing any of it unprompted. A stored identity moves only when a
    /// completed verification read produced the evidence, the operator read the preview of that exact change and
    /// confirmed it, and the connection still names the organization the read named. Nothing associates a list
    /// on connect, open, discovery, refresh or save.
    /// </para>
    /// </summary>
    public sealed class ScopeResolutionSupport
    {
        /// <summary>The checkpoint whose gate had to land before a stored scope could be reassigned.</summary>
        public const string RequiredCheckpoint = "AM-44.3";

        private static readonly ScopeResolutionSupport CanReassign = new ScopeResolutionSupport(true, string.Empty);

        private readonly bool _canReassign;
        private readonly string _deferredReason;

        private ScopeResolutionSupport(bool canReassign, string deferredReason)
        {
            _canReassign = canReassign;
            _deferredReason = deferredReason;
        }

        /// <summary>
        /// The state this version ships: a stored scope may be reassigned, and only through the association
        /// action the operator started, previewed and confirmed. Named <c>Shipped</c> so a test can pin the
        /// product's real answer rather than a lookalike.
        /// </summary>
        public static ScopeResolutionSupport Shipped
        {
            get { return CanReassign; }
        }

        /// <summary>
        /// The state in which applying an association is safe: every consumer of a saved scope resolves the
        /// connected organization the same way. This is what <see cref="Shipped"/> returns today.
        /// </summary>
        public static ScopeResolutionSupport Resolvable()
        {
            return CanReassign;
        }

        /// <summary>
        /// The opposite state, kept for the refusal path and for the tests that prove a build which cannot
        /// resolve a qualified connected scope says so instead of half-applying an identity. No shipped call
        /// site uses it in this version; it is what the deferral wording and its tests are written against.
        /// </summary>
        public static ScopeResolutionSupport NotYetResolvable
        {
            get
            {
                return new ScopeResolutionSupport(
                    false,
                    "This version does not apply an environment association yet. It can resolve the connected Dataverse organization"
                        + " when it loads, lists and matches saved watchlists, and newly discovered flows carry that scope - but re-keying a"
                        + " list you saved earlier is switched off in this build until that resolution has been verified running inside"
                        + " XrmToolBox, and it would then happen only on your confirmation (" + RequiredCheckpoint + ")."
                        + " Nothing was changed and the saved file was not written.");
            }
        }

        /// <summary>True when re-keying a stored list leaves it usable, so an association may be applied.</summary>
        public bool CanReassignStoredScopes
        {
            get { return _canReassign; }
        }

        /// <summary>Why applying is deferred; empty when it is not.</summary>
        public string DeferredReason
        {
            get { return _deferredReason; }
        }

        public override string ToString()
        {
            return _canReassign ? "connected scope is resolvable" : "connected scope is not resolvable yet (" + RequiredCheckpoint + ")";
        }
    }

    /// <summary>Why an association cannot be applied. Every state has wording; none of them is "no".</summary>
    public enum WatchlistAssociationBlocker
    {
        /// <summary>No evidence that the connected organization holds these workflow ids.</summary>
        EvidenceNotVerified = 0,

        /// <summary>The connection could not name an organization, so there is nothing to attach.</summary>
        NoConnectedOrganization = 1,

        /// <summary>The list has no members, so nothing could confirm it belongs here.</summary>
        NothingToAssociate = 2,

        /// <summary>The list already names a different organization.</summary>
        AssociatedWithAnotherOrganization = 3,

        /// <summary>Some members already carry a recorded scope, which association never overwrites.</summary>
        MemberScopesAlreadyRecorded = 4,

        /// <summary>The saved file holds content this build cannot write back, so nothing may be changed.</summary>
        UnrepresentableMembers = 5
    }

    /// <summary>What the operator may do about the plan in front of them.</summary>
    public enum WatchlistAssociationDecision
    {
        /// <summary>Everything checks out and this build may apply it on an explicit confirmation.</summary>
        ReadyToConfirm = 0,

        /// <summary>Nothing would change: the list already has exactly this identity.</summary>
        NoChange = 1,

        /// <summary>The plan is sound but this build must not re-key a stored list yet.</summary>
        Deferred = 2,

        /// <summary>At least one blocker applies; the reasons say which.</summary>
        Blocked = 3
    }

    /// <summary>
    /// What attaching a verified environment identity to a saved watchlist would do, before it does it.
    /// The operator reads this and confirms; nothing in here writes, and the target scope only exists because
    /// a completed read established it.
    ///
    /// <para>
    /// A preview also binds a confirmation to a specific state: <see cref="Token"/> covers the list's
    /// modification time, its membership count, its current scope and the evidence it rests on, so applying a
    /// preview that no longer describes the catalog is refused rather than silently honoured.
    /// </para>
    /// </summary>
    public sealed class WatchlistAssociationPreview
    {
        internal WatchlistAssociationPreview(
            WatchlistAssociationDecision decision,
            IEnumerable<WatchlistAssociationBlocker> blockers,
            IEnumerable<string> reasons,
            string? listId,
            string? listName,
            ResourceScope fromScope,
            ResourceScope? targetScope,
            WorkflowIdentityEvidence? evidence,
            int memberCount,
            int includedCount,
            int rekeyedCount,
            int recordedScopeCount,
            bool selectionMoves,
            bool overridesCarry,
            string token)
        {
            Decision = decision;
            Blockers = new List<WatchlistAssociationBlocker>(blockers);
            Reasons = new List<string>(reasons);
            ListId = listId ?? string.Empty;
            ListName = listName ?? string.Empty;
            FromScope = fromScope ?? ResourceScope.Unverified;
            TargetScope = targetScope ?? ResourceScope.Unverified;
            Evidence = evidence;
            MemberCount = memberCount;
            IncludedCount = includedCount;
            RekeyedCount = rekeyedCount;
            RecordedScopeCount = recordedScopeCount;
            SelectionMoves = selectionMoves;
            OverridesCarry = overridesCarry;
            Token = token;
        }

        public WatchlistAssociationDecision Decision { get; }

        public IReadOnlyList<WatchlistAssociationBlocker> Blockers { get; }

        /// <summary>Human reasons behind <see cref="Decision"/>, in the order a person should read them.</summary>
        public IReadOnlyList<string> Reasons { get; }

        public string ListId { get; }

        /// <summary>The operator's own name for the list. Presentation, and never part of <see cref="Token"/>.</summary>
        public string ListName { get; }

        public ResourceScope FromScope { get; }

        public ResourceScope TargetScope { get; }

        /// <summary>The read this plan rests on, kept so applying re-checks against the same evidence.</summary>
        public WorkflowIdentityEvidence? Evidence { get; }

        public int MemberCount { get; }

        public int IncludedCount { get; }

        /// <summary>Members whose key would move to the target scope.</summary>
        public int RekeyedCount { get; }

        /// <summary>Members that already carry a recorded scope and would be left exactly as they are.</summary>
        public int RecordedScopeCount { get; }

        /// <summary>True when this environment's saved active-list selection moves with the list.</summary>
        public bool SelectionMoves { get; }

        /// <summary>True when the unverified environment's stored overrides carry to the new identity.</summary>
        public bool OverridesCarry { get; }

        /// <summary>Whether this build may apply the plan it describes.</summary>
        public bool CanApply
        {
            get { return Decision == WatchlistAssociationDecision.ReadyToConfirm; }
        }

        public string Token { get; }

        /// <summary>True when the caller is confirming exactly this plan against exactly this state.</summary>
        public bool IsSamePlanAs(WatchlistAssociationPreview? other)
        {
            return other != null && string.Equals(Token, other.Token, StringComparison.Ordinal);
        }

        /// <summary>The copyable block the operator reads before confirming, and afterwards.</summary>
        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("Watchlist environment association - preview").Append(Environment.NewLine).Append(Environment.NewLine);
            text.Append("  Watchlist           : ").Append(ListName).Append(Environment.NewLine);
            text.Append("  Members             : ")
                .Append(Num(MemberCount)).Append(" (")
                .Append(Num(IncludedCount)).Append(" selected, ")
                .Append(Num(MemberCount - IncludedCount)).Append(" excluded)").Append(Environment.NewLine);
            text.Append("  Current environment : ").Append(ResourceScopeLabel.Describe(FromScope)).Append(Environment.NewLine);
            text.Append("  Proposed environment: ").Append(
                TargetScope.IsQualified
                    ? ResourceScopeLabel.Describe(TargetScope, TargetScope)
                    : ResourceScopeLabel.Describe(FromScope)).Append(Environment.NewLine);
            text.Append("  Evidence            : ").Append(
                Evidence == null ? "no identity read has been made" : Evidence.Verdict + " (read " + Evidence.CheckedAtUtc.ToString("u", CultureInfo.InvariantCulture) + ")")
                .Append(Environment.NewLine);
            if (Evidence != null && Evidence.Reason.Length > 0)
            {
                text.Append("                        ").Append(Evidence.Reason).Append(Environment.NewLine);
            }

            text.Append(Environment.NewLine).Append("  What would change").Append(Environment.NewLine);
            if (RekeyedCount > 0)
            {
                text.Append("    - the list's recorded environment becomes the organization named above").Append(Environment.NewLine);
                text.Append("    - ").Append(Num(RekeyedCount))
                    .Append(" member key(s) gain that scope; their ids, inclusion and provenance do not move").Append(Environment.NewLine);
            }
            else if (!FromScope.IsSameResourceAs(TargetScope))
            {
                text.Append("    - the list's recorded environment becomes the organization named above; every member already carries it")
                    .Append(Environment.NewLine);
            }
            else
            {
                text.Append("    - nothing: the list and its members already carry this identity").Append(Environment.NewLine);
            }

            if (SelectionMoves)
            {
                text.Append("    - this environment's saved active-list selection moves to the new identity in the same write").Append(Environment.NewLine);
            }

            text.Append(OverridesCarry
                ? "    - the unverified environment's stored overrides carry to the new identity (the old entry is kept, not deleted)"
                : "    - stored environment overrides are left exactly where they are").Append(Environment.NewLine);

            text.Append(Environment.NewLine).Append("  What cannot change").Append(Environment.NewLine);
            text.Append("    - workflow ids, names, membership mode, description, preferences, coverage and solution provenance").Append(Environment.NewLine);
            text.Append("    - any other saved watchlist, and any other environment's settings").Append(Environment.NewLine);
            text.Append("    - nothing is deleted: a save first keeps the current file as ").Append("watchlists.json.bak").Append(Environment.NewLine);
            text.Append("    - no environment name, url or friendly name is stored: a display name is presentation, never identity").Append(Environment.NewLine);

            text.Append(Environment.NewLine).Append("  Decision: ").Append(DecisionText()).Append(Environment.NewLine);

            if (RecordedScopeCount > 0)
            {
                text.Append("  Note: ").Append(Num(RecordedScopeCount))
                    .Append(" member(s) already carry a recorded scope; association never overwrites a recorded identity.").Append(Environment.NewLine);
            }

            return text.ToString();
        }

        /// <summary>The decision in one sentence, with the reason that applies.</summary>
        public string DecisionText()
        {
            switch (Decision)
            {
                case WatchlistAssociationDecision.ReadyToConfirm:
                    return "READY TO CONFIRM - say so and this build will apply it.";
                case WatchlistAssociationDecision.NoChange:
                    return "NO CHANGE - this list already carries that identity.";
                case WatchlistAssociationDecision.Deferred:
                    return "DEFERRED - " + string.Join(" ", Reasons);
                default:
                    return "REFUSED - " + string.Join(" ", Reasons);
            }
        }

        private static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Decides what attaching a verified organization identity to one saved watchlist means, and performs the
    /// in-memory rewrite. Every rule that protects the operator's file lives here rather than in a view:
    ///
    /// <list type="bullet">
    /// <item><description>the target scope exists only because a completed read established it;</description></item>
    /// <item><description>only a member whose environment was never recorded gains a scope - a recorded scope,
    /// whatever its shape, is never overwritten, because association is not entitled to reinterpret it;</description></item>
    /// <item><description>re-applying an association that is already in place changes nothing at all;</description></item>
    /// <item><description>this build may not re-key a stored list until the connected scope is resolvable
    /// (<see cref="ScopeResolutionSupport"/>).</description></item>
    /// </list>
    /// </summary>
    public static class WatchlistAssociationPlanner
    {
        /// <summary>
        /// Build the preview for one list against one read's evidence. <paramref name="canWriteBack"/> is the
        /// catalog's answer to "could this be saved at all" - a catalog holding content this build cannot
        /// represent is never a candidate for an identity change.
        /// </summary>
        public static WatchlistAssociationPreview Plan(
            Watchlist? list,
            WorkflowIdentityEvidence? evidence,
            ScopeResolutionSupport? support,
            bool canWriteBack)
        {
            ScopeResolutionSupport resolution = support ?? ScopeResolutionSupport.Shipped;

            if (list == null)
            {
                return Block(
                    null,
                    ResourceScope.Unverified,
                    null,
                    null,
                    new WatchlistAssociationBlocker[] { WatchlistAssociationBlocker.NothingToAssociate },
                    new string[] { "no watchlist was given, so there is nothing to associate" },
                    0,
                    0,
                    0,
                    0,
                    false,
                    false);
            }

            ResourceScope from = ResourceScope.Read(list.EnvironmentId);
            ResourceScope target = evidence == null ? ResourceScope.Unverified : evidence.Organization;

            List<WatchlistAssociationBlocker> blockers = new List<WatchlistAssociationBlocker>();
            List<string> reasons = new List<string>();

            if (!canWriteBack)
            {
                blockers.Add(WatchlistAssociationBlocker.UnrepresentableMembers);
                reasons.Add("the saved file holds content this version cannot read back, so no list may be changed from it");
            }

            if (evidence == null)
            {
                blockers.Add(WatchlistAssociationBlocker.EvidenceNotVerified);
                reasons.Add("no identity read has been made, so nothing proves these workflow ids live in any particular organization");
            }
            else if (!target.IsQualified)
            {
                blockers.Add(WatchlistAssociationBlocker.NoConnectedOrganization);
                reasons.Add(evidence.Reason.Length > 0 ? evidence.Reason : evidence.Verdict);
            }
            else if (!evidence.CanSupportAssociation)
            {
                blockers.Add(WatchlistAssociationBlocker.EvidenceNotVerified);
                reasons.Add(evidence.Verdict + (evidence.Reason.Length > 0 ? ": " + evidence.Reason : "."));
            }

            if (list.Members.Count == 0)
            {
                blockers.Add(WatchlistAssociationBlocker.NothingToAssociate);
                reasons.Add("this list has no members, so there is no evidence that could tie it to an environment");
            }

            if (from.IsQualified && target.IsQualified && !from.IsSameResourceAs(target))
            {
                blockers.Add(WatchlistAssociationBlocker.AssociatedWithAnotherOrganization);
                reasons.Add("this list is already associated with " + ResourceScopeLabel.Describe(from)
                    + ", not " + ResourceScopeLabel.Describe(target)
                    + " - a stored identity is never moved by a second association");
            }

            int rekeyed = 0;
            int matching = 0;
            int foreign = 0;
            int included = 0;
            foreach (WatchlistMember member in list.Members)
            {
                if (member.IsIncluded)
                {
                    included++;
                }

                ResourceScope scope = member.Key.Scope;
                if (scope.IsUnverified)
                {
                    rekeyed++;
                }
                else if (target.IsSameResourceAs(scope))
                {
                    matching++;
                }
                else
                {
                    // A member whose environment is already recorded as something else - another organization, a
                    // legacy bare id, an unknown kind or a damaged value - is left alone. Half a list re-keyed and
                    // half not is a watchlist nobody can reason about, so the whole association is refused instead.
                    foreign++;
                }
            }

            int recorded = matching + foreign;
            if (foreign > 0)
            {
                blockers.Add(WatchlistAssociationBlocker.MemberScopesAlreadyRecorded);
                reasons.Add(foreign.ToString(CultureInfo.InvariantCulture)
                    + " member(s) of this list already carry a different recorded environment; association only attaches an identity"
                    + " to members whose environment was never recorded");
            }

            bool selectionMoves = target.IsQualified && (rekeyed > 0 || !from.IsSameResourceAs(target));
            bool overridesCarry = selectionMoves;

            if (blockers.Count > 0)
            {
                return Block(list, from, target, evidence, blockers, reasons, list.Members.Count, included, rekeyed, recorded, selectionMoves, overridesCarry);
            }

            if (rekeyed == 0 && from.IsSameResourceAs(target))
            {
                return new WatchlistAssociationPreview(
                    WatchlistAssociationDecision.NoChange,
                    new WatchlistAssociationBlocker[0],
                    new string[] { "this list and all of its members already carry " + ResourceScopeLabel.Describe(target) },
                    list.Id,
                    list.Name,
                    from,
                    target,
                    evidence,
                    list.Members.Count,
                    included,
                    0,
                    recorded,
                    false,
                    false,
                    Token(list, evidence));
            }

            WatchlistAssociationDecision decision = resolution.CanReassignStoredScopes
                ? WatchlistAssociationDecision.ReadyToConfirm
                : WatchlistAssociationDecision.Deferred;

            List<string> notes = new List<string>();
            if (decision == WatchlistAssociationDecision.Deferred)
            {
                notes.Add(resolution.DeferredReason);
            }

            return new WatchlistAssociationPreview(
                decision,
                new WatchlistAssociationBlocker[0],
                notes,
                list.Id,
                list.Name,
                from,
                target,
                evidence,
                list.Members.Count,
                included,
                rekeyed,
                recorded,
                selectionMoves,
                overridesCarry,
                Token(list, evidence));
        }

        /// <summary>
        /// The in-memory rewrite: the list gains the target scope and only the members whose environment was
        /// never recorded are re-keyed. Everything else about a member is carried across untouched, and an
        /// exclusion moves with its member because it is derived from member identity rather than stored beside it.
        /// </summary>
        public static Watchlist Apply(Watchlist list, ResourceScope target, DateTimeOffset appliedAtUtc)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            if (target == null || !target.IsQualified)
            {
                throw new ArgumentException("An association can only attach a qualified scope.", nameof(target));
            }

            string scope = target.Text;
            List<WatchlistMember> members = new List<WatchlistMember>(list.Members.Count);
            foreach (WatchlistMember member in list.Members)
            {
                members.Add(member.Key.Scope.IsUnverified ? ReKey(member, scope) : member);
            }

            return list.WithEnvironment(scope).WithMembers(members).WithModifiedAt(appliedAtUtc);
        }

        private static WatchlistMember ReKey(WatchlistMember member, string scope)
        {
            WorkflowReference flow = member.Workflow;
            WorkflowKey key = WorkflowKey.Create(flow.Key.Provider, scope, flow.Key.WorkflowId);
            WorkflowReference rekeyed = new WorkflowReference(
                key,
                flow.Name,
                flow.State,
                flow.RawStateCode,
                flow.RawStatusCode,
                flow.RawCategory,
                flow.Description,
                flow.CreatedOnUtc,
                flow.ModifiedOnUtc);
            return member.WithWorkflow(rekeyed);
        }

        /// <summary>
        /// What a confirmation is bound to: the list's state and the evidence, never its display name.
        /// </summary>
        private static string Token(Watchlist list, WorkflowIdentityEvidence? evidence)
        {
            StringBuilder text = new StringBuilder();
            text.Append(list.Id).Append('|')
                .Append(list.ModifiedAtUtc.HasValue ? list.ModifiedAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : "unset").Append('|')
                .Append(list.Members.Count.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(list.EnvironmentId).Append('|')
                .Append(evidence == null ? "none" : evidence.Kind.ToString()).Append('|')
                .Append(evidence == null ? string.Empty : evidence.Organization.Text).Append('|')
                .Append(evidence == null ? string.Empty : evidence.CheckedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static WatchlistAssociationPreview Block(
            Watchlist? list,
            ResourceScope from,
            ResourceScope? target,
            WorkflowIdentityEvidence? evidence,
            IEnumerable<WatchlistAssociationBlocker> blockers,
            IEnumerable<string> reasons,
            int memberCount,
            int included,
            int rekeyed,
            int recorded,
            bool selectionMoves,
            bool overridesCarry)
        {
            return new WatchlistAssociationPreview(
                WatchlistAssociationDecision.Blocked,
                blockers,
                reasons,
                list == null ? null : list.Id,
                list == null ? null : list.Name,
                from,
                target,
                evidence,
                memberCount,
                included,
                rekeyed,
                recorded,
                selectionMoves,
                overridesCarry,
                list == null ? "no-list" : Token(list, evidence));
        }
    }
}
