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

        /// <summary>
        /// Label for the AM-42 read. It names identifiers, not environments: the probe reports what the
        /// connection states, and it never claims the identifier it found is the one a watchlist must be scoped by.
        /// </summary>
        private const string IdentitySource = "Connected environment identity";

        /// <summary>
        /// Label for the AM-44.2 check. It says what was examined - a saved list, not an environment - because
        /// its answer is evidence about workflow ids and nothing else.
        /// </summary>
        private const string AssociationSource = "Saved list identity";

        private readonly DiscoveryView view;
        private readonly IClock clock = new SystemClock();

        /// <summary>
        /// The connection this plugin is on and the organization that connection has been verified against.
        /// Every asynchronous action captures from it on the UI thread and checks its ticket before applying a
        /// result, so a switch or a disconnect in flight discards what was read instead of saving, matching or
        /// labelling it under an environment the operator did not ask about.
        /// </summary>
        private readonly ConnectedDataverseEnvironment environments = new ConnectedDataverseEnvironment();

        /// <summary>
        /// The connected organization as the last completed action established it. Held so a row can be labelled
        /// with what was verified rather than with whatever the connection is now - and only ever replaced by a
        /// callback whose ticket was still current.
        /// </summary>
        private ConnectedOrganization connectedOrganization = ConnectedOrganization.NotAttempted;

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

        /// <summary>
        /// The environment scope the saved catalogue was last READ for, or null when it has not been read in
        /// this session yet. The null matters: a connection may report no environment id, so the empty string
        /// is a real scope and cannot also mean "never read". Spelling both as one string is what left a valid
        /// watchlists.json invisible to the drop-down after a restart (AM-40). WatchlistScope.NeedsRead is the
        /// only rule that reads this; it is armed solely by a usable read.
        /// </summary>
        private string? lastReadScope;

        public AutomationMonitorControl()
        {
            view = new DiscoveryView { Dock = DockStyle.Fill };
            Controls.Add(view);

            view.SolutionsButton.Click += (sender, args) => ExecuteMethod(LoadSolutions);
            view.FlowsButton.Click += (sender, args) => ExecuteMethod(LoadCloudFlows);
            view.WatchlistButton.Click += (sender, args) => BuildWatchlist();
            view.ProbeButton.Click += (sender, args) => ExecuteMethod(RunFlowRunProbe);
            view.IdentityButton.Click += (sender, args) => ExecuteMethod(RunIdentityProbe);
            view.AssociateButton.Click += (sender, args) => ExecuteMethod(VerifySavedList);
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
                environments.Invalidate();
                if (cancellation != null)
                {
                    cancellation.Dispose();
                    cancellation = null;
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// The host says the connection underneath this control changed or closed. Anything still running was
        /// read from the environment the operator was looking at a moment ago, so it is cancelled, its scope is
        /// forgotten and every result that came from it is dropped. A later callback for the same action still
        /// finds its ticket stale and discards rather than applying - this only makes that happen sooner and
        /// keeps the screen from showing one environment's rows while another is connected.
        /// </summary>
        protected override void OnConnectionUpdated(ConnectionUpdatedEventArgs e)
        {
            environments.Invalidate();
            CancelRunningWork();

            if (!IsDisposed)
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(ClearConnectionState));
                }
                else
                {
                    ClearConnectionState();
                }
            }

            base.OnConnectionUpdated(e);
        }

        /// <summary>
        /// Forget what the previous connection produced. Nothing here writes: the saved watchlists belong to
        /// whichever environment they were stored under, so the drop-down simply goes back to "not read yet" and
        /// is filled for the new connection when the operator opens it.
        /// </summary>
        private void ClearConnectionState()
        {
            connectedOrganization = ConnectedOrganization.NotAttempted;
            allSolutions = new List<SolutionReference>();
            discoveredFlows = new List<WorkflowReference>();
            lastSelection = null;
            watchlist = null;
            watchlistIsSaved = false;
            lastReadScope = null;

            view.BindSolutions(new List<SolutionChoice>(), null);
            view.BindFlows(new List<FlowRow>());
            view.BindSavedWatchlists(new List<SavedWatchlistChoice>(), null);
            view.ShowEmptyWatchlist();
            RefreshWatchlistState();
            view.Say(
                "This plugin is on a different connection now. What the previous connection produced was discarded and nothing"
                + " was saved; the saved watchlists are read again when you open the Saved list.");
        }

        /// <summary>
        /// The discard gate every callback runs through first. False means the work belongs to a connection that
        /// is gone: the result is dropped with the shared wording, and neither the screen, the saved file nor the
        /// environment label is touched with it.
        /// </summary>
        private bool IsStale(ConnectionTicket? ticket, string action)
        {
            if (environments.IsCurrent(ticket))
            {
                return false;
            }

            view.Say(ConnectionContext.DescribeDiscard(action));
            LogWarning("Automation Monitor discarded a " + action + " result that arrived after the connection changed.");
            return true;
        }

        private void LoadSolutions()
        {
            DataverseOperation operation = environments.BeginOperation(Service, ConnectionDetail);
            if (!operation.Ticket.IsBound)
            {
                view.Say("Connect to a Dataverse environment before using this tool.");
                return;
            }

            CancellationToken token = BeginWork();

            view.SetBusy(true);
            view.Say("Reading solutions the connection can see...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading solutions...",
                IsCancelable = true,
                Work = (worker, args) =>
                {
                    // The organization is resolved on this worker, against the service captured above, before
                    // anything is read: the scope is part of what every later row is keyed by, and one read
                    // serves the whole connection rather than one per record.
                    ConnectedOrganization organization = operation.Resolve(token);
                    DataverseSolutionFlowSource source = NewFlowSource(operation, organization);
                    args.Result = WorkResult.Of(operation, organization, source.GetSolutions(token));
                },
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

                    WorkResult? reading = args.Result as WorkResult;
                    if (reading == null)
                    {
                        view.Say("The solution read returned no result at all.");
                        return;
                    }

                    if (IsStale(reading.Operation.Ticket, "solution"))
                    {
                        return;
                    }

                    ProviderResult<IReadOnlyList<SolutionReference>>? result = reading.ValueAs<ProviderResult<IReadOnlyList<SolutionReference>>>();
                    if (result == null)
                    {
                        view.Say("The solution read returned no result at all.");
                        return;
                    }

                    connectedOrganization = reading.Organization;
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

            DataverseOperation operation = environments.BeginOperation(Service, ConnectionDetail);
            if (!operation.Ticket.IsBound)
            {
                view.Say("Connect to a Dataverse environment before using this tool.");
                return;
            }

            CancellationToken token = BeginWork();

            view.SetBusy(true);
            view.Say("Reading cloud flows...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading solution cloud flows...",
                IsCancelable = true,
                Work = (worker, args) =>
                {
                    ConnectedOrganization organization = operation.Resolve(token);
                    DataverseSolutionFlowSource source = NewFlowSource(operation, organization);
                    args.Result = WorkResult.Of(operation, organization, source.GetSolutionFlows(solutionId!, token));
                },
                PostWorkCallBack = args =>
                {
                    EndWork();
                    view.SetBusy(false);

                    if (args.Error != null)
                    {
                        ReportFailure(args.Error, FlowsSource);
                        return;
                    }

                    WorkResult? reading = args.Result as WorkResult;
                    if (reading == null)
                    {
                        view.Say("The flow read returned no result at all.");
                        return;
                    }

                    if (IsStale(reading.Operation.Ticket, "cloud flow"))
                    {
                        return;
                    }

                    ProviderResult<CloudFlowSelectionResult>? result =
                        reading.ValueAs<ProviderResult<CloudFlowSelectionResult>>();
                    if (result == null)
                    {
                        view.Say("The flow read returned no result at all.");
                        return;
                    }

                    // The rows carry the organization this action verified, so the identity shown in the
                    // Environment column is the one the workflow ids belong to - and an unresolved read says so
                    // here rather than leaving an empty scope to be discovered later as missing flows.
                    connectedOrganization = reading.Organization;
                    lastSelection = result.Value;
                    discoveredFlows = result.Value == null ? new List<WorkflowReference>() : result.Value.Flows;
                    view.BindFlows(ToRows(discoveredFlows, connectedOrganization));
                    view.Say(SourceStatus.Describe(FlowsSource, result));
                    view.ShowCoverage((result.Value == null
                        ? "No rows were returned by this read, so nothing was added to the list."
                        : result.Value.DescribeCoverage()) + " " + DescribeEnvironment(connectedOrganization));
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

            DataverseOperation operation = environments.BeginOperation(Service, ConnectionDetail);
            if (!operation.Ticket.IsBound)
            {
                view.Say("Connect to a Dataverse environment before running the probe.");
                return;
            }

            DataverseFlowRunProbe probe = new DataverseFlowRunProbe(operation.ConnectedService);
            CancellationToken token = BeginWork();
            DateTimeOffset checkedAtUtc = clock.UtcNow;

            view.SetBusy(true);
            view.Say("Probing run history for the selected flow (read-only, five rows per query)...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Probing run history identity...",
                IsCancelable = true,
                Work = (worker, args) => { args.Result = WorkResult.Of(operation, operation.Organization, probe.Run(workflowId!, checkedAtUtc, token)); },
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

                    WorkResult? reading = args.Result as WorkResult;
                    if (reading == null)
                    {
                        view.Say("The probe returned no result object at all.");
                        return;
                    }

                    if (IsStale(reading.Operation.Ticket, "run-history probe"))
                    {
                        return;
                    }

                    FlowRunProbeOutcome? outcome = reading.ValueAs<FlowRunProbeOutcome>();
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

        /// <summary>
        /// AM-42: measure which stable identifiers the active connection actually offers, and show the answer as
        /// shapes in the same copyable window the run-history probe uses.
        ///
        /// <para>
        /// The read is deliberately small - the connection record's environment id, the already-connected
        /// client's environment id, and one <c>WhoAmI</c> request - because its only job is to replace an
        /// assumption with a measurement before any later checkpoint dares to change how a watchlist is scoped.
        /// It writes nothing: no settings file, no watchlist, no member key. <see cref="Dispose"/> and
        /// <see cref="CancelRunningWork"/> already stop it, and the window's own text says so.
        /// </para>
        /// </summary>
        private void RunIdentityProbe()
        {
            DataverseOperation operation = environments.BeginOperation(Service, ConnectionDetail);
            if (!operation.Ticket.IsBound)
            {
                view.Say("Connect to a Dataverse environment before verifying its identity.");
                return;
            }

            // Built on the UI thread from the connection captured for this action. The probe is handed the
            // service the host already connected rather than ConnectionDetail.ServiceClient, whose getter can
            // construct a client and start a sign-in. It measures the host's own environment-id fields on
            // purpose - that is the AM-42 measurement - and none of what it measures is used as a scope: the
            // scope this plugin keys by comes only from ConnectedDataverseEnvironment.
            DataverseConnectionIdentityProbe probe = new DataverseConnectionIdentityProbe(
                operation.ConnectedService, ConnectionDetail == null ? null : ConnectionDetail.EnvironmentId);

            CancellationToken token = BeginWork();
            DateTimeOffset checkedAtUtc = clock.UtcNow;

            view.SetBusy(true);
            view.Say("Reading the connected environment's identifiers (one WhoAmI request; nothing is written)...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading connected environment identity...",
                IsCancelable = true,
                Work = (worker, args) => { args.Result = WorkResult.Of(operation, operation.Organization, probe.Run(checkedAtUtc, token)); },
                PostWorkCallBack = args =>
                {
                    EndWork();
                    view.SetBusy(false);

                    Exception? error = args.Error;
                    if (error != null)
                    {
                        ReportFailure(error, IdentitySource);
                        return;
                    }

                    WorkResult? reading = args.Result as WorkResult;
                    if (reading == null)
                    {
                        view.Say("The identity read returned no result object at all.");
                        return;
                    }

                    if (IsStale(reading.Operation.Ticket, "connected identity"))
                    {
                        return;
                    }

                    ConnectedIdentityOutcome? outcome = reading.ValueAs<ConnectedIdentityOutcome>();
                    if (outcome == null)
                    {
                        view.Say("The identity read returned no result object at all.");
                        return;
                    }

                    string report = ConnectedIdentityReport.Build(outcome);
                    LogProbeResult(outcome.Severity, report);
                    view.Say(IdentitySummary(outcome));
                    view.ShowProbeReport(report, "Connected environment identity - result");
                }
            });
        }

        /// <summary>
        /// The status line: which identifier kinds were obtained and what the one allowed comparison said. The
        /// full block is in the window, so this line stays one sentence about the connection.
        /// </summary>
        private static string IdentitySummary(ConnectedIdentityOutcome outcome)
        {
            return "Connected identity - " + ConnectedIdentityEvaluator.VerdictFor(outcome)
                + ". The two environment-id sources hold " + IdentityComparison.Describe(outcome.EnvironmentIdMatch)
                + ". Full result: the identity window that just opened, with a Copy all button.";
        }

        /// <summary>
        /// AM-44.2: check one saved list against the organization this connection is authenticated against, and
        /// show what attaching that identity would do. The action reads and reports; it writes nothing - no
        /// settings file, no watchlist - and it never applies anything, because this build cannot yet resolve a
        /// qualified connected scope when it loads and matches saved watchlists. Re-keying a stored list before
        /// then would leave it naming an identity the monitoring path cannot match, which is a list the operator
        /// can no longer use; <see cref="ScopeResolutionSupport"/> carries that deferral and names the checkpoint
        /// that lifts it.
        /// </summary>
        private void VerifySavedList()
        {
            // Captured before the read: the organization the evidence names must be the one this action
            // resolved, through the same mechanism discovery and the saved-list scope use, and the check that
            // the connection is still the same happens before anything is shown.
            DataverseOperation operation = environments.BeginOperation(Service, ConnectionDetail);
            if (!operation.Ticket.IsBound)
            {
                view.Say("Connect to a Dataverse environment before verifying a saved list.");
                return;
            }

            WatchlistManager? mgr = EnsureManager();
            if (mgr == null)
            {
                ShowStorageUnavailable();
                return;
            }

            string? listId = view.SelectedSavedWatchlistId;
            if (listId == null)
            {
                view.Say("Pick a saved watchlist in the Saved box first: this checks a list you have already saved.");
                return;
            }

            Watchlist? list = mgr.Catalog.Find(listId);
            if (list == null)
            {
                // Nothing has been read from the file in this session, or the list is gone. Reading the catalogue
                // is the honest next step; guessing which list the operator meant is not.
                ReloadSavedWatchlists(listId);
                view.Say("The saved watchlists were read - pick the list again to verify it.");
                return;
            }

            // Membership is read on the UI thread so the worker carries a snapshot of identity keys and never
            // touches a control or the connection state.
            List<WorkflowKey> keys = new List<WorkflowKey>();
            foreach (WatchlistMember member in list.Members)
            {
                keys.Add(member.Key);
            }

            DataverseWorkflowIdentitySource source = new DataverseWorkflowIdentitySource(operation.ConnectedService);
            DataverseWorkflowIdentityProbe probe = new DataverseWorkflowIdentityProbe(source, clock);
            CancellationToken token = BeginWork();

            view.SetBusy(true);
            view.Say("Verifying the saved list against the connected organization (one identity read plus small bounded queries; nothing is written)...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Verifying saved list identity...",
                IsCancelable = true,
                Work = (worker, args) =>
                {
                    // The organization comes from the same resolver discovery uses, cached for this connection,
                    // so checking a saved list costs no extra identity read and cannot name an organization some
                    // other code path invented. An unresolved read is passed on as unverified, which the probe
                    // answers with its own honest state instead of asking the environment nothing and reporting
                    // the members missing.
                    ConnectedOrganization organization = operation.Resolve(token);
                    WorkflowIdentityEvidence evidence = probe.Verify(organization.Scope, keys, token);
                    args.Result = new SavedListReading(operation.Ticket, organization, evidence, mgr.PreviewAssociation(listId, evidence));
                },
                PostWorkCallBack = args =>
                {
                    EndWork();
                    view.SetBusy(false);

                    Exception? error = args.Error;
                    if (error != null)
                    {
                        ReportFailure(error, AssociationSource);
                        return;
                    }

                    SavedListReading? reading = args.Result as SavedListReading;
                    if (reading == null || reading.Evidence == null || reading.Preview == null)
                    {
                        view.Say("The saved-list check returned no result object at all.");
                        return;
                    }

                    if (IsStale(reading.Ticket, "saved-list check"))
                    {
                        return;
                    }

                    connectedOrganization = reading.Organization ?? ConnectedOrganization.NotAttempted;

                    string report = reading.Evidence.Describe() + Environment.NewLine + reading.Preview.Describe();
                    LogProbeResult(
                        reading.Evidence.CanSupportAssociation ? ProbeReportSeverity.Information : ProbeReportSeverity.Warning,
                        report);
                    view.Say(AssociationSummary(reading));
                    view.ShowProbeReport(report, "Saved list identity - verification and preview");
                }
            });
        }

        /// <summary>
        /// The status line: what the read established and what may be done about it, in one sentence. Both halves
        /// are Core wording - the verdict and the decision - so this line can never read more confidently than the
        /// evidence does. The detail lives in the copyable window, because the host's log level drops an info line.
        /// </summary>
        private static string AssociationSummary(SavedListReading reading)
        {
            WorkflowIdentityEvidence evidence = reading.Evidence!;
            WatchlistAssociationPreview preview = reading.Preview!;

            string outcome;
            switch (preview.Decision)
            {
                case WatchlistAssociationDecision.ReadyToConfirm:
                    outcome = "the change is ready and would still need an explicit confirmation";
                    break;
                case WatchlistAssociationDecision.NoChange:
                    outcome = "the list already carries that identity";
                    break;
                case WatchlistAssociationDecision.Deferred:
                    outcome = "applying it is not enabled in this version (" + ScopeResolutionSupport.RequiredCheckpoint + " has to land first)";
                    break;
                default:
                    outcome = "it cannot be applied";
                    break;
            }

            return "Saved list check - " + evidence.Verdict + "; " + outcome
                + ". Nothing was written. Full result: the window that just opened, with a Copy all button.";
        }

        /// <summary>
        /// The Core values one saved-list check produced, carried across the worker boundary as one object with
        /// the connection ticket and organization they were read against, so the status line, the report and the
        /// environment label can never disagree about which read they describe - and so a result from a
        /// connection that is gone can be thrown away whole rather than half-applied.
        /// </summary>
        private sealed class SavedListReading
        {
            public SavedListReading(
                ConnectionTicket ticket,
                ConnectedOrganization organization,
                WorkflowIdentityEvidence evidence,
                WatchlistAssociationPreview preview)
            {
                Ticket = ticket;
                Organization = organization;
                Evidence = evidence;
                Preview = preview;
            }

            public ConnectionTicket Ticket { get; }

            public ConnectedOrganization? Organization { get; }

            public WorkflowIdentityEvidence? Evidence { get; }

            public WatchlistAssociationPreview? Preview { get; }
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

        /// <summary>
        /// The scope saved watchlists are keyed by: the verified connected organization, or empty when this
        /// session has not established one. It is the same value discovery keyed its rows with, which is what
        /// makes a saved list and a freshly found flow mean the same environment - and it is never
        /// <c>ConnectionDetail.EnvironmentId</c>, measured empty on this connection, nor a stored scope copied
        /// across from wherever the operator happened to be. An empty value here means unverified, never
        /// "the catalogue has not been read yet": that distinction is <see cref="lastReadScope"/>'s job and
        /// <see cref="WatchlistScope.NeedsRead"/> is the only rule that acts on the pair.
        /// </summary>
        private string EnvironmentScope
        {
            get { return environments.Current.ScopeText; }
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
                choices.Add(new SavedWatchlistChoice(list.Id, SavedListLabel(list)));
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

            // The scope this listing is done for is captured with the connection, and reading the file stays a
            // local read: opening the Saved list must work with no Dataverse connection at all, so the identity
            // read is not triggered here - it is whatever the last completed action established, empty until
            // then, and the operator is told the lists are not environment-scoped in that case.
            DataverseOperation operation = environments.BeginOperation(Service, ConnectionDetail);
            string environmentScope = operation.ScopeText;
            BeginWork();
            view.SetBusy(true);
            view.Say("Reading saved watchlists from this machine...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Reading saved watchlists...",
                IsCancelable = true,
                Work = (worker, args) => { args.Result = WorkResult.Of(operation, operation.Organization, mgr.Load()); },
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

                    WorkResult? reading = args.Result as WorkResult;
                    if (reading == null)
                    {
                        lastReadScope = null;
                        view.SetWatchlistState(DescribeState());
                        view.Say("Saved watchlists - no result was returned. The file on disk was not changed.");
                        return;
                    }

                    if (IsStale(reading.Operation.Ticket, "saved watchlist"))
                    {
                        return;
                    }

                    WatchlistStoreResult? result = reading.ValueAs<WatchlistStoreResult>();
                    if (result == null)
                    {
                        lastReadScope = null;
                        view.SetWatchlistState(DescribeState());
                        view.Say("Saved watchlists - no result was returned. The file on disk was not changed.");
                        return;
                    }

                    connectedOrganization = reading.Organization;

                    // Only a usable read arms the cache. A refusal leaves the catalog empty on purpose and the
                    // next drop-down must try again, because the operator may have repaired or replaced the
                    // file by hand in the meantime - remembering a refusal as a good read hides their lists.
                    lastReadScope = result.IsUsable ? environmentScope : null;
                    BindSavedWatchlists(mgr, environmentScope, keepId ?? mgr.Catalog.ActiveWatchlistId);
                    RefreshWatchlistState();
                    view.Say(SettingsStatus.DescribeForEnvironment(result, environmentScope));
                }
            });
        }

        private void OnSavedListsDropDown()
        {
            WatchlistManager? mgr = EnsureManager();
            if (mgr == null)
            {
                ShowStorageUnavailable();
                return;
            }

            // Nothing can be listed until the catalog has been read: an empty ComboBox over a file that was
            // never opened is a lie about the operator's saved lists. The rule - including a refused read and
            // a connection that now names another environment - lives in Core.
            if (!WatchlistScope.NeedsRead(lastReadScope, EnvironmentScope, mgr.BlocksSave))
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

            SolutionReference solution = chosen.Solution;
            IReadOnlyList<WorkflowReference> flows = discoveredFlows;
            ProviderResultState state = selection.Flows.Count == 0 && selection.RowsRead == 0
                ? ProviderResultState.Unavailable
                : ProviderResultState.Complete;
            string? coverageNote = selection.DescribeCoverage();

            // Captured before the work starts: the save is written under the connection and organization this
            // action began with, and refused outright if the connection is gone by the time the write would
            // happen. Storing freshly discovered flows under whatever environment is connected at the moment of
            // the write is how two environments end up inside one saved list.
            DataverseOperation operation = environments.BeginOperation(Service, ConnectionDetail);
            CancellationToken token = BeginWork();
            view.SetBusy(true);
            view.Say("Saving the watchlist to this machine...");

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Saving the watchlist...",
                IsCancelable = true,
                Work = (worker, args) =>
                {
                    ConnectedOrganization organization = operation.Resolve(token);
                    if (!environments.IsCurrent(operation.Ticket))
                    {
                        args.Result = SaveOutcome.Discarded(ConnectionContext.DescribeDiscard("watchlist save"));
                        return;
                    }

                    args.Result = SaveOverManager(mgr, organization, name, solution, flows, state, coverageNote);
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

                    if (outcome.WasDiscarded)
                    {
                        // Nothing was written, so nothing about the saved file changed and the catalogue this
                        // session read is still the one to show. Saying "could not save" here would be a lie.
                        view.SetWatchlistState(DescribeState());
                        view.Say(outcome.Message);
                        return;
                    }

                    if (!outcome.Success || outcome.Saved == null)
                    {
                        // The catalog this save touched is no longer known-good, so nothing may be offered
                        // from it until it has been read again.
                        lastReadScope = null;
                        watchlistIsSaved = false;
                        RefreshWatchlistState();
                        view.Say(outcome.Message);
                        LogError("Automation Monitor could not save the watchlist.");
                        return;
                    }

                    watchlist = outcome.Saved;
                    watchlistIsSaved = true;

                    // The save read the file first and the drop-down was just bound from the result, so this
                    // session has genuinely read this scope - and it is the scope the list was written under,
                    // not whatever the connection reports now.
                    connectedOrganization = outcome.Organization;
                    lastReadScope = outcome.Organization.ScopeText;
                    view.ShowWatchlist(outcome.Saved);
                    BindSavedWatchlists(mgr, outcome.Organization.ScopeText, outcome.Saved.Id);
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
        /// the chosen name scoped to the organization this action verified, imports the discovered flows with
        /// their solution provenance and coverage, and writes once. Every failure keeps the file untouched - a
        /// change is in memory until Save - and reloads so a half-built list cannot linger in memory.
        ///
        /// <para>
        /// The scope is authored here rather than derived from the members: a list built from verified discovery
        /// is created with the organization its flows were read from, which is a fact about this action. What it
        /// is never allowed to do is adopt a scope from a member key - that path belongs to the association, and
        /// <see cref="WatchlistManager"/> refuses it.
        /// </para>
        /// </summary>
        private static SaveOutcome SaveOverManager(
            WatchlistManager mgr,
            ConnectedOrganization organization,
            string name,
            SolutionReference solution,
            IReadOnlyList<WorkflowReference> flows,
            ProviderResultState state,
            string? coverageNote)
        {
            string environmentScope = organization.ScopeText;

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
            return SaveOutcome.Succeeded(stored ?? created.Watchlist, organization, saved.Describe());
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
                // An unopened drop-down is not the same state as a file with nothing in it: the read that
                // fills it has not happened yet, so name the missing action instead of repeating the request.
                view.Say(lastReadScope == null
                    ? "Open the Saved list first - that reads the watchlists stored on this machine - then pick one to open."
                    : "Choose a saved watchlist to open.");
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
            view.BindFlows(ToRows(IncludedWorkflows(opened), connectedOrganization));
            view.ShowWatchlist(opened);
            view.SetBusy(false);
            view.SetSaveEnabled(false);
            view.SetWatchlistState("Saved");
            view.Say("Opened saved watchlist \"" + opened.Name + "\" - its stored members, with no solution query.");
        }

        /// <summary>Carries a save result off the worker thread without touching controls there.</summary>
        private sealed class SaveOutcome
        {
            private SaveOutcome(bool success, bool discarded, Watchlist? saved, ConnectedOrganization organization, string message)
            {
                Success = success;
                WasDiscarded = discarded;
                Saved = saved;
                Organization = organization ?? ConnectedOrganization.NotAttempted;
                Message = message;
            }

            public bool Success { get; }

            /// <summary>True when the connection changed before the write: nothing was saved and nothing failed.</summary>
            public bool WasDiscarded { get; }

            public Watchlist? Saved { get; }

            /// <summary>
            /// The organization the list was actually written under. The callback re-binds the drop-down with it
            /// rather than with whatever scope the connection reports by then, so a save is always shown in the
            /// environment it was stored in.
            /// </summary>
            public ConnectedOrganization Organization { get; }

            public string Message { get; }

            public static SaveOutcome Succeeded(Watchlist saved, ConnectedOrganization organization, string message)
            {
                return new SaveOutcome(true, false, saved, organization, message);
            }

            public static SaveOutcome Failed(string message)
            {
                return new SaveOutcome(false, false, null, ConnectedOrganization.NotAttempted, message);
            }

            public static SaveOutcome Discarded(string message)
            {
                return new SaveOutcome(false, true, null, ConnectedOrganization.NotAttempted, message);
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

        /// <summary>
        /// One action's result, carried across the worker boundary together with the connection it was read
        /// under and the organization that was verified for it. A callback that cannot find that connection
        /// still current discards the value rather than showing or storing it, so no result can be applied to,
        /// re-labelled by, or saved under a connection that arrived while the work was running.
        /// </summary>
        private sealed class WorkResult
        {
            private WorkResult(DataverseOperation operation, ConnectedOrganization organization, object? value)
            {
                Operation = operation;
                Organization = organization;
                Value = value;
            }

            public DataverseOperation Operation { get; }

            public ConnectedOrganization Organization { get; }

            public object? Value { get; }

            public static WorkResult Of(DataverseOperation operation, ConnectedOrganization organization, object? value)
            {
                return new WorkResult(operation, organization, value);
            }

            public T? ValueAs<T>() where T : class
            {
                return Value as T;
            }
        }

        private static IEnumerable<FlowRow> ToRows(IReadOnlyList<WorkflowReference> flows, ConnectedOrganization connected)
        {
            List<FlowRow> rows = new List<FlowRow>();
            foreach (WorkflowReference flow in flows)
            {
                rows.Add(FlowRow.From(flow, connected));
            }

            return rows;
        }

        /// <summary>
        /// The flow source for one action: the service that action captured, projected with the organization it
        /// resolved. Neither <c>Service</c> nor <c>ConnectionDetail</c> is read here, so an environment switch
        /// mid-read cannot swap the connection or the scope under a discovery that had already started, and the
        /// identity every discovered flow is keyed by is the one this action verified.
        /// </summary>
        private DataverseSolutionFlowSource NewFlowSource(DataverseOperation operation, ConnectedOrganization organization)
        {
            return new DataverseSolutionFlowSource(operation.NewGateway(organization), clock);
        }

        /// <summary>
        /// The drop-down line: the operator's own name, the member count, and the environment state whenever the
        /// list is not the organization this session verified. A legacy unscoped list therefore reads as
        /// unverified instead of looking like it belongs to whatever is connected now, which is the whole point
        /// of leaving its stored scope empty.
        /// </summary>
        private string SavedListLabel(Watchlist list)
        {
            string label = list.Name + "  (" + list.Members.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " member(s))";
            ResourceScope scope = ResourceScope.Read(list.EnvironmentId);
            if (connectedOrganization.IsResolved && scope.IsSameResourceAs(connectedOrganization.Scope))
            {
                return label;
            }

            return label + "  [" + EnvironmentDisplay.Describe(list.EnvironmentId, connectedOrganization) + "]";
        }

        /// <summary>
        /// The one sentence about the environment this action established, shown as coverage rather than hidden:
        /// rows and saved lists are only as well-identified as the organization behind them, and a read that
        /// could not name one must be visible on screen instead of quietly storing an empty scope.
        /// </summary>
        private static string DescribeEnvironment(ConnectedOrganization organization)
        {
            if (organization.IsResolved)
            {
                return "Environment: " + EnvironmentDisplay.DescribeConnected(organization) + ".";
            }

            string reason = organization.State == ConnectedOrganizationState.NotAttempted
                ? "no identity read has run on this connection yet"
                : "this connection did not name an organization (" + (organization.FailureKind ?? "read refused") + ")";

            return "Environment: " + EnvironmentDisplay.UnverifiedText + " - " + reason
                + ", so flows discovered now and lists saved now are recorded without an environment scope.";
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
