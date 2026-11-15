using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// What happens when the saved file holds something this build cannot represent. The rule under test is a
/// data-loss rule: a member that cannot be read must not be quietly dropped and then written away by the next
/// save. Half-read content stays on disk untouched, the operator is told what is blocked and why, and the only
/// way through is the explicit replace they chose themselves. Scope values are the other half of the story:
/// a value this build cannot classify is written back exactly as it came in.
/// </summary>
public class UnrepresentableContentTests
{
    private const string FileName = SettingsLayout.DataFileName;
    private const string Organization = "3f9c1a2b-3d4e-4f50-9a8b-1c2d3e4f5a6b";
    private const string Solution = "55555555-1111-1111-1111-111111111111";

    private readonly FakeSettingsFiles _files = new FakeSettingsFiles();
    private readonly FixedClock _clock = new FixedClock();
    private readonly SettingsLayout _layout = new SettingsLayout("/settings/AutomationMonitor");
    private readonly JsonWatchlistStore _store;
    private readonly WatchlistManager _manager;

    public UnrepresentableContentTests()
    {
        _clock.Now = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
        _files.HasDirectory = true;
        SettingsFileStore storage = new SettingsFileStore(
            _layout, _files, new DataContractJsonSettingsSerializer(), _clock);
        _store = new JsonWatchlistStore(storage, _clock);
        _manager = new WatchlistManager(_store, _clock, new FixedIdGenerator());
    }

    private static string Member(
        string provider,
        string scopeId,
        string workflowId,
        bool included = true,
        string origin = "solution")
    {
        return "{\"provider\":\"" + provider + "\",\"scopeId\":\"" + scopeId + "\",\"workflowId\":\"" + workflowId
            + "\",\"name\":\"Flow " + workflowId + "\",\"state\":\"enabled\",\"stateCode\":1,\"included\":"
            + (included ? "true" : "false")
            + ",\"addedAtUtc\":\"2026-11-01T08:00:00+00:00\",\"lastSeenUtc\":\"2026-11-14T08:00:00+00:00\""
            + ",\"origins\":[\"" + origin + "\"],\"solutionIds\":[\"" + Solution + "\"]}";
    }

    private static string Watchlist(string id, string name, string environmentId, string members)
    {
        return "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"environmentId\":\"" + environmentId
            + "\",\"membershipMode\":\"dynamic\",\"coverageState\":\"complete\",\"sourceSolutionIds\":[\"" + Solution + "\"]"
            + ",\"createdAtUtc\":\"2026-11-01T08:00:00+00:00\",\"modifiedAtUtc\":\"2026-11-14T08:00:00+00:00\""
            + ",\"members\":[" + members + "]}";
    }

    private static string Document(string watchlists, string? activeWatchlistId = "list-1")
    {
        string active = activeWatchlistId == null ? "null" : "\"" + activeWatchlistId + "\"";
        return "{\"schemaVersion\":1,\"kind\":\"watchlist-store\",\"savedAtUtc\":\"2026-11-14T09:00:00+00:00\""
            + ",\"writtenBy\":\"an earlier build\",\"watchlists\":{\"activeWatchlistId\":" + active
            + ",\"items\":[" + watchlists + "]}}";
    }

    private void Plant(string json)
    {
        Assert.True(_files.TryWrite(FileName, json, out string? reason), reason);
        _files.Operations.Clear();
    }

    private static string Workflow(int index)
    {
        return "aaaaaaaa-0000-0000-0000-" + index.ToString("000000000000");
    }

    [Fact]
    public void AnUnknownProviderIsSkippedForReadingButNeverSavedAway()
    {
        Plant(Document(Watchlist(
            "list-1",
            "Go-live",
            string.Empty,
            Member("powerautomate", string.Empty, Workflow(1)) + ","
            + Member("logicapps-standard-v2", string.Empty, Workflow(2)) + ","
            + Member("powerautomate", string.Empty, Workflow(3)))));
        string stored = _files.ContentOf(FileName)!;

        WatchlistStoreResult read = _store.Read();

        // The list is still usable: two of three members are shown rather than nothing.
        Assert.True(read.IsUsable);
        Watchlist list = Assert.Single(read.Catalog.Watchlists);
        Assert.Equal(2, list.Members.Count);
        Assert.True(read.Catalog.HasUnrepresentedMembers);
        Assert.Contains(read.Notes, note => note.Contains("logicapps-standard-v2", StringComparison.Ordinal));
        Assert.Contains(read.Notes, note => note.Contains("saving is blocked", StringComparison.Ordinal));

        WatchlistStoreResult written = _store.Write(read.Catalog);

        Assert.False(written.IsUsable);
        Assert.Contains("cannot read", written.Reason, StringComparison.Ordinal);
        Assert.Contains("Nothing has been lost", written.Reason, StringComparison.Ordinal);
        Assert.Equal(stored, _files.ContentOf(FileName));

        // Refusing a save writes nothing at all: no replacement, no backup, no quarantine copy.
        Assert.Equal(new[] { FileName }, _files.ListFileNames());
    }

    [Fact]
    public void APartialReadBlocksEveryMutationOfEveryList()
    {
        Plant(Document(
            Watchlist("list-1", "Go-live", string.Empty, Member("powerautomate", string.Empty, Workflow(1))
                + "," + Member("quantum-flow", string.Empty, Workflow(2)))
            + "," + Watchlist("list-2", "Other", string.Empty, Member("powerautomate", string.Empty, Workflow(3)))));
        WatchlistStoreResult read = _manager.Load();

        Assert.True(read.IsUsable);
        Assert.True(_manager.BlocksSave);
        Assert.Equal(2, _manager.Catalog.Count);

        Assert.Equal(WatchlistOperationOutcome.UnreadableContent, _manager.Rename("list-2", "Renamed").Outcome);
        Assert.Equal(WatchlistOperationOutcome.UnreadableContent, _manager.Delete("list-2").Outcome);
        Assert.Equal(
            WatchlistOperationOutcome.UnreadableContent,
            _manager.AddWorkflow("list-2", FlowRef.Create(Workflow(9), "Hand added")).Outcome);
        Assert.Equal(
            WatchlistOperationOutcome.UnreadableContent,
            _manager.Create("Fresh", WatchlistMembershipMode.Manual, null).Outcome);
        Assert.False(_manager.HasUnsavedChanges);

        // Deleting the list that carries the unreadable member does not clear the mark: the file still holds
        // that member, and a save would remove it.
        Assert.True(_manager.Catalog.Without("list-1").HasUnrepresentedMembers);
        Assert.Contains("cannot read", _manager.Rename("list-2", "Renamed").Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void APartialReadStillLeavesTheFileExactlyAsItWas()
    {
        Plant(Document(Watchlist(
            "list-1", "Go-live", string.Empty, Member("quantum-flow", string.Empty, Workflow(2)))));
        string stored = _files.ContentOf(FileName)!;

        _manager.Load();

        Assert.Equal(stored, _files.ContentOf(FileName));
        Assert.Equal(new[] { FileName }, _files.ListFileNames());
    }

    [Fact]
    public void AScopeOfAnUnknownKindIsWrittenBackAndBlocksNothing()
    {
        string unknown = "powerplatform-environment:" + Organization;
        Plant(Document(Watchlist("list-1", "Go-live", unknown,
            Member("powerautomate", unknown, Workflow(1)) + "," + Member("powerautomate", string.Empty, Workflow(2)))));

        WatchlistStoreResult read = _store.Read();

        // Preserved content is not lost content: the value came back, so the file may still be written.
        Assert.True(read.IsUsable);
        Watchlist list = Assert.Single(read.Catalog.Watchlists);
        Assert.Equal(2, list.Members.Count);
        Assert.False(read.Catalog.HasUnrepresentedMembers);
        Assert.Equal(unknown, list.EnvironmentId);
        Assert.Equal(unknown, list.Members[0].Key.ScopeId);
        Assert.Equal(ResourceScopeState.UnrecognizedKind, list.Members[0].Key.Scope.State);
        Assert.Contains(read.Notes, note => note.Contains("kept exactly as stored", StringComparison.Ordinal));

        Assert.True(_store.Write(read.Catalog).IsUsable);
        Watchlist again = Assert.Single(_store.Read().Catalog.Watchlists);
        Assert.Equal(unknown, again.Members[0].Key.ScopeId);
    }

    [Fact]
    public void ADamagedScopeIsPreservedRatherThanEmptied()
    {
        string damaged = "dataverse-organization:not-a-guid";
        Plant(Document(Watchlist("list-1", "Go-live", string.Empty, Member("powerautomate", damaged, Workflow(1)))));

        WatchlistStoreResult read = _store.Read();
        Watchlist list = Assert.Single(read.Catalog.Watchlists);

        Assert.Equal(damaged, list.Members[0].Key.ScopeId);
        Assert.Equal(ResourceScopeState.Invalid, list.Members[0].Key.Scope.State);
        Assert.True(_store.Write(read.Catalog).IsUsable);
        Assert.Contains(damaged, _files.ContentOf(FileName)!, StringComparison.Ordinal);
    }

    [Fact]
    public void AQualifiedScopeRoundTripsInOneCanonicalSpelling()
    {
        Plant(Document(Watchlist(
            "list-1", "Go-live", string.Empty,
            Member("powerautomate", "dataverse-organization:{" + Organization.ToUpperInvariant() + "}", Workflow(1))
            + "," + Member("powerautomate", "dataverse-organization:" + Organization.Replace("-", string.Empty), Workflow(2)))));

        WatchlistStoreResult read = _store.Read();
        Watchlist list = Assert.Single(read.Catalog.Watchlists);
        string canonical = "dataverse-organization:" + Organization;

        Assert.All(list.Members, member => Assert.Equal(canonical, member.Key.ScopeId));
        Assert.All(list.Members, member => Assert.True(member.Key.Scope.IsQualified));
        Assert.False(read.Catalog.HasUnrepresentedMembers);
        Assert.True(_store.Write(read.Catalog).IsUsable);
        Assert.Equal(2, Assert.Single(_store.Read().Catalog.Watchlists).Members.Count);
    }

    [Fact]
    public void AnExplicitReplaceIsStillTheOperatorsOwnDecision()
    {
        Plant(Document(Watchlist(
            "list-1", "Go-live", string.Empty, Member("powerautomate", string.Empty, Workflow(1))
            + "," + Member("quantum-flow", string.Empty, Workflow(2)))));
        WatchlistStoreResult read = _store.Read();

        Assert.False(_store.Write(read.Catalog).IsUsable);

        // The override exists for the "I mean it, discard what cannot be read" action, and nothing else
        // reaches it: the ordinary save path stays refused.
        Assert.True(_store.ReplaceUnreadable(read.Catalog).IsUsable);
        Assert.DoesNotContain("quantum-flow", _files.ContentOf(FileName)!, StringComparison.Ordinal);
    }

    [Fact]
    public void APartialReadDoesNotWeakenTheResetThatKeepsTheOldFile()
    {
        Plant(Document(Watchlist("list-1", "Go-live", string.Empty, Member("quantum-flow", string.Empty, Workflow(1)))));
        _store.Read();

        WatchlistStoreResult cleared = _store.Clear();

        Assert.True(cleared.IsUsable);
        Assert.Contains(
            _files.ListFileNames(),
            name => name.Contains("watchlists", StringComparison.Ordinal) && name.Contains("displaced", StringComparison.Ordinal));
    }

    [Fact]
    public void ALegacyUnscopedFiftyNineMemberListOpensWithoutLosingOrMovingAnything()
    {
        Plant(LegacyFiftyNineFile());
        string stored = _files.ContentOf(FileName)!;

        WatchlistStoreResult read = _manager.Load();
        Watchlist list = Assert.Single(_manager.Catalog.Watchlists);

        Assert.True(read.IsUsable);
        Assert.Equal(59, list.Members.Count);
        Assert.Equal(59, list.Members.Count(member => member.Key.ScopeId.Length == 0));
        Assert.Equal(45, list.Members.Count(member => member.IsIncluded));
        Assert.Equal(8, list.Members.Count(member => member.Source.HasFlag(WatchlistMemberSource.Manual)));
        Assert.Equal(51, list.Members.Count(member => member.Source.HasFlag(WatchlistMemberSource.Solution)));
        Assert.Equal(string.Empty, list.EnvironmentId);
        Assert.False(_manager.Catalog.HasUnrepresentedMembers);
        Assert.False(_manager.BlocksSave);
        Assert.Empty(read.Notes);

        // Opening a list is not an association: a connection being open must not move a stored identity, and
        // reading the file must not rewrite it.
        Assert.Equal(stored, _files.ContentOf(FileName));

        Assert.True(_manager.Save().IsUsable);
        Watchlist reopened = Assert.Single(_store.Read().Catalog.Watchlists);
        Assert.Equal(59, reopened.Members.Count);
        Assert.Equal(
            list.Members.Select(member => member.Key.Canonical),
            reopened.Members.Select(member => member.Key.Canonical));
        Assert.Equal(
            list.Members.Select(member => member.IsIncluded),
            reopened.Members.Select(member => member.IsIncluded));
        Assert.Equal(
            list.Members.Select(member => member.AddedAtUtc),
            reopened.Members.Select(member => member.AddedAtUtc));
        Assert.Equal(
            list.Members.Select(member => member.LastSeenUtc),
            reopened.Members.Select(member => member.LastSeenUtc));
        Assert.All(reopened.Members, member => Assert.NotEqual(WatchlistMemberSource.None, member.Source));
        Assert.All(reopened.Members, member => Assert.Equal(string.Empty, member.Key.ScopeId));
    }

    [Fact]
    public void ALegacyListPlusOneFutureMemberIsShownAndProtected()
    {
        Plant(LegacyFiftyNineFile(extraUnreadable: true));
        string stored = _files.ContentOf(FileName)!;

        WatchlistStoreResult read = _manager.Load();
        Watchlist list = Assert.Single(_manager.Catalog.Watchlists);

        // Sixty stored, fifty-nine shown, and nothing written: the readable half is useful, the unreadable
        // half is the operator's, and a save would take it away.
        Assert.True(read.IsUsable);
        Assert.Equal(59, list.Members.Count);
        Assert.True(_manager.Catalog.HasUnrepresentedMembers);
        Assert.True(_manager.BlocksSave);
        Assert.False(_manager.Save().IsUsable);
        Assert.Equal(stored, _files.ContentOf(FileName));
    }

    /// <summary>
    /// The shape of the operator's real file: one dynamic list, no environment recorded, and 59 members with
    /// empty scopes, mixed inclusion and mixed provenance. Built rather than embedded so the counts are
    /// inspectable, and never pointed at the real watchlists.json.
    /// </summary>
    private static string LegacyFiftyNineFile(bool extraUnreadable = false)
    {
        StringBuilder members = new StringBuilder();
        for (int index = 1; index <= 59; index++)
        {
            if (members.Length > 0)
            {
                members.Append(',');
            }

            // Every fourth member is excluded and every seventh arrived by hand: a fixture with one shape
            // would prove one rule and let a second one slip.
            members.Append(Member(
                "powerautomate",
                string.Empty,
                Workflow(index),
                included: index % 4 != 0,
                origin: index % 7 == 0 ? "manual" : "solution"));
        }

        if (extraUnreadable)
        {
            members.Append(',').Append(Member("flowpix-9", string.Empty, Workflow(60)));
        }

        return Document(Watchlist("list-1", "Go-live flows", string.Empty, members.ToString()));
    }
}

