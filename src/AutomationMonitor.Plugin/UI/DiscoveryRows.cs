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
    /// identifier, and the combo is filled per environment so it only ever offers lists scoped to the
    /// connected environment.
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

        public static FlowRow From(WorkflowReference flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            // WorkflowKey normalises the id, so this is the provider id kept for deep links. The environment
            // column is the shared scope label: it says what kind of scope the row carries and never prints a
            // whole identifier, which is how an organization id stays out of an on-screen status line.
            return new FlowRow(
                flow.Name,
                flow.StateLabel,
                flow.WorkflowId,
                ResourceScopeLabel.DescribeStored(flow.EnvironmentId),
                flow.ModifiedOnUtc.HasValue
                    ? flow.ModifiedOnUtc.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)
                    : "not reported");
        }
    }
}
