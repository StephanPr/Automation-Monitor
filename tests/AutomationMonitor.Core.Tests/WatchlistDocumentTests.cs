using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The format contract of the local settings file, and the evidence that the framework JSON serializer
/// can carry it: what it writes, what it refuses, and the two places the core compensates for the engine
/// (ISO 8601 strings instead of its legacy date object, and a raised object-graph quota). Executed on the
/// .NET 10 runtime here; the same engine under .NET Framework 4.8 is a Windows claim, recorded as
/// unverified rather than assumed.
/// </summary>
public class WatchlistDocumentTests
{
    private static readonly DateTimeOffset SavedAt = new DateTimeOffset(2026, 10, 31, 12, 34, 56, TimeSpan.Zero);

    private static DataContractJsonSettingsSerializer Serializer()
    {
        return new DataContractJsonSettingsSerializer();
    }

    private static WatchlistCatalog RoundTrip(WatchlistCatalog catalog, out IReadOnlyList<string> notes)
    {
        DataContractJsonSettingsSerializer serializer = Serializer();
        string json = serializer.Serialize(SettingsMap.ToDocument(catalog, SavedAt));
        SettingsDocument document = serializer.Deserialize(json);
        if (!SettingsMap.TryToCatalog(document, out WatchlistCatalog after, out string? problem, out notes))
        {
            throw new Exception("the round trip was refused: " + (problem ?? "no reason given"));
        }

        return after;
    }

    private static Watchlist SampleWatchlist()
    {
        WatchlistBuilder builder = new WatchlistBuilder(
            "11111111-1111-1111-1111-111111111111",
            "Go-live \"critical\" \\ flows",
            WatchlistMembershipMode.DynamicSolution,
            "Checked against ERP \u00e9t\u00e9 中文 and a  separator",
            FlowRef.Environment);

        SolutionReference solution = new SolutionReference("44444444-4444-4444-4444-444444444444", "erp_team", "ERP", "1.0.0.0", false);
        List<WorkflowReference> flows = new List<WorkflowReference>
        {
            FlowRef.Create("aaaaaaaa-0000-0000-0000-000000000001", "=SUM(A1:B2) looks like a formula"),
            FlowRef.Create("aaaaaaaa-0000-0000-0000-000000000002", "https://contoso.example/Folder/File"),
            FlowRef.Create("aaaaaaaa-0000-0000-0000-000000000003", "Tabs\tand emoji \U0001F600")
        };

        builder.AddSolutionFlows(solution, flows, ProviderResultState.Complete, "3 classic workflow(s) skipped", SavedAt);
        builder.SetIncluded(flows[2].Key, isIncluded: false);
        builder.AddManualFlow(FlowRef.Create("bbbbbbbb-0000-0000-0000-000000000001", "Hand picked"), SavedAt.AddMinutes(-3));
        builder.SetPreferences(new WatchlistPreferences(
            RefreshInterval.FiveMinutes,
            MonitoringDateRange.LastFourHours,
            false,
            new DurationThresholds(300, null)));
        builder.SetTimestamps(SavedAt.AddMonths(-1), SavedAt, SavedAt.AddMinutes(-10));

        return builder.Build();
    }

    private static WatchlistCatalog SampleCatalog()
    {
        ApplicationSettings settings = ApplicationSettings.Defaults
            .WithActiveEnvironment(FlowRef.Environment, SavedAt)
            .WithActiveWatchlist(FlowRef.Environment, "11111111-1111-1111-1111-111111111111", SavedAt)
            .WithDefaults(RefreshInterval.OneMinute, MonitoringDateRange.LastDay, true, new DurationThresholds(600, 3600));

        return new WatchlistCatalog(new[] { SampleWatchlist() }, "11111111-1111-1111-1111-111111111111", settings);
    }

    [Fact]
    public void TimestampsAreStoredAsIso8601UtcStrings()
    {
        string json = Serializer().Serialize(SettingsMap.ToDocument(SampleCatalog(), SavedAt));

        Assert.Contains("\"2026-10-31T12:34:56+00:00\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/Date(", json, StringComparison.Ordinal);
        Assert.DoesNotContain("OffsetMinutes", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSchemaVersionAndKindAreAlwaysWritten()
    {
        string json = Serializer().Serialize(SettingsMap.ToDocument(SampleCatalog(), SavedAt));

        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"" + SettingsSchema.Kind + "\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStoredFieldSurvivesARoundTrip()
    {
        WatchlistCatalog before = SampleCatalog();
        WatchlistCatalog after = RoundTrip(before, out IReadOnlyList<string> notes);

        Assert.Empty(notes);
        Assert.Equal(before.ActiveWatchlistId, after.ActiveWatchlistId);
        Watchlist list = Assert.Single(after.Watchlists);
        Watchlist original = before.Watchlists[0];

        Assert.Equal(original.Id, list.Id);
        Assert.Equal(original.Name, list.Name);
        Assert.Equal(original.Description, list.Description);
        Assert.Equal(original.EnvironmentId, list.EnvironmentId);
        Assert.Equal(WatchlistMembershipMode.DynamicSolution, list.Mode);
        Assert.Equal(original.CreatedAtUtc, list.CreatedAtUtc);
        Assert.Equal(original.ModifiedAtUtc, list.ModifiedAtUtc);
        Assert.Equal(original.LastMembershipRefreshUtc, list.LastMembershipRefreshUtc);
        Assert.Equal(ProviderResultState.Complete, list.State);
        Assert.Equal(original.CoverageNotes, list.CoverageNotes);
        Assert.Equal(original.SourceSolutionIds, list.SourceSolutionIds);

        Assert.Equal(RefreshInterval.FiveMinutes, list.Preferences.AutoRefresh);
        Assert.Equal(MonitoringDateRange.LastFourHours, list.Preferences.DateRange);
        Assert.False(list.Preferences.ShowExcludedMembers);
        Assert.Equal(300, list.Preferences.Thresholds.SlowCompletedSeconds);
        Assert.Null(list.Preferences.Thresholds.MaximumRunningSeconds);

        Assert.Equal(4, list.Members.Count);
        Assert.Equal(3, list.MonitoredFlowCount);
        Assert.Equal(original.Members[0].Workflow.Name, list.Members[0].Workflow.Name);
        Assert.Equal(original.Members[0].LastSeenUtc, list.Members[0].LastSeenUtc);
        Assert.Equal(new[] { "44444444-4444-4444-4444-444444444444" }, list.Members[0].SourceSolutionIds);

        WatchlistMember manual = list.Members[3];
        Assert.True(manual.IsIncluded);
        Assert.True(manual.Source.HasFlag(WatchlistMemberSource.Manual));
        Assert.Equal(original.Members[3].AddedAtUtc, manual.AddedAtUtc);

        // The sample adds this member with an explicit "seen at" minute, so fidelity means getting that
        // exact instant back: not null, and not the moment the file happened to be written.
        Assert.Equal(original.Members[3].LastSeenUtc, manual.LastSeenUtc);
        Assert.Equal(SavedAt.AddMinutes(-3), manual.LastSeenUtc);

        Assert.Equal(RefreshInterval.OneMinute, after.Settings.DefaultAutoRefresh);
        Assert.Equal(MonitoringDateRange.LastDay, after.Settings.DefaultDateRange);
        Assert.Equal(600, after.Settings.DefaultThresholds.SlowCompletedSeconds);
        Assert.Equal(3600, after.Settings.DefaultThresholds.MaximumRunningSeconds);
        Assert.Equal("11111111-1111-1111-1111-111111111111", after.Settings.ActiveWatchlistIdFor(FlowRef.Environment));
    }

    [Theory]
    [InlineData("Go-live \u00e9t\u00e9 中文 \u65e5\u672c\u8a9e")]
    [InlineData("Emoji \U0001F600 and a \u2028 line separator")]
    [InlineData("Quote \" backslash \\ and an \u2026 ellipsis")]
    [InlineData("Zero-width \u200B nbsp\u00A0 and a mid-file \uFEFF BOM")]
    [InlineData("=1+1 @SUM(A1) -4 +4 (formula-looking text)")]
    [InlineData("../../Windows/System32/cmd.exe")]
    [InlineData("https://contoso.example/path?token=not-a-token#fragment")]
    public void AwkwardTextSurvivesTheFileByteForByte(string name)
    {
        // Control characters are the one family this contract excludes on purpose - see
        // TabsAndNewlinesBecomeSpacesRatherThanTravellingAsData - so every case here is printable.
        WatchlistBuilder builder = new WatchlistBuilder("list-1", name, WatchlistMembershipMode.Snapshot, name, null);
        builder.AddManualFlow(FlowRef.Create("aaaaaaaa-0000-0000-0000-0000000000aa", name), SavedAt);
        WatchlistCatalog catalog = new WatchlistCatalog(new[] { builder.Build() }, "list-1", null);

        WatchlistCatalog after = RoundTrip(catalog, out _);
        Assert.Equal(name, after.Watchlists[0].Name);
        Assert.Equal(name, after.Watchlists[0].Description);
        Assert.Equal(name, after.Watchlists[0].Members[0].Workflow.Name);
    }

    [Fact]
    public void ControlCharactersAreStoredWithoutBreakingTheFile()
    {
        // A provider name can carry a control character. It must not terminate the string, forge a new
        // JSON member, or survive as something a grid would have to interpret.
        WatchlistBuilder builder = new WatchlistBuilder("list-ctl", "Bad\u0000name\u001b[31m", WatchlistMembershipMode.Manual);
        WatchlistCatalog after = RoundTrip(new WatchlistCatalog(new[] { builder.Build() }, null, null), out _);

        string stored = after.Watchlists[0].Name;
        Assert.DoesNotContain("\u0000", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", stored, StringComparison.Ordinal);
        Assert.StartsWith("Bad", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void TabsAndNewlinesBecomeSpacesRatherThanTravellingAsData()
    {
        // The deliberate half of the rule above: a tab or a newline inside a name is a defect from the
        // provider, not content worth keeping. Each control character becomes exactly one space and the
        // value is trimmed, so the file stays one line and a grid cell stays one row. Byte-for-byte
        // fidelity is claimed for everything else - see the awkward-text cases.
        WatchlistBuilder builder = new WatchlistBuilder("list-tab", "Tab\there\nand a trailing newline\n", WatchlistMembershipMode.Manual);
        builder.AddManualFlow(FlowRef.Create("aaaaaaaa-0000-0000-0000-0000000000ab", "Line\r\nbreak"), SavedAt);

        WatchlistCatalog after = RoundTrip(new WatchlistCatalog(new[] { builder.Build() }, null, null), out _);

        Assert.Equal("Tab here and a trailing newline", after.Watchlists[0].Name);
        Assert.Equal("Line  break", after.Watchlists[0].Members[0].Workflow.Name);
    }

    [Fact]
    public void AMemberNobodyHasSeenStaysUnseenThroughTheFile()
    {
        // Adding a flow by hand records no observation. An absent timestamp must not silently become
        // "now" on the way through the file, or the grid would claim a check that never happened.
        WatchlistBuilder builder = new WatchlistBuilder("list-unseen", "Unseen", WatchlistMembershipMode.Manual);
        builder.AddManualFlow(FlowRef.Create("aaaaaaaa-0000-0000-0000-0000000000ac", "Hand added, never observed"));

        WatchlistCatalog after = RoundTrip(new WatchlistCatalog(new[] { builder.Build() }, null, null), out _);

        WatchlistMember member = Assert.Single(after.Watchlists[0].Members);
        Assert.Null(member.AddedAtUtc);
        Assert.Null(member.LastSeenUtc);
    }

    [Fact]
    public void OptionalMembersMayBeAbsentOrNull()
    {
        WatchlistBuilder builder = new WatchlistBuilder("list-2", "Sparse", WatchlistMembershipMode.Manual);
        WatchlistCatalog catalog = new WatchlistCatalog(new[] { builder.Build() }, null, ApplicationSettings.Defaults);

        string json = Serializer().Serialize(SettingsMap.ToDocument(catalog, SavedAt));
        Assert.Contains("null", json, StringComparison.Ordinal);

        WatchlistCatalog after = RoundTrip(catalog, out _);
        Watchlist list = Assert.Single(after.Watchlists);
        Assert.Equal(WatchlistMembershipMode.Manual, list.Mode);
        Assert.Equal(string.Empty, list.Description);
        Assert.Equal(string.Empty, list.EnvironmentId);
        Assert.Empty(list.Members);
        Assert.Null(list.CreatedAtUtc);
        Assert.True(list.Preferences.IsInherited);
        Assert.Null(after.ActiveWatchlistId);
    }

    [Fact]
    public void AFileHandwrittenWithOnlyTheRequiredFieldsStillLoads()
    {
        // Compatibility with the shape a v1 file may legitimately take: required members present,
        // every optional one absent. This is what "reads the format an earlier build wrote" means in
        // practice, and it must not be reported as corrupt.
        const string json = "{\"schemaVersion\":1,\"kind\":\"watchlist-store\","
            + "\"watchlists\":{\"items\":[{\"id\":\"list-a\",\"name\":\"Hand made\"}]}}";

        Assert.True(
            SettingsMap.TryToCatalog(Serializer().Deserialize(json), out WatchlistCatalog catalog, out string? problem, out IReadOnlyList<string> notes),
            problem);
        Assert.Null(problem);
        Watchlist list = Assert.Single(catalog.Watchlists);
        Assert.Equal("list-a", list.Id);
        Assert.Equal("Hand made", list.Name);
        Assert.Equal(WatchlistMembershipMode.Snapshot, list.Mode);
        Assert.Empty(list.Members);

        // The absences are stated, not silently filled with a claim of completeness.
        Assert.Contains(notes, note => note.Contains("did not record how complete", StringComparison.Ordinal));
        Assert.Equal(ProviderResultState.Stale, list.State);
    }

    [Fact]
    public void AFileThatDoesNotNameItselfIsRefusedRatherThanReadAsEmpty()
    {
        // A complete JSON object from another tool deserialises into a document whose watchlists are
        // empty. Reading that as "no watchlists" is how somebody else's file gets overwritten on the next
        // save, so the discriminator has to be present - not merely correct when it happens to be there.
        SettingsDocument anonymous = Serializer().Deserialize("{\"schemaVersion\":1,\"watchlists\":{\"items\":[]}}");

        Assert.False(SettingsMap.TryToCatalog(anonymous, out _, out string? problem, out _));
        Assert.Contains("does not say what it holds", problem, StringComparison.Ordinal);

        SettingsDocument foreign = Serializer().Deserialize(
            "{\"schemaVersion\":1,\"kind\":\"some-other-tool\",\"watchlists\":{\"items\":[]}}");

        Assert.False(SettingsMap.TryToCatalog(foreign, out _, out string? foreignProblem, out _));
        Assert.Contains("some-other-tool", foreignProblem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":1")]
    [InlineData("{\"schemaVersion\":1} trailing garbage")]
    [InlineData("{\"schemaVersion\":\"one\"}")]
    [InlineData("{\"schemaVersion\":2147483648}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [InlineData("[1,2,3]")]
    [InlineData("\"just a string\"")]
    [InlineData("123")]
    [InlineData("{\"schemaVersion\":1,\"app\":{\"defaultShowExcludedMembers\":\"yes\"}}")]
    public void MalformedJsonIsRejectedWithAReason(string json)
    {
        SettingsSerializationException error = Assert.Throws<SettingsSerializationException>(() => Serializer().Deserialize(json));
        Assert.False(string.IsNullOrWhiteSpace(error.Reason));
    }

    [Fact]
    public void AMissingSchemaVersionIsRefused()
    {
        // schemaVersion is the one required member: without it there is nothing to gate on.
        SettingsSerializationException error = Assert.Throws<SettingsSerializationException>(
            () => Serializer().Deserialize("{\"kind\":\"watchlist-store\"}"));
        Assert.Contains("schemaVersion", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldThisBuildDoesNotKnowIsIgnoredRatherThanFatal()
    {
        // Forward tolerance inside one schema version: a member a later build added must not stop this
        // build reading what it does understand. Refusing a newer file is the schema version's job.
        const string json = "{\"schemaVersion\":1,\"kind\":\"watchlist-store\",\"someFutureField\":42,"
            + "\"watchlists\":{\"items\":[{\"id\":\"list-9\",\"name\":\"Future\",\"membershipMode\":\"manual\",\"futureFlag\":true}]}}";

        Assert.True(
            SettingsMap.TryToCatalog(Serializer().Deserialize(json), out WatchlistCatalog catalog, out string? problem, out _),
            problem);
        Assert.Null(problem);
        Watchlist list = Assert.Single(catalog.Watchlists);
        Assert.Equal("list-9", list.Id);
        Assert.Equal(WatchlistMembershipMode.Manual, list.Mode);
    }

    [Fact]
    public void ALargeWatchlistStaysWithinTheSerializerQuota()
    {
        // The engine's default object-graph limit is below a watchlist of a few hundred members. The
        // serializer raises it, and this is the test that fails if that is ever removed.
        WatchlistBuilder builder = new WatchlistBuilder("big", "Big list", WatchlistMembershipMode.Manual);
        for (int i = 0; i < 400; i++)
        {
            builder.AddManualFlow(FlowRef.Create("aaaaaaaa-0000-0000-0000-" + i.ToString("D12")), SavedAt);
        }

        WatchlistCatalog catalog = new WatchlistCatalog(new[] { builder.Build() }, null, null);
        WatchlistCatalog after = RoundTrip(catalog, out _);
        Assert.Equal(400, Assert.Single(after.Watchlists).Members.Count);
    }

    [Fact]
    public void TheEngineIsTheFrameworkSerializerWithNoExtraAssembly()
    {
        // Guards the packaging decision: the type named by the store must be the framework's own, so a
        // future edit cannot quietly introduce a JSON dependency that the two-DLL contract forbids.
        ISettingsSerializer serializer = new DataContractJsonSettingsSerializer();
        Assert.Equal("DataContractJsonSerializer", serializer.EngineName);
        Assert.IsType<System.Runtime.Serialization.Json.DataContractJsonSerializer>(
            new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(SettingsDocument)));
    }
}
