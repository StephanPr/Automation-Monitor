using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Plugin.UI
{
    /// <summary>
    /// One entry of the solution selector. The label is produced by the shared core formatter so
    /// publisher, version and managed flag cannot be dropped by a view.
    /// </summary>
    public sealed class SolutionChoice
    {
        public SolutionChoice(SolutionReference solution)
        {
            if (solution == null)
            {
                throw new ArgumentNullException(nameof(solution));
            }

            Solution = solution;
            Label = SolutionSearch.FormatLabel(solution);
        }

        public SolutionReference Solution { get; }

        public string Label { get; }

        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// One entry of the saved-watchlist combo (C6). The label is a display string; the id is the stored
    /// identity the host opens by. The id is kept off the label so a name can never be mistaken for an
    /// identifier, and the host supplies the label - including the environment state - from Core wording so
    /// this class holds no rule of its own.
    /// </summary>
    public sealed class SavedWatchlistChoice
    {
        public SavedWatchlistChoice(string id, string label)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Label = label;
        }

        public string Id { get; }

        public string Label { get; }

        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// One grid row. Every value is a pre-formatted string built from a core record: provider
    /// text stays data, and no provider value is ever interpreted or composed into a path or URL.
    /// </summary>
    public sealed class FlowRow
    {
        private FlowRow(string name, string stateLabel, string workflowId, string environmentLabel, string modifiedOnUtc)
        {
            Name = name;
            StateLabel = stateLabel;
            WorkflowId = workflowId;
            EnvironmentLabel = environmentLabel;
            ModifiedOnUtc = modifiedOnUtc;
        }

        public string Name { get; }

        public string StateLabel { get; }

        public string WorkflowId { get; }

        public string EnvironmentLabel { get; }

        public string ModifiedOnUtc { get; }

        /// <summary>
        /// A row as the operator reads it. The workflow id is the provider's own, canonicalised by
        /// <see cref="WorkflowKey"/>, and kept for the deep links later milestones need. The environment column is
        /// the shared display wording: it names the organization the row's identity carries - as a fingerprint,
        /// beside the connected organization's display name when this session verified that organization - or says
        /// plainly that no environment was verified for this row. It never implies one: a stored member whose
        /// scope was never verified reads as unverified however the current connection looks, because opening a
        /// list is not evidence about where its flows came from.
        /// </summary>
        public static FlowRow From(WorkflowReference flow, ConnectedOrganization connected)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            return new FlowRow(
                flow.Name,
                flow.StateLabel,
                flow.WorkflowId,
                EnvironmentDisplay.Describe(flow.Key.Scope, connected),
                flow.ModifiedOnUtc.HasValue
                    ? flow.ModifiedOnUtc.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)
                    : "not reported");
        }
    }
}
