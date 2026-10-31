using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// The answer to a <see cref="MonitoringQuery"/>: the merged runs, plus what every requested
    /// workflow actually contributed. The coverage list is not decoration - it is how a caller
    /// tells "these five flows had no runs" apart from "these five flows could not be read", and it
    /// is what keeps a partial collection labelled as one all the way into a summary or an export.
    /// </summary>
    public sealed class RunQueryResult
    {
        private readonly WorkflowCoverage[] _coverage;

        internal RunQueryResult(
            MonitoringQuery query,
            IReadOnlyList<WorkflowRun> runs,
            IReadOnlyList<WorkflowCoverage> coverage,
            int rowsReceived,
            int duplicateRunCount,
            int windowExcludedCount,
            int statusExcludedCount,
            int undatedRunCount,
            int truncatedRunCount,
            int conflictCount,
            bool wasCancelled,
            DateTimeOffset checkedAtUtc)
        {
            Query = query;
            Runs = runs;
            _coverage = coverage == null ? new WorkflowCoverage[0] : new List<WorkflowCoverage>(coverage).ToArray();
            RowsReceived = rowsReceived;
            DuplicateRunCount = duplicateRunCount;
            WindowExcludedCount = windowExcludedCount;
            StatusExcludedCount = statusExcludedCount;
            UndatedRunCount = undatedRunCount;
            TruncatedRunCount = truncatedRunCount;
            ConflictCount = conflictCount;
            WasCancelled = wasCancelled;
            CheckedAtUtc = checkedAtUtc.ToUniversalTime();

            List<ProviderResultState> states = new List<ProviderResultState>(_coverage.Length);
            for (int i = 0; i < _coverage.Length; i++)
            {
                states.Add(_coverage[i].State);
            }

            State = ProviderResultStates.Worst(states);
            Summary = RunSummary.FromRuns(runs, State != ProviderResultState.Complete);
        }

        /// <summary>The request these runs answer.</summary>
        public MonitoringQuery Query { get; }

        /// <summary>Merged, de-duplicated, filtered runs, newest start first; undated rows last.</summary>
        public IReadOnlyList<WorkflowRun> Runs { get; }

        /// <summary>One entry per requested workflow, in the order it was asked for.</summary>
        public IReadOnlyList<WorkflowCoverage> Coverage { get { return _coverage; } }

        /// <summary>Rows received from all sources before filtering and de-duplication.</summary>
        public int RowsReceived { get; }

        /// <summary>Repeat observations of a run identity that collapsed into one row.</summary>
        public int DuplicateRunCount { get; }

        /// <summary>Runs excluded because their start instant falls outside the requested window.</summary>
        public int WindowExcludedCount { get; }

        /// <summary>Runs excluded because their outcome group was not requested.</summary>
        public int StatusExcludedCount { get; }

        /// <summary>
        /// Runs kept because the source sent no start instant, so they could not be placed in the
        /// window. Counted, never dropped and never guessed at.
        /// </summary>
        public int UndatedRunCount { get; }

        /// <summary>Runs left out because the row ceiling was reached.</summary>
        public int TruncatedRunCount { get; }

        /// <summary>
        /// Runs observed twice with different terminal outcomes. One row is kept (the later
        /// observation) and the disagreement is reported instead of averaged away.
        /// </summary>
        public int ConflictCount { get; }

        /// <summary>True when the refresh was cancelled part-way; the rows that arrived are still here.</summary>
        public bool WasCancelled { get; }

        /// <summary>When this query finished asking its sources.</summary>
        public DateTimeOffset CheckedAtUtc { get; }

        /// <summary>Worst coverage across the requested workflows.</summary>
        public ProviderResultState State { get; }

        /// <summary>Counters over <see cref="Runs"/>, labelled partial when any read was degraded.</summary>
        public RunSummary Summary { get; }

        public bool IsComplete { get { return State == ProviderResultState.Complete && !WasCancelled; } }


        /// <summary>Requested workflows that produced no rows, whatever the reason.</summary>
        public IReadOnlyList<WorkflowKey> WorkflowsWithoutRows()
        {
            List<WorkflowKey> keys = new List<WorkflowKey>();
            for (int i = 0; i < _coverage.Length; i++)
            {
                if (_coverage[i].RunsReturned == 0)
                {
                    keys.Add(_coverage[i].Workflow);
                }
            }

            return keys;
        }

        /// <summary>
        /// Workflows whose data is missing or untrustworthy: anything but a complete read that
        /// produced rows. A view uses this to say which sources contributed nothing instead of
        /// letting a short grid imply that the watchlist was quiet.
        /// </summary>
        public IReadOnlyList<WorkflowKey> WorkflowsWithoutData()
        {
            List<WorkflowKey> keys = new List<WorkflowKey>();
            for (int i = 0; i < _coverage.Length; i++)
            {
                if (_coverage[i].State != ProviderResultState.Complete || _coverage[i].RunsReturned == 0)
                {
                    keys.Add(_coverage[i].Workflow);
                }
            }

            return keys;
        }

        /// <summary>
        /// One honest sentence about the whole result: state, size, what was collapsed, what was
        /// left out and why. Grid headers, status bars and export footers all use it so the
        /// qualifier cannot be kept in one view and dropped in another.
        /// </summary>
        public string DescribeCoverage()
        {
            StringBuilder text = new StringBuilder();
            text.Append(SourceStatus.Label(State))
                .Append(" - ")
                .Append(Runs.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" run(s) from ")
                .Append(_coverage.Length.ToString(CultureInfo.InvariantCulture))
                .Append(" workflow(s) - received ")
                .Append(RowsReceived.ToString(CultureInfo.InvariantCulture))
                .Append(" row(s) - checked ")
                .Append(CheckedAtUtc.ToString("u", CultureInfo.InvariantCulture));

            Append(text, DuplicateRunCount, "duplicate(s) collapsed");
            Append(text, WindowExcludedCount, "run(s) outside the window");
            Append(text, StatusExcludedCount, "run(s) outside the status filter");
            Append(text, UndatedRunCount, "run(s) with no start time kept");
            Append(text, TruncatedRunCount, "run(s) beyond the row ceiling");
            Append(text, ConflictCount, "conflicting observation(s)");

            if (WasCancelled)
            {
                text.Append(" - CANCELLED before every workflow was read");
            }

            return text.ToString();
        }

        private static void Append(StringBuilder text, int count, string label)
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
