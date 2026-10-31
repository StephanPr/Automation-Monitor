using System;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The execution model's invariants: UTC storage, a duration that is never invented, timing gaps that
/// stay visible, and provenance that travels with every row.
/// </summary>
public class WorkflowRunTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    private static WorkflowRun Build(
        RunStatus status = RunStatus.Succeeded,
        string? rawStatus = "Succeeded",
        DateTimeOffset? startedUtc = null,
        DateTimeOffset? endedUtc = null,
        long? reportedDurationMs = null,
        ProviderResultState coverage = ProviderResultState.Complete)
    {
        return new WorkflowRun(
            RunKey.Create(FakeRunRowSource.Key(), "run-1"),
            status,
            rawStatus,
            startedUtc ?? Start,
            endedUtc,
            reportedDurationMs,
            "Dataverse FlowRun",
            Start.AddHours(4),
            coverage);
    }

    [Fact]
    public void BothStatusSpellingsAreStored()
    {
        WorkflowRun run = Build();

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal("Succeeded", run.RawStatus);
        Assert.Equal("Succeeded", run.StatusLabel);
    }

    [Fact]
    public void UnknownStatusShowsTheRawValueItCameFrom()
    {
        WorkflowRun run = Build(RunStatus.Unknown, "Teleported");

        Assert.Equal(RunStatus.Unknown, run.Status);
        Assert.Equal("Unknown (Teleported)", run.StatusLabel);
        Assert.False(run.IsUnsuccessful);
        Assert.False(RunStatusRules.IsTerminal(run.Status));
    }

    [Fact]
    public void OffsetsAreStoredAsUtc()
    {
        DateTimeOffset local = new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.FromHours(2));
        WorkflowRun run = new WorkflowRun(
            RunKey.Create(FakeRunRowSource.Key(), "run-offset"),
            RunStatus.Succeeded, "Succeeded", local, local.AddMinutes(10), null, "s", Start,
            ProviderResultState.Complete);

        Assert.Equal(Start, run.StartedAtUtc);
        Assert.Equal(TimeSpan.Zero, run.StartedAtUtc!.Value.Offset);
        Assert.Equal(600000L, run.DurationMs);
    }

    [Fact]
    public void DurationIsMeasuredFromTheStoredInstants()
    {
        WorkflowRun run = Build(endedUtc: Start.AddMilliseconds(1500));

        Assert.Equal(1500L, run.DurationMs);
        Assert.Equal(RunDurationBasis.Measured, run.DurationBasis);
        Assert.Equal(1500L, run.MeasuredDurationMs);
    }

    [Fact]
    public void ProviderDurationIsUsedWhenNoEndInstantExistsAndKeptForDiagnosis()
    {
        WorkflowRun run = Build(reportedDurationMs: 4200);

        Assert.Equal(4200L, run.DurationMs);
        Assert.Equal(RunDurationBasis.ProviderReported, run.DurationBasis);
        Assert.Equal(4200L, run.ReportedDurationMs);
        Assert.Null(run.MeasuredDurationMs);
    }

    [Fact]
    public void MeasuredDurationWinsOverAConflictingReportedOneAndTheConflictStaysVisible()
    {
        WorkflowRun run = Build(endedUtc: Start.AddSeconds(90), reportedDurationMs: 120000);

        Assert.Equal(90000L, run.DurationMs);
        Assert.True(run.DurationConflictsWithTimestamps);
        Assert.Equal(120000L, run.ReportedDurationMs);
    }

    [Fact]
    public void ARunningRunHasNoStoredDurationButAnElapsedAgainstAGivenInstant()
    {
        WorkflowRun run = Build(RunStatus.Running, "Running");

        Assert.Null(run.DurationMs);
        Assert.Equal(RunDurationBasis.NotAvailable, run.DurationBasis);
        Assert.True(run.IsRunning);
        Assert.Equal(0L, run.ElapsedMsAt(Start));
        Assert.Equal(180000L, run.ElapsedMsAt(Start.AddMinutes(3)));
        Assert.Null(run.ElapsedMsAt(Start.AddMinutes(-1)));
    }

    [Fact]
    public void TerminalStatusWithoutAnEndInstantIsATimingGap()
    {
        WorkflowRun run = Build(RunStatus.Failed, "Failed");

        Assert.True(run.HasTimingGap);
        Assert.False(run.IsCompleteOutcome);
        Assert.True(run.IsUnsuccessful);
        Assert.Null(run.DurationMs);
    }

    [Fact]
    public void AnEndBeforeAStartYieldsNoDurationAndIsReported()
    {
        WorkflowRun run = Build(RunStatus.Succeeded, "Succeeded", Start, Start.AddMinutes(-5));

        Assert.Null(run.DurationMs);
        Assert.True(run.HasTimingGap);
    }

    [Fact]
    public void AnEndWithNoStartIsAlsoATimingGap()
    {
        WorkflowRun run = new WorkflowRun(
            RunKey.Create(FakeRunRowSource.Key(), "run-no-start"),
            RunStatus.Succeeded, "Succeeded", null, Start, null, "s", Start,
            ProviderResultState.Complete);

        Assert.Null(run.StartedAtUtc);
        Assert.True(run.HasTimingGap);
        Assert.Null(run.DurationMs);
    }

    [Fact]
    public void ASourceThatSentNeitherInstantInventsNothing()
    {
        WorkflowRun run = new WorkflowRun(
            RunKey.Create(FakeRunRowSource.Key(), "run-empty"),
            RunStatus.Unknown, null, null, null, null, "s", Start,
            ProviderResultState.Partial);

        Assert.False(run.HasTimingGap);
        Assert.Null(run.DurationMs);
        Assert.Equal("Unknown", run.StatusLabel);
    }

    [Fact]
    public void CoverageAndCheckedAtTravelWithTheRow()
    {
        WorkflowRun run = Build(coverage: ProviderResultState.Partial);

        Assert.Equal(ProviderResultState.Partial, run.CoverageState);
        Assert.Equal(Start.AddHours(4), run.CheckedAtUtc);
        Assert.Equal("Dataverse FlowRun", run.DataSource);
    }

    [Fact]
    public void BlankStringsBecomeNullRatherThanAnEmptyClaim()
    {
        WorkflowRun run = new WorkflowRun(
            RunKey.Create(FakeRunRowSource.Key(), "run-blank"),
            RunStatus.Unknown, "   ", null, null, null, "   ", Start,
            ProviderResultState.Complete,
            errorCode: " ",
            errorSummary: "\t",
            parentRunId: "  ",
            correlationId: "",
            triggerType: " ");

        Assert.Null(run.RawStatus);
        Assert.Null(run.DataSource);
        Assert.Null(run.ErrorCode);
        Assert.Null(run.ErrorSummary);
        Assert.Null(run.ParentRunId);
        Assert.Null(run.CorrelationId);
        Assert.Null(run.TriggerType);
        Assert.Equal("(unnamed workflow)", run.WorkflowName);
    }

    [Fact]
    public void IdentityComesFromTheKeyAndNotFromAName()
    {
        WorkflowRun run = Build();

        Assert.Equal(ProviderKind.PowerAutomate, run.Provider);
        Assert.Equal(FakeRunRowSource.Environment, run.EnvironmentId);
        Assert.Equal(FakeRunRowSource.Workflow, run.WorkflowId);
        Assert.Equal("run-1", run.RunId);
        Assert.Equal(FakeRunRowSource.Key().Canonical, run.WorkflowKey.Canonical);
    }

    [Fact]
    public void AKeyIsMandatory()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkflowRun(
            null!, RunStatus.Succeeded, "Succeeded", Start, null, null, "s", Start, ProviderResultState.Complete));
    }

    [Fact]
    public void CancelledAndSkippedAreTerminalButNotProblems()
    {
        Assert.True(RunStatusRules.IsTerminal(RunStatus.Cancelled));
        Assert.False(RunStatusRules.IsUnsuccessful(RunStatus.Cancelled));
        Assert.True(RunStatusRules.IsTerminal(RunStatus.Skipped));
        Assert.False(RunStatusRules.IsUnsuccessful(RunStatus.Skipped));
        Assert.True(RunStatusRules.IsUnsuccessful(RunStatus.TimedOut));
        Assert.False(RunStatusRules.IsUnsuccessful(RunStatus.Unknown));
    }
}


