using System;
using System.Collections.Generic;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// What the operator asked for: which workflows, which half-open UTC window, which outcome
    /// groups, and how much to bring back. A pure value - it reads no source, touches no clock and
    /// holds no booleans that hide a decision.
    ///
    /// <para>
    /// The window is <c>[FromUtc, ToUtc)</c> and is compared in UTC only. A source may apply these
    /// criteria on the server, but the query service re-applies them: a provider that ignored a
    /// criterion must not widen what the operator asked for.
    /// </para>
    /// </summary>
    public sealed class MonitoringQuery
    {
        /// <summary>Rows a normal interactive refresh asks for.</summary>
        public const int DefaultMaxRows = 2000;

        /// <summary>
        /// Ceiling on any single request, whatever the caller asked for. It protects the plugin host
        /// and the Elastic-table reader from an unbounded "last 90 days across 200 flows".
        /// </summary>
        public const int HardMaxRows = 20000;

        private static readonly RunStatus[] NoStatusFilter = new RunStatus[0];

        private readonly RunStatus[] _statuses;

        public MonitoringQuery(
            IEnumerable<WorkflowKey>? workflows,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            IEnumerable<RunStatus>? statuses = null,
            int maxRows = DefaultMaxRows)
        {
            FromUtc = fromUtc.ToUniversalTime();
            ToUtc = toUtc.ToUniversalTime();
            if (ToUtc <= FromUtc)
            {
                throw new ArgumentException(
                    "The monitoring window must move forward in time; it is the half-open interval [from, to).",
                    toUtc <= fromUtc ? nameof(toUtc) : nameof(fromUtc));
            }

            List<WorkflowKey> keys = new List<WorkflowKey>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (workflows != null)
            {
                foreach (WorkflowKey workflow in workflows)
                {
                    if (workflow == null || !seen.Add(workflow.Canonical))
                    {
                        continue;
                    }

                    keys.Add(workflow);
                }
            }

            Workflows = keys.ToArray();

            List<RunStatus> statuses2 = new List<RunStatus>();
            if (statuses != null)
            {
                foreach (RunStatus status in statuses)
                {
                    if (!statuses2.Contains(status))
                    {
                        statuses2.Add(status);
                    }
                }
            }

            _statuses = statuses2.Count == 0 ? NoStatusFilter : statuses2.ToArray();
            MaxRows = maxRows <= 0 ? DefaultMaxRows : (maxRows > HardMaxRows ? HardMaxRows : maxRows);
        }

        /// <summary>The workflows to read, de-duplicated by provider-scoped identity.</summary>
        public IReadOnlyList<WorkflowKey> Workflows { get; }

        /// <summary>
        /// Outcome groups to keep. Empty means "every group", including <see cref="RunStatus.Unknown"/>,
        /// which is never filtered away implicitly - an unrecognised status is data.
        /// </summary>
        public IReadOnlyList<RunStatus> Statuses { get { return _statuses; } }

        public DateTimeOffset FromUtc { get; }

        public DateTimeOffset ToUtc { get; }

        /// <summary>Row ceiling actually honoured, never above <see cref="HardMaxRows"/>.</summary>
        public int MaxRows { get; }

        public bool HasStatusFilter { get { return _statuses.Length > 0; } }

        /// <summary>One-workflow convenience for run-detail and spike queries.</summary>
        public static MonitoringQuery ForWorkflow(WorkflowKey workflow, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxRows = DefaultMaxRows)
        {
            if (workflow == null)
            {
                throw new ArgumentNullException(nameof(workflow));
            }

            return new MonitoringQuery(new WorkflowKey[] { workflow }, fromUtc, toUtc, null, maxRows);
        }

        /// <summary>Whether an outcome group survives the status filter.</summary>
        public bool MatchesStatus(RunStatus status)
        {
            if (_statuses.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < _statuses.Length; i++)
            {
                if (_statuses[i] == status)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether an instant falls inside the window. A null instant can never match: a run whose
        /// start the source did not report is not "inside" or "outside", it is undated, and the
        /// query service counts those separately instead of guessing.
        /// </summary>
        public bool MatchesWindow(DateTimeOffset? instantUtc)
        {
            if (instantUtc == null)
            {
                return false;
            }

            DateTimeOffset value = instantUtc.Value.ToUniversalTime();
            return value >= FromUtc && value < ToUtc;
        }

        public override string ToString()
        {
            return Workflows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " workflow(s) " + FromUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture)
                + " .. " + ToUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
