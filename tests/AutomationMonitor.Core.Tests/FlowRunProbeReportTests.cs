using System;
using System.Collections.Generic;
using System.Linq;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// AM-36 exists because the probe's decisive result was reachable only through the host log, and XrmToolBox
/// drops plugin entries below its configured log level. What these tests guard is therefore the hand-back
/// itself: every state has its own words, the report is a pure function of the outcome with no host, logger or
/// settings involved, the severity means something instead of being raised to make a file appear, and nothing
/// a tenant owns can ride along on the text.
/// </summary>
public class FlowRunProbeReportTests
{
    private const string Requested = "ab222222-2222-2222-2222-222222222222";
    private const string Other = "cd777777-7777-7777-7777-777777777777";
    private const string RunId = "0f4a5b6c-7d8e-9f01-2345-6789abcdef01";
    private const string RunName = "08584308152949360664437597343";

    private static readonly DateTimeOffset ToUtc = new DateTimeOffset(2026, 11, 10, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FromUtc = new DateTimeOffset(2026, 11, 3, 6, 0, 0, TimeSpan.Zero);

    private static FlowRunProbeRead Answered(string column, string kind, params string?[] runIds)
    {
        return new FlowRunProbeRead(column, kind)
        {
            WasAttempted = true,
            Rows = runIds.Length,
            PagesRead = 1,
            RunIds = runIds,
            WorkflowIdValues = runIds,
            ColumnTypes = new[] { "flowrunid=Guid", "workflow=EntityReference", "workflowid=String" }
        };
    }

    private static FlowRunProbeRead RefusedRead(string column, string kind, ProviderResultState state)
    {
        return new FlowRunProbeRead(column, kind)
        {
            WasAttempted = true,
            Failure = state,
            FailureKind = state == ProviderResultState.Unauthorized ? "SecurityException" : "HttpRequestException"
        };
    }

    private static FlowRunProbeRead NotSent(string column, string kind, string reason)
    {
        return new FlowRunProbeRead(column, kind) { NotAttemptedReason = reason };
    }

    private static FlowRunProbeRead Text(params string?[] runIds)
    {
        return Answered(DataverseSchema.RunWorkflowId, "text", runIds);
    }

    private static FlowRunProbeRead Lookup(params string?[] runIds)
    {
        return Answered(DataverseSchema.RunWorkflowLookup, "lookup", runIds);
    }

    private static FlowRunProbeOutcome Outcome(FlowRunProbeRead stringFilter, FlowRunProbeRead lookupFilter)
    {
        return new FlowRunProbeOutcome(stringFilter, lookupFilter, Requested, FromUtc, ToUtc);
    }

    /// <summary>One fixture per verdict, so a test that names a state is honest about how it got there.</summary>
    private static FlowRunProbeOutcome For(ProbeVerdict verdict, ProviderResultState refusal = ProviderResultState.Unauthorized)
    {
        switch (verdict)
        {
            case ProbeVerdict.NotAttempted:
                return Outcome(
                    NotSent(DataverseSchema.RunWorkflowId, "text", "no workflow identity was selected"),
                    NotSent(DataverseSchema.RunWorkflowLookup, "lookup", "no workflow identity was selected"));
            case ProbeVerdict.Incomplete:
                return Outcome(Text(RunId), NotSent(DataverseSchema.RunWorkflowLookup, "lookup", "requested identity is not guid-shaped"));
            case ProbeVerdict.Refused:
                return Outcome(RefusedRead(DataverseSchema.RunWorkflowId, "text", refusal), Lookup(RunId));
            case ProbeVerdict.BothEmpty:
                return Outcome(Text(), Lookup());
            case ProbeVerdict.TextOnly:
                return Outcome(Text(RunId, RunName), Lookup());
            case ProbeVerdict.LookupOnly:
                return Outcome(Text(), Lookup(RunId));
            case ProbeVerdict.BothAgree:
                return Outcome(Text(RunId), Lookup(RunId));
            default:
                return Outcome(Text(RunId), Lookup(Other));
        }
    }

    private static IEnumerable<ProbeVerdict> AllVerdicts()
    {
        return Enum.GetValues(typeof(ProbeVerdict)).Cast<ProbeVerdict>();
    }

    [Fact]
    public void TheReportIsBuiltFromTheOutcomeAloneWithNoHostInvolved()
    {
        // The point of AM-36 in one test: the text exists whether or not the host logs a single word. Nothing
        // here takes a logger, a settings object or a control, and no state may come back blank.
        foreach (ProbeVerdict verdict in AllVerdicts())
        {
            FlowRunProbeOutcome outcome = For(verdict);
            string report = FlowRunProbeReport.Build(outcome);

            Assert.False(string.IsNullOrWhiteSpace(report));
            Assert.StartsWith(FlowRunProbeReport.Heading, report);
            Assert.Contains("Verdict: " + outcome.VerdictLabel, report);
            Assert.Contains("What it means: ", report);
            Assert.Contains("Same run returned by both queries: ", report);
            Assert.Contains(FlowRunProbeReport.CopyInstruction, report);
        }
    }

    [Fact]
    public void EvenAMissingResultObjectIsStillReportedOnScreen()
    {
        string report = FlowRunProbeReport.Build(null!);

        Assert.Contains("the probe returned no result object at all", report);
        Assert.Contains("no column is ruled out", report);
        Assert.Contains(FlowRunProbeReport.CopyInstruction, report);
    }

    [Fact]
    public void TheWindowTheCountsTheOverlapTheVerdictAndTheFailuresAreAllOnTheBlock()
    {
        string report = FlowRunProbeReport.Build(For(ProbeVerdict.TextOnly));

        Assert.Contains("Read at (UTC): 2026-11-10 06:00:00", report);
        Assert.Contains("Window queried: 2026-11-03 06:00:00 to 2026-11-10 06:00:00 UTC (7 days", report);
        Assert.Contains("at most 5 per query, newest first - a sample, not a read of the whole window", report);
        Assert.Contains("Query A - workflowid, text filter: 2 row(s), 1 page(s), 2 distinct run id(s) - answered", report);
        Assert.Contains("Query B - workflow, lookup filter: 0 row(s), 1 page(s), 0 distinct run id(s) - answered", report);
        Assert.Contains("Same run returned by both queries: 0", report);
        Assert.Contains("Verdict: only the text-column filter returned rows", report);
        Assert.Contains("Failures: none - both queries were sent and the environment answered", report);
    }

    [Fact]
    public void AWindowThatWasNeverRecordedSaysSoInsteadOfInventingOne()
    {
        string report = FlowRunProbeReport.Build(new FlowRunProbeOutcome(Text(), Lookup(), Requested));

        Assert.Contains("Read at (UTC): not recorded", report);
        Assert.Contains("Window queried: not recorded by this run", report);
    }

    [Fact]
    public void TheWindowIsStampedInUtcWhateverZoneTheClockSatIn()
    {
        DateTimeOffset local = new DateTimeOffset(2026, 11, 10, 17, 0, 0, TimeSpan.FromHours(11));
        FlowRunProbeOutcome outcome = new FlowRunProbeOutcome(
            Text(), Lookup(), Requested, local.ToUniversalTime().AddDays(-7), local);

        string report = FlowRunProbeReport.Build(outcome);

        Assert.Contains("2026-11-10 06:00:00", report);
        Assert.Contains("2026-11-03 06:00:00", report);
        Assert.DoesNotContain("17:00:00", report);
    }

    [Fact]
    public void ARefusedQueryCarriesNoRowCountAndShowsUpAsAFailure()
    {
        string report = FlowRunProbeReport.Build(For(ProbeVerdict.Refused));

        Assert.Contains("Query A - workflowid, text filter: refused: unauthorized - no row count, because this query has no answer", report);
        Assert.Contains("Failures: query A refused: unauthorized (SecurityException)", report);
        Assert.DoesNotContain("workflowid, text filter: 0 row(s)", report);
    }

    [Fact]
    public void AQueryThatWasNeverSentSaysWhy()
    {
        string report = FlowRunProbeReport.Build(For(ProbeVerdict.Incomplete));

        Assert.Contains("Query B - workflow, lookup filter: not sent - no row count", report);
        Assert.Contains("Failures: query B not sent (requested identity is not guid-shaped)", report);
        Assert.Contains("Verdict: incomplete - only one of the two queries could be sent", report);
    }

    [Fact]
    public void AReadStoppedAtTheCeilingIsStillCalledASample()
    {
        FlowRunProbeRead text = Text(RunId, RunName);
        text.CeilingReached = true;
        string report = FlowRunProbeReport.Build(Outcome(text, Lookup()));

        Assert.Contains("(stopped at the row ceiling, so this is a sample)", report);
        Assert.DoesNotContain("the whole window", report.Replace("a sample, not a read of the whole window", string.Empty));
    }

    [Fact]
    public void EveryStateHasItsOwnWordsOnScreen()
    {
        List<string> labels = new List<string>();
        List<string> explanations = new List<string>();
        foreach (ProbeVerdict verdict in AllVerdicts())
        {
            FlowRunProbeOutcome outcome = For(verdict);
            labels.Add(outcome.VerdictLabel);
            explanations.Add(outcome.Explanation);

            Assert.False(string.IsNullOrWhiteSpace(outcome.VerdictLabel));
            Assert.False(string.IsNullOrWhiteSpace(outcome.Explanation));
        }

        Assert.Equal(8, labels.Count);
        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(explanations.Count, explanations.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AnEmptyAnswerARefusalAndAnIncompleteRunDoNotReadAlike()
    {
        // The three states the maintainer actually met, and the three the old single sentence collapsed into
        // "inconclusive". Each has to point at a different next action.
        string empty = For(ProbeVerdict.BothEmpty).Explanation;
        string refused = For(ProbeVerdict.Refused).Explanation;
        string incomplete = For(ProbeVerdict.Incomplete).Explanation;

        Assert.Contains("Both queries were sent and the environment answered with no rows", empty);
        Assert.Contains("At least one query was refused", refused);
        Assert.Contains("Only one of the two queries could be sent", incomplete);
        Assert.DoesNotContain(refused, empty);
        Assert.DoesNotContain(incomplete, empty);
        Assert.DoesNotContain(incomplete, refused);
    }

    [Fact]
    public void ZeroRowsIsNeverWordedAsTheFlowHavingNeverRun()
    {
        foreach (ProbeVerdict verdict in AllVerdicts())
        {
            string explanation = For(verdict).Explanation;

            Assert.DoesNotContain("has never executed", explanation);
            Assert.DoesNotContain("the flow has no runs", explanation);
            Assert.DoesNotContain("never ran", explanation);
            Assert.DoesNotContain("proves", explanation);
        }

        Assert.Contains(
            "That is not evidence that the flow has never run",
            For(ProbeVerdict.BothEmpty).Explanation);
    }

    [Theory]
    [InlineData(ProbeVerdict.BothEmpty, ProbeReportSeverity.Information)]
    [InlineData(ProbeVerdict.TextOnly, ProbeReportSeverity.Information)]
    [InlineData(ProbeVerdict.LookupOnly, ProbeReportSeverity.Information)]
    [InlineData(ProbeVerdict.BothAgree, ProbeReportSeverity.Information)]
    [InlineData(ProbeVerdict.Incomplete, ProbeReportSeverity.Warning)]
    [InlineData(ProbeVerdict.NotAttempted, ProbeReportSeverity.Warning)]
    [InlineData(ProbeVerdict.BothDiffer, ProbeReportSeverity.Warning)]
    public void ACleanAnswerIsQuieterThanAFailedExperiment(ProbeVerdict verdict, ProbeReportSeverity expected)
    {
        // A routine empty read stays Information even though the host would drop it, because the copyable
        // window is what carries it now. Only a refusal, a query that could not be sent, or two reads that
        // contradict each other earns a louder level - nothing is raised just to make a log file appear.
        Assert.Equal(expected, For(verdict).Severity);
    }

    [Theory]
    [InlineData(ProviderResultState.Unauthorized, ProbeReportSeverity.Error)]
    [InlineData(ProviderResultState.Unavailable, ProbeReportSeverity.Warning)]
    [InlineData(ProviderResultState.Partial, ProbeReportSeverity.Warning)]
    [InlineData(ProviderResultState.Unsupported, ProbeReportSeverity.Warning)]
    [InlineData(ProviderResultState.Stale, ProbeReportSeverity.Warning)]
    public void ARefusalIsAlwaysLoudAndLostAccessIsLoudest(ProviderResultState state, ProbeReportSeverity expected)
    {
        FlowRunProbeOutcome outcome = For(ProbeVerdict.Refused, state);

        Assert.Equal(ProbeVerdict.Refused, outcome.Verdict);
        Assert.Equal(expected, outcome.Severity);
    }

    [Fact]
    public void TheReportCarriesNoIdentifierNameOrPayload()
    {
        FlowRunProbeRead text = Text(RunId, RunName);
        text.WorkflowIdValues = new[] { Requested, Other };
        text.ColumnTypes = new[]
        {
            "flowrunid=Guid",
            "see https://contoso.crm123.dynamics.com/api/data/v9.2/flowruns for workflowid=String"
        };

        string report = FlowRunProbeReport.Build(Outcome(text, Lookup(RunId)));

        Assert.DoesNotContain(RunId, report);
        Assert.DoesNotContain("0f4a5b6c", report);
        Assert.DoesNotContain(RunName, report);
        Assert.DoesNotContain(Requested, report);
        Assert.DoesNotContain("ab222222", report);
        Assert.DoesNotContain(Other, report);
        Assert.DoesNotContain("contoso.crm123.dynamics.com", report);
        Assert.Contains("[url]", report);
    }

    [Fact]
    public void TheReportKeepsItsWordForARefusalOutOfTheFailureLineToo()
    {
        // The failure line names the exception's type, which is a code, never its message: the probe drops the
        // message where it catches it. A type name that happens to look like text is still only a type name.
        string report = FlowRunProbeReport.Build(For(ProbeVerdict.Refused, ProviderResultState.Unavailable));

        Assert.Contains("query A refused: unavailable (HttpRequestException)", report);
    }

    [Fact]
    public void TheLogLineAndTheReportNeverTellTwoDifferentStories()
    {
        foreach (ProbeVerdict verdict in AllVerdicts())
        {
            FlowRunProbeOutcome outcome = For(verdict);
            string report = FlowRunProbeReport.Build(outcome);

            Assert.Contains(outcome.VerdictLabel, outcome.Describe());
            Assert.Contains(FlowRunProbeDiagnostics.Describe(outcome.StringFilter), report);
            Assert.Contains(FlowRunProbeDiagnostics.Describe(outcome.LookupFilter), report);
        }
    }

    [Theory]
    [InlineData(true, null, "answered", false)]
    [InlineData(false, null, "not sent", true)]
    [InlineData(true, ProviderResultState.Unauthorized, "refused: unauthorized", true)]
    [InlineData(true, ProviderResultState.Unsupported, "refused: unsupported", true)]
    public void OneReadHasExactlyOneStateWording(
        bool attempted,
        ProviderResultState? failure,
        string expected,
        bool expectedInLogLine)
    {
        FlowRunProbeRead read = new FlowRunProbeRead(DataverseSchema.RunWorkflowId, "text")
        {
            WasAttempted = attempted,
            Failure = failure,
            NotAttemptedReason = attempted ? null : "requested identity is not guid-shaped"
        };

        Assert.Equal(expected, read.StateLabel);

        // A read that answered is described in the log by what it returned; a read that could not answer is
        // described by why. The report carries the state word in both cases, so the two sinks can never tell
        // different stories about the same query - and neither one has to invent a second wording.
        Assert.Equal(expectedInLogLine, FlowRunProbeDiagnostics.Describe(read).Contains(expected));

        string report = FlowRunProbeReport.Build(new FlowRunProbeOutcome(
            read,
            new FlowRunProbeRead(DataverseSchema.RunWorkflowLookup, "lookup"),
            Requested,
            FromUtc,
            ToUtc));

        Assert.Contains(expected, report);
    }
}
