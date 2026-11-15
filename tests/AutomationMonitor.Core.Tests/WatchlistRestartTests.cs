using System;
using System.IO;
using System.Linq;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Services;
using AutomationMonitor.Plugin.Storage;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// What a plugin restart is allowed to forget. A saved watchlist has to come back from the file on disk
/// through a brand new manager, store and in-memory catalog, so every case here writes with one manager and
/// reads with another over a real temporary directory - the same linked <see cref="DiskSettingsFileAccess"/>
/// the plugin compiles, not a fake. AM-40 was exactly this boundary: a valid file, a working read, and a
/// drop-down that never asked for the read because "never read" and "read for an unscoped environment"
/// were the same string.
/// </summary>
public sealed class WatchlistRestartTests : IDisposable
{
    private const string EnvironmentA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string EnvironmentB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string EnvironmentC = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string SolutionId = "11111111-1111-1111-1111-111111111111";
    private const string CorruptMarker = "not json at all";

    private static readonly DateTimeOffset Stamp = new DateTimeOffset(2026, 11, 15, 9, 45, 39, TimeSpan.Zero);

    // The shape this build writes: current schema, own kind, token enum values, ISO 8601 UTC timestamps,
    // one unscoped snapshot list with three members from one solution. Synthetic ids and names only.
    private static readonly string FixtureJson = @"{
      'schemaVersion': 1,
      'kind': 'watchlist-store',
      'savedAtUtc': '2026-11-15T09:45:39.4492594+00:00',
      'writtenBy': 'Automation Monitor for XrmToolBox',
      'app': {
        'activeEnvironmentId': '',
        'defaultAutoRefresh': 'off',
        'defaultDateRange': '1h',
        'defaultShowExcludedMembers': true,
        'defaultThresholds': null,
        'environments': [
          { 'environmentId': '', 'activeWatchlistId': 'fixture-list', 'autoRefresh': null, 'dateRange': null, 'lastUsedUtc': '2026-11-15T09:45:39.4492594+00:00' }
        ]
      },
      'watchlists': {
        'activeWatchlistId': 'fixture-list',
        'items': [
          {
            'id': 'fixture-list',
            'name': 'Fixture watchlist',
            'description': '',
            'environmentId': '',
            'membershipMode': 'snapshot',
            'createdAtUtc': '2026-11-15T09:45:39.4411146+00:00',
            'modifiedAtUtc': '2026-11-15T09:45:39.4492594+00:00',
            'lastMembershipRefreshUtc': null,
            'sourceSolutionIds': ['11111111-1111-1111-1111-111111111111'],
            'members': [
              { 'provider': 'powerautomate', 'scopeId': '', 'workflowId': '22222222-2222-2222-2222-222222222222', 'name': 'Fixture flow one', 'state': 'enabled', 'stateCode': 1, 'included': true, 'addedAtUtc': '2026-11-15T09:45:39.4431211+00:00', 'lastSeenUtc': '2026-11-15T09:45:39.4431211+00:00', 'origins': ['solution'], 'solutionIds': ['11111111-1111-1111-1111-111111111111'] },
              { 'provider': 'powerautomate', 'scopeId': '', 'workflowId': '33333333-3333-3333-3333-333333333333', 'name': 'Fixture flow two', 'state': 'enabled', 'stateCode': 1, 'included': true, 'addedAtUtc': '2026-11-15T09:45:39.4431211+00:00', 'lastSeenUtc': '2026-11-15T09:45:39.4431211+00:00', 'origins': ['solution'], 'solutionIds': ['11111111-1111-1111-1111-111111111111'] },
              { 'provider': 'powerautomate', 'scopeId': '', 'workflowId': '44444444-4444-4444-4444-444444444444', 'name': 'Fixture flow three', 'state': 'disabled', 'stateCode': 2, 'included': true, 'addedAtUtc': '2026-11-15T09:45:39.4431211+00:00', 'lastSeenUtc': '2026-11-15T09:45:39.4431211+00:00', 'origins': ['solution'], 'solutionIds': ['11111111-1111-1111-1111-111111111111'] }
            ],
            'coverageState': 'complete',
            'coverageNotes': ['Read 3 flow(s) from 1 solution.'],
            'preferences': { 'autoRefresh': null, 'dateRange': null, 'showExcludedMembers': null, 'thresholds': null }
          }
        ]
      }
    }".Replace('\'', '"');

    private readonly string _root;

    public WatchlistRestartTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AutomationMonitor-RestartTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // A temp folder the OS holds for a moment is not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static WatchlistManager Open(string root)
    {
        SettingsLayout layout = new SettingsLayout(root);
        FixedClock clock = new FixedClock { Now = Stamp };
        SettingsFileStore storage = new SettingsFileStore(
            layout, new DiskSettingsFileAccess(layout), new DataContractJsonSettingsSerializer(), clock);
        return new WatchlistManager(new JsonWatchlistStore(storage, clock), clock, new GuidIdGenerator());
    }

    private string DataPath { get { return Path.Combine(_root, SettingsLayout.DataFileName); } }

    private void WriteFixture()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(DataPath, FixtureJson);
    }

    private static string CreateAndSave(WatchlistManager manager, string name, string? environmentId)
    {
        WatchlistOperationResult created = manager.Create(name, WatchlistMembershipMode.Snapshot, environmentId);
        Assert.True(created.IsApplied, name + " could not be created: " + created.Describe());
        Assert.NotNull(created.Watchlist);
        return created.Watchlist!.Id;
    }

    [Fact]
    public void AWatchlistSavedByOneManagerIsListedByTheNext()
    {
        WatchlistManager first = Open(_root);
        string id = CreateAndSave(first, "Go-live", EnvironmentA);
        Assert.True(first.Save().IsUsable);

        WatchlistManager reopened = Open(_root);
        WatchlistStoreResult read = reopened.Load();

        Assert.Equal(SettingsOutcome.Loaded, read.Outcome);
        Watchlist stored = Assert.Single(reopened.Lists(EnvironmentA));
        Assert.Equal(id, stored.Id);
        Assert.Equal("Go-live", stored.Name);
        Assert.Equal(EnvironmentA, stored.EnvironmentId);
        Assert.Equal(Stamp, stored.ModifiedAtUtc);
    }

    [Fact]
    public void AWatchlistSavedWithoutAnEnvironmentIsListedByEveryConnection()
    {
        // A connection that reports no environment id (measured on XrmToolBox 1.2026.9.78) stores the list
        // unscoped. Those lists must come back for the next session, whatever it reports - that is the
        // documented rule in WatchlistCatalog.ForEnvironment, and it is what AM-40 depended on.
        WatchlistManager first = Open(_root);
        string id = CreateAndSave(first, "Unscoped", null);
        Assert.True(first.Save().IsUsable);
        Assert.Equal(string.Empty, first.Find(id)!.EnvironmentId);

        WatchlistManager reopened = Open(_root);
        reopened.Load();

        Assert.Single(reopened.Lists(null));
        Assert.Single(reopened.Lists(string.Empty));
        Assert.Single(reopened.Lists(EnvironmentB));
    }

    [Fact]
    public void AWatchlistSavedForAnotherEnvironmentIsNeverListedHere()
    {
        WatchlistManager first = Open(_root);
        string id = CreateAndSave(first, "Production only", EnvironmentA);
        Assert.True(first.Save().IsUsable);

        WatchlistManager reopened = Open(_root);
        reopened.Load();

        Assert.Empty(reopened.Lists(EnvironmentB));
        Assert.Single(reopened.Lists(EnvironmentA));
        Assert.Equal(WatchlistOperationOutcome.EnvironmentMismatch, reopened.Select(id, EnvironmentB).Outcome);
        Assert.Equal(WatchlistOperationOutcome.Applied, reopened.Select(id, EnvironmentA).Outcome);
    }

    [Fact]
    public void SeveralWatchlistsAllSurviveOneCatalogueRoundTrip()
    {
        WatchlistManager first = Open(_root);
        string alpha = CreateAndSave(first, "Alpha", EnvironmentA);
        string beta = CreateAndSave(first, "Beta", EnvironmentA);
        string gamma = CreateAndSave(first, "Gamma", null);
        Assert.True(first.Save().IsUsable);

        WatchlistManager reopened = Open(_root);
        WatchlistStoreResult read = reopened.Load();

        Assert.Equal(3, read.Catalog.Count);
        Assert.Equal(
            new[] { alpha, beta, gamma },
            reopened.Lists(EnvironmentA).Select(list => list.Id).ToArray());
        Assert.Equal(new[] { gamma }, reopened.Lists(EnvironmentB).Select(list => list.Id).ToArray());
        Assert.Equal(3, reopened.Lists(string.Empty).Count);
    }

    [Fact]
    public void AFileWrittenInThisBuildsShapeOpensWithoutRefusal()
    {
        WriteFixture();
        WatchlistManager reopened = Open(_root);

        WatchlistStoreResult read = reopened.Load();

        Assert.Equal(SettingsOutcome.Loaded, read.Outcome);
        Assert.True(read.IsUsable);
        Assert.False(read.IsPreservedRefusal);
        Assert.False(reopened.BlocksSave);
        Assert.Empty(read.Notes);

        Watchlist list = Assert.Single(read.Catalog.Watchlists);
        Assert.Equal("fixture-list", list.Id);
        Assert.Equal(WatchlistMembershipMode.Snapshot, list.Mode);
        Assert.Equal(string.Empty, list.EnvironmentId);
        Assert.Equal(ProviderResultState.Complete, list.State);
        Assert.Single(list.SourceSolutionIds);
        Assert.Single(list.CoverageNotes);
        Assert.Equal(3, list.Members.Count);
        Assert.All(list.Members, member => Assert.True(member.IsIncluded));
        Assert.Equal(SolutionId, Assert.Single(list.Members[0].SourceSolutionIds));

        // The drop-down's own enumeration, on both an unscoped and a named connection.
        Assert.Single(reopened.Lists(string.Empty));
        Assert.Single(reopened.Lists(EnvironmentB));
        Assert.Equal("fixture-list", reopened.ActiveWatchlist(string.Empty)!.Id);
    }

    [Fact]
    public void AFreshSessionArmsTheReadCacheOnlyAfterAUsableRead()
    {
        WriteFixture();
        WatchlistManager reopened = Open(_root);

        // Before any read: the AM-40 state. A session that has read nothing must never be treated as
        // already showing the file, whatever environment its connection reports.
        Assert.True(WatchlistScope.NeedsRead(null, string.Empty, reopened.BlocksSave));

        WatchlistStoreResult read = reopened.Load();
        string? lastReadScope = read.IsUsable ? WatchlistScope.Normalize(string.Empty) : null;

        Assert.NotNull(lastReadScope);
        Assert.False(WatchlistScope.NeedsRead(lastReadScope, string.Empty, reopened.BlocksSave));
        Assert.True(WatchlistScope.NeedsRead(lastReadScope, EnvironmentA, reopened.BlocksSave));
        Assert.Single(reopened.Lists(string.Empty));
    }

    [Fact]
    public void ARefusedFileIsNeverReportedAsAnEmptyCatalogue()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(DataPath, "{ " + CorruptMarker);
        WatchlistManager reopened = Open(_root);

        WatchlistStoreResult read = reopened.Load();

        Assert.Equal(SettingsOutcome.RejectedCorrupt, read.Outcome);
        Assert.False(read.IsUsable);
        Assert.True(read.IsPreservedRefusal);
        Assert.True(reopened.BlocksSave);
        Assert.Empty(read.Catalog.Watchlists);
        Assert.Empty(reopened.Lists(string.Empty));
        Assert.Contains("CORRUPT AND PRESERVED", read.Describe(), StringComparison.Ordinal);

        // A refusal must not be remembered as a good read, and it must not have eaten the file.
        Assert.True(WatchlistScope.NeedsRead(WatchlistScope.Normalize(string.Empty), string.Empty, reopened.BlocksSave));
        Assert.Contains(
            Directory.GetFiles(_root),
            file => File.ReadAllText(file).Contains(CorruptMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void AFirstRunReadsAsAFirstRunAndNotAsAFailure()
    {
        WatchlistManager firstRun = Open(_root);
        WatchlistStoreResult missing = firstRun.Load();
        string line = SettingsStatus.DescribeForEnvironment(missing, string.Empty);

        Assert.Equal(SettingsOutcome.CreatedDefault, missing.Outcome);
        Assert.True(missing.IsUsable);
        Assert.False(firstRun.BlocksSave);
        Assert.Contains("first run", line, StringComparison.Ordinal);
        Assert.Contains("0 of 0 saved list(s) shown here", line, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDropDownLineCountsWhatThisEnvironmentSees()
    {
        WatchlistManager first = Open(_root);
        CreateAndSave(first, "Alpha", EnvironmentA);
        CreateAndSave(first, "Beta", EnvironmentA);
        CreateAndSave(first, "Gamma", EnvironmentB);
        Assert.True(first.Save().IsUsable);

        WatchlistManager reopened = Open(_root);
        WatchlistStoreResult read = reopened.Load();

        Assert.Contains(
            "2 of 3 saved list(s) shown here",
            SettingsStatus.DescribeForEnvironment(read, EnvironmentA),
            StringComparison.Ordinal);
        Assert.Contains(
            "1 of 3 saved list(s) shown here",
            SettingsStatus.DescribeForEnvironment(read, EnvironmentB),
            StringComparison.Ordinal);

        string alone = SettingsStatus.DescribeForEnvironment(read, EnvironmentC);
        Assert.Contains("0 of 3 saved list(s) shown here", alone, StringComparison.Ordinal);
        Assert.Contains("they were saved for other environments", alone, StringComparison.Ordinal);

        // An unscoped connection sees all three and is told why, so isolation is never implied.
        string unscoped = SettingsStatus.DescribeForEnvironment(read, string.Empty);
        Assert.Contains("3 of 3 saved list(s) shown here", unscoped, StringComparison.Ordinal);
        Assert.Contains("not environment-scoped", unscoped, StringComparison.Ordinal);
    }
}
