using System;
using System.IO;
using System.Linq;
using System.Text;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Services;
using AutomationMonitor.Plugin.Storage;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The disk. Everything above the settings seam is already tested against an in-memory fake; these
/// cases ask the question that fake cannot answer - what does a real file system do to the contract -
/// and they do it on the exact classes that ship inside the plugin assembly (linked in by the test
/// project, which cannot reference net48 otherwise).
///
/// <para>
/// Each test works in a folder of its own under the system temp directory and removes it on disposal,
/// so nothing here can reach a settings directory a person owns. The full-stack cases compose the real
/// store, the real JSON engine and the real manager, so a byte written by one test is a byte the plugin
/// would really have written.
/// </para>
/// </summary>
public sealed class DiskSettingsFileAccessTests : IDisposable
{
    private const string FileName = SettingsLayout.DataFileName;
    private const string BackupName = FileName + ".bak";
    private const string PendingName = FileName + ".new";
    private const string FlowId = "af000000-0000-0000-0000-0000000000af";
    private const string EnvironmentA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string EnvironmentB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private static readonly DateTimeOffset FixedStamp = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root;

    public DiskSettingsFileAccessTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AutomationMonitor-DiskTests", Guid.NewGuid().ToString("N"));
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
            // A temp folder the OS holds for a moment is not a test failure; the test already asserted
            // what it came to assert, and temp folders are the OS's to reap.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private DiskSettingsFileAccess Files()
    {
        return new DiskSettingsFileAccess(new SettingsLayout(_root));
    }

    /// <summary>A fresh stack over the same folder - in memory terms, closing the tab and opening it again.</summary>
    private JsonWatchlistStore Store()
    {
        SettingsLayout layout = new SettingsLayout(_root);
        return new JsonWatchlistStore(
            new SettingsFileStore(layout, new DiskSettingsFileAccess(layout), new DataContractJsonSettingsSerializer(), new SystemClock()),
            new SystemClock());
    }

    private WatchlistManager Manager(JsonWatchlistStore? store = null)
    {
        return new WatchlistManager(store ?? Store(), new SystemClock(), new GuidIdGenerator());
    }

    private static WatchlistCatalog CatalogOf(string name)
    {
        WatchlistBuilder builder = new WatchlistBuilder("list-1", name, WatchlistMembershipMode.Snapshot, null, FlowRef.Environment);
        builder.AddManualFlow(FlowRef.Create(FlowId, "Nightly approval"), FixedStamp);
        return new WatchlistCatalog(new[] { builder.Build() }, "list-1", ApplicationSettings.Defaults);
    }

    [Fact]
    public void TheFolderIsNotThereUntilSomethingAsksForIt()
    {
        DiskSettingsFileAccess files = Files();

        Assert.False(Directory.Exists(_root));
        Assert.False(files.DirectoryExists());
        Assert.False(files.FileExists(FileName));

        Assert.True(files.TryCreateDirectory(out string? reason));
        Assert.Null(reason);
        Assert.True(files.DirectoryExists());
        Assert.False(files.FileExists(FileName));

        // Creating the folder created nothing else: no file, no backup, no scratch name.
        Assert.Empty(files.ListFileNames());
    }

    [Fact]
    public void ADirectoryThatCannotBeCreatedIsAReasonAndNotACrash()
    {
        // A file wearing the name the directory needs is exactly what a half-done install or a stray
        // export leaves behind. The seam has to answer with a reason, because the store turns it into a
        // message instead of an exception.
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "occupied"), "not a folder");
        DiskSettingsFileAccess files = new DiskSettingsFileAccess(new SettingsLayout(Path.Combine(_root, "occupied", SettingsLayout.DirectoryName)));

        Assert.False(files.TryCreateDirectory(out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.False(files.DirectoryExists());
    }

    [Fact]
    public void AMissingFileIsRefusedAndNamed()
    {
        DiskSettingsFileAccess files = Files();
        Assert.True(files.TryCreateDirectory(out _));

        bool read = files.TryRead(FileName, out string? content, out string? reason);

        Assert.False(read);
        Assert.Null(content);
        Assert.Contains(FileName, reason!, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("..")]
    [InlineData("../outside.json")]
    [InlineData("sub/outside.json")]
    [InlineData("C:outside.json")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    public void ANameThatIsNotAPlainFileNeverReachesTheDisk(string name)
    {
        // The backslash forms are unsafe on Windows, where the plugin runs, but on a Linux runner a
        // backslash is an ordinary file-name character, so they are asserted where SettingsLayout owns
        // the rule instead of here. Every name in this table is refused on both file systems.
        DiskSettingsFileAccess files = Files();
        Assert.True(files.TryCreateDirectory(out _));

        Assert.False(files.TryWrite(name, "escaped", out string? writeReason));
        Assert.False(string.IsNullOrWhiteSpace(writeReason));
        Assert.False(files.TryRead(name, out _, out string? readReason));
        Assert.False(string.IsNullOrWhiteSpace(readReason));
        Assert.False(files.TryDelete(name, out string? deleteReason));
        Assert.False(string.IsNullOrWhiteSpace(deleteReason));
        Assert.False(files.FileExists(name));
        Assert.Empty(files.ListFileNames());
    }

    [Fact]
    public void WrittenTextComesBackExactlyAsItWasWritten()
    {
        DiskSettingsFileAccess files = Files();
        const string text = "{\"watchlists\":\"Prüfen 测量 – dash\"}";
        Assert.True(files.TryCreateDirectory(out _));

        Assert.True(files.TryWrite(FileName, text, out string? writeReason));
        Assert.Null(writeReason);
        Assert.True(files.TryRead(FileName, out string? content, out string? readReason));
        Assert.Null(readReason);
        Assert.Equal(text, content);

        // No byte-order mark: the file must stay readable by anything that expects plain UTF-8 JSON,
        // and the same bytes must not gain one just because a Windows machine wrote them.
        byte[] bytes = File.ReadAllBytes(Path.Combine(_root, FileName));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void AMoveReplacesANameThatIsAlreadyThere()
    {
        DiskSettingsFileAccess files = Files();
        Assert.True(files.TryCreateDirectory(out _));
        Assert.True(files.TryWrite("keeper.json", "newest", out _));
        Assert.True(files.TryWrite(BackupName, "older", out _));

        // The decisive case: .NET Framework's File.Move throws when the target exists, and the store's
        // save moves the live file onto the previous backup on every save after the first. A move that
        // only works when the target is absent is how a second save dies on the shipped framework while
        // passing against the fake.
        Assert.True(files.TryMove("keeper.json", BackupName, out string? reason));
        Assert.Null(reason);
        Assert.True(File.Exists(Path.Combine(_root, BackupName)));
        Assert.False(File.Exists(Path.Combine(_root, "keeper.json")));

        Assert.True(files.TryRead(BackupName, out string? content, out _));
        Assert.Equal("newest", content);
    }

    [Fact]
    public void MovingSomethingThatIsNotThereRefusesAndLeavesTheTargetAlone()
    {
        DiskSettingsFileAccess files = Files();
        Assert.True(files.TryCreateDirectory(out _));
        Assert.True(files.TryWrite(BackupName, "keep me", out _));

        // The check order matters: a source that is absent must refuse before any existing target is
        // touched, or a refused move would still have destroyed the older generation.
        Assert.False(files.TryMove(FileName, BackupName, out string? reason));
        Assert.Contains(FileName, reason!, StringComparison.Ordinal);

        Assert.True(files.TryRead(BackupName, out string? content, out _));
        Assert.Equal("keep me", content);
    }

    [Fact]
    public void DeletingSomethingThatIsNotThereIsRefusedTheWayTheSeamDocumentsIt()
    {
        DiskSettingsFileAccess files = Files();
        Assert.True(files.TryCreateDirectory(out _));

        // File.Delete on a missing file stays silent; the seam reports it, because the store cleans up
        // with this and needs to know the name it asked about was really there.
        Assert.False(files.TryDelete(FileName, out string? reason));
        Assert.Contains(FileName, reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFileListHoldsNamesAndNeverPaths()
    {
        DiskSettingsFileAccess files = Files();
        Assert.True(files.TryCreateDirectory(out _));
        Assert.True(files.TryWrite(FileName, "{}", out _));
        Assert.True(files.TryWrite(BackupName, "{}", out _));
        Directory.CreateDirectory(Path.Combine(_root, "ASubFolder"));
        File.WriteAllText(Path.Combine(_root, "unrelated.txt"), "x");

        IReadOnlyList<string> names = files.ListFileNames();

        Assert.Contains(FileName, names, StringComparer.Ordinal);
        Assert.Contains(BackupName, names, StringComparer.Ordinal);
        Assert.Contains("unrelated.txt", names, StringComparer.Ordinal);
        Assert.DoesNotContain("ASubFolder", names, StringComparer.Ordinal);
        foreach (string name in names)
        {
            Assert.DoesNotContain(Path.DirectorySeparatorChar.ToString(), name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ListingAFolderThatDoesNotExistYieldsNothingAndThrowsNothing()
    {
        Assert.Empty(Files().ListFileNames());
    }
    [Fact]
    public void AWatchlistSavedToDiskComesBackFromAFreshLoad()
    {
        WatchlistManager writer = Manager();
        WatchlistOperationResult created = writer.Create("Go-live checks", WatchlistMembershipMode.Manual, FlowRef.Environment);
        Assert.True(created.IsApplied);
        Assert.True(writer.Save().IsUsable);

        // A brand-new stack over the same folder: nothing in memory survives, exactly like closing the
        // tab and opening the plugin again.
        WatchlistStoreResult reloaded = Manager().Load();

        Assert.Equal(SettingsOutcome.Loaded, reloaded.Outcome);
        Watchlist list = Assert.Single(reloaded.Catalog.Watchlists);
        Assert.Equal("Go-live checks", list.Name);
        Assert.Equal(WatchlistMembershipMode.Manual, list.Mode);
        Assert.Equal(FlowRef.Environment, list.EnvironmentId);
        Assert.Equal(created.Watchlist!.Id, reloaded.Catalog.ActiveWatchlistId);
    }

    [Fact]
    public void AMembershipIdentitySurvivesWithEveryKeyPartIntact()
    {
        Assert.True(Store().Write(CatalogOf("Keyed")).IsUsable);

        Watchlist reloaded = Assert.Single(Store().Read().Catalog.Watchlists);
        WatchlistMember member = Assert.Single(reloaded.Members);
        WorkflowKey key = member.Key;

        // Identity is provider + scope + provider id, stored as three fields. If any part were lost in
        // the round trip, a portal deep link or a later merge would silently follow the wrong flow.
        Assert.Equal(ProviderKind.PowerAutomate, key.Provider);
        Assert.Equal(FlowRef.Environment, key.ScopeId);
        Assert.Equal(FlowId, key.WorkflowId);
        Assert.Equal("Nightly approval", member.Workflow.Name);
        Assert.True(member.IsIncluded);
        Assert.Equal(FixedStamp, member.AddedAtUtc!.Value);
    }

    [Fact]
    public void TwoEnvironmentsShareTheFileWithoutMixingTheirLists()
    {
        WatchlistManager writer = Manager();
        WatchlistOperationResult first = writer.Create("Daily", WatchlistMembershipMode.Manual, EnvironmentA);
        WatchlistOperationResult second = writer.Create("Daily", WatchlistMembershipMode.Manual, EnvironmentB);

        // The same name in two environments is allowed: the name rule is per-environment, so both must
        // stand and the file must keep both.
        Assert.True(first.IsApplied);
        Assert.True(second.IsApplied);
        Assert.True(writer.Save().IsUsable);

        WatchlistStoreResult reloaded = Manager().Load();
        Assert.Equal(2, reloaded.Catalog.Count);
        Assert.Equal("Daily", Assert.Single(reloaded.Catalog.ForEnvironment(EnvironmentA)).Name);
        Assert.Equal("Daily", Assert.Single(reloaded.Catalog.ForEnvironment(EnvironmentB)).Name);

        // The active selection is stored per environment too, so reopening against the other one does
        // not inherit it.
        Assert.Equal(first.Watchlist!.Id, reloaded.Catalog.Settings.ActiveWatchlistIdFor(EnvironmentA));
        Assert.Equal(second.Watchlist!.Id, reloaded.Catalog.Settings.ActiveWatchlistIdFor(EnvironmentB));
    }
    [Fact]
    public void ASecondSaveReplacesTheBackupNameThatIsAlreadyThere()
    {
        string file = Path.Combine(_root, FileName);
        Assert.True(Store().Write(CatalogOf("First")).IsUsable);
        string first = File.ReadAllText(file, Encoding.UTF8);

        WatchlistStoreResult second = Store().Write(CatalogOf("Second"));

        Assert.True(second.IsUsable);
        Assert.Equal(BackupName, second.BackupFileName);
        Assert.Equal(first, File.ReadAllText(Path.Combine(_root, BackupName), Encoding.UTF8));
        Assert.Contains("Second", File.ReadAllText(file, Encoding.UTF8), StringComparison.Ordinal);
        Assert.DoesNotContain("\"First\"", File.ReadAllText(file, Encoding.UTF8), StringComparison.Ordinal);

        // The third save has to replace that backup a second time. A move that only works onto an
        // absent name is exactly how this click dies on the shipped framework while passing on the
        // in-memory fake.
        WatchlistStoreResult third = Store().Write(CatalogOf("Third"));

        Assert.True(third.IsUsable);
        Assert.Contains("Third", File.ReadAllText(file, Encoding.UTF8), StringComparison.Ordinal);
        Assert.Contains("Second", File.ReadAllText(Path.Combine(_root, BackupName), Encoding.UTF8), StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableContentIsPreservedOnDiskAndCannotBeQuietlyOverwritten()
    {
        const string corrupt = "{\"schemaVersion\":1,\"watchlists\":{\"items\":[{\"id\":\"list-1\",";
        Assert.True(Files().TryCreateDirectory(out _));
        Assert.True(Files().TryWrite(FileName, corrupt, out _));

        JsonWatchlistStore store = Store();
        WatchlistStoreResult read = store.Read();

        Assert.False(read.IsUsable);
        Assert.Equal(SettingsOutcome.RejectedCorrupt, read.Outcome);
        Assert.True(read.IsPreservedRefusal);

        // The bytes moved aside, not destroyed: no live file, one quarantine copy holding them.
        string[] quarantined = Directory.GetFiles(_root, "*.corrupt-*.json");
        Assert.Equal(corrupt, File.ReadAllText(quarantined.Single(), Encoding.UTF8));
        Assert.False(File.Exists(Path.Combine(_root, FileName)));

        // The refusal holds the door for the store that saw it, because replacing what this build could
        // not read is exactly the data loss the store exists to prevent.
        WatchlistStoreResult refused = store.Write(CatalogOf("Replacement"));
        Assert.False(refused.IsUsable);
        Assert.Contains("not something this version can read", refused.Reason, StringComparison.Ordinal);

        // A stack that never read the bad file has no memory of the refusal: it sees a missing live
        // file and reports a first run. The quarantined bytes stay on disk as the evidence either way.
        Assert.Equal(SettingsOutcome.CreatedDefault, Store().Read().Outcome);
        Assert.Single(Directory.GetFiles(_root, "*.corrupt-*.json"));

        // Only the explicit override writes - and the quarantine copy survives it.
        Assert.True(store.ReplaceUnreadable(CatalogOf("Replacement")).IsUsable);
        WatchlistStoreResult reloaded = Store().Read();
        Assert.Equal(SettingsOutcome.Loaded, reloaded.Outcome);
        Assert.Equal("Replacement", Assert.Single(reloaded.Catalog.Watchlists).Name);
        Assert.Single(Directory.GetFiles(_root, "*.corrupt-*.json"));
    }

    [Fact]
    public void AForeignSettingsFileIsRefusedAndLeftWhereItIs()
    {
        // Another tool's file, complete and parseable, sitting in the settings folder. Read as an empty
        // store it would say "no watchlists", and the next save would delete somebody else's data - so
        // the refusal must leave the bytes exactly where they are, not quarantine them as damage.
        const string foreign = "{\"schemaVersion\":1,\"records\":[{\"id\":\"not-ours\"}]}";
        Assert.True(Files().TryCreateDirectory(out _));
        Assert.True(Files().TryWrite(FileName, foreign, out _));

        JsonWatchlistStore store = Store();
        WatchlistStoreResult read = store.Read();

        Assert.False(read.IsUsable);
        Assert.Equal(SettingsOutcome.RejectedCorrupt, read.Outcome);
        Assert.Equal(foreign, File.ReadAllText(Path.Combine(_root, FileName), Encoding.UTF8));
        Assert.Empty(Directory.GetFiles(_root, "*.corrupt-*.json"));

        Assert.False(store.Write(CatalogOf("Mine")).IsUsable);
        Assert.True(store.ReplaceUnreadable(CatalogOf("Mine")).IsUsable);
        Assert.Contains("Mine", File.ReadAllText(Path.Combine(_root, FileName), Encoding.UTF8), StringComparison.Ordinal);
    }
    [Fact]
    public void AWriteInterruptedBeforeTheMoveIsRecoveredAndNamed()
    {
        Assert.True(Store().Write(CatalogOf("HalfWritten")).IsUsable);

        // Reproduce the crash point on a real disk: the pending name exists, the live name does not.
        File.Move(Path.Combine(_root, FileName), Path.Combine(_root, PendingName));

        WatchlistStoreResult recovered = Store().Read();

        Assert.Equal(SettingsOutcome.Recovered, recovered.Outcome);
        Assert.Equal(PendingName, recovered.RecoveredFromFileName);
        Assert.Equal("HalfWritten", Assert.Single(recovered.Catalog.Watchlists).Name);
    }

    [Fact]
    public void ReadingAFolderThatDoesNotExistWritesNothing()
    {
        // Opening the plugin must be side-effect free: no directory, no file, no backup, no scratch
        // name. First run stays a first run until the operator asks to keep something.
        WatchlistStoreResult read = Store().Read();

        Assert.Equal(SettingsOutcome.CreatedDefault, read.Outcome);
        Assert.True(read.IsUsable);
        Assert.Empty(read.Catalog.Watchlists);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void TheSavedFileIsJsonThatCarriesItsOwnSchema()
    {
        Assert.True(Store().Write(CatalogOf("Contract")).IsUsable);
        string text = File.ReadAllText(Path.Combine(_root, FileName), Encoding.UTF8);

        // The file has to explain itself: version, kind and writer survive on disk, so a later build or
        // a different tool can tell what it is before it parses a byte.
        Assert.Contains("\"schemaVersion\"", text, StringComparison.Ordinal);
        Assert.Contains(SettingsSchema.Kind, text, StringComparison.Ordinal);
        Assert.Contains(SettingsSchema.CurrentVersion.ToString(), text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASaveIntoAFolderThatCannotBeMadeReportsTheReasonAndWritesNothing()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "occupied"), "not a folder");
        SettingsLayout layout = new SettingsLayout(Path.Combine(_root, "occupied", SettingsLayout.DirectoryName));
        JsonWatchlistStore store = new JsonWatchlistStore(
            new SettingsFileStore(layout, new DiskSettingsFileAccess(layout), new DataContractJsonSettingsSerializer(), new SystemClock()),
            new SystemClock());

        WatchlistStoreResult saved = store.Write(CatalogOf("Nowhere"));

        Assert.False(saved.IsUsable);
        Assert.False(string.IsNullOrWhiteSpace(saved.Reason));
        Assert.Single(Directory.GetFiles(_root));
    }
}
