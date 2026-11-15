using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using AutomationMonitor.Plugin.Storage;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// AM-44.2 measured against real files on a real disk: an isolated copy of the shape the operator's legacy
/// watchlist file has - one list, 59 members, no environment recorded anywhere, 45 monitored and 14 excluded,
/// 8 added by hand. Scope text is identity, so every case asserts the stored bytes as well as the in-memory
/// state: opening must write nothing, a refusal must leave the file byte-identical, and the one change that
/// does move identity must be undoable from the backup the save keeps. The operator's own file is never touched;
/// each test gets a fresh directory under the temp path.
/// </summary>
public sealed class LegacyWatchlistAssociationFileTests : IDisposable
{
    private const string Organization = "77777777-7777-7777-7777-777777777777";
    private const string Qualified = "dataverse-organization:" + Organization;
    private const string Solution = "11111111-1111-1111-1111-111111111111";
    private const string ListId = "legacy-list";
    private const int MemberCount = 59;
    private const int ExcludedCount = 14;
    private const int HandAddedCount = 8;

    private static readonly DateTimeOffset Stamp = new DateTimeOffset(2026, 11, 15, 9, 45, 39, TimeSpan.Zero);
    private static readonly ResourceScope Connected = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));

    private readonly string _root;

    public LegacyWatchlistAssociationFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AutomationMonitor-LegacyAssociationTests", Guid.NewGuid().ToString("N"));
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

    private static string FlowId(int index)
    {
        return index.ToString("x8", CultureInfo.InvariantCulture) + "-2222-3333-4444-444444444444";
    }

    /// <summary>
    /// The legacy document: no scope on the list, none on any member, and the unscoped environment profile
    /// holding this list as its selection - exactly what an unverified 59-member file looks like.
    /// </summary>
    private static string LegacyDocument()
    {
        StringBuilder members = new StringBuilder();
        for (int i = 1; i <= MemberCount; i++)
        {
            if (i > 1)
            {
                members.Append(",\n          ");
            }

            bool included = i > ExcludedCount;
            bool handAdded = i > MemberCount - HandAddedCount;
            string origin = handAdded ? "manual" : "solution";

            members.Append("{'provider': 'powerautomate', 'scopeId': '', 'workflowId': '").Append(FlowId(i))
                .Append("', 'name': 'Legacy flow ").Append(i.ToString(CultureInfo.InvariantCulture)).Append("'")
                .Append(", 'state': 'enabled', 'stateCode': 1, 'included': ").Append(included ? "true" : "false")
                .Append(", 'addedAtUtc': '2026-11-01T08:00:00+00:00', 'lastSeenUtc': '2026-11-14T08:00:00+00:00'")
                .Append(", 'origins': ['").Append(origin).Append("'], 'solutionIds': ['").Append(Solution).Append("']}");
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
            + "    'environments': [ { 'environmentId': '', 'activeWatchlistId': '" + ListId + "',"
            + " 'autoRefresh': null, 'dateRange': '1h', 'lastUsedUtc': '2026-11-15T09:45:39+00:00' } ]\n"
            + "  },\n"
            + "  'watchlists': {\n"
            + "    'activeWatchlistId': '" + ListId + "',\n"
            + "    'items': [\n"
            + "      {\n"
            + "        'id': '" + ListId + "',\n"
            + "        'name': 'My production flows',\n"
            + "        'description': '',\n"
            + "        'environmentId': '',\n"
            + "        'membershipMode': 'snapshot',\n"
            + "        'createdAtUtc': '2026-11-01T08:00:00+00:00',\n"
            + "        'modifiedAtUtc': '2026-11-14T08:00:00+00:00',\n"
            + "        'lastMembershipRefreshUtc': '2026-11-14T08:00:00+00:00',\n"
            + "        'sourceSolutionIds': ['" + Solution + "'],\n"
            + "        'members': [\n          " + members + "\n        ],\n"
            + "        'coverageState': 'complete',\n"
            + "        'coverageNotes': ['Read 59 flow(s) from 1 solution.'],\n"
            + "        'preferences': { 'autoRefresh': null, 'dateRange': null, 'showExcludedMembers': null, 'thresholds': null }\n"
            + "      }\n"
            + "    ]\n"
            + "  }\n"
            + "}").Replace('\'', '"');
    }

    private string DataPath
    {
        get { return Path.Combine(_root, SettingsLayout.DataFileName); }
    }

    private string BackupPath
    {
        get { return Path.Combine(_root, SettingsLayout.DataFileName + ".bak"); }
    }

    /// <summary>Put an isolated copy of the legacy shape on disk and return the exact bytes it holds.</summary>
    private string WriteLegacyFile()
    {
        Directory.CreateDirectory(_root);
        string text = LegacyDocument();
        File.WriteAllText(DataPath, text);
        return text;
    }

    private WatchlistManager Open(ScopeResolutionSupport? support = null)
    {
        SettingsLayout layout = new SettingsLayout(_root);
        FixedClock clock = new FixedClock { Now = Stamp };
        SettingsFileStore storage = new SettingsFileStore(
            layout, new DiskSettingsFileAccess(layout), new DataContractJsonSettingsSerializer(), clock);
        return new WatchlistManager(new JsonWatchlistStore(storage, clock), clock, new GuidIdGenerator(), support);
    }

    private static WorkflowIdentityEvidence VerifiedEvidence()
    {
        WorkflowIdentityCounts counts = new WorkflowIdentityCounts(MemberCount, MemberCount, 0, MemberCount, 0, 0, 3, 3, MemberCount);
        return WorkflowIdentityEvidence.From(WorkflowIdentityEvidenceKind.Verified, Connected, counts, Stamp);
    }

    private static Watchlist OneList(WatchlistManager manager)
    {
        return Assert.Single(manager.Catalog.Watchlists);
    }

    [Fact]
    public void AFiftyNineMemberLegacyFileOpensBindsAndWritesNothing()
    {
        string bytes = WriteLegacyFile();

        WatchlistManager manager = Open();
        WatchlistStoreResult read = manager.Load();

        Assert.True(read.IsUsable);
        Watchlist list = OneList(manager);
        Assert.Equal(MemberCount, list.Members.Count);
        Assert.Equal(MemberCount - ExcludedCount, list.MonitoredFlowCount);
        Assert.Equal(string.Empty, list.EnvironmentId);
        Assert.All(list.Members, member => Assert.True(member.Key.Scope.IsUnverified));
        Assert.Equal("no verified environment", ResourceScopeLabel.DescribeStored(list.EnvironmentId));

        // AC-25: opening a list on any connection writes nothing - not a scope, not a re-save, not a backup.
        Assert.Equal(bytes, File.ReadAllText(DataPath));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void TheShippedBuildRefusesToReassignThisFileAndLeavesEveryByteAlone()
    {
        string bytes = WriteLegacyFile();

        WatchlistManager manager = Open();
        manager.Load();
        WatchlistAssociationPreview preview = manager.PreviewAssociation(ListId, VerifiedEvidence());
        WatchlistOperationResult result = manager.AssociateScope(ListId, preview);

        // The plan is real and readable, and applying it is deferred until AM-44.3 can resolve the scope.
        Assert.Equal(WatchlistAssociationDecision.Deferred, preview.Decision);
        Assert.Equal(MemberCount, preview.RekeyedCount);
        Assert.Equal(WatchlistOperationOutcome.AssociationDeferred, result.Outcome);
        Assert.False(manager.HasUnsavedChanges);
        Assert.Equal(bytes, File.ReadAllText(DataPath));
        Assert.Single(Directory.GetFiles(_root));
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public void AConfirmedAssociationRewritesTheFileAndKeepsTheLegacyBytesInTheBackup()
    {
        string bytes = WriteLegacyFile();

        WatchlistManager manager = Open(ScopeResolutionSupport.Resolvable());
        manager.Load();
        WatchlistAssociationPreview preview = manager.PreviewAssociation(ListId, VerifiedEvidence());
        Assert.True(preview.CanApply);

        Assert.Equal(WatchlistOperationOutcome.Applied, manager.AssociateScope(ListId, preview).Outcome);
        WatchlistStoreResult saved = manager.Save();

        Assert.True(saved.IsUsable);
        string onDisk = File.ReadAllText(DataPath);
        Assert.NotEqual(bytes, onDisk);
        Assert.Contains(Qualified, onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("\"scopeId\":\"\"", onDisk, StringComparison.Ordinal);
        Assert.Contains("My production flows", onDisk, StringComparison.Ordinal);

        // The save moved the previous file aside rather than replacing it: the legacy bytes survive in the
        // backup under their own name, which is the undo path the acceptance criteria ask for.
        Assert.True(File.Exists(BackupPath));
        Assert.Equal(bytes, File.ReadAllText(BackupPath));
    }

    [Fact]
    public void AnAssociatedFileReopensWithEveryMemberAndTheEnvironmentsSelection()
    {
        WriteLegacyFile();
        WatchlistManager first = Open(ScopeResolutionSupport.Resolvable());
        first.Load();
        Assert.Equal(
            WatchlistOperationOutcome.Applied,
            first.AssociateScope(ListId, first.PreviewAssociation(ListId, VerifiedEvidence())).Outcome);
        Assert.True(first.Save().IsUsable);

        WatchlistManager reopened = Open();
        WatchlistStoreResult read = reopened.Load();

        Assert.True(read.IsUsable);
        Watchlist list = OneList(reopened);
        Assert.Equal(Qualified, list.EnvironmentId);
        Assert.Equal("Dataverse organization 77777777", ResourceScopeLabel.DescribeStored(list.EnvironmentId));
        Assert.Equal(MemberCount, list.Members.Count);
        Assert.All(list.Members, member => Assert.Equal(Qualified, member.Key.ScopeId));

        // Nothing but identity moved: the exclusions, the mode and the coverage the list was saved with.
        Assert.Equal(MemberCount - ExcludedCount, list.MonitoredFlowCount);
        Assert.Equal(WatchlistMembershipMode.Snapshot, list.Mode);
        Assert.Equal("My production flows", list.Name);
        Assert.Equal(ListId, reopened.Catalog.Settings.ActiveWatchlistIdFor(Qualified));
    }

    [Fact]
    public void ADamagedAssociatedFileComesBackFromTheBackupAsTheUnscopedList()
    {
        string legacy = WriteLegacyFile();
        WatchlistManager first = Open(ScopeResolutionSupport.Resolvable());
        first.Load();
        first.AssociateScope(ListId, first.PreviewAssociation(ListId, VerifiedEvidence()));
        Assert.True(first.Save().IsUsable);
        Assert.Contains(Qualified, File.ReadAllText(DataPath), StringComparison.Ordinal);

        // Whatever happens to the rewritten file, the operator's legacy list is not lost: the store recovers
        // from the copy the save kept, and preserves the damaged bytes rather than shredding them.
        File.WriteAllText(DataPath, "{ this is not json");

        WatchlistManager reopened = Open();
        WatchlistStoreResult read = reopened.Load();

        Assert.True(read.IsUsable);
        Watchlist list = OneList(reopened);
        Assert.Equal(string.Empty, list.EnvironmentId);
        Assert.Equal(MemberCount, list.Members.Count);
        Assert.All(list.Members, member => Assert.True(member.Key.Scope.IsUnverified));
        Assert.Equal(legacy, File.ReadAllText(BackupPath));
        Assert.Contains(Directory.GetFiles(_root), file => Path.GetFileName(file).StartsWith("watchlists.corrupt-", StringComparison.Ordinal));
    }

    [Fact]
    public void AListAlreadyAssociatedWithThisOrganizationChangesNothingWhenAskedAgain()
    {
        WriteLegacyFile();
        WatchlistManager first = Open(ScopeResolutionSupport.Resolvable());
        first.Load();
        first.AssociateScope(ListId, first.PreviewAssociation(ListId, VerifiedEvidence()));
        Assert.True(first.Save().IsUsable);
        string associated = File.ReadAllText(DataPath);

        // Reopening with the shipped gate: an already-associated list reads as "no change", not as a deferral
        // and never as a second migration, so a repeat of the action is inert.
        WatchlistManager reopened = Open();
        reopened.Load();
        WatchlistAssociationPreview preview = reopened.PreviewAssociation(ListId, VerifiedEvidence());

        Assert.Equal(WatchlistAssociationDecision.NoChange, preview.Decision);
        Assert.Equal(WatchlistOperationOutcome.NoChange, reopened.AssociateScope(ListId, preview).Outcome);
        Assert.False(reopened.HasUnsavedChanges);
        Assert.Equal(associated, File.ReadAllText(DataPath));
    }
}
