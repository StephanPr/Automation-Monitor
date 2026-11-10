using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// AM-35 is a spike, and a spike is only worth what its measurement is worth. These tests guard the four
/// things the live run depends on: that the probe asks for the columns it means to ask for while the
/// production run projection stays byte-for-byte what the 2026-11-09 session proved, that the SDK type
/// shapes are classified rather than guessed, that the row ceiling really bounds the read, and that the
/// reported line carries counts and codes and nothing a tenant owns.
/// </summary>
public class FlowRunProbeTests
{
    // Hex letters on purpose: a spelling test that used only digits would pass without proving anything
    // about case or braces.
    private const string Requested = "ab222222-2222-2222-2222-222222222222";
    private const string RequestedUpper = "AB222222-2222-2222-2222-222222222222";
    private const string Other = "cd777777-7777-7777-7777-777777777777";
    private const string OtherUpper = "CD777777-7777-7777-7777-777777777777";
    private const string RunName = "08584308152949360664437597343";

    private static FlowRunProbeRead Attempted(string column = DataverseSchema.RunWorkflowId, string kind = "text")
    {
        return new FlowRunProbeRead(column, kind) { WasAttempted = true };
    }

    /// <summary>
    /// The scope correction this spike was approved with: the production query, its criteria and its
    /// normalizer are untouched until the live answer arrives. The lookup column belongs to the probe only,
    /// so the projection the monitoring path sends has to stay exactly the thirteen names a real environment
    /// already answered - a column added for a diagnostic that quietly lands in the production list is the
    /// <c>primarycategory</c> failure waiting to happen again.
    /// </summary>
    [Fact]
    public void TheProductionRunProjectionIsStillExactlyWhatTheLiveSessionProved()
    {
        Assert.Equal(
            new[]
            {
                "flowrunid", "name", "workflowid", "starttime", "endtime", "duration", "status",
                "errorcode", "errormessage", "parentrunid", "clienttrackingid", "isprimary", "triggertype"
            },
            DataverseSchema.RunAttributes);
    }

    [Fact]
    public void TheProbeProjectionAddsTheLookupColumnAndNothingElse()
    {
        Assert.Equal("workflow", DataverseSchema.RunWorkflowLookup);
        Assert.DoesNotContain(DataverseSchema.RunWorkflowLookup, DataverseSchema.RunAttributes);
        Assert.Contains(DataverseSchema.RunWorkflowLookup, DataverseSchema.RunProbeAttributes);
        Assert.Equal(DataverseSchema.RunAttributes.Length + 1, DataverseSchema.RunProbeAttributes.Length);
        Assert.Equal(
            DataverseSchema.RunProbeAttributes.Length,
            DataverseSchema.RunProbeAttributes.Distinct(StringComparer.Ordinal).Count());

        // Dataverse logical names are lowercase; a schema name in a ColumnSet fails the whole read.
        foreach (string column in DataverseSchema.RunProbeAttributes)
        {
            Assert.False(string.IsNullOrWhiteSpace(column));
            Assert.Equal(column.ToLowerInvariant(), column);
        }

        // The probe projection is the production columns plus the lookup and nothing else. Column order
        // carries no meaning in a ColumnSet, so the comparison is by content: what matters is that no
        // production column was dropped or slipped in on the way, because that is how a diagnostic becomes a
        // regression in the monitoring path.
        Assert.Equal(
            DataverseSchema.RunAttributes.Append(DataverseSchema.RunWorkflowLookup).OrderBy(c => c, StringComparer.Ordinal),
            DataverseSchema.RunProbeAttributes.OrderBy(c => c, StringComparer.Ordinal));
    }

    /// <summary>The experiment is defined by its window; a probe that drifted off seven days would answer a different question.</summary>
    [Fact]
    public void TheProbeWindowIsSevenDaysUpToTheInstantItRuns()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 10, 6, 30, 0, TimeSpan.FromHours(11));
        DateTimeOffset start = FlowRunProbeDiagnostics.WindowStart(now);

        Assert.Equal(FlowRunProbeDiagnostics.WindowDays, 7);
        Assert.Equal(TimeSpan.FromDays(7), now.ToUniversalTime() - start);
        Assert.Equal(0, start.Offset.TotalMinutes);
    }

    /// <summary>
    /// The text column is text. Sending a braced or bare GUID would match nothing and read as an empty
    /// window, which is the difference between "this flow has no runs" and "we asked the wrong question".
    /// </summary>
    [Fact]
    public void TheTextFilterValueIsTheCanonicalDashedSpelling()
    {
        Guid identity = Guid.Parse("ABCD1234-5678-9ABC-DEF0-1234567890AB");

        Assert.Equal("abcd1234-5678-9abc-def0-1234567890ab", FlowRunProbeDiagnostics.RequestIdText(identity));
        Assert.Equal(36, FlowRunProbeDiagnostics.RequestIdText(identity).Length);
        Assert.Equal(string.Empty, FlowRunProbeDiagnostics.RequestIdText(Guid.Empty));
    }

    /// <summary>
    /// The whole point of the experiment is that a workflow id column and a run name column are different
    /// things. Classifying them by shape is what lets the answer be reported without quoting a value.
    /// </summary>
    [Theory]
    [InlineData(new string[0], ProbeValueShape.NoValues)]
    [InlineData(new string?[] { null }, ProbeValueShape.Absent)]
    [InlineData(new string?[] { null, "", "   " }, ProbeValueShape.Absent)]
    [InlineData(new[] { Requested }, ProbeValueShape.GuidLike)]
    [InlineData(new[] { Requested, "{" + OtherUpper + "}" }, ProbeValueShape.GuidLike)]
    [InlineData(new[] { RunName, "0858430815294936" }, ProbeValueShape.NonGuidText)]
    [InlineData(new[] { Requested, RunName }, ProbeValueShape.Mixed)]
    [InlineData(new string?[] { Requested, null, RunName }, ProbeValueShape.Mixed)]
    [InlineData(new string?[] { Requested, null }, ProbeValueShape.GuidLike)]
    public void ColumnValuesAreClassifiedByShapeNotByContent(string?[] values, ProbeValueShape expected)
    {
        Assert.Equal(expected, FlowRunProbeDiagnostics.ShapeOf(values));
    }

    [Fact]
    public void MissingValuesAreCountedSeparatelyFromShapes()
    {
        Assert.Equal(3, FlowRunProbeDiagnostics.CountAbsent(new string?[] { Requested, null, "", " " }));
        Assert.Equal(0, FlowRunProbeDiagnostics.CountAbsent(new[] { Requested }));
        Assert.Equal(0, FlowRunProbeDiagnostics.CountAbsent(null!));
    }

    /// <summary>
    /// The overlap is the measurement, so it obeys the same identity rule as the merge pipeline: one run
    /// observed twice, or spelled three ways, is still one run. An overlap inflated by a duplicate page
    /// would turn an inconclusive probe into an apparent confirmation.
    /// </summary>
    [Fact]
    public void OneRunSpelledDifferentlyIsStillOneRun()
    {
        string[] spellings =
        {
            Requested,
            Requested.ToUpperInvariant(),
            "{" + Requested.ToUpperInvariant() + "}",
            Requested.Replace("-", string.Empty),
            "  " + Requested + "  "
        };

        // Every one of these is the same identifier, so the shape rule must see one kind of value - and the
        // bare spelling is derived rather than typed, so the 32 characters cannot drift.
        Assert.Equal(ProbeValueShape.GuidLike, FlowRunProbeDiagnostics.ShapeOf(spellings));
        Assert.Equal(1, FlowRunProbeDiagnostics.CountDistinct(spellings));
        Assert.Equal(1, FlowRunProbeDiagnostics.CountOverlap(spellings, new[] { Requested }));
        Assert.Equal(1, FlowRunProbeDiagnostics.CountOverlap(new[] { Requested, Requested, Requested }, new[] { Requested, Requested }));
    }

    [Fact]
    public void OverlapCountsOnlyRunsSeenByBothQueries()
    {
        Assert.Equal(2, FlowRunProbeDiagnostics.CountOverlap(new[] { Requested, Other }, new[] { Other.ToLowerInvariant(), Requested, "extra" }));
        Assert.Equal(0, FlowRunProbeDiagnostics.CountOverlap(new[] { Requested }, new[] { Other }));
        Assert.Equal(0, FlowRunProbeDiagnostics.CountOverlap(new string?[0], new[] { Requested }));
        Assert.Equal(0, FlowRunProbeDiagnostics.CountOverlap(new string?[] { Requested, null }, new string?[] { null }));
    }

    /// <summary>
    /// "The column was not there" and "the column said something else" must never collapse into one answer:
    /// the first is the server not populating a column, the second is a real identity disagreement.
    /// </summary>
    [Fact]
    public void AColumnThatCarriedNothingIsAbsentRatherThanWrong()
    {
        FlowRunProbeRead read = Attempted();
        read.Rows = 2;

        Assert.Equal(ProbeMatch.ColumnAbsent, FlowRunProbeDiagnostics.MatchOf(Requested, read, new string?[] { null, null }));
        Assert.Equal(ProbeMatch.ColumnAbsent, FlowRunProbeDiagnostics.MatchOf(Requested, read, new string[0]));
    }

    [Fact]
    public void MatchIsJudgedOnTheCanonicalSpellingNotTheText()
    {
        FlowRunProbeRead read = Attempted();
        read.Rows = 3;

        Assert.Equal(ProbeMatch.AllMatch, FlowRunProbeDiagnostics.MatchOf(Requested, read, new[] { RequestedUpper, "{" + Requested + "}" }));
        Assert.Equal(ProbeMatch.SomeMatch, FlowRunProbeDiagnostics.MatchOf(Requested, read, new[] { Requested, Other }));
        Assert.Equal(ProbeMatch.NoneMatch, FlowRunProbeDiagnostics.MatchOf(Requested, read, new[] { Other, OtherUpper }));
    }

    [Fact]
    public void NoQueryNoRowsAndNoComparisonAreThreeDifferentAnswers()
    {
        FlowRunProbeRead notSent = new FlowRunProbeRead(DataverseSchema.RunWorkflowLookup, "lookup");
        Assert.Equal(ProbeMatch.NotAttempted, FlowRunProbeDiagnostics.MatchOf(Requested, notSent, new[] { Requested }));
        Assert.Equal(ProbeMatch.NotAttempted, FlowRunProbeDiagnostics.MatchOf(Requested, null, new[] { Requested }));

        FlowRunProbeRead empty = Attempted();
        Assert.Equal(ProbeMatch.NoRows, FlowRunProbeDiagnostics.MatchOf(Requested, empty, new[] { Requested }));

        FlowRunProbeRead refused = Attempted();
        refused.Rows = 5;
        refused.Failure = ProviderResultState.Unauthorized;
        Assert.Equal(ProbeMatch.NoRows, FlowRunProbeDiagnostics.MatchOf(Requested, refused, new[] { Requested }));

        FlowRunProbeRead asked = Attempted();
        asked.Rows = 1;
        Assert.Equal(ProbeMatch.NotAttempted, FlowRunProbeDiagnostics.MatchOf(null, asked, new[] { Requested }));
    }

    [Fact]
    public void TwoEmptyQueriesAreInconclusiveAndNameNoCause()
    {
        FlowRunProbeOutcome outcome = new FlowRunProbeOutcome(Attempted(), Lookup(), Requested);

        Assert.Equal(ProbeVerdict.BothEmpty, outcome.Verdict);
        Assert.True(outcome.IsInconclusive);

        string line = outcome.Describe();
        Assert.Contains("inconclusive - no rows either way", line);
        Assert.Contains("does not say which column is authoritative", line);
        Assert.Contains("ingestion", line);
        Assert.Contains("retention", line);
        Assert.Contains("cannot tell those apart", line);
    }

    private static FlowRunProbeRead Lookup()
    {
        return new FlowRunProbeRead(DataverseSchema.RunWorkflowLookup, "lookup") { WasAttempted = true };
    }

    [Fact]
    public void OneSidedAnswersAreReportedAsWhatActuallyHappened()
    {
        FlowRunProbeRead text = Attempted();
        text.Rows = 3;
        text.RunIds = new[] { Requested, Other };

        FlowRunProbeOutcome textOnly = new FlowRunProbeOutcome(text, Lookup(), Requested);
        Assert.Equal(ProbeVerdict.TextOnly, textOnly.Verdict);
        Assert.Contains("only the text-column filter returned rows", textOnly.Describe());

        FlowRunProbeRead lookup = Lookup();
        lookup.Rows = 2;
        lookup.RunIds = new[] { Requested, Other };
        FlowRunProbeOutcome lookupOnly = new FlowRunProbeOutcome(Attempted(), lookup, Requested);
        Assert.Equal(ProbeVerdict.LookupOnly, lookupOnly.Verdict);
        Assert.Contains("only the lookup-column filter returned rows", lookupOnly.Describe());
    }

    /// <summary>
    /// The confirmation case, and the refutation case. Two filters that both answered are only an
    /// agreement when a run came back through both; rows on either side that never meet say nothing about
    /// identity and must not be dressed up as a finding.
    /// </summary>
    [Fact]
    public void AgreementNeedsARunThatCameBackBothWays()
    {
        FlowRunProbeRead text = Attempted();
        text.Rows = 2;
        text.RunIds = new[] { Requested, Other };

        FlowRunProbeRead lookup = Lookup();
        lookup.Rows = 2;
        lookup.RunIds = new[] { OtherUpper, RunName };

        FlowRunProbeOutcome agree = new FlowRunProbeOutcome(text, lookup, Requested);
        Assert.Equal(ProbeVerdict.BothAgree, agree.Verdict);
        Assert.Equal(1, agree.OverlapCount);
        Assert.False(agree.IsInconclusive);
        Assert.Contains("both filters return the same runs", agree.Describe());

        lookup.RunIds = new[] { RunName };
        FlowRunProbeOutcome differ = new FlowRunProbeOutcome(text, lookup, Requested);
        Assert.Equal(ProbeVerdict.BothDiffer, differ.Verdict);
        Assert.Equal(0, differ.OverlapCount);
        Assert.True(differ.IsInconclusive);
    }

    [Fact]
    public void ARefusedQueryIsNeverReportedAsAnEmptyOne()
    {
        FlowRunProbeRead refused = Attempted();
        refused.Failure = ProviderResultState.Unauthorized;
        refused.FailureKind = "SecurityException";

        FlowRunProbeOutcome outcome = new FlowRunProbeOutcome(refused, Lookup(), Requested);
        Assert.Equal(ProbeVerdict.Refused, outcome.Verdict);
        Assert.True(outcome.IsInconclusive);

        string line = outcome.Describe();
        Assert.Contains("refused: unauthorized (SecurityException)", line);
        Assert.Contains("A refused query is not an empty query", line);
    }

    /// <summary>
    /// A one-query experiment cannot rank two columns, however many rows the surviving query returned.
    /// This is the case the guard exists for: an unusable identity silently turning "4 rows from one
    /// filter" into a verdict about both.
    /// </summary>
    [Fact]
    public void AQueryThatCouldNotBeSentMakesTheExperimentIncomplete()
    {
        FlowRunProbeRead text = Attempted();
        text.Rows = 4;
        text.RunIds = new[] { Requested };

        FlowRunProbeRead lookup = new FlowRunProbeRead(DataverseSchema.RunWorkflowLookup, "lookup")
        {
            NotAttemptedReason = "requested identity is not guid-shaped"
        };

        FlowRunProbeOutcome outcome = new FlowRunProbeOutcome(text, lookup, "not-a-guid-at-all");
        Assert.False(outcome.RequestedIdentityIsGuid);
        Assert.Equal(ProbeVerdict.Incomplete, outcome.Verdict);
        Assert.True(outcome.IsInconclusive);

        string line = outcome.Describe();
        Assert.Contains("not sent (requested identity is not guid-shaped)", line);
        Assert.Contains("requested identity is not guid-shaped. Verdict:", line);
        Assert.Contains("only one of the two queries could be sent", line);
    }

    [Fact]
    public void WhenNothingWasAskedTheProbeSaysSo()
    {
        FlowRunProbeOutcome outcome = new FlowRunProbeOutcome(
            new FlowRunProbeRead(DataverseSchema.RunWorkflowId, "text"),
            new FlowRunProbeRead(DataverseSchema.RunWorkflowLookup, "lookup"),
            Requested);

        Assert.Equal(ProbeVerdict.NotAttempted, outcome.Verdict);
        Assert.Contains("not attempted", outcome.Describe());
    }

    /// <summary>
    /// The ceiling is the whole reason a diagnostic is allowed against a live environment at all, so it is
    /// tested against a source that never stops offering rows: five rows, one round trip, and an answer that
    /// says it is a sample instead of implying it read the window.
    /// </summary>
    [Fact]
    public void TheCeilingStopsTheReadAfterOneRoundTripAndSaysItIsASample()
    {
        EndlessRunRowSource source = new EndlessRunRowSource();
        DateTimeOffset to = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
        RunQueryCriteria criteria = new RunQueryCriteria(
            FakeRunRowSource.Key(),
            FlowRunProbeDiagnostics.WindowStart(to),
            to,
            null,
            FlowRunProbeDiagnostics.RowCeiling);

        PagedReadResult<RawRunRecord> read = PageReader.Read(
            request => source.ReadFlowRuns(criteria, request, CancellationToken.None),
            FlowRunProbeDiagnostics.RowCeiling,
            FlowRunProbeDiagnostics.RowCeiling,
            2);

        Assert.Equal(FlowRunProbeDiagnostics.RowCeiling, read.Items.Count);
        Assert.Equal(1, source.Calls);
        Assert.True(read.IsTruncated);
        Assert.False(read.IsComplete);
        Assert.Equal(ProviderResultState.Partial, read.State);
        Assert.Contains("row ceiling", read.StopReason);
    }

    [Fact]
    public void AReadThatHitTheCeilingIsNeverWordedAsTheWholeWindow()
    {
        FlowRunProbeRead read = SampleRead();
        read.CeilingReached = true;

        string line = FlowRunProbeDiagnostics.Describe(read);
        Assert.Contains("stopped at the row ceiling with more rows pending", line);
        Assert.Contains("workflow=EntityReference", line);
        Assert.DoesNotContain("complete", line);
    }

    /// <summary>
    /// The privacy rule for this spike in one test: the line the maintainer copies out of the log carries
    /// counts, column names, CLR type names and state codes. A run id, a flow name, a workflow id or a URL
    /// from a live tenant must not survive into it, and provider-supplied names are sanitized on the way.
    /// </summary>
    [Fact]
    public void TheReportedLineCarriesNoIdentifierNameOrPayload()
    {
        const string SecretRunId = "0f4a5b6c-7d8e-9f01-2345-6789abcdef01";

        FlowRunProbeRead text = SampleRead();
        text.RunIds = new[] { SecretRunId, RunName };
        text.WorkflowIdValues = new[] { Requested, Other };
        text.ColumnTypes = new[]
        {
            "flowrunid=Guid",
            "see https://contoso.crm123.dynamics.com/api/data/v9.2/flowruns for flows=String"
        };

        FlowRunProbeRead lookup = new FlowRunProbeRead(DataverseSchema.RunWorkflowLookup, "lookup") { WasAttempted = true };
        lookup.Rows = 1;
        lookup.PagesRead = 1;
        lookup.LookupColumnArrived = true;
        lookup.RunIds = new[] { SecretRunId };
        lookup.WorkflowIdValues = new string?[] { null };
        lookup.ColumnTypes = new[] { "workflow=EntityReference" };

        string line = new FlowRunProbeOutcome(text, lookup, Requested).Describe();

        Assert.DoesNotContain(SecretRunId, line);
        Assert.DoesNotContain("0f4a5b6c", line);
        Assert.DoesNotContain(RunName, line);
        Assert.DoesNotContain(Requested, line);
        Assert.DoesNotContain(Other, line);
        Assert.DoesNotContain("contoso.crm123.dynamics.com", line);
        Assert.Contains("[url]", line);
        Assert.Contains("2 row(s)", line);
        Assert.Contains("1 distinct run id(s)", line);
    }

    /// <summary>
    /// Type names are the evidence this spike exists to collect, and they are the one provider fact the
    /// report is allowed to name per column: the shape, never the value. A reduced AliasedValue or picklist
    /// arrives here as its plain CLR type, which is exactly what the gateway guarantees.
    /// </summary>
    [Fact]
    public void AColumnNameIsReportedWithItsClrShapeAndNothingElse()
    {
        Assert.Equal("starttime=DateTime", FlowRunProbeDiagnostics.ColumnTypeText("starttime", new DateTime(2026, 11, 10)));
        Assert.Equal("duration=Int64", FlowRunProbeDiagnostics.ColumnTypeText("duration", 42L));
        Assert.Equal("status=String", FlowRunProbeDiagnostics.ColumnTypeText("status", "Succeeded"));
        Assert.Equal("isprimary=Int32", FlowRunProbeDiagnostics.ColumnTypeText("isprimary", 1));
        Assert.Equal("workflow=Guid", FlowRunProbeDiagnostics.ColumnTypeText("workflow", Guid.Parse(Requested)));
        Assert.Equal("workflowid=empty", FlowRunProbeDiagnostics.ColumnTypeText("workflowid", null));
        Assert.Equal("unnamed=String", FlowRunProbeDiagnostics.ColumnTypeText("   ", "value"));
        Assert.DoesNotContain("Succeeded", FlowRunProbeDiagnostics.ColumnTypeText("status", "Succeeded"));
    }

    private static FlowRunProbeRead SampleRead()
    {
        FlowRunProbeRead read = Attempted();
        read.Rows = 2;
        read.PagesRead = 1;
        read.LookupColumnArrived = true;
        read.WorkflowIdColumnArrived = true;
        read.RunIds = new[] { Requested };
        read.WorkflowIdValues = new[] { Requested };
        read.ColumnTypes = new[] { "flowrunid=Guid", "workflow=EntityReference" };
        return read;
    }

    /// <summary>A source that keeps promising more rows, which is what a ceiling has to survive.</summary>
    private sealed class EndlessRunRowSource : IDataverseRunRowSource
    {
        public int Calls { get; private set; }

        public string? EnvironmentId { get { return FakeRunRowSource.Environment; } }

        public Page<RawRunRecord>? ReadFlowRuns(RunQueryCriteria criteria, PageRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;

            List<RawRunRecord> rows = new List<RawRunRecord>();
            for (int i = 0; i < criteria.PageSize; i++)
            {
                rows.Add(FakeRunRowSource.RunRow("run-" + request.PageNumber + "-" + i));
            }

            return new Page<RawRunRecord>(rows, true, "cookie-" + request.PageNumber);
        }
    }
}



