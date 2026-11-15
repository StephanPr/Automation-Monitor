using System;
using System.IO;
using System.Linq;
using System.Text;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Services;
using AutomationMonitor.Plugin.Storage;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The resource-scope contract measured against real files on a real disk, through the same
/// <see cref="DiskSettingsFileAccess"/> the plugin ships. Scope text is identity: what the file holds is what a
/// member is keyed by, so these cases assert the stored characters as well as the in-memory state - unverified,
/// recognized and damaged must stay distinguishable in the bytes, not only in a model. That the file also survives
/// the previous build is the separate round trip recorded in docs/provider-capabilities.md.
/// </summary>
public sealed class WatchlistScopeFileTests : IDisposable
{
    private const string Organization = "77777777-7777-7777-7777-777777777777";
    private const string OtherOrganization = "88888888-8888-8888-8888-888888888888";
    private const string Qualified = "dataverse-organization:" + Organization;
    private const string Damaged = "dataverse-organization:not-a-guid";
    private const string SolutionId = "11111111-1111-1111-1111-111111111111";

    private static readonly DateTimeOffset Stamp = new DateTimeOffset(2026, 11, 15, 9, 45, 39, TimeSpan.Zero);

    /// <summary>One list, three members, each with the scope the case asks for.</summary>
    private static string Fixture(string environmentId, params string[] memberScopes)
    {
        return FixtureWrittenBy("powerautomate", environmentId, memberScopes);
    }

    /// <summary>The same fixture with a provider token of the caller's choosing - including one to come.</summary>
    private static string FixtureWrittenBy(string provider, string environmentId, params string[] memberScopes)
    {
        StringBuilder members = new StringBuilder();
        string[] names = { "Fixture flow one", "Fixture flow two", "Fixture flow three" };
        string[] workflowIds =
        {
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333",
            "44444444-4444-4444-4444-444444444444",
        };
        for (int i = 0; i < memberScopes.Length; i++)
        {
            if (i > 0)
            {
                members.Append(",\n          ");
            }

            members.Append("{'provider': '").Append(provider).Append("', 'scopeId': '").Append(memberScopes[i])
                .Append("', 'workflowId': '").Append(workflowIds[i])
                .Append("', 'name': '").Append(names[i])
                .Append("', 'state': 'enabled', 'stateCode': 1, 'included': true,")
                .Append(" 'addedAtUtc': '2026-11-15T09:45:39.4431211+00:00',")
                .Append(" 'lastSeenUtc': '2026-11-15T09:45:39.4431211+00:00',")
                .Append(" 'origins': ['solution'], 'solutionIds': ['").Append(SolutionId).Append("']}");
        }

        return ("{\n"
            + "  'schemaVersion': 1,\n"
            + "  'kind': 'watchlist-store',\n"
            + "  'savedAtUtc': '2026-11-15T09:45:39.4492594+00:00',\n"
            + "  'writtenBy': 'Automation Monitor for XrmToolBox',\n"
            + "  'app': {\n"
            + "    'activeEnvironmentId': '',\n"
            + "    'defaultAutoRefresh': 'off',\n"
            + "    'defaultDateRange': '1h',\n"
            + "    'defaultShowExcludedMembers': true,\n"
            + "    'defaultThresholds': null,\n"
            + "    'environments': []\n"
            + "  },\n"
            + "  'watchlists': {\n"
            + "    'activeWatchlistId': 'fixture-list',\n"
            + "    'items': [\n"
            + "      {\n"
            + "        'id': 'fixture-list',\n"
            + "        'name': 'Fixture watchlist',\n"
            + "        'description': '',\n"
            + "        'environmentId': '" + environmentId + "',\n"
            + "        'membershipMode': 'snapshot',\n"
            + "        'createdAtUtc': '2026-11-15T09:45:39.4411146+00:00',\n"
            + "        'modifiedAtUtc': '2026-11-15T09:45:39.4492594+00:00',\n"
            + "        'lastMembershipRefreshUtc': null,\n"
            + "        'sourceSolutionIds': ['" + SolutionId + "'],\n"
            + "        'members': [\n          " + members + "\n        ],\n"
            + "        'coverageState': 'complete',\n"
            + "        'coverageNotes': ['Read 3 flow(s) from 1 solution.'],\n"
            + "        'preferences': { 'autoRefresh': null, 'dateRange': null, 'showExcludedMembers': null, 'thresholds': null }\n"
            + "      }\n"
            + "    ]\n"
            + "  }\n"
            + "}").Replace('\'', '"');
    }

    private readonly string _root;

    public WatchlistScopeFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AutomationMonitor-ScopeFileTests", Guid.NewGuid().ToString("N"));
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

    private string DataPath
    {
        get { return Path.Combine(_root, SettingsLayout.DataFileName); }
    }

    private void WriteFixture(string json)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(DataPath, json);
    }

    private static Watchlist OneList(WatchlistManager manager)
    {
        return Assert.Single(manager.Catalog.Watchlists);
    }

    [Fact]
    public void APopulatedUnscopedFileOpensAsUnverifiedAndWritesNothing()
    {
        WriteFixture(Fixture(string.Empty, string.Empty, string.Empty, string.Empty));
        string before = File.ReadAllText(DataPath);

        WatchlistManager reopened = Open(_root);
        WatchlistStoreResult read = reopened.Load();

        Assert.True(read.IsUsable);
        Watchlist list = OneList(reopened);
        Assert.Equal(3, list.Members.Count);
        Assert.All(list.Members, member => Assert.True(member.Key.Scope.IsUnverified));
        Assert.All(list.Members, member => Assert.Equal(string.Empty, member.Key.ScopeId));
        Assert.False(reopened.Catalog.HasUnrepresentedMembers);
        Assert.False(reopened.BlocksSave);

        // Opening a file writes nothing. The bytes the operator has are the bytes they had.
        Assert.Equal(before, File.ReadAllText(DataPath));
    }

    [Fact]
    public void AnUnscopedMemberIsStillUnscopedWhenAQualifiedConnectionOpensTheFile()
    {
        WriteFixture(Fixture(string.Empty, string.Empty, string.Empty, string.Empty));

        WatchlistManager reopened = Open(_root);
        reopened.Load();

        // A connection's identity is never poured into an empty scope: the list stays visible to this connection
        // and each member keeps the scope the file gave it.
        Assert.Single(reopened.Lists(Qualified));
        Assert.All(OneList(reopened).Members, member => Assert.Equal(string.Empty, member.Key.ScopeId));
    }

    [Fact]
    public void EverySpellingOfOneOrganizationIsReadAsOneResourceScope()
    {
        WriteFixture(Fixture(
            Qualified,
            Qualified,
            "DATAVERSE-ORGANIZATION:{" + Organization.ToUpperInvariant() + "}",
            "dataverse-organization:" + Organization.Replace("-", string.Empty).ToUpperInvariant()));

        WatchlistManager reopened = Open(_root);
        WatchlistStoreResult read = reopened.Load();

        Assert.True(read.IsUsable);
        Watchlist list = OneList(reopened);
        Assert.Equal(3, list.Members.Count);
        Assert.False(reopened.Catalog.HasUnrepresentedMembers);
        Assert.All(list.Members, member => Assert.Equal(Qualified, member.Key.ScopeId));
        // Two spellings of one organization are one resource, proved with the identity comparison the pipeline
        // already uses for merge and de-duplication - never with a name or a looser match.
        WorkflowKey canonical = WorkflowKey.Create(
            ProviderKind.PowerAutomate, Qualified, "22222222-2222-2222-2222-222222222222");
        WorkflowKey braced = WorkflowKey.Create(
            ProviderKind.PowerAutomate,
            "DATAVERSE-ORGANIZATION:{" + Organization.ToUpperInvariant() + "}",
            "22222222-2222-2222-2222-222222222222");
        Assert.Equal(canonical, braced);
        Assert.Equal(canonical.ScopeId, list.Members[1].Key.ScopeId);
        Assert.Equal(
            ResourceScopeLabel.Fingerprint(list.Members[0].Key.Scope.Identifier),
            ResourceScopeLabel.Fingerprint(list.Members[2].Key.Scope.Identifier));

        // The list's own environment line is qualified too, so the connection that reports the same organization
        // sees it and a different organization does not.
        Assert.Equal(Qualified, list.EnvironmentId);
        Assert.Single(reopened.Lists(Qualified));
        Assert.Empty(reopened.Lists("dataverse-organization:" + OtherOrganization));
    }

    [Fact]
    public void ASaveWritesTheQualifiedScopeToEveryMemberAndTheNextManagerReadsIt()
    {
        WriteFixture(Fixture(Qualified, Qualified, Qualified, Qualified));

        WatchlistManager first = Open(_root);
        first.Load();
        WatchlistStoreResult written = first.Save();

        Assert.True(written.IsUsable);
        string onDisk = File.ReadAllText(DataPath);
        Assert.Equal(4, Count(onDisk, Qualified));
        Assert.Contains("22222222-2222-2222-2222-222222222222", onDisk, StringComparison.Ordinal);
        Assert.Contains("44444444-4444-4444-4444-444444444444", onDisk, StringComparison.Ordinal);

        WatchlistManager reopened = Open(_root);
        reopened.Load();
        Assert.All(OneList(reopened).Members, member => Assert.Equal(Qualified, member.Key.ScopeId));
    }

    [Fact]
    public void ADamagedQualifiedScopeSurvivesTheRewriteAndIsNeverCalledAnEmptyScope()
    {
        WriteFixture(Fixture(string.Empty, Damaged, string.Empty, string.Empty));

        WatchlistManager reopened = Open(_root);
        WatchlistStoreResult read = reopened.Load();

        Assert.True(read.IsUsable);
        Watchlist list = OneList(reopened);
        Assert.Equal(3, list.Members.Count);
        Assert.Equal(Damaged, list.Members[0].Key.ScopeId);
        Assert.Equal(ResourceScopeState.Invalid, list.Members[0].Key.Scope.State);

        // A scope this build cannot interpret is held in memory as the text it came in, so rewriting the file
        // loses nothing - which is why it does not block a save the way an unknown provider does. It is still
        // said out loud, and never shown as though the member had no environment.
        Assert.False(reopened.Catalog.HasUnrepresentedMembers);
        Assert.Contains(read.Notes, note => note.Contains("kept exactly as stored", StringComparison.Ordinal));
        Assert.DoesNotContain(
            "no verified environment",
            ResourceScopeLabel.Describe(list.Members[0].Key.Scope),
            StringComparison.Ordinal);

        Assert.True(reopened.Save().IsUsable);
        Assert.Contains(Damaged, File.ReadAllText(DataPath), StringComparison.Ordinal);

        WatchlistManager again = Open(_root);
        again.Load();
        Assert.Equal(Damaged, OneList(again).Members[0].Key.ScopeId);
    }

    [Fact]
    public void AMemberThisBuildCannotRebuildStopsTheRewriteAndLeavesTheBytesAlone()
    {
        WriteFixture(FixtureWrittenBy(
            "logicapps-standard-v2", string.Empty, string.Empty, string.Empty, string.Empty));
        string before = File.ReadAllText(DataPath);

        WatchlistManager reopened = Open(_root);
        WatchlistStoreResult read = reopened.Load();

        // Every member is one this build cannot rebuild: the list looks empty in memory while the file still
        // holds three. That is exactly the state a save must not be allowed to act on.
        Assert.True(read.IsUsable);
        Assert.Empty(OneList(reopened).Members);
        Assert.True(reopened.Catalog.HasUnrepresentedMembers);
        Assert.True(reopened.BlocksSave);

        WatchlistStoreResult refused = reopened.Save();
        Assert.False(refused.IsUsable);
        Assert.Equal(before, File.ReadAllText(DataPath));

        // The reason says what cannot be rewritten and that nothing was lost - the file is the operator's.
        Assert.Contains("cannot read", refused.Describe(), StringComparison.Ordinal);
        Assert.Contains("Nothing has been lost", refused.Describe(), StringComparison.Ordinal);
    }

    private static int Count(string haystack, string needle)
    {
        return haystack.Split(new[] { needle }, StringSplitOptions.None).Length - 1;
    }
}
