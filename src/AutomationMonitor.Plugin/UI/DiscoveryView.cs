using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Plugin.UI
{
    /// <summary>
    /// The discovery layout, built in code (no designer, no resources). It owns controls and
    /// nothing else: no provider call, no filtering rule, no decision about what a result means.
    /// </summary>
    public sealed class DiscoveryView : UserControl
    {
        public DiscoveryView()
        {
            SuspendLayout();

            SolutionsButton = new Button { Text = "1. Load solutions", Dock = DockStyle.Left, Width = 132, Height = 28 };
            FlowsButton = new Button { Text = "2. Load flows", Dock = DockStyle.Left, Width = 112, Height = 28, Enabled = false };
            WatchlistButton = new Button { Text = "3. Build watchlist", Dock = DockStyle.Left, Width = 132, Height = 28, Enabled = false };
            CancelButton = new Button { Text = "Cancel", Dock = DockStyle.Right, Width = 84, Height = 28, Enabled = false };

            SolutionSelector = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(SolutionChoice.Label) };
            SearchBox = new TextBox { Dock = DockStyle.Right, Width = 210 };
            Label searchHint = new Label { Text = "Filter:", Dock = DockStyle.Right, Width = 46, TextAlign = ContentAlignment.MiddleRight };

            Panel buttonHost = new Panel { Dock = DockStyle.Left, Width = 380 };
            buttonHost.Controls.Add(WatchlistButton);
            buttonHost.Controls.Add(FlowsButton);
            buttonHost.Controls.Add(SolutionsButton);

            Panel comboHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
            comboHost.Controls.Add(SolutionSelector);
            comboHost.Controls.Add(searchHint);
            comboHost.Controls.Add(SearchBox);

            Panel toolbar = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 8, 8, 4) };
            toolbar.Controls.Add(comboHost);
            toolbar.Controls.Add(buttonHost);
            toolbar.Controls.Add(CancelButton);

            FlowGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false
            };
            FlowGrid.Columns.Add(NewColumn("Cloud flow", nameof(FlowRow.Name), 280));
            FlowGrid.Columns.Add(NewColumn("State", nameof(FlowRow.StateLabel), 110));
            FlowGrid.Columns.Add(NewColumn("Workflow id", nameof(FlowRow.WorkflowId), 300));
            FlowGrid.Columns.Add(NewColumn("Environment", nameof(FlowRow.EnvironmentLabel), 240));
            FlowGrid.Columns.Add(NewColumn("Modified (UTC)", nameof(FlowRow.ModifiedOnUtc), 180));

            StatusLabel = new Label { Dock = DockStyle.Top, Height = 24, AutoEllipsis = true, Text = "Connect to a Dataverse environment to begin." };
            CoverageLabel = new Label { Dock = DockStyle.Bottom, Height = 24, AutoEllipsis = true, Text = "No watchlist built yet. Nothing is written to disk in this milestone." };
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            footer.Controls.Add(StatusLabel);
            footer.Controls.Add(CoverageLabel);

            Controls.Add(FlowGrid);
            Controls.Add(footer);
            Controls.Add(toolbar);

            ResumeLayout(true);
        }
        public Button SolutionsButton { get; }

        public Button FlowsButton { get; }

        public Button WatchlistButton { get; }

        public Button CancelButton { get; }

        public ComboBox SolutionSelector { get; }

        public TextBox SearchBox { get; }

        public DataGridView FlowGrid { get; }

        public Label StatusLabel { get; }

        public Label CoverageLabel { get; }

        /// <summary>The solution the operator has picked, or null when none is selectable.</summary>
        public string? SelectedSolutionId
        {
            get
            {
                SolutionChoice? choice = SolutionSelector.SelectedItem as SolutionChoice;
                return choice == null ? null : choice.Solution.Id;
            }
        }

        /// <summary>Rebinds the selector, keeping the current pick when it still matches.</summary>
        public void BindSolutions(IEnumerable<SolutionChoice> choices, string? keepSelectedId)
        {
            SolutionSelector.BeginUpdate();
            SolutionSelector.Items.Clear();
            foreach (SolutionChoice choice in choices)
            {
                SolutionSelector.Items.Add(choice);
            }

            int restoreAt = -1;
            for (int i = 0; i < SolutionSelector.Items.Count; i++)
            {
                SolutionChoice candidate = (SolutionChoice)SolutionSelector.Items[i];
                if (keepSelectedId != null && string.Equals(candidate.Solution.Id, keepSelectedId, StringComparison.Ordinal))
                {
                    restoreAt = i;
                    break;
                }
            }

            if (restoreAt >= 0)
            {
                SolutionSelector.SelectedIndex = restoreAt;
            }
            else if (SolutionSelector.Items.Count > 0)
            {
                SolutionSelector.SelectedIndex = 0;
            }

            SolutionSelector.EndUpdate();
        }

        public void BindFlows(IEnumerable<FlowRow> rows)
        {
            FlowGrid.DataSource = new List<FlowRow>(rows);
        }

        /// <summary>One place decides what "busy" means, so no view can be left half-clickable.</summary>
        public void SetBusy(bool isBusy)
        {
            SolutionsButton.Enabled = !isBusy;
            FlowsButton.Enabled = !isBusy && SelectedSolutionId != null;
            WatchlistButton.Enabled = !isBusy && FlowGrid.DataSource != null;
            CancelButton.Enabled = isBusy;
            SolutionSelector.Enabled = !isBusy;
            SearchBox.Enabled = !isBusy;
            UseWaitCursor = isBusy;
        }

        public void Say(string message)
        {
            StatusLabel.Text = message;
        }

        public void ShowCoverage(string message)
        {
            CoverageLabel.Text = message;
        }

        /// <summary>The watchlist line always comes from the shared core summary, never from view wording.</summary>
        public void ShowWatchlist(Watchlist watchlist)
        {
            CoverageLabel.Text = WatchlistSummary.Describe(watchlist);
        }

        public void ShowEmptyWatchlist()
        {
            CoverageLabel.Text = WatchlistSummary.Empty;
        }

        private static DataGridViewTextBoxColumn NewColumn(string header, string member, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = member,
                Width = width,
                SortMode = DataGridViewColumnSortMode.Automatic,
                Resizable = DataGridViewTriState.True
            };
        }
    }
}
