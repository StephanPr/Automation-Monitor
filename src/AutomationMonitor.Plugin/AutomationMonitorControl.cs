using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using AutomationMonitor.Plugin.Connectivity;
using AutomationMonitor.Plugin.UI;
using XrmToolBox.Extensibility;

namespace AutomationMonitor.Plugin
{
    /// <summary>
    /// Solution and cloud flow discovery over the XrmToolBox Dataverse connection.
    ///
    /// <para>
    /// This control is the single coordinator of background work: it starts one operation at a
    /// time through <see cref="PluginControlBase.WorkAsync"/>, cancels it on demand and on close,
    /// and touches controls only on the UI thread. Every decision - paging, identity, selection,
    /// coverage, error class - lives in AutomationMonitor.Core.
    /// </para>
    /// </summary>
    public class AutomationMonitorControl : PluginControlBase
    {
        private const string SolutionsSource = "Solutions";
        private const string FlowsSource = "Solution cloud flows";

        /// <summary>
        /// Label for the AM-35 diagnostic. It is deliberately not called "run history": one five-row sample
        /// per filter is an identity check, and a label that promised history would read as coverage the
        /// probe does not have.
        /// </summary>
        private const string ProbeSource = "Run-history identity probe";

        private readonly DiscoveryView view;
        private readonly IClock clock = new SystemClock();

        /// <summary>Own cancellation so a close or a cancel stops the read between pages.</summary>
        private CancellationTokenSource? cancellation;

        private IReadOnlyList<SolutionReference> allSolutions = new List<SolutionReference>();
        private IReadOnlyList<WorkflowReference> discoveredFlows = new List<WorkflowReference>();
        private CloudFlowSelectionResult? lastSelection;
        private Watchlist? watchlist;

        public AutomationMonitorControl()
        {
            view = new DiscoveryView { Dock = DockStyle.Fill };
            Controls.Add(view);

            view.SolutionsButton.Click += (sender, args) => ExecuteMethod(LoadSolutions);
            view.FlowsButton.Click += (sender, args) => ExecuteMethod(LoadCloudFlows);
            view.WatchlistButton.Click += (sender, args) => BuildWatchlist();
            view.ProbeButton.Click += (sender, args) => ExecuteMethod(RunFlowRunProbe);
            view.CancelButton.Click += (sender, args) => CancelRunningWork();
            view.SearchBox.TextChanged += (sender, args) => BindSolutionChoices();
            view.SolutionSelector.SelectedIndexChanged += (sender, args) => OnSolutionChanged();
        }

        /// <summary>Closing the plugin must stop every read: no task may outlive the control.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelRunningWork();
                if (cancellation != null)
                {
                    cancellation.Dispose();
                    cancellation = null;
                }
            }

            base.Dispose(disposing);
        }

        private void LoadSolutions()
        {
            DataverseSolutionFlowSource source = NewFlowSource();
            CancellationToken token = BeginWork();

            view.SetBusy(true);
            view.Say("Reading solutions the connection can see...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading solutions...",
                IsCancelable = true,
                Work = (worker, args) => { args.Result = source.GetSolutions(token); },
                PostWorkCallBack = args =>
                {
                    EndWork();
                    view.SetBusy(false);

                    Exception? error = args.Error;
                    if (error != null)
                    {
                        ReportFailure(error, SolutionsSource);
                        return;
                    }

                    ProviderResult<IReadOnlyList<SolutionReference>>? result =
                        args.Result as ProviderResult<IReadOnlyList<SolutionReference>>;
                    if (result == null)
                    {
                        view.Say("The solution read returned no result at all.");
                        return;
                    }

                    allSolutions = result.Value ?? new List<SolutionReference>();
                    BindSolutionChoices();
                    view.Say(SourceStatus.Describe(SolutionsSource, result));
                    if (result.IsUsable && allSolutions.Count == 0)
                    {
                        view.ShowCoverage("This environment exposes no solutions this connection can read.");
                    }
                }
            });
        }
        private void LoadCloudFlows()
        {
            string? solutionId = view.SelectedSolutionId;
            if (string.IsNullOrEmpty(solutionId))
            {
                view.Say("Choose a solution first.");
                return;
            }

            DataverseSolutionFlowSource source = NewFlowSource();
            CancellationToken token = BeginWork();

            view.SetBusy(true);
            view.Say("Reading cloud flows...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading solution cloud flows...",
                IsCancelable = true,
                Work = (worker, args) => { args.Result = source.GetSolutionFlows(solutionId!, token); },
                PostWorkCallBack = args =>
                {
                    EndWork();
                    view.SetBusy(false);

                    if (args.Error != null)
                    {
                        ReportFailure(args.Error, FlowsSource);
                        return;
                    }

                    ProviderResult<CloudFlowSelectionResult>? result = args.Result as ProviderResult<CloudFlowSelectionResult>;
                    if (result == null)
                    {
                        view.Say("The flow read returned no result at all.");
                        return;
                    }

                    lastSelection = result.Value;
                    discoveredFlows = result.Value == null ? new List<WorkflowReference>() : result.Value.Flows;
                    view.BindFlows(ToRows(discoveredFlows));
                    view.Say(SourceStatus.Describe(FlowsSource, result));
                    view.ShowCoverage(result.Value == null
                        ? "No rows were returned by this read, so nothing was added to the list."
                        : result.Value.DescribeCoverage());
                    view.WatchlistButton.Enabled = result.IsUsable;

                    // An empty or degraded flow list is the case where "what did we ask for, and what
                    // did the rows contain" decides the diagnosis - which includes rows this build could not
                    // identify, because the status line can only count those, not explain where the id went.
                    // That detail goes to the log, never into the status line, and it carries column names
                    // and codes only.
                    CloudFlowSelectionResult? selection = result.Value;
                    if (selection == null
                        || discoveredFlows.Count == 0
                        || !result.IsUsable
                        || selection.MissingIdCount > 0
                        || selection.ConflictingIdCount > 0)
                    {
                        LogInfo(selection == null
                            ? "Automation Monitor diagnostic - the flow read returned no result object at all."
                            : selection.DescribeDiagnostics());
                    }
                }
            });
        }

        /// <summary>Builds the in-memory watchlist from the discovered flows. Nothing is saved.</summary>
        private void BuildWatchlist()
        {
            SolutionChoice? chosen = view.SolutionSelector.SelectedItem as SolutionChoice;
            if (chosen == null)
            {
                view.Say("Choose a solution first.");
                return;
            }

            if (lastSelection == null)
            {
                view.Say("Load this solution's flows before building a watchlist.");
                return;
            }

            // The watchlist inherits the coverage of the discovery it came from.
            ProviderResultState state = lastSelection.Flows.Count == 0 && lastSelection.RowsRead == 0
                ? ProviderResultState.Unavailable
                : ProviderResultState.Complete;

            WatchlistBuilder builder = new WatchlistBuilder(
                chosen.Solution.Id,
                chosen.Solution.DisplayName,
                WatchlistMembershipMode.Snapshot);
            builder.AddSolutionFlows(chosen.Solution, discoveredFlows, state, lastSelection.DescribeCoverage());

            watchlist = builder.Build();
            view.ShowWatchlist(watchlist);
            view.Say(watchlist.HasFullCoverage
                ? "Watchlist built in memory from " + chosen.Label + "."
                : "Watchlist built in memory, but its discovery was " + SourceStatus.Label(watchlist.State) + ".");
        }

        /// <summary>
        /// AM-35: the read-only identity probe over the selected flow. One operation on the same worker seam
        /// every other read uses, cancelable, and gone when the control is disposed.
        ///
        /// <para>
        /// The screen gets a sentence with two counts, because a status line describes the environment rather
        /// than this tool's reasoning. The structural detail - column shapes, paging, the verdict - goes to
        /// the log, which is where a maintainer reads implementation detail without it becoming an operator's
        /// problem. Neither line can carry an id, a name, an error message or a payload: the outcome type is
        /// built from counts and codes, and the probe drops the provider message where it catches it.
        /// </para>
        /// </summary>
        private void RunFlowRunProbe()
        {
            string? workflowId = view.SelectedWorkflowId;
            if (string.IsNullOrEmpty(workflowId))
            {
                view.Say("Select a cloud flow row first, then run the probe.");
                return;
            }

            if (Service == null)
            {
                view.Say("Connect to a Dataverse environment before running the probe.");
                return;
            }

            DataverseFlowRunProbe probe = new DataverseFlowRunProbe(Service);
            CancellationToken token = BeginWork();
            DateTimeOffset checkedAtUtc = clock.UtcNow;

            view.SetBusy(true);
            view.Say("Probing run history for the selected flow (read-only, five rows per query)...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Probing run history identity...",
                IsCancelable = true,
                Work = (worker, args) => { args.Result = probe.Run(workflowId!, checkedAtUtc, token); },
                PostWorkCallBack = args =>
                {
                    EndWork();
                    view.SetBusy(false);

                    Exception? error = args.Error;
                    if (error != null)
                    {
                        ReportFailure(error, ProbeSource);
                        return;
                    }

                    FlowRunProbeOutcome? outcome = args.Result as FlowRunProbeOutcome;
                    if (outcome == null)
                    {
                        view.Say("The probe returned no result object at all.");
                        return;
                    }

                    // The detail belongs in the log; the status line stays about the environment.
                    LogInfo(outcome.Describe());
                    view.Say(ProbeSummary(outcome));
                }
            });
        }

        /// <summary>
        /// The on-screen sentence: two counts, how many runs came back both ways, and where the detail is. It
        /// calls a five-row sample what it is and never implies the window was read.
        /// </summary>
        private static string ProbeSummary(FlowRunProbeOutcome outcome)
        {
            return "Probe read " + outcome.StringFilter.Rows.ToString() + " row(s) through the text filter and "
                + outcome.LookupFilter.Rows.ToString() + " row(s) through the lookup filter; "
                + outcome.OverlapCount.ToString() + " run(s) came back both ways. "
                + (outcome.IsInconclusive
                    ? "Identity question still open - this probe does not say why."
                    : "Both filters answered; see the log for the shapes.")
                + " Structural detail is in the XrmToolBox log.";
        }

        private void BindSolutionChoices()
        {
            string? keep = view.SelectedSolutionId;
            IReadOnlyList<SolutionReference> matches = SolutionSearch.Filter(allSolutions, view.SearchBox.Text);
            List<SolutionChoice> choices = new List<SolutionChoice>();
            foreach (SolutionReference solution in matches)
            {
                choices.Add(new SolutionChoice(solution));
            }

            view.BindSolutions(choices, keep);
            OnSolutionChanged();
        }

        private void OnSolutionChanged()
        {
            view.FlowsButton.Enabled = view.SelectedSolutionId != null;
            SolutionChoice? chosen = view.SolutionSelector.SelectedItem as SolutionChoice;
            if (chosen == null)
            {
                return;
            }

            view.ShowCoverage(chosen.Solution.IsManaged
                ? "Managed solution: its flows can be monitored here, but not edited in this environment."
                : "Unmanaged solution.");
        }
        /// <summary>
        /// A cancelled read is a normal outcome and must not look like a failure; anything else
        /// keeps its sanitized reason in the status bar as well as the log.
        /// </summary>
        private void ReportFailure(Exception error, string sourceLabel)
        {
            if (error is OperationCanceledException)
            {
                view.Say(sourceLabel + " read was cancelled. Nothing was changed.");
                return;
            }

            ProviderResultState state = ProviderErrorClassifier.Classify(error.GetType().Name, error.Message);
            ProviderErrorClassifier.DefectKind defect = ProviderErrorClassifier.DefectKindOf(error.GetType().Name, error.Message);
            string reason = DiagnosticsSanitizer.Describe(
                ProviderErrorClassifier.AdviceFor(state, defect),
                DiagnosticsSanitizer.Sanitize(error.Message));
            view.Say(SourceStatus.Describe(sourceLabel, state, 0, 0, clock.UtcNow, reason));
            LogError("Automation Monitor could not read " + sourceLabel + ".");
        }

        private static IEnumerable<FlowRow> ToRows(IReadOnlyList<WorkflowReference> flows)
        {
            List<FlowRow> rows = new List<FlowRow>();
            foreach (WorkflowReference flow in flows)
            {
                rows.Add(FlowRow.From(flow));
            }

            return rows;
        }

        /// <summary>The gateway is built on the UI thread because Service is connection state.</summary>
        private DataverseSolutionFlowSource NewFlowSource()
        {
            if (Service == null)
            {
                throw new InvalidOperationException("Connect to a Dataverse environment before using this tool.");
            }

            DataverseGateway gateway = new DataverseGateway(Service, ConnectionDetail == null ? null : ConnectionDetail.EnvironmentId);
            return new DataverseSolutionFlowSource(gateway, clock);
        }

        private CancellationToken BeginWork()
        {
            // One refresh at a time: a new read supersedes whatever was still running.
            CancelRunningWork();
            cancellation = new CancellationTokenSource();
            return cancellation.Token;
        }

        private void EndWork()
        {
            if (cancellation != null)
            {
                cancellation.Dispose();
                cancellation = null;
            }
        }

        private void CancelRunningWork()
        {
            CancellationTokenSource? active = cancellation;
            if (active == null)
            {
                return;
            }

            try
            {
                active.Cancel();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            CancelWorker();
        }
    }
}
