using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The file discipline of the local settings store: the schema gate, the order of write operations, what
/// survives a crash, and the rule that a file this build cannot read is never silently replaced. Every
/// case runs against the fake file system so the interruption points are real and repeatable, instead of
/// a mock standing in for the logic under test.
/// </summary>
public class WatchlistStoreTests
{
    private const string FileName = SettingsLayout.DataFileName;
    private const string BackupName = FileName + ".bak";
    private const string PendingName = FileName + ".new";
    private const string Environment = FlowRef.Environment;

    private readonly FakeSettingsFiles _files = new FakeSettingsFiles();
    private readonly FixedClock _clock = new FixedClock();
    private readonly SettingsLayout _layout = new SettingsLayout("/settings/AutomationMonitor");
    private readonly SettingsFileStore _storage;
    private readonly JsonWatchlistStore _store;

    public WatchlistStoreTests()
    {
        _clock.Now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        _files.HasDirectory = true;
        _storage = new SettingsFileStore(_layout, _files, new DataContractJsonSettingsSerializer(), _clock);
        _store = new JsonWatchlistStore(_storage, _clock);
    }

    private static Watchlist List(string id, string name, WatchlistMembershipMode mode, DateTimeOffset? seenAtUtc = null)
    {
        WatchlistBuilder builder = new WatchlistBuilder(id, name, mode, null, Environment);
        builder.AddManualFlow(FlowRef.Create(id + "01", name + " flow"), seenAtUtc);
        return builder.Build();
    }

    private static WatchlistCatalog Catalog(params Watchlist[] watchlists)
    {
        return new WatchlistCatalog(watchlists, null, ApplicationSettings.Defaults);
    }

    private WatchlistStoreResult Save(WatchlistCatalog catalog)
    {
        return _store.Write(catalog);
    }

    [Fact]
    public void AFirstRunReadsNothingAndWritesNothing()
    {
        WatchlistStoreResult read = _store.Read();

        Assert.Equal(SettingsOutcome.CreatedDefault, read.Outcome);
        Assert.True(read.IsUsable);
        Assert.Empty(read.Catalog.Watchlists);
        Assert.Empty(_files.Operations);
        Assert.Empty(_files.ListFileNames());
    }

    [Fact]
    public void AWatchlistSurvivesSaveAndReload()
    {
        WatchlistCatalog catalog = new WatchlistCatalog(
            new[] { List("list-1", "Go-live", WatchlistMembershipMode.Manual, _clock.UtcNow) },
            "list-1",
            ApplicationSettings.Defaults);

        Assert.True(Save(catalog).IsUsable);
        WatchlistStoreResult reloaded = _store.Read();

        Assert.Equal(SettingsOutcome.Loaded, reloaded.Outcome);
        Watchlist list = Assert.Single(reloaded.Catalog.Watchlists);
        Assert.Equal("list-1", list.Id);
        Assert.Equal("Go-live", list.Name);
        Assert.Equal(WatchlistMembershipMode.Manual, list.Mode);
        Assert.Equal(Environment, list.EnvironmentId);
        WatchlistMember member = Assert.Single(list.Members);
        Assert.Equal("Go-live flow", member.Workflow.Name);
        Assert.True(member.IsIncluded);
        Assert.Equal(_clock.UtcNow, member.AddedAtUtc);
        Assert.Equal("list-1", reloaded.Catalog.ActiveWatchlistId);
    }

    [Fact]
    public void ASaveWritesThePendingFileThenTheBackupThenTheTarget()
    {
        Save(Catalog(List("list-1", "First", WatchlistMembershipMode.Snapshot)));
        _files.Operations.Clear();

        Save(Catalog(List("list-1", "Second", WatchlistMembershipMode.Snapshot)));

        Assert.Equal(
            new[] { "write:" + PendingName, "move:" + FileName + "->" + BackupName, "move:" + PendingName + "->" + FileName },
            new List<string>(_files.Operations).ToArray());
    }

    [Fact]
    public void ThePreviousContentIsKeptAsTheBackup()
    {
        Save(Catalog(List("list-1", "First", WatchlistMembershipMode.Snapshot)));
        string first = _files.ContentOf(FileName)!;

        WatchlistStoreResult second = Save(Catalog(List("list-1", "Second", WatchlistMembershipMode.Snapshot)));

        Assert.Equal(BackupName, second.BackupFileName);
        Assert.Equal(first, _files.ContentOf(BackupName));
        Assert.DoesNotContain("First", _files.ContentOf(FileName), StringComparison.Ordinal);
        Assert.Contains("Second", _files.ContentOf(FileName), StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteThatCannotStartLeavesTheExistingFileAlone()
    {
        Save(Catalog(List("list-1", "First", WatchlistMembershipMode.Snapshot)));
        string before = _files.ContentOf(FileName)!;
        _files.FailWrite = PendingName;

        WatchlistStoreResult failed = Save(Catalog(List("list-1", "Second", WatchlistMembershipMode.Snapshot)));

        Assert.Equal(SettingsOutcome.Failed, failed.Outcome);
        Assert.Equal(before, _files.ContentOf(FileName));
        Assert.False(_files.FileExists(BackupName));
    }

    [Fact]
    public void ABackupThatFailsLeavesTheOriginalInPlace()
    {
        Save(Catalog(List("list-1", "First", WatchlistMembershipMode.Snapshot)));
        string before = _files.ContentOf(FileName)!;
        _files.FailMoves.Add(FakeSettingsFiles.MovePair(FileName, BackupName));

        WatchlistStoreResult failed = Save(Catalog(List("list-1", "Second", WatchlistMembershipMode.Snapshot)));

        Assert.Equal(SettingsOutcome.Failed, failed.Outcome);
        Assert.Contains("Nothing was changed", failed.Reason, StringComparison.Ordinal);
        Assert.Equal(before, _files.ContentOf(FileName));
        Assert.False(_files.FileExists(PendingName));
    }

    [Fact]
    public void AFailedFinalMoveRestoresTheOriginalFromTheBackup()
    {
        Save(Catalog(List("list-1", "First", WatchlistMembershipMode.Snapshot)));
        string before = _files.ContentOf(FileName)!;
        _files.FailMoves.Add(FakeSettingsFiles.MovePair(PendingName, FileName));

        WatchlistStoreResult failed = Save(Catalog(List("list-1", "Second", WatchlistMembershipMode.Snapshot)));

        Assert.Equal(SettingsOutcome.Failed, failed.Outcome);
        Assert.Equal(before, _files.ContentOf(FileName));
        Assert.Contains(failed.Notes, note => note.Contains("previous copy was put back", StringComparison.Ordinal));
    }

    [Fact]
    public void ARestoreThatAlsoFailsSaysWhereThePreviousCopyStillIs()
    {
        Save(Catalog(List("list-1", "First", WatchlistMembershipMode.Snapshot)));
        _files.FailMoves.Add(FakeSettingsFiles.MovePair(PendingName, FileName));
        _files.FailMoves.Add(FakeSettingsFiles.MovePair(BackupName, FileName));

        WatchlistStoreResult failed = Save(Catalog(List("list-1", "Second", WatchlistMembershipMode.Snapshot)));

        Assert.Equal(SettingsOutcome.Failed, failed.Outcome);
        Assert.False(_files.FileExists(FileName));
        Assert.Contains(failed.Notes, note => note.Contains("still in " + BackupName, StringComparison.Ordinal));

        // The content is not gone: the next load finds that copy and says out loud that it recovered.
        _files.FailMoves.Clear();
        WatchlistStoreResult read = _store.Read();

        Assert.Equal(SettingsOutcome.Recovered, read.Outcome);
        Assert.Equal(BackupName, read.RecoveredFromFileName);
        Assert.Equal("First", read.Catalog.Watchlists[0].Name);
    }

    [Fact]
    public void ADirectoryThatCannotBeCreatedIsAFailureWithAReason()
    {
        _files.HasDirectory = false;
        _files.FailCreateDirectory = "the settings folder is read-only";

        WatchlistStoreResult failed = Save(Catalog(List("list-1", "First", WatchlistMembershipMode.Snapshot)));

        Assert.Equal(SettingsOutcome.Failed, failed.Outcome);
        Assert.Contains("read-only", failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ACorruptFileIsRefusedAndPreservedRatherThanReplaced()
    {
        const string corrupt = "{\"schemaVersion\":1,\"watchlists\":{\"items\":[{\"id\":\"list-1\",";
        _files.TryWrite(FileName, corrupt, out _);

        WatchlistStoreResult read = _store.Read();

        Assert.Equal(SettingsOutcome.RejectedCorrupt, read.Outcome);
        Assert.False(read.IsUsable);
        Assert.True(read.IsPreservedRefusal);
        Assert.Empty(read.Catalog.Watchlists);
        Assert.Contains(read.Notes, note => note.Contains("preserved as", StringComparison.Ordinal));
        Assert.Equal(corrupt, _files.ContentOf(SettingsLayout.QuarantineOf(FileName, _clock.UtcNow)));
    }

    [Fact]
    public void AnEmptyFileIsTreatedAsCorruptionNotAsNoSettings()
    {
        _files.TryWrite(FileName, string.Empty, out _);

        WatchlistStoreResult read = _store.Read();

        Assert.Equal(SettingsOutcome.RejectedCorrupt, read.Outcome);
        Assert.Contains("empty", read.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACorruptFileRecoversFromTheBackupAndSaysSo()
    {
        Save(Catalog(List("list-1", "Good", WatchlistMembershipMode.Snapshot)));
        Save(Catalog(List("list-1", "Better", WatchlistMembershipMode.Snapshot)));
        _files.TryWrite(FileName, "not json at all", out _);

        WatchlistStoreResult read = _store.Read();

        Assert.Equal(SettingsOutcome.Recovered, read.Outcome);
        Assert.Equal(BackupName, read.RecoveredFromFileName);
        Assert.Equal("Good", Assert.Single(read.Catalog.Watchlists).Name);
        Assert.Contains(read.Notes, note => note.Contains("preserved as", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingFileWithAGoodBackupIsRecoveredNotTreatedAsFirstRun()
    {
        Save(Catalog(List("list-1", "Committed", WatchlistMembershipMode.Snapshot)));

        // The crash point between moving the original to the backup and moving the pending file into
        // place: the name is gone and only the backup holds the data. Reading this as a first run would
        // leave the operator with an empty list and no explanation.
        Assert.True(_files.TryMove(FileName, BackupName, out _));

        WatchlistStoreResult recovered = _store.Read();

        Assert.Equal(SettingsOutcome.Recovered, recovered.Outcome);
        Assert.Equal(BackupName, recovered.RecoveredFromFileName);
        Assert.Equal("Committed", Assert.Single(recovered.Catalog.Watchlists).Name);
    }

    [Fact]
    public void AMissingFileWithAGoodPendingWriteIsRecovered()
    {
        Save(Catalog(List("list-1", "HalfWritten", WatchlistMembershipMode.Snapshot)));
        Assert.True(_files.TryMove(FileName, PendingName, out _));

        WatchlistStoreResult recovered = _store.Read();

        Assert.Equal(SettingsOutcome.Recovered, recovered.Outcome);
        Assert.Equal(PendingName, recovered.RecoveredFromFileName);
        Assert.Equal("HalfWritten", Assert.Single(recovered.Catalog.Watchlists).Name);
    }

    [Fact]
    public void AStalePendingFileIsIgnoredWhileTheRealFileStillReads()
    {
        Save(Catalog(List("list-1", "Committed", WatchlistMembershipMode.Snapshot)));
        _files.PlantInterruptedWrite(FileName, "garbage that never finished");

        WatchlistStoreResult read = _store.Read();

        Assert.Equal(SettingsOutcome.Loaded, read.Outcome);
        Assert.Equal("Committed", Assert.Single(read.Catalog.Watchlists).Name);
    }

    [Fact]
    public void AForeignJsonFileIsRefusedAndLeftWhereItIs()
    {
        // Another tool's file, complete and parseable, sitting in the settings folder. Read as an empty
        // store it would say "no watchlists", and the next save would delete somebody else's data - so
        // the discriminator has to refuse it, and the refusal has to leave the bytes exactly where they
        // are: nothing this build replaced, nothing this build needs to move out of the way.
        const string foreign = "{\"schemaVersion\":1,\"records\":[{\"id\":\"not-ours\"}]}";
        _files.TryWrite(FileName, foreign, out _);

        WatchlistStoreResult read = _store.Read();

        Assert.False(read.IsUsable);
        Assert.Equal(SettingsOutcome.RejectedCorrupt, read.Outcome);
        Assert.Contains("watchlist store", read.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(foreign, _files.ContentOf(FileName));

        // The refusal holds the door: an ordinary save may not write over what this build cannot read.
        Assert.Equal(SettingsOutcome.Failed, Save(Catalog(List("list-1", "Mine", WatchlistMembershipMode.Snapshot))).Outcome);
        Assert.Equal(foreign, _files.ContentOf(FileName));

        // Only the explicit override replaces it, and then the save behaves like any other.
        Assert.True(_store.ReplaceUnreadable(Catalog(List("list-1", "Mine", WatchlistMembershipMode.Snapshot))).IsUsable);
        Assert.Contains("Mine", _files.ContentOf(FileName), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileFromANewerVersionIsRefusedWholeAndLeftAlone()
    {
        Save(Catalog(List("list-1", "Ours", WatchlistMembershipMode.Snapshot)));
        string future = _files.ContentOf(FileName)!.Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
        Assert.True(_files.TryWrite(FileName, future, out _));

        WatchlistStoreResult read = _store.Read();

        Assert.Equal(SettingsOutcome.RejectedFutureVersion, read.Outcome);
        Assert.Contains("newer version", read.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(read.Catalog.Watchlists);
        Assert.Equal(future, _files.ContentOf(FileName));

        // The refusal is not a licence to write: a later save must not replace what it could not read.
        WatchlistStoreResult refused = Save(Catalog(List("list-2", "Mine", WatchlistMembershipMode.Manual)));
        Assert.Equal(SettingsOutcome.Failed, refused.Outcome);
        Assert.Equal(future, _files.ContentOf(FileName));
    }

    [Fact]
    public void ReplacingARefusedFileRequiresTheExplicitOverride()
    {
        Save(Catalog(List("list-1", "Ours", WatchlistMembershipMode.Snapshot)));
        Assert.True(_files.TryWrite(FileName, _files.ContentOf(FileName)!.Replace("\"schemaVersion\":1", "\"schemaVersion\":9"), out _));
        _store.Read();

        WatchlistStoreResult replaced = _store.ReplaceUnreadable(Catalog(List("list-2", "Mine", WatchlistMembershipMode.Manual)));

        Assert.Equal(SettingsOutcome.Saved, replaced.Outcome);
        Assert.Equal("Mine", Assert.Single(_store.Read().Catalog.Watchlists).Name);
    }

    [Fact]
    public void AFileTooOldToReadIsRefusedWithTheSupportedRange()
    {
        Assert.True(_files.TryWrite(FileName, "{\"schemaVersion\":0,\"kind\":\"watchlist-store\"}", out _));

        WatchlistStoreResult read = _store.Read();

        Assert.Equal(SettingsOutcome.RejectedOldVersion, read.Outcome);
        Assert.Contains("schema version 0", read.Reason, StringComparison.Ordinal);
        Assert.Contains("this build writes 1", read.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatCannotBeOpenedIsNotReportedAsEmpty()
    {
        Save(Catalog(List("list-1", "Ours", WatchlistMembershipMode.Snapshot)));
        _files.FailRead = FileName;

        WatchlistStoreResult read = _store.Read();

        Assert.Equal(SettingsOutcome.RejectedUnreadable, read.Outcome);
        Assert.Empty(read.Catalog.Watchlists);
        Assert.False(read.IsUsable);
    }

    [Fact]
    public void ClearingTheStoreKeepsTheNewestContentAndStaysCleared()
    {
        Save(Catalog(List("list-1", "First", WatchlistMembershipMode.Snapshot)));
        Save(Catalog(List("list-1", "Second", WatchlistMembershipMode.Snapshot)));
        string newest = _files.ContentOf(FileName)!;

        WatchlistStoreResult cleared = _store.Clear();

        Assert.Equal(SettingsOutcome.Saved, cleared.Outcome);
        Assert.False(_files.FileExists(FileName));

        // A reset is not a shredder. The content the operator last saved survives somewhere they can
        // open, and the result names it; the older generation is kept under its own name too rather than
        // overwritten by the newer one.
        Assert.NotNull(cleared.BackupFileName);
        Assert.Equal(newest, _files.ContentOf(cleared.BackupFileName!));
        Assert.Contains("Second", _files.ContentOf(cleared.BackupFileName!), StringComparison.Ordinal);
        Assert.Equal(2, CountNamesHolding(".displaced-"));

        // And nothing left behind resurrects what was just cleared: recovery only ever reads the backup
        // and the pending write, and both have been moved out of its way.
        Assert.False(_files.FileExists(BackupName));
        Assert.False(_files.FileExists(PendingName));
        WatchlistStoreResult after = _store.Read();
        Assert.Equal(SettingsOutcome.CreatedDefault, after.Outcome);
        Assert.Empty(after.Catalog.Watchlists);
    }

    private int CountNamesHolding(string fragment)
    {
        int matches = 0;
        foreach (string name in _files.ListFileNames())
        {
            if (name.Contains(fragment, StringComparison.Ordinal))
            {
                matches++;
            }
        }

        return matches;
    }

    [Fact]
    public void TheDescriptionNamesTheFileAndEngineButNoTokenOrPayload()
    {
        Assert.Contains(FileName, _store.Description, StringComparison.Ordinal);
        Assert.Contains("DataContractJsonSerializer", _store.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("token", _store.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StatusWordingNeverInventsASuccess()
    {
        Assert.Equal("CORRUPT AND PRESERVED", SettingsStatus.Label(SettingsOutcome.RejectedCorrupt));
        Assert.Equal("WRITTEN BY A NEWER VERSION", SettingsStatus.Label(SettingsOutcome.RejectedFutureVersion));
        Assert.Equal("RECOVERED FROM A COPY", SettingsStatus.Label(SettingsOutcome.Recovered));
        Assert.Equal("first run", SettingsStatus.Label(SettingsOutcome.CreatedDefault));

        Save(Catalog(List("list-1", "Ours", WatchlistMembershipMode.Snapshot)));
        WatchlistStoreResult read = _store.Read();
        string described = SettingsStatus.DescribeCatalog(read.Catalog, _storage.Load());
        Assert.Contains("1 saved watchlist(s)", described, StringComparison.Ordinal);
        Assert.Contains("loaded", described, StringComparison.Ordinal);
    }
}
