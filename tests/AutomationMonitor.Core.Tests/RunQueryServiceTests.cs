using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The one merge rule: many workflows in, one labelled collection out. These are the cases an
/// operator would otherwise only meet as a wrong number on screen - a run counted twice, a flow that
/// could not be read vanishing, a partial collection presented as a quiet day.
/// </summary>
public class RunQueryServiceTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(1);

    private static RunQueryService Service(FakeRunSource source)
    {
        return new RunQueryService(source, new FixedClock());
    }

    private static MonitoringQuery Query(params WorkflowKey[] workflows)
    {
        return new MonitoringQuery(workflows, From, To);
    }

    /// <summary>A canned read. Partial keeps a reason so coverage text can be asserted too.</summary>
    private static ProviderResult<IReadOnlyList<WorkflowRun>> Read(ProviderResultState state, params WorkflowRun[] runs)
    {
        IReadOnlyList<WorkflowRun> items = new List<WorkflowRun>(runs);
        DateTimeOffset checkedAtUtc = items.Count == 0 ? From : items[0].CheckedAtUtc;
        if (state == ProviderResultState.Complete)
        {
            return ProviderResult<IReadOnlyList<WorkflowRun>>.Complete(items, 1, items.Count, checkedAtUtc);
        }

        return ProviderResult<IReadOnlyList<WorkflowRun>>.Partial(items, 1, items.Count, checkedAtUtc, "page 2 was not returned");
    }

    [Fact]
    public void RunsFromSeveralWorkflowsArriveMergedAndNewestFirst()
    {
        WorkflowKey a = FakeRunRowSource.Key("aaaaaaaa-0000-0000-0000-000000000001");
        WorkflowKey b = FakeRunRowSource.Key("bbbbbbbb-0000-0000-0000-000000000002");
        FakeRunSource source = new FakeRunSource();
        source.Supply(a,
            RunBuilder.Create("a-old", startedUtc: From.AddHours(1), workflow: a),
            RunBuilder.Create("a-new", startedUtc: From.AddHours(9), workflow: a));
        source.Supply(b, RunBuilder.Create("b-mid", startedUtc: From.AddHours(5), workflow: b));

        RunQueryResult result = Service(source).Query(Query(a, b), CancellationToken.None);

        Assert.Equal(3, result.Runs.Count);
        Assert.Equal(new[] { "a-new", "b-mid", "a-old" }, result.Runs.Select(r => r.RunId).ToArray());
        Assert.Equal(2, result.Summary.DistinctWorkflows);
        Assert.Equal(ProviderResultState.Complete, result.State);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void EveryRequestedWorkflowIsAccountedForEvenWhenItReturnedNothing()
    {
        WorkflowKey withRows = FakeRunRowSource.Key("aaaaaaaa-0000-0000-0000-000000000001");
        WorkflowKey empty = FakeRunRowSource.Key("bbbbbbbb-0000-0000-0000-000000000002");
        WorkflowKey denied = FakeRunRowSource.Key("cccccccc-0000-0000-0000-000000000003");
        FakeRunSource source = new FakeRunSource();
        source.Supply(withRows, RunBuilder.Create("run-1", workflow: withRows));
        source.Supply(empty);
        source.Supply(denied, ProviderResult<IReadOnlyList<WorkflowRun>>.Unauthorized(0, From, "no privilege"));

        RunQueryResult result = Service(source).Query(Query(withRows, empty, denied), CancellationToken.None);

        Assert.Equal(3, result.Coverage.Count);
        Assert.Equal(1, result.Runs.Count);
        Assert.Equal(
            new[] { withRows.Canonical, empty.Canonical, denied.Canonical },
            result.Coverage.Select(c => c.Workflow.Canonical).ToArray());
        Assert.Equal(
            new[] { ProviderResultState.Complete, ProviderResultState.Complete, ProviderResultState.Unauthorized },
            result.Coverage.Select(c => c.State).ToArray());
        Assert.Equal(new[] { empty.Canonical, denied.Canonical }, result.WorkflowsWithoutRows().Select(k => k.Canonical).ToArray());
        Assert.Equal(new[] { empty.Canonical, denied.Canonical }, result.WorkflowsWithoutData().Select(k => k.Canonical).ToArray());
        Assert.Equal(ProviderResultState.Unauthorized, result.State);
    }

    [Fact]
    public void OneFailingWorkflowNeverDiscardsAnotherFlowsRows()
    {
        WorkflowKey good = FakeRunRowSource.Key("aaaaaaaa-0000-0000-0000-000000000001");
        WorkflowKey bad = FakeRunRowSource.Key("bbbbbbbb-0000-0000-0000-000000000002");
        FakeRunSource source = new FakeRunSource();
        source.Supply(good, RunBuilder.Create("run-1", workflow: good));
        source.UnlistedState = ProviderResultState.Unavailable;

        RunQueryResult result = Service(source).Query(Query(good, bad), CancellationToken.None);

        Assert.Equal(1, result.Runs.Count);
        Assert.Equal(ProviderResultState.Unavailable, result.State);
        Assert.False(result.IsComplete);
        Assert.Contains("UNAVAILABLE", result.DescribeCoverage(), StringComparison.Ordinal);
    }


    [Fact]
    public void TheSameRunSeenTwiceCollapsesToOneRow()
    {
        WorkflowKey flow = FakeRunRowSource.Key();
        FakeRunSource source = new FakeRunSource();
        source.Supply(flow, Read(ProviderResultState.Complete,
            RunBuilder.Create("run-1", RunStatus.Running, "Running", checkedAtUtc: From.AddMinutes(1)),
            RunBuilder.Create("run-1", RunStatus.Running, "Running", checkedAtUtc: From.AddMinutes(2))));

        RunQueryResult result = Service(source).Query(Query(flow), CancellationToken.None);

        Assert.Single(result.Runs);
        Assert.Equal(1, result.DuplicateRunCount);
        Assert.Equal(0, result.ConflictCount);
    }

    [Fact]
    public void AFinishedObservationSupersedesARunningOne()
    {
        WorkflowKey flow = FakeRunRowSource.Key();
        FakeRunSource source = new FakeRunSource();
        source.Supply(flow, Read(ProviderResultState.Complete,
            RunBuilder.Create("run-1", RunStatus.Running, "Running", checkedAtUtc: From.AddMinutes(9)),
            RunBuilder.Create("run-1", RunStatus.Failed, "Failed", checkedAtUtc: From.AddMinutes(1))));

        RunQueryResult result = Service(source).Query(Query(flow), CancellationToken.None);

        Assert.Equal(RunStatus.Failed, Assert.Single(result.Runs).Status);
    }

    [Fact]
    public void TwoTerminalOutcomesForOneRunAreSurfacedNotAveraged()
    {
        WorkflowKey flow = FakeRunRowSource.Key();
        FakeRunSource source = new FakeRunSource();
        source.Supply(flow, Read(ProviderResultState.Complete,
            RunBuilder.Create("run-1", RunStatus.Succeeded, "Succeeded", checkedAtUtc: From.AddMinutes(1)),
            RunBuilder.Create("run-1", RunStatus.Failed, "Failed", checkedAtUtc: From.AddMinutes(9))));

        RunQueryResult result = Service(source).Query(Query(flow), CancellationToken.None);

        Assert.Equal(RunStatus.Failed, Assert.Single(result.Runs).Status);
        Assert.Equal(1, result.ConflictCount);
        Assert.Contains("conflicting observation(s)", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheQueryIsReappliedSoASourceThatIgnoredItCannotWidenTheAnswer()
    {
        WorkflowKey flow = FakeRunRowSource.Key();
        FakeRunSource source = new FakeRunSource();
        source.Supply(flow, Read(ProviderResultState.Complete,
            RunBuilder.Create("inside", startedUtc: From.AddHours(3)),
            RunBuilder.Create("before", startedUtc: From.AddHours(-2)),
            RunBuilder.Create("at-end", startedUtc: To),
            RunBuilder.WithoutInstants("undated")));

        RunQueryResult result = Service(source).Query(Query(flow), CancellationToken.None);

        Assert.Equal(new[] { "inside", "undated" }, result.Runs.Select(r => r.RunId).ToArray());
        Assert.Equal(2, result.WindowExcludedCount);
        Assert.Equal(1, result.UndatedRunCount);
        Assert.Contains("run(s) with no start time kept", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AStatusSelectionFiltersTheMergedRowsAndStillCountsTheRest()
    {
        WorkflowKey flow = FakeRunRowSource.Key();
        FakeRunSource source = new FakeRunSource();
        source.Supply(flow, Read(ProviderResultState.Complete,
            RunBuilder.Create("ok", RunStatus.Succeeded),
            RunBuilder.Create("bad", RunStatus.Failed),
            RunBuilder.Create("odd", RunStatus.Unknown, "Teleported")));

        RunQueryResult failures = Service(source).Query(
            new MonitoringQuery(new[] { flow }, From, To, new[] { RunStatus.Failed }), CancellationToken.None);

        Assert.Equal(new[] { "bad" }, failures.Runs.Select(r => r.RunId).ToArray());
        Assert.Equal(2, failures.StatusExcludedCount);
        Assert.Equal(3, failures.RowsReceived);
    }

    [Fact]
    public void TheRowCeilingTrimsAndSaysHowManyItDropped()
    {
        WorkflowKey flow = FakeRunRowSource.Key();
        FakeRunSource source = new FakeRunSource();
        source.Supply(flow, Read(ProviderResultState.Complete,
            RunBuilder.Create("r1", startedUtc: From.AddHours(1)),
            RunBuilder.Create("r2", startedUtc: From.AddHours(2)),
            RunBuilder.Create("r3", startedUtc: From.AddHours(3))));

        RunQueryResult result = Service(source).Query(
            new MonitoringQuery(new[] { flow }, From, To, null, 2), CancellationToken.None);

        Assert.Equal(new[] { "r3", "r2" }, result.Runs.Select(r => r.RunId).ToArray());
        Assert.Equal(1, result.TruncatedRunCount);
        Assert.Contains("beyond the row ceiling", result.DescribeCoverage(), StringComparison.Ordinal);
    }


    [Fact]
    public void CancellingPartWayKeepsTheRowsThatArrivedAndSaysWhatIsMissing()
    {
        WorkflowKey first = FakeRunRowSource.Key("aaaaaaaa-0000-0000-0000-000000000001");
        WorkflowKey second = FakeRunRowSource.Key("bbbbbbbb-0000-0000-0000-000000000002");
        using CancellationTokenSource tokens = new CancellationTokenSource();
        FakeRunSource source = new FakeRunSource();
        source.Supply(first, RunBuilder.Create("run-1", workflow: first));
        source.OnRead = workflow => tokens.Cancel();

        RunQueryResult result = Service(source).Query(Query(first, second), tokens.Token);

        Assert.True(result.WasCancelled);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.Runs.Count);
        Assert.Equal(2, result.Coverage.Count);
        Assert.Equal(ProviderResultState.Unavailable, result.Coverage[1].State);
        Assert.Contains("cancelled", result.Coverage[1].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CANCELLED", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AProviderThatThrowsCancellationIsRecordedInsteadOfLosingTheRefresh()
    {
        WorkflowKey flow = FakeRunRowSource.Key();
        FakeRunSource source = new FakeRunSource { ThrowCancellation = true };

        RunQueryResult result = Service(source).Query(Query(flow), CancellationToken.None);

        Assert.True(result.WasCancelled);
        Assert.Equal(ProviderResultState.Unavailable, result.State);
        Assert.Empty(result.Runs);
    }

    [Fact]
    public void AnEmptyRequestIsACompleteEmptyResultNotAnError()
    {
        RunQueryResult result = Service(new FakeRunSource()).Query(new MonitoringQuery(null, From, To), CancellationToken.None);

        Assert.Equal(ProviderResultState.Complete, result.State);
        Assert.Empty(result.Runs);
        Assert.Empty(result.Coverage);
        Assert.Equal(0, result.Summary.TotalRuns);
        Assert.Null(result.Summary.FailureRate);
    }

    [Fact]
    public void EveryWorkflowIsAskedExactlyOnceWithTheSameQuery()
    {
        WorkflowKey a = FakeRunRowSource.Key("aaaaaaaa-0000-0000-0000-000000000001");
        WorkflowKey b = FakeRunRowSource.Key("bbbbbbbb-0000-0000-0000-000000000002");
        FakeRunSource source = new FakeRunSource();

        Service(source).Query(Query(a, b, a), CancellationToken.None);

        Assert.Equal(2, source.Requested.Count);
        Assert.Same(source.ObservedQueries[0], source.ObservedQueries[1]);
    }

    [Fact]
    public void MergingRunsDirectlyUsesTheSameIdentityRuleAsTheQuery()
    {
        WorkflowKey flow = FakeRunRowSource.Key();
        WorkflowRun stale = RunBuilder.Create("run-1", RunStatus.Running, "Running", checkedAtUtc: From, workflow: flow);
        WorkflowRun fresh = RunBuilder.Create("run-1", RunStatus.TimedOut, "TimedOut", checkedAtUtc: From.AddMinutes(5), workflow: flow);
        WorkflowRun other = RunBuilder.Create("run-2", RunStatus.Succeeded, "Succeeded", startedUtc: From.AddHours(12), checkedAtUtc: From, workflow: flow);

        IReadOnlyList<WorkflowRun> merged = RunQueryService.MergeRuns(new[] { stale, other, fresh });

        Assert.Equal(new[] { "run-2", "run-1" }, merged.Select(r => r.RunId).ToArray());
        Assert.Equal(RunStatus.TimedOut, merged[1].Status);
    }

    [Fact]
    public void TheSummaryCarriesTheQualifierOfTheReadsItCameFrom()
    {
        WorkflowKey good = FakeRunRowSource.Key("aaaaaaaa-0000-0000-0000-000000000001");
        WorkflowKey partial = FakeRunRowSource.Key("bbbbbbbb-0000-0000-0000-000000000002");
        FakeRunSource source = new FakeRunSource();
        source.Supply(good, RunBuilder.Create("g1", RunStatus.Succeeded, workflow: good));
        source.Supply(partial, Read(ProviderResultState.Partial, RunBuilder.Create("p1", RunStatus.Failed, workflow: partial)));

        RunQueryResult result = Service(source).Query(Query(good, partial), CancellationToken.None);

        Assert.Equal(ProviderResultState.Partial, result.State);
        Assert.True(result.Summary.IsPartial);
        Assert.Equal(2, result.Summary.TotalRuns);
        Assert.Equal(0.5d, result.Summary.FailureRate);
        Assert.Contains("PARTIAL", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void CoverageLinesComeFromTheSharedRenderer()
    {
        WorkflowKey denied = FakeRunRowSource.Key("cccccccc-0000-0000-0000-000000000003");
        FakeRunSource source = new FakeRunSource { UnlistedState = ProviderResultState.Unauthorized };

        RunQueryResult result = Service(source).Query(Query(denied), CancellationToken.None);

        string line = result.Coverage[0].Describe();
        Assert.Contains("NOT AUTHORIZED", line, StringComparison.Ordinal);
        Assert.Contains("checked", line, StringComparison.Ordinal);
        Assert.Contains(SourceStatus.Label(ProviderResultState.Unauthorized), line, StringComparison.Ordinal);
    }

    [Fact]
    public void AWorkflowWhoseProviderHasNoRunProviderIsUnsupportedAndNeverAsked()
    {
        WorkflowKey logicApp = FakeRunRowSource.Key(
            "dddddddd-0000-0000-0000-000000000004",
            environmentId: "subscription-alpha",
            provider: ProviderKind.LogicAppsConsumption);
        FakeRunSource source = new FakeRunSource();
        source.Supply(logicApp, RunBuilder.Create("should-not-be-read", workflow: logicApp));

        RunQueryResult result = Service(source).Query(Query(logicApp), CancellationToken.None);

        Assert.Equal(ProviderResultState.Unsupported, result.State);
        Assert.Empty(result.Runs);
        Assert.Empty(source.Requested);
        Assert.Contains("logicapps-consumption", result.Coverage[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheQueryServiceRefusesAMissingSourceOrQuery()
    {
        Assert.Throws<ArgumentNullException>(() => new RunQueryService(null!, null));
        Assert.Throws<ArgumentNullException>(() => Service(new FakeRunSource()).Query(null!, CancellationToken.None));
    }
}
