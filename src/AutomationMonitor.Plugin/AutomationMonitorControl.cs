using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using AutomationMonitor.Plugin.Connectivity;
using AutomationMonitor.Plugin.Storage;
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

        // C6 persistence. The storage stack is built on first use, never in the constructor, so opening the
        // plugin cannot touch the settings directory. The manager is the only writer; these flags are this
        // control's honest view of where the shown list sits relative to the file on disk.
        private WatchlistStorage? storage;
        private WatchlistManager? manager;
        private string? storageFailure;
        private bool watchlistIsSaved;
        private string loadedEnvironmentKey = string.Empty;

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
            view.SaveWatchlistButton.Click += (sender, args) => ExecuteMethod(SaveWatchlist);
            view.OpenWatchlistButton.Click += (sender, args) => OpenWatchlist();
            view.SavedWatchlists.DropDown += (sender, args) => ExecuteMethod(OnSavedListsDropDown);
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
            watchlistIsSaved = false;
            view.ShowWatchlist(watchlist);
            view.Say(watchlist.HasFullCoverage
                ? "Watchlist built in memory from " + chosen.Label + "."
                : "Watchlist built in memory, but its discovery was " + SourceStatus.Label(watchlist.State) + ".");

            // Suggest the solution-derived name so a first save needs one click; RefreshWatchlistState then
            // lights Save for a list that is genuinely not on disk yet.
            if (string.IsNullOrWhiteSpace(view.WatchlistNameBox.Text))
            {
                view.WatchlistNameBox.Text = watchlist.Name;
            }

            RefreshWatchlistState();
        }

        /// <summary>
        /// AM-35: the read-only identity probe over the selected flow. One operation on the same worker seam
        /// every other read uses, cancelable, and gone when the control is disposed.
        ///
        /// <para>
        /// AM-36: the result is shown in the plugin, not left in the host log. XrmToolBox writes plugin entries
        /// to Logs\AutomationMonitor.Plugin.log only at Warning or above and its log level defaults to Warning,
        /// so a diagnostic logged as Information is invisible - which is exactly how the first live probe run
        /// lost its verdict. The copyable result window now carries the window, both counts, the overlap, the
        /// verdict and any failure; the status line keeps one short sentence about the environment; and the log
        /// keeps the same text at the severity Core computed. Nothing on any of them can carry an id, a name, an
        /// error message or a payload: the outcome type is built from counts and codes, and the probe drops the
        /// provider message where it catches it.
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

                    // AM-36: the result has to be readable from the plugin itself. XrmToolBox writes plugin log
                    // entries only at Warning or above (its LogManager defaults to Warning), so a log line alone
                    // was never a hand-back path. The report window carries the whole sanitized block with a Copy
                    // button; the log keeps the same text at the severity the result actually earned.
                    string report = FlowRunProbeReport.Build(outcome);
                    LogProbeResult(outcome.Severity, report);
                    view.Say(ProbeSummary(outcome));
                    view.ShowProbeReport(report);
                }
            });
        }

        /// <summary>
        /// The on-screen sentence: the verdict in words, the two counts and the overlap. It calls a five-row
        /// sample what it is, never implies the window was read, and never sends the operator to a log panel -
        /// the result window opened beside it carries the detail.
        /// </summary>
        private static string ProbeSummary(FlowRunProbeOutcome outcome)
        {
            string failures = outcome.StringFilter.Failure != null || outcome.LookupFilter.Failure != null
                ? " A query was refused, so these counts do not measure what this flow has run."
                : string.Empty;

            return "Probe - " + outcome.VerdictLabel + ": "
                + outcome.StringFilter.Rows.ToString() + " row(s) through the text filter, "
                + outcome.LookupFilter.Rows.ToString() + " row(s) through the lookup filter, "
                + outcome.OverlapCount.ToString() + " run(s) returned by both queries."
                + failures
                + " Full result: the probe window that just opened, with a Copy all button.";
        }

        /// <summary>
        /// One place decides how loudly the host speaks about a probe result, from the severity Core computed.
        /// A routine empty answer stays Information: raising it to Warning to force a log file would make the
        /// log say "attention needed" when nothing does, and the copyable window already carries the answer.
        /// </summary>
        private void LogProbeResult(ProbeReportSeverity severity, string report)
        {
            switch (severity)
            {
                case ProbeReportSeverity.Error:
                    LogError(report);
                    break;
                case ProbeReportSeverity.Warning:
                    LogWarning(report);
                    break;
                default:
                    LogInfo(report);
                    break;
            }
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

        // ---- C6: save the built watchlist and open a saved one, over the one storage stack. ----
        /// <summary>
        /// Build the storage stack on first use. Construction only resolves a directory and reads nothing, so
        /// this is safe on the UI thread; every real read or write stays on a worker. Returns null and keeps
        /// the reason when there is no settings folder to use, so save/open disable honestly rather than throw.
        /// </summary>
        private WatchlistManager? EnsureManager()
        {
            if (manager != null || storageFailure != null)
            {
                return manager;
            }

            if (WatchlistStorage.TryCreate(out WatchlistStorage? created, out string? reason) && created != null)
            {
                storage = created;
                manager = created.Manager;
            }
            else
            {
                storageFailure = reason ?? "Watchlist storage is not available on this machine.";
            }

            return manager;
        }

        /// <summary>The scope saved watchlists are keyed by. Empty means the connection reported no id.</summary>
        private string EnvironmentScope
        {
            get { return (ConnectionDetail == null ? null : ConnectionDetail.EnvironmentId) ?? string.Empty; }
        }

        private static string NormalizeScope(string? environmentId)
        {
            return (environmentId ?? string.Empty).Trim();
        }

        /// <summary>
        /// The short saved / not-saved / modified token. Every branch is Core state - BlocksSave,
        /// HasUnsavedChanges and whether the shown list is the one on disk - never wording the view invents.
        /// </summary>
        private string DescribeState()
        {
            if (storageFailure != null)
            {
                return "Saving unavailable";
            }

            if (manager != null && manager.BlocksSave)
            {
                return "Reload needed";
            }

            if (watchlist == null)
            {
                return "Nothing to save yet";
            }

            if (!watchlistIsSaved)
            {
                return "Not saved yet";
            }

            return manager != null && manager.HasUnsavedChanges ? "Modified since save" : "Saved";
        }

        private void RefreshWatchlistState()
        {
            view.SetSaveEnabled(watchlist != null && !watchlistIsSaved);
            view.SetWatchlistState(DescribeState());
        }

        private void ShowStorageUnavailable()
        {
            view.SetSaveEnabled(false);
            view.SetWatchlistState("Saving unavailable");
            view.Say(storageFailure ?? "Watchlist storage is not available on this machine.");
        }

        /// <summary>
        /// The manager's own refusals (duplicate name, empty name, an unwritable file) are this tool's wording
        /// and carry no tenant data, so they surface verbatim; anything unexpected stays generic.
        /// </summary>
        private static string DescribeStorageError(Exception error)
        {
            if (error is InvalidOperationException || error is ArgumentException)
            {
                return error.Message;
            }

            return "The watchlist could not be saved or read. The file on disk was not changed.";
        }

        private string DescribeBlocked(WatchlistManager mgr)
        {
            WatchlistStoreResult? last = mgr.LastStoreResult;
            return last != null
                ? last.Describe()
                : "The saved watchlists could not be read, so nothing can be saved or opened until they are reloaded. The file on disk was left untouched.";
        }

        private void BindSavedWatchlists(WatchlistManager mgr, string environmentScope, string? keepId)
        {
            List<SavedWatchlistChoice> choices = new List<SavedWatchlistChoice>();
            foreach (Watchlist list in mgr.Lists(environmentScope))
            {
                choices.Add(new SavedWatchlistChoice(list.Id, list.Name + "  (" + list.Members.Count + " member(s))"));
            }

            view.BindSavedWatchlists(choices, keepId);
        }

        private static IReadOnlyList<WorkflowReference> IncludedWorkflows(Watchlist watchlist)
        {
            List<WorkflowReference> flows = new List<WorkflowReference>();
            foreach (WatchlistMember member in watchlist.Members)
            {
                if (member.IsIncluded)
                {
                    flows.Add(member.Workflow);
                }
            }

            return flows;
        }

        /// <summary>
        /// Fill the "Saved" combo from the local file, on a worker. Reading the saved catalogue is never a
        /// provider call - the whole point of a saved list is that it is already there without Dataverse.
        /// </summary>
        private void ReloadSavedWatchlists(string? keepId)
        {
            WatchlistManager? mgr = EnsureManager();
            if (mgr == null)
            {
                ShowStorageUnavailable();
                return;
            }

            string environmentScope = EnvironmentScope;
            BeginWork();
            view.SetBusy(true);
            view.Say("Reading saved watchlists from this machine...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading saved watchlists...",
                IsCancelable = true,
                Work = (worker, args) => { args.Result = mgr.Load(); },
                PostWorkCallBack = args =>
                {
                    EndWork();
                    view.SetBusy(false);

                    if (args.Error != null)
                    {
                        view.SetWatchlistState("Reload needed");
                        view.Say(DescribeStorageError(args.Error));
                        LogError("Automation Monitor could not read the saved watchlists.");
                        return;
                    }

                    WatchlistStoreResult? result = args.Result as WatchlistStoreResult;
                    loadedEnvironmentKey = NormalizeScope(environmentScope);
                    BindSavedWatchlists(mgr, environmentScope, keepId ?? mgr.Catalog.ActiveWatchlistId);
                    RefreshWatchlistState();
                    view.Say(result == null
                        ? "Saved watchlists - no result was returned."
                        : ScopeQualified(result, environmentScope));
                }
            });
        }

        /// <summary>
        /// The store's own honest line, with one added qualifier when the connection reported no environment
        /// id: the saved lists are then not environment-scoped, and provider-capabilities requires that to be
        /// visible rather than silently shown as if per-environment isolation were working.
        /// </summary>
        private static string ScopeQualified(WatchlistStoreResult result, string environmentScope)
        {
            string line = result.Describe();
            if (NormalizeScope(environmentScope).Length == 0)
            {
                line += " - the connection reported no environment id, so these saved lists are not environment-scoped.";
            }

            return line;
        }

        private void OnSavedListsDropDown()
        {
            WatchlistManager? mgr = EnsureManager();
            if (mgr == null)
            {
                ShowStorageUnavailable();
                return;
            }

            // Enumerate the file at most once per environment, and again if the last read was refused.
            if (!mgr.BlocksSave && string.Equals(loadedEnvironmentKey, NormalizeScope(EnvironmentScope), StringComparison.Ordinal))
            {
                return;
            }

            ReloadSavedWatchlists(view.SelectedSavedWatchlistId);
        }

        private void SaveWatchlist()
        {
            if (watchlist == null)
            {
                view.SetWatchlistState("Nothing to save yet");
                view.Say("Build a watchlist before saving it.");
                return;
            }

            if (watchlistIsSaved)
            {
                view.SetWatchlistState("Saved");
                view.Say("This watchlist is already saved. Build a new one to save it under another name.");
                return;
            }

            SolutionChoice? chosen = view.SolutionSelector.SelectedItem as SolutionChoice;
            CloudFlowSelectionResult? selection = lastSelection;
            if (chosen == null || selection == null)
            {
                view.SetWatchlistState("Not saved yet");
                view.Say("Save stores the watchlist built from a solution's flows - load them and build first.");
                return;
            }

            WatchlistManager? mgr = EnsureManager();
            if (mgr == null)
            {
                ShowStorageUnavailable();
                return;
            }

            string name = (view.WatchlistNameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                view.SetWatchlistState("Name required");
                view.Say("Enter a name for the watchlist before saving it.");
                return;
            }

            if (mgr.BlocksSave)
            {
                view.SetWatchlistState("Reload needed");
                view.Say(DescribeBlocked(mgr));
                return;
            }

            string environmentScope = EnvironmentScope;
            SolutionReference solution = chosen.Solution;
            IReadOnlyList<WorkflowReference> flows = discoveredFlows;
            ProviderResultState state = selection.Flows.Count == 0 && selection.RowsRead == 0
                ? ProviderResultState.Unavailable
                : ProviderResultState.Complete;
            string? coverageNote = selection.DescribeCoverage();

            BeginWork();
            view.SetBusy(true);
            view.Say("Saving the watchlist to this machine...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Saving the watchlist...",
                IsCancelable = true,
                Work = (worker, args) =>
                {
                    args.Result = SaveOverManager(mgr, environmentScope, name, solution, flows, state, coverageNote);
                },
                PostWorkCallBack = args =>
                {
                    EndWork();
                    view.SetBusy(false);

                    SaveOutcome outcome = args.Result as SaveOutcome ?? SaveOutcome.Failed("The save returned no result.");
                    if (args.Error != null)
                    {
                        outcome = SaveOutcome.Failed(DescribeStorageError(args.Error));
                    }

                    if (!outcome.Success || outcome.Saved == null)
                    {
                        loadedEnvironmentKey = string.Empty;
                        watchlistIsSaved = false;
                        RefreshWatchlistState();
                        view.Say(outcome.Message);
                        LogError("Automation Monitor could not save the watchlist.");
                        return;
                    }

                    watchlist = outcome.Saved;
                    watchlistIsSaved = true;
                    loadedEnvironmentKey = NormalizeScope(environmentScope);
                    view.ShowWatchlist(outcome.Saved);
                    BindSavedWatchlists(mgr, environmentScope, outcome.Saved.Id);
                    view.SetSaveEnabled(false);
                    view.SetWatchlistState("Saved");
                    view.Say(outcome.Message);
                }
            });
        }

        /// <summary>
        /// The whole save, on the worker. It reads the file first so this save ADDS to what is already stored
        /// rather than replacing it (the manager's catalog is empty until it is loaded, and writing an empty
        /// catalog would silently drop the operator's other saved lists). It then creates a snapshot list under
        /// the chosen name scoped to the environment, imports the discovered flows with their solution
        /// provenance and coverage, and writes once. Every failure keeps the file untouched - a change is in
        /// memory until Save - and reloads so a half-built list cannot linger in memory.
        /// </summary>
        private static SaveOutcome SaveOverManager(
            WatchlistManager mgr,
            string environmentScope,
            string name,
            SolutionReference solution,
            IReadOnlyList<WorkflowReference> flows,
            ProviderResultState state,
            string? coverageNote)
        {
            WatchlistStoreResult read = mgr.Load();
            if (!read.IsUsable)
            {
                return SaveOutcome.Failed(read.Describe());
            }

            WatchlistOperationResult created = mgr.Create(name, WatchlistMembershipMode.Snapshot, environmentScope);
            if (!created.IsApplied || created.Watchlist == null)
            {
                return SaveOutcome.Failed(created.Describe());
            }

            string id = created.Watchlist.Id;
            WatchlistOperationResult imported = mgr.ImportSolutionFlows(id, solution, flows, state, coverageNote);
            if (imported.Outcome != WatchlistOperationOutcome.Applied && imported.Outcome != WatchlistOperationOutcome.NoChange)
            {
                mgr.Load();
                return SaveOutcome.Failed(imported.Describe());
            }

            WatchlistStoreResult saved = mgr.Save();
            if (!saved.IsUsable)
            {
                mgr.Load();
                return SaveOutcome.Failed(saved.Describe());
            }

            Watchlist? stored = mgr.Find(id);
            return SaveOutcome.Succeeded(stored ?? created.Watchlist, saved.Describe());
        }

        private void OpenWatchlist()
        {
            WatchlistManager? mgr = EnsureManager();
            if (mgr == null)
            {
                ShowStorageUnavailable();
                return;
            }

            if (mgr.BlocksSave)
            {
                view.SetWatchlistState("Reload needed");
                view.Say(DescribeBlocked(mgr));
                return;
            }

            string? id = view.SelectedSavedWatchlistId;
            if (id == null)
            {
                view.Say("Choose a saved watchlist to open.");
                return;
            }

            // In-memory lookup over the loaded catalogue: opening shows what is stored and must never re-query
            // the solution. The combo is environment-scoped, so the id belongs to this environment.
            Watchlist? opened = mgr.Find(id);
            if (opened == null)
            {
                view.Say("That saved watchlist is no longer here. Open the saved list again.");
                return;
            }

            watchlist = opened;
            watchlistIsSaved = true;
            view.BindFlows(ToRows(IncludedWorkflows(opened)));
            view.ShowWatchlist(opened);
            view.SetBusy(false);
            view.SetSaveEnabled(false);
            view.SetWatchlistState("Saved");
            view.Say("Opened saved watchlist \"" + opened.Name + "\" - its stored members, with no solution query.");
        }

        /// <summary>Carries a save result off the worker thread without touching controls there.</summary>
        private sealed class SaveOutcome
        {
            private SaveOutcome(bool success, Watchlist? saved, string message)
            {
                Success = success;
                Saved = saved;
                Message = message;
            }

            public bool Success { get; }

            public Watchlist? Saved { get; }

            public string Message { get; }

            public static SaveOutcome Succeeded(Watchlist saved, string message)
            {
                return new SaveOutcome(true, saved, message);
            }

            public static SaveOutcome Failed(string message)
            {
                return new SaveOutcome(false, null, message);
            }
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
