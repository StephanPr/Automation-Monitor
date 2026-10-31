using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Accumulates watchlist membership and the facts stored beside it. One rule set serves the
    /// in-memory discovery of M2/M3 and the saved lists of M4: membership is keyed on the
    /// provider-scoped workflow key, sources combine instead of overwrite, an exclusion keeps the
    /// member instead of deleting it, and the coverage of the read that produced the members stays
    /// attached to them. Nothing here touches disk: <c>Persistence/</c> maps the built list to DTOs.
    /// </summary>
    public sealed class WatchlistBuilder
    {
        private readonly List<WatchlistMember> _members = new List<WatchlistMember>();
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> _solutionIds = new List<string>();
        private readonly List<string> _coverageNotes = new List<string>();
        private ProviderResultState _state = ProviderResultState.Complete;
        private string _name;
        private string _description;
        private string _environmentId;
        private DateTimeOffset? _createdAtUtc;
        private DateTimeOffset? _modifiedAtUtc;
        private DateTimeOffset? _lastRefreshUtc;
        private WatchlistMembershipMode _mode;
        private WatchlistPreferences _preferences;

        public WatchlistBuilder(
            string id,
            string name,
            WatchlistMembershipMode mode,
            string? description = null,
            string? environmentId = null,
            WatchlistPreferences? preferences = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A watchlist id is required.", nameof(id));
            }

            Id = id.Trim();
            _name = string.IsNullOrWhiteSpace(name) ? "Untitled watchlist" : name.Trim();
            _mode = mode;
            _description = (description ?? string.Empty).Trim();
            _environmentId = (environmentId ?? string.Empty).Trim();
            _preferences = preferences ?? WatchlistPreferences.Inherited;
        }

        public string Id { get; }

        public string Name { get { return _name; } }

        public string Description { get { return _description; } }

        public string EnvironmentId { get { return _environmentId; } }

        public WatchlistMembershipMode Mode { get { return _mode; } }

        public WatchlistPreferences Preferences { get { return _preferences; } }

        public int MemberCount { get { return _members.Count; } }

        /// <summary>
        /// Start from an existing watchlist so a mutation keeps every stored fact: identity,
        /// timestamps, provenance and the coverage the list was saved with. Without this a rename
        /// would silently reset a list to "complete" coverage and drop its history.
        /// </summary>
        public static WatchlistBuilder From(Watchlist watchlist)
        {
            if (watchlist == null)
            {
                throw new ArgumentNullException(nameof(watchlist));
            }

            WatchlistBuilder builder = new WatchlistBuilder(
                watchlist.Id,
                watchlist.Name,
                watchlist.Mode,
                watchlist.Description,
                watchlist.EnvironmentId,
                watchlist.Preferences);

            foreach (WatchlistMember member in watchlist.Members)
            {
                builder._members.Add(member);
                builder._index[member.Key.Canonical] = builder._members.Count - 1;
            }

            foreach (string solutionId in watchlist.SourceSolutionIds)
            {
                builder.AddSolutionId(solutionId);
            }

            foreach (string note in watchlist.CoverageNotes)
            {
                builder._coverageNotes.Add(note);
            }

            builder._state = watchlist.State;
            builder._createdAtUtc = watchlist.CreatedAtUtc;
            builder._modifiedAtUtc = watchlist.ModifiedAtUtc;
            builder._lastRefreshUtc = watchlist.LastMembershipRefreshUtc;
            return builder;
        }

        /// <summary>
        /// Generate membership from a solution's cloud flows. Returns the number of new members;
        /// flows already present (for example added manually) are kept and only gain provenance.
        /// The coverage of that read travels with the members: a partial or failed discovery
        /// leaves the built watchlist labelled, never silently "complete".
        /// </summary>
        /// <summary>
        /// Generate membership from a solution's cloud flows. Returns the number of new members;
        /// flows already present (for example added manually) are kept and only gain provenance.
        /// The coverage of that read travels with the members: a partial or failed discovery
        /// leaves the built watchlist labelled, never silently "complete".
        /// </summary>
        public int AddSolutionFlows(
            SolutionReference solution,
            IEnumerable<WorkflowReference>? flows,
            ProviderResultState state = ProviderResultState.Complete,
            string? coverageNote = null,
            DateTimeOffset? seenAtUtc = null)
        {
            if (solution == null)
            {
                throw new ArgumentNullException(nameof(solution));
            }

            AddSolutionId(solution.Id);
            RecordCoverage(state, coverageNote);
            if (flows == null)
            {
                return 0;
            }

            int added = 0;
            foreach (WorkflowReference flow in flows)
            {
                if (flow == null)
                {
                    continue;
                }

                if (Merge(flow, WatchlistMemberSource.Solution, new string[] { solution.Id }, seenAtUtc))
                {
                    added++;
                }
            }

            return added;
        }

        /// <summary>Add one operator-chosen flow. It survives every later dynamic refresh.</summary>
        public bool AddManualFlow(WorkflowReference flow, DateTimeOffset? addedAtUtc = null)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            return Merge(flow, WatchlistMemberSource.Manual, null, addedAtUtc);
        }

        /// <summary>
        /// Add a member exactly as stored, with its provenance, inclusion and timestamps intact. Used
        /// when reading a saved list back: re-deriving a member from its workflow would silently reset
        /// an exclusion or lose the record of when it was added.
        /// </summary>
        /// <returns>False when a member with the same provider-scoped identity is already present.</returns>
        public bool AddStoredMember(WatchlistMember member)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            if (IndexOf(member.Key) >= 0)
            {
                return false;
            }

            _members.Add(member);
            _index[member.Key.Canonical] = _members.Count - 1;
            foreach (string solutionId in member.SourceSolutionIds)
            {
                AddSolutionId(solutionId);
            }

            return true;
        }

        /// <summary>Include or exclude a member. Exclusion never removes the member.</summary>
        public bool SetIncluded(WorkflowKey key, bool isIncluded)
        {
            int position = IndexOf(key);
            if (position < 0)
            {
                return false;
            }

            _members[position] = _members[position].WithInclusion(isIncluded);
            return true;
        }

        /// <summary>
        /// Take a member out of the list by operator decision. Unlike an exclusion this deletes the
        /// record, so a dynamic refresh may add the same flow again later - which is exactly what
        /// "removed by hand" means and is why the two operations are separate.
        /// </summary>
        public bool RemoveMember(WorkflowKey key)
        {
            int position = IndexOf(key);
            if (position < 0)
            {
                return false;
            }

            _members.RemoveAt(position);
            RebuildIndex();
            return true;
        }

        public void Rename(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _name = name.Trim();
            }
        }

        public void SetDescription(string? description)
        {
            _description = (description ?? string.Empty).Trim();
        }

        /// <summary>Record which environment this list belongs to. Empty means "not tied to one".</summary>
        public void SetEnvironment(string? environmentId)
        {
            _environmentId = (environmentId ?? string.Empty).Trim();
        }

        public void SetMode(WatchlistMembershipMode mode)
        {
            _mode = mode;
        }

        public void SetPreferences(WatchlistPreferences? preferences)
        {
            _preferences = preferences ?? WatchlistPreferences.Inherited;
        }

        /// <summary>Replace the stored source solution ids, as reconciliation or an edit decides.</summary>
        public void SetSourceSolutions(IEnumerable<string>? solutionIds)
        {
            _solutionIds.Clear();
            if (solutionIds != null)
            {
                foreach (string id in solutionIds)
                {
                    AddSolutionId(id);
                }
            }
        }

        /// <summary>
        /// Replace the coverage carried by the list. Reconciliation uses this to overwrite a stale
        /// statement with what the newest read actually found, instead of accumulating notes from
        /// every pass forever.
        /// </summary>
        public void SetCoverage(ProviderResultState state, IEnumerable<string>? notes)
        {
            _state = state;
            _coverageNotes.Clear();
            if (notes != null)
            {
                foreach (string note in notes)
                {
                    if (!string.IsNullOrWhiteSpace(note) && !_coverageNotes.Contains(note))
                    {
                        _coverageNotes.Add(note);
                    }
                }
            }
        }

        /// <summary>Stamp the stored times. Null leaves a value untouched, so a load can restore it.</summary>
        public void SetTimestamps(DateTimeOffset? createdUtc, DateTimeOffset? modifiedUtc, DateTimeOffset? lastMembershipRefreshUtc)
        {
            _createdAtUtc = createdUtc;
            _modifiedAtUtc = modifiedUtc;
            _lastRefreshUtc = lastMembershipRefreshUtc;
        }

        /// <summary>Move the modification time, setting creation time when the list has never been saved.</summary>
        public void Stamp(DateTimeOffset touchedUtc)
        {
            DateTimeOffset touched = touchedUtc.ToUniversalTime();
            _createdAtUtc = _createdAtUtc ?? touched;
            _modifiedAtUtc = touched;
        }

        /// <summary>Record that membership was reconciled against its source at this UTC instant.</summary>
        public void StampMembershipRefresh(DateTimeOffset refreshedUtc)
        {
            _lastRefreshUtc = refreshedUtc.ToUniversalTime();
        }

        public Watchlist Build()
        {
            return new Watchlist(
                Id,
                _name,
                _mode,
                _solutionIds,
                _members.ToArray(),
                _state,
                _coverageNotes,
                _description,
                _environmentId,
                _createdAtUtc,
                _modifiedAtUtc,
                _lastRefreshUtc,
                _preferences);
        }

        /// <summary>Record the coverage of one discovery pass; the worst state seen wins.</summary>
        public void RecordCoverage(ProviderResultState state, string? note)
        {
            if (Severity(state) > Severity(_state))
            {
                _state = state;
            }
            else if (_state == ProviderResultState.Complete)
            {
                _state = state;
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                string text = note!.Trim();
                if (!_coverageNotes.Contains(text))
                {
                    _coverageNotes.Add(text);
                }
            }
        }

        /// <summary>How far a state is from "everything was read", for combining two sources.</summary>
        private static int Severity(ProviderResultState state)
        {
            return ProviderResultStates.Severity(state);
        }

        private bool Merge(WorkflowReference flow, WatchlistMemberSource source, IEnumerable<string>? solutionIds, DateTimeOffset? seenAtUtc)
        {
            int position = IndexOf(flow.Key);
            if (position >= 0)
            {
                WatchlistMember existing = _members[position];
                List<string> mergedIds = new List<string>(existing.SourceSolutionIds);
                if (solutionIds != null)
                {
                    foreach (string id in solutionIds)
                    {
                        if (!string.IsNullOrWhiteSpace(id) && !mergedIds.Contains(id))
                        {
                            mergedIds.Add(id);
                        }

                        AddSolutionId(id);
                    }
                }

                // The fresher record wins for display, while inclusion and origin stay as the
                // operator left them: a re-read must never re-include an excluded flow.
                DateTimeOffset? lastSeen = seenAtUtc.HasValue ? seenAtUtc.Value.ToUniversalTime() : existing.LastSeenUtc;
                _members[position] = new WatchlistMember(
                    flow,
                    existing.Source | source,
                    mergedIds,
                    existing.IsIncluded,
                    existing.AddedAtUtc,
                    lastSeen);
                return false;
            }

            DateTimeOffset? addedAtUtc = seenAtUtc.HasValue ? seenAtUtc.Value.ToUniversalTime() : (DateTimeOffset?)null;
            _members.Add(new WatchlistMember(flow, source, solutionIds, true, addedAtUtc, addedAtUtc));
            _index[flow.Key.Canonical] = _members.Count - 1;
            return true;
        }

        /// <summary>
        /// Replace the whole member list verbatim, as reconciliation does when it has already decided
        /// each member's fate. Identity collisions are refused rather than merged: reconciliation has the
        /// de-duplication rule, and a second copy of a flow would silently double its monitoring.
        /// </summary>
        public void SetMembers(IEnumerable<WatchlistMember> members)
        {
            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            _members.Clear();
            _index.Clear();
            foreach (WatchlistMember member in members)
            {
                AddStoredMember(member);
            }
        }

        /// <summary>
        /// Note that a discovery pass found this member again, without changing its inclusion. Used by
        /// reconciliation so "seen again today" and "last seen weeks ago" stay distinguishable.
        /// </summary>
        public bool Observe(WorkflowKey key, WatchlistMemberSource addedSource, IEnumerable<string>? solutionIds, DateTimeOffset seenAtUtc)
        {
            int position = IndexOf(key);
            if (position < 0)
            {
                return false;
            }

            _members[position] = _members[position].WithObservation(addedSource, solutionIds, seenAtUtc);
            if (solutionIds != null)
            {
                foreach (string id in solutionIds)
                {
                    AddSolutionId(id);
                }
            }

            return true;
        }

        /// <summary>Re-point the identity index after a removal shifted the list.</summary>
        private void RebuildIndex()
        {
            _index.Clear();
            for (int i = 0; i < _members.Count; i++)
            {
                _index[_members[i].Key.Canonical] = i;
            }
        }

        private void AddSolutionId(string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && !_solutionIds.Contains(id))
            {
                _solutionIds.Add(id);
            }
        }

        private int IndexOf(WorkflowKey? key)
        {
            if (key == null)
            {
                return -1;
            }

            int position;
            return _index.TryGetValue(key.Canonical, out position) ? position : -1;
        }
    }
}
