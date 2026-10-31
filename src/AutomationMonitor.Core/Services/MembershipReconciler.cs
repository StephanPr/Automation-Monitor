using System;
using System.Collections.Generic;
using System.Globalization;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// The one membership rule: what a set of solution reads says about a saved watchlist, and what may
    /// therefore change. A pure function of (stored list, evidence), so the same rule serves the preview
    /// and the applied refresh, and the safety properties are testable without a file or a tenant.
    ///
    /// <para>The rules, in the order they are decided:</para>
    /// <list type="number">
    /// <item><description>A flow reached through several solutions is one member with all of them as
    /// provenance. Identity is the provider-scoped key, never the name.</description></item>
    /// <item><description>A manually added member is never removed by discovery, whether or not it is
    /// still in a solution.</description></item>
    /// <item><description>An exclusion is a recorded decision: a re-discovered flow stays excluded, and
    /// reconciliation never re-includes anything.</description></item>
    /// <item><description>A member is removed only when every solution it came from was read completely
    /// and did not report it. A partial, unauthorized, unavailable or stale read keeps the member and
    /// labels it unresolved instead.</description></item>
    /// <item><description>Only a dynamic list changes at all. Snapshot and manual lists get the same
    /// report with nothing applied, because their membership belongs to the operator.</description></item>
    /// </list>
    /// </summary>
    public static class MembershipReconciler
    {
        /// <summary>
        /// Compare the stored membership with what the sources reported. Nothing is read or written here:
        /// the caller supplies the evidence it gathered and decides whether to persist the result.
        /// </summary>
        public static MembershipReconciliationResult Reconcile(
            Watchlist watchlist,
            IEnumerable<MembershipEvidence>? evidence,
            bool applyChanges,
            DateTimeOffset checkedAtUtc)
        {
            if (watchlist == null)
            {
                throw new ArgumentNullException(nameof(watchlist));
            }

            List<string> notes = new List<string>();
            List<MembershipEvidence> sources = Collect(evidence, notes);
            DateTimeOffset seenAtUtc = checkedAtUtc.ToUniversalTime();
            bool isDynamic = watchlist.Mode == WatchlistMembershipMode.DynamicSolution;

            if (sources.Count == 0)
            {
                notes.Add("no source solution was read this pass, so membership is exactly as stored");
                return new MembershipReconciliationResult(
                    watchlist, watchlist, false, ProviderResultState.Unavailable, seenAtUtc, null, notes, null);
            }

            ProviderResultState combined = ProviderResultState.Complete;
            Dictionary<string, Discovery> found = new Dictionary<string, Discovery>(StringComparer.Ordinal);
            foreach (MembershipEvidence source in sources)
            {
                combined = ProviderResultStates.Worst(combined, source.State);
                if (!source.IsUsable)
                {
                    notes.Add(source.SolutionName + " could not be read (" + SourceStatus.Label(source.State) + ")");
                    continue;
                }

                foreach (WorkflowReference flow in source.Flows)
                {
                    Discovery.Record(found, flow, source.SolutionId, seenAtUtc);
                }
            }

            List<MembershipChange> changes = new List<MembershipChange>();
            List<WatchlistMember> kept = new List<WatchlistMember>();
            foreach (WatchlistMember member in watchlist.Members)
            {
                Discovery? hit;
                found.TryGetValue(member.Key.Canonical, out hit);
                bool isManual = member.Source.HasFlag(WatchlistMemberSource.Manual);

                if (hit != null)
                {
                    WatchlistMember observed = member.WithObservation(WatchlistMemberSource.Solution, hit.SolutionIds, hit.SeenAtUtc);
                    kept.Add(observed);
                    if (!observed.IsIncluded)
                    {
                        changes.Add(new MembershipChange(
                            MembershipChangeKind.KeptExcluded,
                            observed,
                            "found again in " + DescribeSolutions(hit.SolutionIds) + " but stays excluded because you excluded it"));
                    }
                    else if (isManual)
                    {
                        changes.Add(new MembershipChange(
                            MembershipChangeKind.KeptManual,
                            observed,
                            "in " + DescribeSolutions(hit.SolutionIds) + " and added by hand, so no pass can remove it"));
                    }
                    else
                    {
                        changes.Add(new MembershipChange(
                            MembershipChangeKind.Retained,
                            observed,
                            "still a component of " + DescribeSolutions(hit.SolutionIds)));
                    }

                    found.Remove(member.Key.Canonical);
                    continue;
                }

                DecideAbsent(watchlist, member, sources, isDynamic, applyChanges, isManual, changes, kept);
            }

            foreach (KeyValuePair<string, Discovery> pair in found)
            {
                Discovery newcomer = pair.Value;
                WatchlistMember member = new WatchlistMember(
                    newcomer.Flow,
                    WatchlistMemberSource.Solution,
                    newcomer.SolutionIds,
                    true,
                    seenAtUtc,
                    seenAtUtc);

                if (isDynamic && applyChanges)
                {
                    kept.Add(member);
                    changes.Add(new MembershipChange(
                        MembershipChangeKind.Added,
                        member,
                        "new component of " + DescribeSolutions(newcomer.SolutionIds)));
                }
                else
                {
                    changes.Add(new MembershipChange(
                        MembershipChangeKind.NotApplied,
                        member,
                        "new component of " + DescribeSolutions(newcomer.SolutionIds) + " - " + NotAppliedReason(watchlist, applyChanges)));
                }
            }

            bool wasApplied = isDynamic && applyChanges;
            Watchlist after = watchlist;
            if (wasApplied)
            {
                WatchlistBuilder builder = WatchlistBuilder.From(watchlist);
                builder.SetMembers(kept);
                builder.SetCoverage(combined, CoverageNotes(sources, changes));
                builder.StampMembershipRefresh(seenAtUtc);
                after = builder.Build();
            }
            else
            {
                notes.Add(NotAppliedReason(watchlist, applyChanges));
            }

            return new MembershipReconciliationResult(watchlist, after, wasApplied, combined, seenAtUtc, changes, notes, sources);
        }

        /// <summary>
        /// One statement per solution: the last one wins. Two answers about the same solution in one pass
        /// means one superseded the other, and merging them would make a component appear in a solution
        /// that no longer reports it.
        /// </summary>
        private static List<MembershipEvidence> Collect(IEnumerable<MembershipEvidence>? evidence, List<string> notes)
        {
            List<MembershipEvidence> sources = new List<MembershipEvidence>();
            if (evidence == null)
            {
                return sources;
            }

            foreach (MembershipEvidence statement in evidence)
            {
                if (statement == null || string.IsNullOrWhiteSpace(statement.SolutionId))
                {
                    continue;
                }

                MembershipEvidence? repeated = Find(sources, statement.SolutionId);
                if (repeated == null)
                {
                    sources.Add(statement);
                    continue;
                }

                for (int i = 0; i < sources.Count; i++)
                {
                    if (string.Equals(sources[i].SolutionId, statement.SolutionId, StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add(statement.SolutionName + " reported twice in one pass; the later statement was used");
                        sources[i] = statement;
                        break;
                    }
                }
            }

            return sources;
        }

        /// <summary>
        /// Decide a stored member no usable source reported this pass. The whole question is whether the
        /// evidence could have seen it: if it could not, the member stays and is labelled unresolved,
        /// because losing a member to a permission error is worse than carrying a stale one.
        /// </summary>
        private static void DecideAbsent(
            Watchlist watchlist,
            WatchlistMember member,
            List<MembershipEvidence> sources,
            bool isDynamic,
            bool applyChanges,
            bool isManual,
            List<MembershipChange> changes,
            List<WatchlistMember> kept)
        {
            if (isManual)
            {
                kept.Add(member);
                changes.Add(new MembershipChange(
                    MembershipChangeKind.KeptManual,
                    member,
                    "added by hand, so it is kept even though no source reported it"));
                return;
            }

            if (member.SourceSolutionIds.Count == 0)
            {
                kept.Add(member);
                changes.Add(new MembershipChange(
                    MembershipChangeKind.Unresolved,
                    member,
                    "the record does not say which solution it came from, so nothing could be checked about it"));
                return;
            }

            bool everySourceReadCompletely = true;
            List<string> unread = new List<string>();
            foreach (string solutionId in member.SourceSolutionIds)
            {
                MembershipEvidence? source = Find(sources, solutionId);
                if (source == null)
                {
                    everySourceReadCompletely = false;
                    unread.Add(solutionId);
                    continue;
                }

                if (!source.IsComplete)
                {
                    everySourceReadCompletely = false;
                    unread.Add(source.SolutionName);
                }
            }

            if (!everySourceReadCompletely)
            {
                kept.Add(member);
                changes.Add(new MembershipChange(
                    MembershipChangeKind.Unresolved,
                    member,
                    "its source " + Join(unread) + " was not read completely this pass, so it was kept instead of assumed gone"));
                return;
            }

            if (isDynamic && applyChanges)
            {
                changes.Add(new MembershipChange(
                    MembershipChangeKind.Removed,
                    member,
                    "no longer a component of " + Join(member.SourceSolutionIds) + ", which was read completely"));
                return;
            }

            kept.Add(member);
            changes.Add(new MembershipChange(
                MembershipChangeKind.NotApplied,
                member,
                "no longer a component of " + Join(member.SourceSolutionIds) + " - " + NotAppliedReason(watchlist, applyChanges)));
        }

        /// <summary>
        /// What the pass says about itself, stored with the membership it produced. Only degraded sources
        /// are listed: a complete read needs no note, and a wall of text would bury the one line that
        /// matters. An unresolved count is always stated, because that is the number an operator has to
        /// trust before acting on the list.
        /// </summary>
        private static List<string> CoverageNotes(List<MembershipEvidence> sources, List<MembershipChange> changes)
        {
            List<string> notes = new List<string>();
            foreach (MembershipEvidence source in sources)
            {
                if (!source.IsComplete)
                {
                    notes.Add(source.Describe());
                }
            }

            int unresolved = 0;
            int manual = 0;
            foreach (MembershipChange change in changes)
            {
                if (change.Kind == MembershipChangeKind.Unresolved)
                {
                    unresolved++;
                }
                else if (change.Kind == MembershipChangeKind.KeptManual)
                {
                    manual++;
                }
            }

            if (unresolved > 0)
            {
                notes.Add(unresolved.ToString(CultureInfo.InvariantCulture)
                    + " member(s) could not be checked this pass and were kept as they were");
            }

            if (manual > 0)
            {
                notes.Add(manual.ToString(CultureInfo.InvariantCulture) + " member(s) were added by hand and cannot be removed by a refresh");
            }

            if (notes.Count == 0)
            {
                notes.Add("every source solution was read completely");
            }

            return notes;
        }

        private static string NotAppliedReason(Watchlist watchlist, bool applyChanges)
        {
            if (!applyChanges)
            {
                return "this was a preview, so nothing was changed";
            }

            return "membership is " + WatchlistMembershipModes.Describe(watchlist.Mode) + ", so a refresh does not change it";
        }

        private static string DescribeSolutions(IEnumerable<string> solutionIds)
        {
            return Join(solutionIds);
        }

        private static string Join(IEnumerable<string> values)
        {
            List<string> parts = new List<string>();
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value.Trim());
                }
            }

            return parts.Count == 0 ? "(none)" : string.Join(", ", parts.ToArray());
        }

        private static MembershipEvidence? Find(List<MembershipEvidence> sources, string solutionId)
        {
            foreach (MembershipEvidence source in sources)
            {
                if (string.Equals(source.SolutionId, solutionId, StringComparison.OrdinalIgnoreCase))
                {
                    return source;
                }
            }

            return null;
        }

        /// <summary>One flow as the sources reported it, with every solution that claimed it.</summary>
        private sealed class Discovery
        {
            private Discovery(WorkflowReference flow, List<string> solutionIds, DateTimeOffset seenAtUtc)
            {
                Flow = flow;
                SolutionIds = solutionIds;
                SeenAtUtc = seenAtUtc;
            }

            public WorkflowReference Flow { get; }

            public List<string> SolutionIds { get; }

            public DateTimeOffset SeenAtUtc { get; private set; }

            /// <summary>
            /// Record a flow against a solution, merging rather than replacing: a flow in two solutions is
            /// one discovered flow with two provenances, which is exactly the de-duplication the
            /// specification asks for, and the later observation wins for "last seen".
            /// </summary>
            public static void Record(Dictionary<string, Discovery> found, WorkflowReference flow, string solutionId, DateTimeOffset seenAtUtc)
            {
                Discovery existing;
                if (found.TryGetValue(flow.Key.Canonical, out existing))
                {
                    if (!existing.SolutionIds.Contains(solutionId))
                    {
                        existing.SolutionIds.Add(solutionId);
                    }

                    if (seenAtUtc > existing.SeenAtUtc)
                    {
                        existing.SeenAtUtc = seenAtUtc;
                    }

                    return;
                }

                found[flow.Key.Canonical] = new Discovery(flow, new List<string> { solutionId }, seenAtUtc);
            }
        }
    }
}
