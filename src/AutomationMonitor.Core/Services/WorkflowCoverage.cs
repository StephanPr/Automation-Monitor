using System;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// What one workflow's run read delivered. This is the record that keeps
    /// "the flow has no runs" apart from "the flow's runs could not be read", and it is the row a
    /// view must show next to the grid so an incomplete collection never looks like a quiet one.
    /// </summary>
    public sealed class WorkflowCoverage
    {
        internal WorkflowCoverage(
            WorkflowKey workflow,
            ProviderResultState state,
            int rowsRead,
            int runsReturned,
            int pagesRead,
            DateTimeOffset checkedAtUtc,
            string? message)
        {
            Workflow = workflow;
            State = state;
            RowsRead = rowsRead;
            RunsReturned = runsReturned;
            PagesRead = pagesRead;
            CheckedAtUtc = checkedAtUtc.ToUniversalTime();
            Message = string.IsNullOrWhiteSpace(message) ? null : message!.Trim();
        }

        /// <summary>The workflow that was asked for, even when nothing came back.</summary>
        public WorkflowKey Workflow { get; }

        public ProviderResultState State { get; }

        /// <summary>Rows the source returned for this workflow before filtering and de-duplication.</summary>
        public int RowsRead { get; }

        /// <summary>Runs kept for this workflow after the query was applied.</summary>
        public int RunsReturned { get; }

        public int PagesRead { get; }

        /// <summary>When this workflow's source was last contacted.</summary>
        public DateTimeOffset CheckedAtUtc { get; }

        /// <summary>Sanitized reason for a degraded read.</summary>
        public string? Message { get; }

        /// <summary>True when rows arrived and may be displayed, with the state shown beside them.</summary>
        public bool HasData
        {
            get
            {
                return State == ProviderResultState.Complete
                    || State == ProviderResultState.Partial
                    || State == ProviderResultState.Stale;
            }
        }

        /// <summary>
        /// True when this workflow contributes nothing to the grid - either it genuinely had no
        /// runs in the window, or it could not be read. The two cases are distinguished by
        /// <see cref="State"/>, never by the absence of a row.
        /// </summary>
        public bool DeliveredNothing
        {
            get { return !HasData || RunsReturned == 0; }
        }

        /// <summary>Status line through the shared renderer, so wording cannot drift per view.</summary>
        public string Describe()
        {
            return SourceStatus.Describe(
                "Run history for " + Workflow.WorkflowId,
                State,
                RowsRead,
                PagesRead,
                CheckedAtUtc,
                Message);
        }

        public override string ToString()
        {
            return Workflow.WorkflowId + " " + SourceStatus.Label(State) + " " + RunsReturned;
        }
    }
}
