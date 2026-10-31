using System;
using System.Collections.Generic;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// How membership is decided. The mode is a stored fact, never inferred: a saved watchlist has
    /// to say whether its membership is owned by the operator or follows a solution, because that
    /// single value decides whether a refresh may add or remove anything at all.
    /// </summary>
    public enum WatchlistMembershipMode
    {
        /// <summary>Members are fixed until the operator edits or re-imports them.</summary>
        Snapshot = 0,

        /// <summary>Membership follows the source solution and is reconciled on explicit refresh.</summary>
        DynamicSolution = 1,

        /// <summary>
        /// Built and owned by hand: no discovery pass may add or remove a member. Distinct from
        /// <see cref="Snapshot"/> because a snapshot remembers the solution it came from and can be
        /// re-imported from it, while a manual list has no source to reconcile against.
        /// </summary>
        Manual = 2
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
            bool isIncluded,
            DateTimeOffset? addedAtUtc = null,
            DateTimeOffset? lastSeenUtc = null)
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
            AddedAtUtc = addedAtUtc;
            LastSeenUtc = lastSeenUtc;
        }

        public WorkflowReference Workflow { get; }

        public WorkflowKey Key { get { return Workflow.Key; } }

        public WatchlistMemberSource Source { get; }

        /// <summary>Solution ids whose components produced this membership, for later reconciliation.</summary>
        public IReadOnlyList<string> SourceSolutionIds { get; }

        /// <summary>False means the operator excluded this flow; it stays visible, not deleted.</summary>
        public bool IsIncluded { get; }

        /// <summary>When this flow entered the watchlist. UTC; null for a member a pre-M4 build made.</summary>
        public DateTimeOffset? AddedAtUtc { get; }

        /// <summary>
        /// When a discovery pass last actually found this flow. Null means "never observed", which is
        /// how a manually added or currently unreachable member stays distinguishable from a flow
        /// that was seen and then removed from its solution.
        /// </summary>
        public DateTimeOffset? LastSeenUtc { get; }

        /// <summary>Apply the operator's include/exclude decision, keeping provenance and timestamps.</summary>
        public WatchlistMember WithInclusion(bool isIncluded)
        {
            return new WatchlistMember(Workflow, Source, SourceSolutionIds, isIncluded, AddedAtUtc, LastSeenUtc);
        }

        /// <summary>
        /// Record what a discovery pass saw: more solution provenance and a fresh observation time.
        /// Inclusion is deliberately not touched - discovery never silently re-includes a flow the
        /// operator excluded.
        /// </summary>
        public WatchlistMember WithObservation(WatchlistMemberSource addedSource, IEnumerable<string>? solutionIds, DateTimeOffset seenAtUtc)
        {
            List<string> ids = new List<string>(SourceSolutionIds);
            if (solutionIds != null)
            {
                foreach (string id in solutionIds)
                {
                    if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            return new WatchlistMember(Workflow, Source | addedSource, ids, IsIncluded, AddedAtUtc, seenAtUtc.ToUniversalTime());
        }

        /// <summary>Refresh the display record when a re-read finds the same identity again.</summary>
        public WatchlistMember WithWorkflow(WorkflowReference workflow)
        {
            if (workflow == null)
            {
                throw new ArgumentNullException(nameof(workflow));
            }

            return new WatchlistMember(workflow, Source, SourceSolutionIds, IsIncluded, AddedAtUtc, LastSeenUtc);
        }

        public override string ToString()
        {
            return Workflow.Name + (IsIncluded ? string.Empty : " (excluded)");
        }
    }

    /// <summary>
    /// A set of workflows to monitor, with the provenance and intent that decided each member.
    /// The model itself stays free of file and serialisation behaviour: <c>Persistence/</c> maps it
    /// to explicit data-contract DTOs, so the shape the operator sees can never drift into the
    /// shape written to disk.
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
            IEnumerable<string>? coverageNotes,
            string? description = null,
            string? environmentId = null,
            DateTimeOffset? createdAtUtc = null,
            DateTimeOffset? modifiedAtUtc = null,
            DateTimeOffset? lastMembershipRefreshUtc = null,
            WatchlistPreferences? preferences = null)
        {
            Id = id;
            Name = name;
            Mode = mode;
            State = state;
            Description = (description ?? string.Empty).Trim();
            EnvironmentId = (environmentId ?? string.Empty).Trim();
            CreatedAtUtc = createdAtUtc;
            ModifiedAtUtc = modifiedAtUtc;
            LastMembershipRefreshUtc = lastMembershipRefreshUtc;
            Preferences = preferences ?? WatchlistPreferences.Inherited;
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

            List<string> excluded = new List<string>();
            foreach (WatchlistMember member in list)
            {
                if (!member.IsIncluded && !excluded.Contains(member.Key.Canonical))
                {
                    excluded.Add(member.Key.Canonical);
                }
            }

            ExcludedWorkflowKeys = excluded;
        }

        /// <summary>Stored identity. Never derived from the name and never re-generated on rename.</summary>
        public string Id { get; }

        public string Name { get; }

        public WatchlistMembershipMode Mode { get; }

        /// <summary>Operator note stored with the list. Never part of identity or uniqueness.</summary>
        public string Description { get; }

        /// <summary>
        /// Environment this list belongs to, or empty when it was built without one. A saved list
        /// that names an environment is never offered as the active list of another.
        /// </summary>
        public string EnvironmentId { get; }

        /// <summary>When the list was created. UTC; null only for a list built in memory and never saved.</summary>
        public DateTimeOffset? CreatedAtUtc { get; }

        /// <summary>When the stored content last changed. UTC. A caller may compare it to detect a conflict.</summary>
        public DateTimeOffset? ModifiedAtUtc { get; }

        /// <summary>When membership was last reconciled against its source solution(s); null when never.</summary>
        public DateTimeOffset? LastMembershipRefreshUtc { get; }

        /// <summary>Refresh interval, date range and threshold overrides carried by this list.</summary>
        public WatchlistPreferences Preferences { get; }

        /// <summary>
        /// Canonical keys the operator excluded. They stay members of the list, and a dynamic
        /// refresh may not silently re-include them.
        /// </summary>
        public IReadOnlyList<string> ExcludedWorkflowKeys { get; }

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

        /// <summary>
        /// Stored timestamps as one honest line for a grid cell: what was created, what was last
        /// changed and when membership was last checked against its source. UTC, and "not recorded"
        /// is stated instead of being faked with a placeholder date.
        /// </summary>
        public string TimestampsDescription
        {
            get
            {
                return "created " + Describe(CreatedAtUtc)
                    + ", changed " + Describe(ModifiedAtUtc)
                    + ", membership checked " + Describe(LastMembershipRefreshUtc);
            }
        }

        private static string Describe(DateTimeOffset? instantUtc)
        {
            return instantUtc.HasValue
                ? instantUtc.Value.ToUniversalTime().ToString("u", System.Globalization.CultureInfo.InvariantCulture)
                : "not recorded";
        }

        /// <summary>
        /// Copy with one or more parts replaced. A null argument means "keep what this list has",
        /// which is why clearing something is done with <c>string.Empty</c> or an empty collection
        /// and never with null: a mutation must not be able to erase a field by accident.
        /// </summary>
        private Watchlist Copy(
            string? name = null,
            WatchlistMembershipMode? mode = null,
            string? description = null,
            string? environmentId = null,
            WatchlistPreferences? preferences = null,
            IEnumerable<WatchlistMember>? members = null,
            ProviderResultState? state = null,
            IEnumerable<string>? coverageNotes = null,
            DateTimeOffset? createdUtc = null,
            DateTimeOffset? modifiedUtc = null,
            DateTimeOffset? refreshedUtc = null)
        {
            return new Watchlist(
                Id,
                name ?? Name,
                mode ?? Mode,
                SourceSolutionIds,
                members ?? Members,
                state ?? State,
                coverageNotes ?? CoverageNotes,
                description ?? Description,
                environmentId ?? EnvironmentId,
                createdUtc ?? CreatedAtUtc,
                modifiedUtc ?? ModifiedAtUtc,
                refreshedUtc ?? LastMembershipRefreshUtc,
                preferences ?? Preferences);
        }

        public Watchlist WithName(string name)
        {
            return Copy(name: name);
        }

        public Watchlist WithDescription(string description)
        {
            return Copy(description: description ?? string.Empty);
        }

        public Watchlist WithMode(WatchlistMembershipMode mode)
        {
            return Copy(mode: mode);
        }

        public Watchlist WithEnvironment(string? environmentId)
        {
            return Copy(environmentId: environmentId ?? string.Empty);
        }

        public Watchlist WithPreferences(WatchlistPreferences preferences)
        {
            return Copy(preferences: preferences ?? WatchlistPreferences.Inherited);
        }

        public Watchlist WithMembers(IEnumerable<WatchlistMember> members)
        {
            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            return Copy(members: members);
        }

        public Watchlist WithCoverage(ProviderResultState state, IEnumerable<string> coverageNotes)
        {
            if (coverageNotes == null)
            {
                throw new ArgumentNullException(nameof(coverageNotes));
            }

            return Copy(state: state, coverageNotes: coverageNotes);
        }

        /// <summary>Stamp a change: the modification time moves, and creation time is set when unset.</summary>
        public Watchlist WithModifiedAt(DateTimeOffset touchedUtc)
        {
            DateTimeOffset touched = touchedUtc.ToUniversalTime();
            return Copy(createdUtc: CreatedAtUtc ?? touched, modifiedUtc: touched);
        }

        /// <summary>Record that membership was reconciled at this instant. UTC.</summary>
        public Watchlist WithMembershipRefresh(DateTimeOffset refreshedUtc)
        {
            return Copy(refreshedUtc: refreshedUtc.ToUniversalTime());
        }
    }

    /// <summary>
    /// Stored form of a <see cref="WatchlistMembershipMode"/>. The token is what decides whether a
    /// later refresh may add or remove members, so an unrecognised value must be reported and not
    /// defaulted: guessing "snapshot" for a dynamic list stops it following its solution, guessing
    /// "dynamic" for a snapshot lets a refresh edit membership the operator owns.
    /// </summary>
    public static class WatchlistMembershipModes
    {
        public static string Token(WatchlistMembershipMode mode)
        {
            switch (mode)
            {
                case WatchlistMembershipMode.DynamicSolution:
                    return "dynamic";
                case WatchlistMembershipMode.Manual:
                    return "manual";
                default:
                    return "snapshot";
            }
        }

        public static bool TryParse(string? token, out WatchlistMembershipMode mode)
        {
            mode = WatchlistMembershipMode.Snapshot;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            switch (token!.Trim().ToLowerInvariant())
            {
                case "snapshot":
                    return true;
                case "dynamic":
                case "dynamicsolution":
                    mode = WatchlistMembershipMode.DynamicSolution;
                    return true;
                case "manual":
                    mode = WatchlistMembershipMode.Manual;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>What the mode means, in the words an operator sees next to the list.</summary>
        public static string Describe(WatchlistMembershipMode mode)
        {
            switch (mode)
            {
                case WatchlistMembershipMode.DynamicSolution:
                    return "dynamic: membership follows its solution(s) on refresh";
                case WatchlistMembershipMode.Manual:
                    return "manual: only you add or remove members";
                default:
                    return "snapshot: membership is fixed until you re-import it";
            }
        }
    }

    /// <summary>
    /// Stored form of <see cref="WatchlistMemberSource"/>. Origins are written as a list of tokens
    /// rather than a number, so a file stays readable and a new flag cannot silently change what an
    /// older value means.
    /// </summary>
    public static class WatchlistMemberSources
    {
        public static IReadOnlyList<string> Tokens(WatchlistMemberSource source)
        {
            List<string> tokens = new List<string>();
            if (source.HasFlag(WatchlistMemberSource.Solution))
            {
                tokens.Add("solution");
            }

            if (source.HasFlag(WatchlistMemberSource.Manual))
            {
                tokens.Add("manual");
            }

            if (tokens.Count == 0)
            {
                tokens.Add("unknown");
            }

            return tokens;
        }

        /// <summary>
        /// Read stored origins. An unrecognised token is reported as false while the recognised ones
        /// are still applied, so one odd entry degrades one member's provenance instead of losing it.
        /// </summary>
        public static bool TryParse(IEnumerable<string>? tokens, out WatchlistMemberSource source)
        {
            source = WatchlistMemberSource.None;
            bool recognised = true;
            if (tokens == null)
            {
                return false;
            }

            foreach (string token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                switch (token.Trim().ToLowerInvariant())
                {
                    case "solution":
                        source |= WatchlistMemberSource.Solution;
                        break;
                    case "manual":
                        source |= WatchlistMemberSource.Manual;
                        break;
                    case "unknown":
                        break;
                    default:
                        recognised = false;
                        break;
                }
            }

            return recognised && source != WatchlistMemberSource.None;
        }

        public static string Label(WatchlistMemberSource source)
        {
            if (source.HasFlag(WatchlistMemberSource.Solution) && source.HasFlag(WatchlistMemberSource.Manual))
            {
                return "solution + manual";
            }

            if (source.HasFlag(WatchlistMemberSource.Solution))
            {
                return "solution";
            }

            if (source.HasFlag(WatchlistMemberSource.Manual))
            {
                return "manual";
            }

            return "unknown origin";
        }
    }
}
