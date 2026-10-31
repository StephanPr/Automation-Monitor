using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Accumulates watchlist membership in memory. This milestone only builds a snapshot from one
    /// solution, but the accumulation rule is already the M4 rule: membership is keyed on the
    /// provider-scoped workflow key, sources combine instead of overwrite, and an exclusion keeps
    /// the member instead of deleting it. Nothing here touches disk.
    /// </summary>
    public sealed class WatchlistBuilder
    {
        private readonly List<WatchlistMember> _members = new List<WatchlistMember>();
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> _solutionIds = new List<string>();
        private readonly List<string> _coverageNotes = new List<string>();
        private ProviderResultState _state = ProviderResultState.Complete;
        private string _name;
        private WatchlistMembershipMode _mode;

        public WatchlistBuilder(string id, string name, WatchlistMembershipMode mode)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A watchlist id is required.", nameof(id));
            }

            Id = id.Trim();
            _name = string.IsNullOrWhiteSpace(name) ? "Untitled watchlist" : name.Trim();
            _mode = mode;
        }

        public string Id { get; }

        public string Name { get { return _name; } }

        public WatchlistMembershipMode Mode { get { return _mode; } }

        public int MemberCount { get { return _members.Count; } }

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
            string? coverageNote = null)
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

                if (Merge(flow, WatchlistMemberSource.Solution, new string[] { solution.Id }))
                {
                    added++;
                }
            }

            return added;
        }

        /// <summary>Add one operator-chosen flow. Reserved for the manual-selection milestone.</summary>
        public bool AddManualFlow(WorkflowReference flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            return Merge(flow, WatchlistMemberSource.Manual, null);
        }

        /// <summary>Include or exclude a member. Exclusion never removes the member.</summary>
        public bool SetIncluded(WorkflowKey key, bool isIncluded)
        {
            int position = IndexOf(key);
            if (position < 0)
            {
                return false;
            }

            WatchlistMember existing = _members[position];
            _members[position] = new WatchlistMember(existing.Workflow, existing.Source, existing.SourceSolutionIds, isIncluded);
            return true;
        }

        public void Rename(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _name = name.Trim();
            }
        }

        public void SetMode(WatchlistMembershipMode mode)
        {
            _mode = mode;
        }

        public Watchlist Build()
        {
            return new Watchlist(Id, _name, _mode, _solutionIds, _members.ToArray(), _state, _coverageNotes);
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

        private bool Merge(WorkflowReference flow, WatchlistMemberSource source, IEnumerable<string>? solutionIds)
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

                _members[position] = new WatchlistMember(
                    existing.Workflow,
                    existing.Source | source,
                    mergedIds,
                    existing.IsIncluded);
                return false;
            }

            _members.Add(new WatchlistMember(flow, source, solutionIds, true));
            _index[flow.Key.Canonical] = _members.Count - 1;
            return true;
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
