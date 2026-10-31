using System;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The one place a Dataverse <c>flowrun</c> row becomes a normalized run. Every case here is a shape
/// the table can actually produce, taken from the entity reference recorded in
/// docs/provider-capabilities.md rather than from a guess about what a run should look like.
/// </summary>
public class DataverseRunNormalizerTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    private static RunRowDisposition ToRun(RawRunRecord? row, out WorkflowRun? run, WorkflowKey? requested = null, string? name = "Approvals")
    {
        return DataverseRunNormalizer.TryToRun(
            row, requested ?? FakeRunRowSource.Key(), name, DataverseFlowRunSource.SourceName, Start, ProviderResultState.Complete, out run);
    }

    [Theory]
    [InlineData("Succeeded", RunStatus.Succeeded)]
    [InlineData("succeeded", RunStatus.Succeeded)]
    [InlineData("  Succeeded  ", RunStatus.Succeeded)]
    [InlineData("Failed", RunStatus.Failed)]
    [InlineData("Faulted", RunStatus.Failed)]
    [InlineData("Aborted", RunStatus.Failed)]
    [InlineData("TimedOut", RunStatus.TimedOut)]
    [InlineData("Timeout", RunStatus.TimedOut)]
    [InlineData("Cancelled", RunStatus.Cancelled)]
    [InlineData("Skipped", RunStatus.Skipped)]
    [InlineData("AtomicsSkipped", RunStatus.Skipped)]
    [InlineData("Running", RunStatus.Running)]
    [InlineData("Waiting", RunStatus.Running)]
    [InlineData("Suspended", RunStatus.Running)]
    [InlineData("Paused", RunStatus.Running)]
    [InlineData("InProgress", RunStatus.Running)]
    [InlineData("Teleported", RunStatus.Unknown)]
    [InlineData("", RunStatus.Unknown)]
    [InlineData("   ", RunStatus.Unknown)]
    [InlineData(null, RunStatus.Unknown)]
    public void StatusTextBecomesAGroupWithoutLosingItself(string? raw, RunStatus expected)
    {
        WorkflowRun? run;
        Assert.Equal(RunRowDisposition.Normalized, ToRun(FakeRunRowSource.RunRow("run-1", raw), out run));

        Assert.Equal(expected, run!.Status);
        Assert.Equal(string.IsNullOrWhiteSpace(raw) ? null : raw.Trim(), run.RawStatus);
        Assert.Equal(RunStatusRules.Label(expected, run.RawStatus), run.StatusLabel);
    }

    [Fact]
    public void UnknownStatusIsNeverCountedAsASuccessOrAProblem()
    {
        WorkflowRun? run;
        ToRun(FakeRunRowSource.RunRow("run-x", "WeirdState"), out run);

        Assert.Equal(RunStatus.Unknown, run!.Status);
        Assert.False(run.IsUnsuccessful);
        Assert.False(RunStatusRules.IsTerminal(run.Status));
    }

    [Fact]
    public void TheRunNameIsTheIdentityBecauseThatIsWhatParentrunidPointsAt()
    {
        WorkflowRun? run;
        ToRun(FakeRunRowSource.RunRow("08583000124706209406282721132CU56", rowId: "eeeeeeee-1111-1111-1111-111111111111"), out run);

        Assert.Equal("08583000124706209406282721132cu56", run!.RunId);
        Assert.Equal("eeeeeeee-1111-1111-1111-111111111111", run.RunRecordId);
    }

    [Fact]
    public void ARowWithOnlyItsRecordIdStillCarriesIdentity()
    {
        WorkflowRun? run;

        Assert.Equal(RunRowDisposition.Normalized, ToRun(FakeRunRowSource.RunRow(null, rowId: "eeeeeeee-2222-2222-2222-222222222222"), out run));
        Assert.Equal("eeeeeeee-2222-2222-2222-222222222222", run!.RunId);
    }

    [Fact]
    public void ARowWithNoIdAtAllCannotBeMerged()
    {
        WorkflowRun? run;
        RawRunRecord row = FakeRunRowSource.RunRow("   ");
        row.RunRecordId = null;

        Assert.Equal(RunRowDisposition.MissingIdentity, ToRun(row, out run));
        Assert.Null(run);
        Assert.Equal(RunRowDisposition.MissingIdentity, ToRun(null, out run));
    }

    [Fact]
    public void ARowForAnotherFlowIsRefusedRatherThanMergedIntoTheRequestedFlow()
    {
        WorkflowRun? run;
        RawRunRecord row = FakeRunRowSource.RunRow("run-other", workflowId: "99999999-9999-9999-9999-999999999999");

        Assert.Equal(RunRowDisposition.BelongsToAnotherWorkflow, ToRun(row, out run));
        Assert.Null(run);
    }

    [Fact]
    public void IdSpellingDoesNotDecideWhetherARowBelongsToTheFlow()
    {
        WorkflowRun? run;

        Assert.Equal(RunRowDisposition.Normalized, ToRun(FakeRunRowSource.RunRow("run-1", workflowId: "{" + FakeRunRowSource.Workflow.ToUpperInvariant() + "}"), out run));
        Assert.Equal(FakeRunRowSource.Workflow, run!.WorkflowId);
    }

    [Fact]
    public void ABlankWorkflowColumnIsNotEvidenceOfAnotherFlow()
    {
        WorkflowRun? run;

        Assert.Equal(RunRowDisposition.Normalized, ToRun(FakeRunRowSource.RunRow("run-1", workflowId: " "), out run));
    }


    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, null)]
    [InlineData(null, null)]
    public void ThePrimaryRunMarkerIsAPicklistNotABoolean(int? code, bool? expected)
    {
        Assert.Equal(expected, DataverseRunNormalizer.NormalizePrimaryFlag(code, null));
    }

    [Fact]
    public void ABooleanFromAnotherReaderIsHonouredAndAbsenceIsNotFalse()
    {
        Assert.True(DataverseRunNormalizer.NormalizePrimaryFlag(0, true));
        Assert.True(DataverseRunNormalizer.NormalizePrimaryFlag(null, true));
        Assert.False(DataverseRunNormalizer.NormalizePrimaryFlag(null, false));
        Assert.Null(DataverseRunNormalizer.NormalizePrimaryFlag(null, null));
    }

    [Fact]
    public void DurationComesFromTheTimestampsAndTheBigIntColumnStaysVisible()
    {
        WorkflowRun? run;
        ToRun(FakeRunRowSource.RunRow("run-1", "Succeeded", Start, Start.AddSeconds(90), 90000), out run);

        Assert.Equal(90000L, run!.DurationMs);
        Assert.Equal(RunDurationBasis.Measured, run.DurationBasis);
        Assert.Equal(90000L, run.ReportedDurationMs);
    }

    [Fact]
    public void MillisecondsAreNeverRescaled()
    {
        WorkflowRun? run;
        ToRun(FakeRunRowSource.RunRow("run-1", "Succeeded", null, null, 1500), out run);

        Assert.Equal(1500L, run!.DurationMs);
        Assert.Equal(RunDurationBasis.ProviderReported, run.DurationBasis);
    }

    [Fact]
    public void TheRequestedWorkflowSuppliesTheIdentityScope()
    {
        WorkflowKey other = FakeRunRowSource.Key(environmentId: "77777777-7777-7777-7777-777777777777");
        WorkflowRun? run;

        ToRun(FakeRunRowSource.RunRow("run-1"), out run, other);

        Assert.Equal(other.Canonical, run!.WorkflowKey.Canonical);
        Assert.Equal("77777777-7777-7777-7777-777777777777", run.EnvironmentId);
    }

    [Fact]
    public void TheWorkflowNameComesFromTheRequestAndIsNotInvented()
    {
        WorkflowRun? named;
        WorkflowRun? unnamed;
        ToRun(FakeRunRowSource.RunRow("run-1"), out named, name: "Nightly sync");
        ToRun(FakeRunRowSource.RunRow("run-2"), out unnamed, name: null);

        Assert.Equal("Nightly sync", named!.WorkflowName);
        Assert.Equal("(unnamed workflow)", unnamed!.WorkflowName);
    }

    [Fact]
    public void ParentAndTrackingIdentifiersAreCarriedVerbatim()
    {
        WorkflowRun? run;
        RawRunRecord row = FakeRunRowSource.RunRow("run-child", "Failed", Start, Start.AddMinutes(1), 60000);
        row.ParentRunId = "parent-run-name";
        row.CorrelationId = "tracking-42";
        row.ErrorCode = "BadRequest";
        row.ErrorText = "The connection call failed.";
        row.TriggerType = "Recurrence";

        ToRun(row, out run);

        Assert.Equal("parent-run-name", run!.ParentRunId);
        Assert.Equal("tracking-42", run.CorrelationId);
        Assert.Equal("BadRequest", run.ErrorCode);
        Assert.Equal("The connection call failed.", run.ErrorSummary);
        Assert.Equal("Recurrence", run.TriggerType);
    }

    [Fact]
    public void A20000CharacterErrorMemoIsTrimmedNotDropped()
    {
        WorkflowRun? run;
        RawRunRecord row = FakeRunRowSource.RunRow("run-1", "Failed");
        row.ErrorText = new string('x', 25000);

        ToRun(row, out run);

        Assert.Equal(DataverseRunNormalizer.MaxErrorSummaryLength + 3, run!.ErrorSummary!.Length);
        Assert.EndsWith("...", run.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void CoverageAndCheckedAtAreStampedFromTheReadNotFromTheRow()
    {
        WorkflowRun? run;
        Assert.Equal(RunRowDisposition.Normalized, DataverseRunNormalizer.TryToRun(
            FakeRunRowSource.RunRow("run-1"), FakeRunRowSource.Key(), null, DataverseFlowRunSource.SourceName,
            Start.AddHours(3), ProviderResultState.Partial, out run));

        Assert.Equal(ProviderResultState.Partial, run!.CoverageState);
        Assert.Equal(Start.AddHours(3), run.CheckedAtUtc);
        Assert.Equal("Dataverse FlowRun", run.DataSource);
    }

    [Fact]
    public void TheFilterVocabularyCoversEveryGroupThatCanBeDisplayed()
    {
        RunStatus[] displayable =
        {
            RunStatus.Running, RunStatus.Succeeded, RunStatus.Failed,
            RunStatus.Cancelled, RunStatus.Skipped, RunStatus.TimedOut
        };

        foreach (RunStatus status in displayable)
        {
            Assert.NotEmpty(StatusNormalizer.ProviderValuesFor(status));
        }

        // Unknown has no provider spelling: a status filter cannot ask for "whatever we do not know".
        Assert.Empty(StatusNormalizer.ProviderValuesFor(RunStatus.Unknown));
    }

    [Fact]
    public void TheFilterVocabularyRoundTripsThroughTheNormalizer()
    {
        foreach (string value in StatusNormalizer.ProviderValuesFor(RunStatus.Failed))
        {
            Assert.Equal(RunStatus.Failed, StatusNormalizer.Normalize(value));
        }
    }
}
