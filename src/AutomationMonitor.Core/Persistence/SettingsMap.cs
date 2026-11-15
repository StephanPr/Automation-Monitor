using System;
using System.Collections.Generic;
using System.Globalization;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// The only place the domain model and the stored documents meet. Reading is deliberately
    /// two-tiered, because "this file is not what the build expects" has two very different answers:
    ///
    /// <list type="bullet">
    /// <item><description>
    /// A <em>structural</em> problem - an unparseable timestamp, an unrecognised membership mode, a
    /// missing or duplicated id - refuses the whole file. Those values decide whether a later refresh
    /// may delete membership, so guessing one is how a watchlist gets quietly rewritten.
    /// </description></item>
    /// <item><description>
    /// A <em>field-level</em> oddity - one unreadable origin token, one out-of-range threshold - is dropped
    /// with a note. The operator keeps the rest of the file and is told exactly what was not understood. An
    /// oddity that removes a <em>member</em> - a provider this build does not know, a stored id that is
    /// missing - is reported as unrepresented content on top of the note: the catalog stays usable, but the
    /// store refuses to write it back, because saving would delete from the file what the read could not
    /// understand. Scope text is never dropped at all; a value this build cannot interpret goes back out
    /// exactly as it came in.
    /// </description></item>
    /// </list>
    /// </summary>
    public static class SettingsMap
    {
        /// <summary>Build the document to write. Nothing is inferred: the catalog is the authority.</summary>
        public static SettingsDocument ToDocument(WatchlistCatalog catalog, DateTimeOffset savedAtUtc)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            SettingsDocument document = new SettingsDocument();
            document.SchemaVersion = SettingsSchema.CurrentVersion;
            document.Kind = SettingsSchema.Kind;
            document.WrittenBy = SettingsSchema.WrittenBy;
            document.SavedAtUtc = SettingsFormat.Timestamp(savedAtUtc);

            WatchlistFileDocument watchlists = new WatchlistFileDocument();
            watchlists.ActiveWatchlistId = catalog.ActiveWatchlistId;
            watchlists.Items = new List<WatchlistDocument>();
            foreach (Watchlist watchlist in catalog.Watchlists)
            {
                watchlists.Items.Add(ToDocument(watchlist));
            }

            document.Watchlists = watchlists;
            document.App = ToDocument(catalog.Settings);
            return document;
        }

        private static WatchlistDocument ToDocument(Watchlist watchlist)
        {
            WatchlistDocument document = new WatchlistDocument();
            document.Id = watchlist.Id;
            document.Name = SettingsFormat.Text(watchlist.Name, 200);
            document.Description = SettingsFormat.Text(watchlist.Description, 1000);
            document.EnvironmentId = watchlist.EnvironmentId;
            document.MembershipMode = WatchlistMembershipModes.Token(watchlist.Mode);
            document.CreatedAtUtc = SettingsFormat.Timestamp(watchlist.CreatedAtUtc);
            document.ModifiedAtUtc = SettingsFormat.Timestamp(watchlist.ModifiedAtUtc);
            document.LastMembershipRefreshUtc = SettingsFormat.Timestamp(watchlist.LastMembershipRefreshUtc);
            document.CoverageState = ProviderResultStates.Token(watchlist.State);
            document.SourceSolutionIds = new List<string>(watchlist.SourceSolutionIds);
            document.CoverageNotes = new List<string>();
            foreach (string note in watchlist.CoverageNotes)
            {
                document.CoverageNotes.Add(SettingsFormat.Text(note, 400));
            }

            document.Members = new List<WatchlistMemberDocument>();
            foreach (WatchlistMember member in watchlist.Members)
            {
                document.Members.Add(ToDocument(member));
            }

            document.Preferences = ToDocument(watchlist.Preferences);
            return document;
        }

        private static WatchlistMemberDocument ToDocument(WatchlistMember member)
        {
            WatchlistMemberDocument document = new WatchlistMemberDocument();
            document.Provider = ProviderKinds.Prefix(member.Key.Provider);
            document.ScopeId = member.Key.ScopeId;
            document.WorkflowId = member.Key.WorkflowId;
            document.Name = SettingsFormat.Text(member.Workflow.Name, 200);
            document.State = WorkflowStates.Token(member.Workflow.State);
            document.StateCode = member.Workflow.RawStateCode;
            document.Included = member.IsIncluded;
            document.AddedAtUtc = SettingsFormat.Timestamp(member.AddedAtUtc);
            document.LastSeenUtc = SettingsFormat.Timestamp(member.LastSeenUtc);
            document.Origins = new List<string>(WatchlistMemberSources.Tokens(member.Source));
            document.SolutionIds = new List<string>(member.SourceSolutionIds);
            return document;
        }

        private static ApplicationSettingsDocument ToDocument(ApplicationSettings settings)
        {
            ApplicationSettingsDocument document = new ApplicationSettingsDocument();
            document.ActiveEnvironmentId = settings.ActiveEnvironmentId;
            document.DefaultAutoRefresh = RefreshIntervals.Token(settings.DefaultAutoRefresh);
            document.DefaultDateRange = MonitoringDateRanges.Token(settings.DefaultDateRange);
            document.DefaultShowExcludedMembers = settings.DefaultShowExcludedMembers;
            document.DefaultThresholds = ToDocument(settings.DefaultThresholds);

            document.Environments = new List<EnvironmentProfileDocument>();
            foreach (EnvironmentProfile profile in settings.Environments)
            {
                EnvironmentProfileDocument stored = new EnvironmentProfileDocument();
                stored.EnvironmentId = profile.EnvironmentId;
                stored.ActiveWatchlistId = profile.ActiveWatchlistId;

                // An override that is not set is written as nothing at all: writing "off" would turn
                // "inherit" into a decision and stop an environment inheriting.
                stored.AutoRefresh = profile.AutoRefresh.HasValue ? RefreshIntervals.Token(profile.AutoRefresh.Value) : null;
                stored.DateRange = profile.DateRange.HasValue ? MonitoringDateRanges.Token(profile.DateRange.Value) : null;
                stored.LastUsedUtc = SettingsFormat.Timestamp(profile.LastUsedUtc);
                document.Environments.Add(stored);
            }

            return document;
        }

        private static WatchlistPreferencesDocument ToDocument(WatchlistPreferences preferences)
        {
            WatchlistPreferencesDocument document = new WatchlistPreferencesDocument();
            document.AutoRefresh = preferences.AutoRefresh.HasValue ? RefreshIntervals.Token(preferences.AutoRefresh.Value) : null;
            document.DateRange = preferences.DateRange.HasValue ? MonitoringDateRanges.Token(preferences.DateRange.Value) : null;
            document.ShowExcludedMembers = preferences.ShowExcludedMembers;
            document.Thresholds = ToDocument(preferences.Thresholds);
            return document;
        }

        private static DurationThresholdsDocument? ToDocument(DurationThresholds thresholds)
        {
            if (thresholds == null || thresholds.IsEmpty)
            {
                return null;
            }

            DurationThresholdsDocument document = new DurationThresholdsDocument();
            document.SlowCompletedSeconds = thresholds.SlowCompletedSeconds;
            document.MaximumRunningSeconds = thresholds.MaximumRunningSeconds;
            return document;
        }

        /// <summary>
        /// Read a document into a catalog. A false result leaves <paramref name="catalog"/> empty and
        /// <paramref name="problem"/> naming the refusal, so a caller can never mistake a rejected file
        /// for an empty one.
        /// </summary>
        public static bool TryToCatalog(SettingsDocument? document, out WatchlistCatalog catalog, out string? problem, out IReadOnlyList<string> notes)
        {
            return TryToCatalog(document, out catalog, out problem, out notes, out _);
        }

        /// <summary>
        /// The same read, additionally reporting what it had to leave out.
        ///
        /// <para>
        /// <paramref name="unrepresentedMembers"/> is not a warning to be shown and forgotten: a catalog
        /// carrying it is a partial view of the file, and a save from that catalog would rewrite the file
        /// without those members. Dropping a provider this build does not know and then writing the file back
        /// is the data loss AC-29 exists to prevent, so the store refuses the save and the caller shows these
        /// lines to explain what is blocked and why.
        /// </para>
        /// </summary>
        public static bool TryToCatalog(
            SettingsDocument? document,
            out WatchlistCatalog catalog,
            out string? problem,
            out IReadOnlyList<string> notes,
            out IReadOnlyList<string> unrepresentedMembers)
        {
            List<string> messages = new List<string>();
            notes = messages;
            List<string> unrepresented = new List<string>();
            unrepresentedMembers = unrepresented;
            problem = null;
            catalog = WatchlistCatalog.Empty;

            if (document == null)
            {
                problem = "There was no settings document to read.";
                return false;
            }

            // The discriminator is required, not a courtesy: it is what tells this build that the bytes in
            // front of it are a watchlist store rather than another tool's JSON that merely parses. A
            // complete foreign object deserialises into an empty document, and "empty" is exactly the
            // answer that would let the next save overwrite somebody else's file - so a file that does not
            // name itself is refused whole and left where it is.
            if (string.IsNullOrWhiteSpace(document.Kind))
            {
                problem = "The file does not say what it holds, so it is not treated as this tool's watchlist store.";
                return false;
            }

            if (!string.Equals(document.Kind, SettingsSchema.Kind, StringComparison.OrdinalIgnoreCase))
            {
                problem = "The file calls itself \"" + SettingsFormat.Text(document.Kind, 80)
                    + "\", which is not this tool's watchlist store.";
                return false;
            }

            if (document.App == null && document.Watchlists == null)
            {
                messages.Add("the file held neither watchlists nor settings, so defaults are in use");
            }

            ApplicationSettings settings;
            if (!TryRead(document.App, out settings, out problem, messages))
            {
                return false;
            }

            List<Watchlist> watchlists = new List<Watchlist>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? activeWatchlistId = null;
            if (document.Watchlists != null)
            {
                activeWatchlistId = string.IsNullOrWhiteSpace(document.Watchlists.ActiveWatchlistId)
                    ? null
                    : document.Watchlists.ActiveWatchlistId!.Trim();

                if (document.Watchlists.Items != null)
                {
                    foreach (WatchlistDocument stored in document.Watchlists.Items)
                    {
                        Watchlist? watchlist;
                        if (!TryRead(stored, out watchlist, out problem, messages, unrepresented))
                        {
                            return false;
                        }

                        if (watchlist == null)
                        {
                            continue;
                        }

                        if (!ids.Add(watchlist.Id))
                        {
                            problem = "Two watchlists in the file share the id \"" + watchlist.Id
                                + "\". Identity cannot be guessed, so nothing was loaded.";
                            return false;
                        }

                        watchlists.Add(watchlist);
                    }
                }
            }

            catalog = new WatchlistCatalog(watchlists, activeWatchlistId, settings, unrepresented);
            return true;
        }

        private static bool TryRead(
            WatchlistDocument? stored,
            out Watchlist? watchlist,
            out string? problem,
            List<string> notes,
            List<string> unrepresented)
        {
            watchlist = null;
            problem = null;
            if (stored == null)
            {
                return true;
            }

            string id = SettingsFormat.Text(stored.Id, 100);
            if (id.Length == 0)
            {
                problem = "A watchlist entry in the file has no id, and membership cannot be trusted without one.";
                return false;
            }

            string name = SettingsFormat.Text(stored.Name, 200);
            if (name.Length == 0)
            {
                name = "Untitled watchlist";
                notes.Add("the watchlist saved as \"" + id + "\" had no name");
            }

            WatchlistMembershipMode mode = WatchlistMembershipMode.Snapshot;
            if (string.IsNullOrWhiteSpace(stored.MembershipMode))
            {
                notes.Add("watchlist \"" + name + "\" did not say how membership is decided, so it is treated as a snapshot");
            }
            else if (!WatchlistMembershipModes.TryParse(stored.MembershipMode, out mode))
            {
                problem = "Watchlist \"" + name + "\" says its membership mode is \""
                    + SettingsFormat.Text(stored.MembershipMode, 60) + "\", which this version does not understand.";
                return false;
            }

            DateTimeOffset? createdAtUtc;
            DateTimeOffset? modifiedAtUtc;
            DateTimeOffset? refreshedUtc;
            if (!SettingsFormat.TryTimestamp(stored.CreatedAtUtc, out createdAtUtc)
                || !SettingsFormat.TryTimestamp(stored.ModifiedAtUtc, out modifiedAtUtc)
                || !SettingsFormat.TryTimestamp(stored.LastMembershipRefreshUtc, out refreshedUtc))
            {
                problem = "Watchlist \"" + name + "\" has a stored timestamp that is not ISO 8601 UTC text.";
                return false;
            }

            ProviderResultState state = ProviderResultState.Stale;
            if (string.IsNullOrWhiteSpace(stored.CoverageState))
            {
                notes.Add("watchlist \"" + name + "\" did not record how complete its stored membership is, so it is shown as unverified");
            }
            else if (!ProviderResultStates.TryParse(stored.CoverageState, out state))
            {
                state = ProviderResultState.Stale;
                notes.Add("watchlist \"" + name + "\" recorded a coverage state this version cannot read, so it is shown as unverified");
            }

            WatchlistBuilder builder = new WatchlistBuilder(id, name, mode, stored.Description, stored.EnvironmentId);
            builder.SetPreferences(ReadPreferences(stored.Preferences, name, notes));
            NoteUninterpretableScope(stored.EnvironmentId, "watchlist \"" + name + "\"", notes);

            if (stored.Members != null)
            {
                foreach (WatchlistMemberDocument member in stored.Members)
                {
                    WatchlistMember? read;
                    if (!TryRead(member, name, out read, out problem, notes, unrepresented))
                    {
                        return false;
                    }

                    if (read != null && !builder.AddStoredMember(read))
                    {
                        notes.Add("watchlist \"" + name + "\" listed the same flow twice, so the second entry was dropped");
                    }
                }
            }

            builder.SetSourceSolutions(stored.SourceSolutionIds);

            List<string> coverageNotes = new List<string>();
            if (stored.CoverageNotes != null)
            {
                foreach (string note in stored.CoverageNotes)
                {
                    if (!string.IsNullOrWhiteSpace(note))
                    {
                        coverageNotes.Add(SettingsFormat.Text(note, 400));
                    }
                }
            }

            builder.SetCoverage(state, coverageNotes);
            builder.SetTimestamps(createdAtUtc, modifiedAtUtc, refreshedUtc);
            watchlist = builder.Build();
            return true;
        }

        /// <summary>
        /// Read one stored member. A member whose identity cannot be reconstructed is dropped with a
        /// note rather than loaded with a guessed key: a wrong identity merges two flows together, which
        /// is worse than a watchlist that says one flow is missing.
        ///
        /// <para>
        /// Dropping is not harmless. The file on disk still holds that member, so every drop is reported again
        /// in <paramref name="unrepresented"/>, and that report is what stops the catalog being written back
        /// over a file this build only half read.
        /// </para>
        /// </summary>
        private static bool TryRead(
            WatchlistMemberDocument? stored,
            string watchlistName,
            out WatchlistMember? member,
            out string? problem,
            List<string> notes,
            List<string> unrepresented)
        {
            member = null;
            problem = null;
            if (stored == null)
            {
                return true;
            }

            ProviderKind provider;
            if (!ProviderKinds.TryParse(stored.Provider, out provider))
            {
                notes.Add("a member of \"" + watchlistName + "\" names the provider \""
                    + SettingsFormat.Text(stored.Provider, 60) + "\", which this version cannot read, so it was skipped");
                unrepresented.Add("a member of \"" + watchlistName + "\" belongs to the provider \""
                    + SettingsFormat.Text(stored.Provider, 60) + "\"");
                return true;
            }

            string workflowId = SettingsFormat.Text(stored.WorkflowId, 200);
            if (workflowId.Length == 0)
            {
                notes.Add("a member of \"" + watchlistName + "\" has no workflow id, so it was skipped");
                unrepresented.Add("a member of \"" + watchlistName + "\" carries no workflow id");
                return true;
            }

            DateTimeOffset? addedAtUtc;
            DateTimeOffset? lastSeenUtc;
            if (!SettingsFormat.TryTimestamp(stored.AddedAtUtc, out addedAtUtc)
                || !SettingsFormat.TryTimestamp(stored.LastSeenUtc, out lastSeenUtc))
            {
                problem = "A member of \"" + watchlistName + "\" has a stored timestamp that is not ISO 8601 UTC text.";
                return false;
            }

            WorkflowState state = WorkflowState.Unknown;
            if (!string.IsNullOrWhiteSpace(stored.State))
            {
                WorkflowStates.TryParse(stored.State, out state);
            }

            WatchlistMemberSource source;
            if (!WatchlistMemberSources.TryParse(stored.Origins, out source))
            {
                notes.Add("a member of \"" + watchlistName + "\" recorded an origin this version cannot read, so its provenance is shown as unknown");
            }

            bool isIncluded = stored.Included ?? true;
            if (!stored.Included.HasValue)
            {
                notes.Add("a member of \"" + watchlistName + "\" did not say whether it is included, so it is monitored");
            }

            NoteUninterpretableScope(stored.ScopeId, "a member of \"" + watchlistName + "\"", notes);
            WorkflowKey key = WorkflowKey.Create(provider, stored.ScopeId, workflowId);
            WorkflowReference workflow = new WorkflowReference(
                key,
                string.IsNullOrWhiteSpace(stored.Name) ? workflowId : stored.Name,
                state,
                rawStateCode: stored.StateCode);

            member = new WatchlistMember(workflow, source, stored.SolutionIds, isIncluded, addedAtUtc, lastSeenUtc);
            return true;
        }

        private static WatchlistPreferences ReadPreferences(WatchlistPreferencesDocument? stored, string watchlistName, List<string> notes)
        {
            if (stored == null)
            {
                return WatchlistPreferences.Inherited;
            }

            RefreshInterval? autoRefresh = null;
            if (!string.IsNullOrWhiteSpace(stored.AutoRefresh))
            {
                RefreshInterval parsed;
                if (RefreshIntervals.TryParse(stored.AutoRefresh, out parsed))
                {
                    autoRefresh = parsed;
                }
                else
                {
                    notes.Add("watchlist \"" + watchlistName + "\" stored the refresh interval \""
                        + SettingsFormat.Text(stored.AutoRefresh, 40) + "\", which is not offered, so it inherits the default");
                }
            }

            MonitoringDateRange? dateRange = null;
            if (!string.IsNullOrWhiteSpace(stored.DateRange))
            {
                MonitoringDateRange parsedRange;
                if (MonitoringDateRanges.TryParse(stored.DateRange, out parsedRange))
                {
                    dateRange = parsedRange;
                }
                else
                {
                    notes.Add("watchlist \"" + watchlistName + "\" stored the date range \""
                        + SettingsFormat.Text(stored.DateRange, 40) + "\", which is not offered, so it inherits the default");
                }
            }

            return new WatchlistPreferences(autoRefresh, dateRange, stored.ShowExcludedMembers, ReadThresholds(stored.Thresholds, watchlistName, notes));
        }

        private static DurationThresholds ReadThresholds(DurationThresholdsDocument? stored, string owner, List<string> notes)
        {
            if (stored == null)
            {
                return DurationThresholds.Empty;
            }

            DurationThresholds thresholds;
            string? thresholdProblem;
            if (!DurationThresholds.TryCreate(stored.SlowCompletedSeconds, stored.MaximumRunningSeconds, out thresholds, out thresholdProblem))
            {
                notes.Add(owner + " stored an unusable duration threshold (" + thresholdProblem + "), so it was dropped");
                return DurationThresholds.Empty;
            }

            return thresholds;
        }

        private static bool TryRead(
            ApplicationSettingsDocument? stored,
            out ApplicationSettings settings,
            out string? problem,
            List<string> notes)
        {
            problem = null;
            settings = ApplicationSettings.Defaults;
            if (stored == null)
            {
                return true;
            }

            RefreshInterval defaultAutoRefresh = RefreshInterval.Off;
            if (!string.IsNullOrWhiteSpace(stored.DefaultAutoRefresh)
                && !RefreshIntervals.TryParse(stored.DefaultAutoRefresh, out defaultAutoRefresh))
            {
                defaultAutoRefresh = RefreshInterval.Off;
                notes.Add("the stored default refresh interval \""
                    + SettingsFormat.Text(stored.DefaultAutoRefresh, 40) + "\" is not offered, so auto-refresh stays off");
            }

            MonitoringDateRange defaultDateRange = MonitoringDateRanges.Default;
            if (!string.IsNullOrWhiteSpace(stored.DefaultDateRange)
                && !MonitoringDateRanges.TryParse(stored.DefaultDateRange, out defaultDateRange))
            {
                defaultDateRange = MonitoringDateRanges.Default;
                notes.Add("the stored default date range \""
                    + SettingsFormat.Text(stored.DefaultDateRange, 40) + "\" is not offered, so the one-hour window is used");
            }

            settings = new ApplicationSettings(
                stored.ActiveEnvironmentId,
                defaultAutoRefresh,
                defaultDateRange,
                stored.DefaultShowExcludedMembers ?? true,
                ReadThresholds(stored.DefaultThresholds, "the global defaults", notes),
                null);

            if (stored.Environments == null)
            {
                return true;
            }

            foreach (EnvironmentProfileDocument profile in stored.Environments)
            {
                if (profile == null)
                {
                    continue;
                }

                DateTimeOffset? lastUsedUtc;
                if (!SettingsFormat.TryTimestamp(profile.LastUsedUtc, out lastUsedUtc))
                {
                    problem = "A stored environment profile has a last-used timestamp that is not ISO 8601 UTC text.";
                    return false;
                }

                // A scope this build cannot interpret stays exactly as stored here too: the profile is the
                // environment's own record, and guessing at it would be worse than leaving it unread.
                NoteUninterpretableScope(profile.EnvironmentId, "an environment profile", notes);

                RefreshInterval? autoRefresh = null;
                if (!string.IsNullOrWhiteSpace(profile.AutoRefresh))
                {
                    RefreshInterval parsedInterval;
                    if (RefreshIntervals.TryParse(profile.AutoRefresh, out parsedInterval))
                    {
                        autoRefresh = parsedInterval;
                    }
                    else
                    {
                        notes.Add("the environment profile " + ResourceScopeLabel.DescribeStored(profile.EnvironmentId)
                            + " stored an unusable refresh interval, so it inherits the default");
                    }
                }

                MonitoringDateRange? dateRange = null;
                if (!string.IsNullOrWhiteSpace(profile.DateRange))
                {
                    MonitoringDateRange parsedRange;
                    if (MonitoringDateRanges.TryParse(profile.DateRange, out parsedRange))
                    {
                        dateRange = parsedRange;
                    }
                    else
                    {
                        notes.Add("the environment profile " + ResourceScopeLabel.DescribeStored(profile.EnvironmentId)
                            + " stored an unusable date range, so it inherits the default");
                    }
                }

                settings = settings.WithProfile(new EnvironmentProfile(
                    profile.EnvironmentId,
                    profile.ActiveWatchlistId,
                    autoRefresh,
                    dateRange,
                    lastUsedUtc));
            }

            return true;
        }

        /// <summary>
        /// Note a stored scope this build cannot interpret, without touching it. A scope is identity the
        /// operator's file holds and a later build may understand, so it is written back exactly as it came in
        /// and never converted to an empty scope. An unqualified value (no kind token) is the ordinary shape a
        /// file had before kinds were recorded, so it stays quiet; only an unknown kind or a damaged
        /// identifier is worth the operator's attention.
        /// </summary>
        private static void NoteUninterpretableScope(string? storedScope, string owner, List<string> notes)
        {
            ResourceScope scope = ResourceScope.Read(storedScope);
            if (scope.State != ResourceScopeState.UnrecognizedKind && scope.State != ResourceScopeState.Invalid)
            {
                return;
            }

            notes.Add(owner + " recorded an environment scope this version cannot interpret ("
                + scope.State + "), so it is kept exactly as stored and shown as unreadable");
        }
    }
}
