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
            FlowsButton = new Button { Text = "2. Load solution flows", Dock = DockStyle.Left, Width = 150, Height = 28, Enabled = false };
            WatchlistButton = new Button { Text = "3. Build watchlist", Dock = DockStyle.Left, Width = 132, Height = 28, Enabled = false };
            CancelButton = new Button { Text = "Cancel", Dock = DockStyle.Right, Width = 84, Height = 28, Enabled = false };

            // The AM-35 spike control: one labelled action over the selected flow, on its own row so it can
            // never be mistaken for a step of the discovery the operator is working through.
            ProbeButton = new Button { Text = "Probe run history (diagnostic)", Dock = DockStyle.Left, Width = 216, Height = 26, Enabled = false };

            // The AM-42 measurement: one labelled action that needs no selection, because it asks about the
            // connection rather than about a flow. Same row as the other diagnostic and same "(diagnostic)"
            // wording, so neither reads as a step of the discovery the operator is working through.
            IdentityButton = new Button { Text = "Verify connected identity (diagnostic)", Dock = DockStyle.Left, Width = 226, Height = 26 };

            // AM-44.2: the same measurement applied to one saved list - a bounded read plus the association
            // preview, shown and never applied by this button. Enabled only when a saved list is picked.
            AssociateButton = new Button { Text = "Verify saved list (diagnostic)", Dock = DockStyle.Left, Width = 226, Height = 26, Enabled = false };

            // AM-44.3b: the operator action. It runs the same verification read and shows the same preview, then
            // asks once, and writes only on a Yes. Kept visibly separate from the diagnostic - and it never runs
            // by itself on connect, open, discovery, refresh or save.
            AssociateApplyButton = new Button
            {
                Text = "Associate saved list with connected organization",
                Dock = DockStyle.Left,
                Width = 336,
                Height = 26,
                Enabled = false
            };

            SolutionSelector = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(SolutionChoice.Label) };
            SearchBox = new TextBox { Dock = DockStyle.Right, Width = 210 };
            Label searchHint = new Label { Text = "Filter:", Dock = DockStyle.Right, Width = 46, TextAlign = ContentAlignment.MiddleRight };

            Panel buttonHost = new Panel { Dock = DockStyle.Left, Width = 420 };
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
            CoverageLabel = new Label { Dock = DockStyle.Bottom, Height = 24, AutoEllipsis = true, Text = "No watchlist yet. Build one from the selected solution, or open a saved watchlist." };

            // Selection state is control state, so the view decides only whether an action is clickable -
            // never what a probe means or what it will ask the environment for.
            FlowGrid.SelectionChanged += (sender, args) => UpdateProbeEnabled();

            Label probeHint = new Label
            {
                Text = "Read-only checks on the connection, the selected flow and a picked saved list: shapes and counts only, in a"
                    + " copyable window. The last button is the one action that can change a saved list, and it asks first.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            Panel probeRow = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(8, 2, 8, 4) };
            probeRow.Controls.Add(probeHint);

            // Docked controls are laid out back to front, so the identity button is added before the flow probe
            // to sit to its right, exactly as the toolbar's numbered steps rely on. The saved-list check sits
            // right of the connection check, since both are diagnostics and neither is a discovery step, and the
            // one action that can change a saved identity sits rightmost of all.
            probeRow.Controls.Add(AssociateApplyButton);
            probeRow.Controls.Add(AssociateButton);
            probeRow.Controls.Add(IdentityButton);
            probeRow.Controls.Add(ProbeButton);

            // The C6 persistence row, kept directly under the discovery steps and never styled as another
            // step: name the built watchlist and save it, or open one already saved for this environment.
            // Counts, membership mode and status are Core wording - this row only paints them.
            Label saveHint = new Label { Text = "Save as:", Dock = DockStyle.Left, Width = 56, TextAlign = ContentAlignment.MiddleLeft };
            WatchlistNameBox = new TextBox { Dock = DockStyle.Left, Width = 200 };
            SaveWatchlistButton = new Button { Text = "Save watchlist", Dock = DockStyle.Left, Width = 108, Height = 26, Enabled = false };
            Label savedHint = new Label { Text = "Saved:", Dock = DockStyle.Left, Width = 48, TextAlign = ContentAlignment.MiddleLeft };
            SavedWatchlists = new ComboBox { Dock = DockStyle.Left, Width = 236, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(SavedWatchlistChoice.Label) };
            OpenWatchlistButton = new Button { Text = "Open", Dock = DockStyle.Left, Width = 60, Height = 26, Enabled = false };
            WatchlistStateLabel = new Label { Dock = DockStyle.Right, Width = 200, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, Text = "Nothing to save yet" };
            SavedWatchlists.SelectedIndexChanged += (sender, args) => UpdateOpenEnabled();

            Panel watchlistRow = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(8, 6, 8, 4) };
            watchlistRow.Controls.Add(WatchlistStateLabel);
            watchlistRow.Controls.Add(OpenWatchlistButton);
            watchlistRow.Controls.Add(SavedWatchlists);
            watchlistRow.Controls.Add(savedHint);
            watchlistRow.Controls.Add(SaveWatchlistButton);
            watchlistRow.Controls.Add(WatchlistNameBox);
            watchlistRow.Controls.Add(saveHint);

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            footer.Controls.Add(StatusLabel);
            footer.Controls.Add(CoverageLabel);

            Controls.Add(FlowGrid);
            Controls.Add(footer);
            Controls.Add(probeRow);
            Controls.Add(watchlistRow);
            Controls.Add(toolbar);

            ResumeLayout(true);
        }
        public Button SolutionsButton { get; }

        public Button FlowsButton { get; }

        public Button WatchlistButton { get; }

        public Button CancelButton { get; }

        /// <summary>The AM-35 diagnostic action, disabled until a flow row is selected.</summary>
        public Button ProbeButton { get; }

        /// <summary>
        /// The AM-42 measurement of the connected environment's identifiers. Always clickable, because it asks
        /// about the connection rather than about a flow - the handler is what refuses when nothing is connected.
        /// </summary>
        public Button IdentityButton { get; }

        /// <summary>
        /// The AM-44.2 saved-list check: a bounded verification read over the picked saved list plus the
        /// association preview. Enabled only when a saved list is picked, because it asks about a list rather
        /// than about the connection or a flow.
        /// </summary>
        public Button AssociateButton { get; }

        /// <summary>
        /// The AM-44.3b operator action: verify the picked saved list against the connected organization, show the
        /// association preview, and apply it only on an explicit confirmation. Enabled with the same selection rule
        /// as the diagnostic; the handler is what refuses when nothing is connected or the read cannot support it.
        /// </summary>
        public Button AssociateApplyButton { get; }

        // C6 persistence controls: name and save the built watchlist, or open one already saved for this
        // environment. They hold no logic - the host control drives them and Core supplies the wording.
        public TextBox WatchlistNameBox { get; }

        public Button SaveWatchlistButton { get; }

        public ComboBox SavedWatchlists { get; }

        public Button OpenWatchlistButton { get; }

        public Label WatchlistStateLabel { get; }

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

        /// <summary>
        /// The workflow id of the highlighted flow row, or null when nothing is selected. It is already the
        /// canonical provider identity the grid displays, so the probe asks about exactly the row the
        /// operator is looking at rather than a name that could collide.
        /// </summary>
        public string? SelectedWorkflowId
        {
            get
            {
                DataGridViewRow? row = FlowGrid.CurrentRow;
                if (row == null)
                {
                    return null;
                }

                FlowRow? flow = row.DataBoundItem as FlowRow;
                return flow == null || string.IsNullOrWhiteSpace(flow.WorkflowId) ? null : flow.WorkflowId;
            }
        }

        private bool busy;

        /// <summary>One rule for when the diagnostic may be clicked, so no path can leave it half-enabled.</summary>
        private void UpdateProbeEnabled()
        {
            ProbeButton.Enabled = !busy && SelectedWorkflowId != null;
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
            busy = isBusy;
            SolutionsButton.Enabled = !isBusy;
            FlowsButton.Enabled = !isBusy && SelectedSolutionId != null;
            WatchlistButton.Enabled = !isBusy && FlowGrid.DataSource != null;
            CancelButton.Enabled = isBusy;
            SolutionSelector.Enabled = !isBusy;
            SearchBox.Enabled = !isBusy;
            WatchlistNameBox.Enabled = !isBusy;
            SavedWatchlists.Enabled = !isBusy;
            if (isBusy)
            {
                // A read in flight must not be raced by a save or an open; the host re-enables them once
                // it knows what is selected, so a busy pass never grants them back on its own.
                SaveWatchlistButton.Enabled = false;
                OpenWatchlistButton.Enabled = false;
                AssociateButton.Enabled = false;
                AssociateApplyButton.Enabled = false;
            }
            else
            {
                // Coming out of a busy pass, the two actions that need a picked saved list are clickable again
                // if something is still picked - the same rule as before the read started.
                UpdateOpenEnabled();
            }

            UseWaitCursor = isBusy;
            UpdateProbeEnabled();
        }

        /// <summary>
        /// Fill the "Saved" combo for the connected environment. The id is kept off the label so a display
        /// name can never be read as an identity; the host opens strictly by id. The current pick survives
        /// a refresh when it still exists, else the selection clears.
        /// </summary>
        public void BindSavedWatchlists(IEnumerable<SavedWatchlistChoice> choices, string? keepSelectedId)
        {
            SavedWatchlists.BeginUpdate();
            SavedWatchlists.Items.Clear();
            foreach (SavedWatchlistChoice choice in choices)
            {
                SavedWatchlists.Items.Add(choice);
            }

            SavedWatchlists.SelectedIndex = IndexOfSavedId(keepSelectedId);
            SavedWatchlists.EndUpdate();
            UpdateOpenEnabled();
        }

        /// <summary>The id of the highlighted saved watchlist, or null when none is picked.</summary>
        public string? SelectedSavedWatchlistId
        {
            get
            {
                SavedWatchlistChoice? choice = SavedWatchlists.SelectedItem as SavedWatchlistChoice;
                return choice == null ? null : choice.Id;
            }
        }

        /// <summary>
        /// Paint the short saved / not-saved / modified token. The wording is the caller's, derived from
        /// Core state in the host control - the view never invents its own meaning for a storage outcome.
        /// </summary>
        public void SetWatchlistState(string state)
        {
            WatchlistStateLabel.Text = state;
        }

        /// <summary>One rule for when Save may be clicked, so no path leaves it enabled over nothing.</summary>
        public void SetSaveEnabled(bool enabled)
        {
            SaveWatchlistButton.Enabled = !busy && enabled;
        }

        private void UpdateOpenEnabled()
        {
            OpenWatchlistButton.Enabled = !busy && SelectedSavedWatchlistId != null;

            // The same selection rule as opening: a saved-list check with no list picked has nothing to ask. The
            // association action needs the same selection - whether the connection can support it is the host's
            // refusal to make, and it is stated with a reason rather than by leaving the button dead.
            AssociateButton.Enabled = !busy && SelectedSavedWatchlistId != null;
            AssociateApplyButton.Enabled = !busy && SelectedSavedWatchlistId != null;
        }

        private int IndexOfSavedId(string? id)
        {
            if (id == null)
            {
                return -1;
            }

            for (int i = 0; i < SavedWatchlists.Items.Count; i++)
            {
                SavedWatchlistChoice candidate = (SavedWatchlistChoice)SavedWatchlists.Items[i];
                if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public void Say(string message)
        {
            StatusLabel.Text = message;
        }

        public void ShowCoverage(string message)
        {
            CoverageLabel.Text = message;
        }

        /// <summary>
        /// AM-36: shows the probe's decisive numbers inside the plugin. The host log is not a hand-back path an
        /// operator can be pointed at - XrmToolBox keeps plugin entries at Warning or above unless its log level
        /// is raised - so the result has to be readable and copyable where the run happened. The text is Core's;
        /// this view only displays it.
        /// </summary>
        public void ShowProbeReport(string report, string? title = null)
        {
            using (ProbeReportForm form = new ProbeReportForm(report, title))
            {
                Form? owner = FindForm();
                if (owner != null)
                {
                    form.ShowDialog(owner);
                }
                else
                {
                    form.ShowDialog();
                }
            }
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
