using System;
using System.Collections.Generic;
using System.Linq;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Parent/child resolution from run evidence only. The failure modes that matter are the ones a
/// confident wrong answer would cause: a link invented from similar names, a parent outside the
/// window replaced by a made-up one, or a cyclic parent claim walked until the host dies.
/// </summary>
public class RunCorrelationResolverTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    private static WorkflowRun Run(
        string runId,
        string? parentRunId = null,
        RunStatus status = RunStatus.Succeeded,
        DateTimeOffset? startedUtc = null,
        long? durationMs = 60000)
    {
        DateTimeOffset at = startedUtc ?? Start;

        // The end instant is derived from the requested duration so the measured value and the
        // reported one agree; the model prefers measured, and a helper that disagreed with itself
        // would silently test the wrong number.
        bool running = status == RunStatus.Running;
        return RunBuilder.Create(
            runId,
            status,
            status.ToString(),
            at,
            running ? (DateTimeOffset?)null : at.AddMilliseconds(durationMs ?? 0L),
            running ? (long?)null : durationMs,
            parentRunId: parentRunId);
    }

    [Fact]
    public void AParentReferenceResolvesToTheRunItNames()
    {
        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[] { Run("parent"), Run("child", "parent") });

        Assert.Equal(1, result.LinkedRunCount);
        RunTree root = Assert.Single(result.Roots);
        Assert.Equal("parent", root.Run.RunId);
        Assert.Equal(new[] { "child" }, root.Children.Select(c => c.Run.RunId).ToArray());
        Assert.Equal(1, root.DescendantCount);
        Assert.Equal(1, root.Children[0].Depth);
        Assert.True(root.Children[0].ParentWasResolved);
    }

    [Fact]
    public void OneParentCanHaveSeveralChildrenInStartOrder()
    {
        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[]
        {
            Run("parent"),
            Run("c-late", "parent", startedUtc: Start.AddMinutes(20)),
            Run("c-early", "parent", startedUtc: Start.AddMinutes(5))
        });

        RunTree root = Assert.Single(result.Roots);
        Assert.Equal(new[] { "c-early", "c-late" }, root.Children.Select(c => c.Run.RunId).ToArray());
        Assert.Equal(2, root.DescendantCount);
        Assert.Equal(2, result.LinkedRunCount);
    }

    [Fact]
    public void NestingIsWalkedToAnyDepthWithoutRecursion()
    {
        List<WorkflowRun> runs = new List<WorkflowRun> { Run("level-0") };
        for (int i = 1; i <= 500; i++)
        {
            runs.Add(Run("level-" + i, "level-" + (i - 1)));
        }

        RunCorrelationResult result = RunCorrelationResolver.Resolve(runs);

        RunTree node = Assert.Single(result.Roots);
        for (int depth = 0; depth < 500; depth++)
        {
            node = node.Children[0];
        }

        Assert.Equal(500, node.Depth);
        Assert.Equal(500, result.Roots[0].DescendantCount);
        Assert.Equal(500, result.LinkedRunCount);
    }

    [Fact]
    public void AParentOutsideTheWindowLeavesTheChildVisibleAndTheGapReported()
    {
        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[] { Run("child", "parent-not-retrieved") });

        RunTree root = Assert.Single(result.Roots);
        Assert.Equal("child", root.Run.RunId);
        Assert.Equal(0, root.Depth);
        Assert.False(root.ParentWasResolved);
        Assert.Equal(new[] { "parent-not-retrieved" }, result.UnresolvedParentRunIds.ToArray());
        Assert.Equal(0, result.LinkedRunCount);
        Assert.Contains("unresolved parent id(s)", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AParentCycleIsReportedAndNeverWalkedForever()
    {
        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[] { Run("a", "b"), Run("b", "a") });

        Assert.Equal(2, result.Roots.Count);
        Assert.True(result.Roots.All(r => r.IsInCycle));
        Assert.Equal(new[] { "a", "b" }, result.CyclicRunIds.ToArray());
        Assert.All(result.Roots, root => Assert.Empty(root.Children));
    }

    [Fact]
    public void ALongerCycleIsReportedRatherThanResolved()
    {
        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[] { Run("a", "c"), Run("b", "a"), Run("c", "b") });

        Assert.Equal(3, result.CyclicRunIds.Count);
        Assert.Equal(3, result.Roots.Count);
        Assert.Equal(0, result.LinkedRunCount);
    }

    [Fact]
    public void ARunThatIsItsOwnParentIsNotLinkedToItself()
    {
        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[] { Run("self", "self") });

        RunTree root = Assert.Single(result.Roots);
        Assert.Empty(root.Children);
        Assert.Equal(0, result.LinkedRunCount);
        Assert.Equal(new[] { "self" }, result.UnresolvedParentRunIds.ToArray());
    }

    [Fact]
    public void SimilarNamesAndNearIdenticalStartsAreNotEvidenceOfAnything()
    {
        WorkflowKey first = FakeRunRowSource.Key("aaaaaaaa-0000-0000-0000-000000000001");
        WorkflowKey second = FakeRunRowSource.Key("bbbbbbbb-0000-0000-0000-000000000002");
        WorkflowRun parent = RunBuilder.Create("p", workflow: first, startedUtc: Start, endedUtc: Start.AddMinutes(1), durationMs: 60000);
        WorkflowRun lookalike = RunBuilder.Create("q", workflow: second, startedUtc: Start.AddSeconds(1), endedUtc: Start.AddMinutes(1), durationMs: 60000);

        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[] { parent, lookalike });

        Assert.Equal(2, result.Roots.Count);
        Assert.Equal(0, result.LinkedRunCount);
        Assert.True(result.ReportsNoHierarchy);
    }

    [Fact]
    public void ASharedCorrelationIdIsNeverTurnedIntoATree()
    {
        WorkflowKey first = FakeRunRowSource.Key("aaaaaaaa-0000-0000-0000-000000000001");
        WorkflowKey second = FakeRunRowSource.Key("bbbbbbbb-0000-0000-0000-000000000002");
        WorkflowRun one = RunBuilder.Create("one", workflow: first, correlationId: "same-tracking-id", startedUtc: Start, endedUtc: Start.AddMinutes(1), durationMs: 1000);
        WorkflowRun two = RunBuilder.Create("two", workflow: second, correlationId: "same-tracking-id", startedUtc: Start, endedUtc: Start.AddMinutes(1), durationMs: 1000);

        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[] { one, two });

        Assert.Equal(2, result.Roots.Count);
        Assert.Equal(0, result.LinkedRunCount);
    }

    [Fact]
    public void AnIdTwoRunsShareCannotBeUsedAsAParentReference()
    {
        WorkflowKey first = FakeRunRowSource.Key("aaaaaaaa-0000-0000-0000-000000000001");
        WorkflowKey second = FakeRunRowSource.Key("bbbbbbbb-0000-0000-0000-000000000002");
        WorkflowRun twinA = RunBuilder.Create("twin", workflow: first, startedUtc: Start, endedUtc: Start.AddMinutes(1), durationMs: 1000);
        WorkflowRun twinB = RunBuilder.Create("twin", workflow: second, startedUtc: Start, endedUtc: Start.AddMinutes(1), durationMs: 1000);
        WorkflowRun child = RunBuilder.Create("child", workflow: first, parentRunId: "twin", startedUtc: Start, endedUtc: Start.AddMinutes(1), durationMs: 1000);

        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[] { twinA, twinB, child });

        Assert.Equal(new[] { "twin" }, result.AmbiguousRunIds.ToArray());
        Assert.Equal(0, result.LinkedRunCount);
        Assert.Equal(3, result.Roots.Count);
        Assert.Contains("ambiguous run id(s)", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void SubtreeTotalsCountRunsAndAddUpStatedDurations()
    {
        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[]
        {
            Run("parent", durationMs: 10000),
            Run("child", "parent", durationMs: 5000),
            Run("grand", "child", durationMs: 2000)
        });

        RunTree root = Assert.Single(result.Roots);
        Assert.Equal(2, root.DescendantCount);
        Assert.Equal(17000L, root.TotalDurationMs);
        Assert.False(root.HasDurationGap);
        Assert.Equal(1, root.Children[0].DescendantCount);
    }

    [Fact]
    public void ARunningDescendantMakesTheSubtreeTotalIncomplete()
    {
        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[]
        {
            Run("parent", durationMs: 10000),
            Run("child", "parent", RunStatus.Running)
        });

        RunTree root = Assert.Single(result.Roots);
        Assert.Equal(1, root.DescendantCount);
        Assert.True(root.HasDurationGap);
        Assert.Equal(10000L, root.TotalDurationMs);
    }

    [Fact]
    public void TheChildrenLookupIsKeyedOnTheParentsRunId()
    {
        RunCorrelationResult result = RunCorrelationResolver.Resolve(new[] { Run("parent"), Run("child", "parent"), Run("sibling", "parent") });

        IReadOnlyList<WorkflowRun> children = result.ChildrenByParentRunId["parent"];

        Assert.Equal(new[] { "child", "sibling" }, children.Select(r => r.RunId).ToArray());
    }

    [Fact]
    public void NothingIsInventedWhenThereAreNoRuns()
    {
        Assert.Empty(RunCorrelationResolver.Resolve(null).Roots);

        RunCorrelationResult empty = RunCorrelationResolver.Resolve(new WorkflowRun[0]);
        Assert.Empty(empty.Roots);
        Assert.True(empty.ReportsNoHierarchy);
        Assert.Equal(0, empty.LinkedRunCount);
    }

    [Fact]
    public void EveryRetrievedRunAppearsExactlyOnceInTheForest()
    {
        List<WorkflowRun> runs = new List<WorkflowRun> { Run("root") };
        for (int i = 0; i < 40; i++)
        {
            runs.Add(Run("kid-" + i, i % 3 == 0 ? "missing" : "root"));
        }

        runs.Add(Run("loop-1", "loop-2"));
        runs.Add(Run("loop-2", "loop-1"));

        RunCorrelationResult result = RunCorrelationResolver.Resolve(runs);

        List<string> placed = new List<string>();
        Collect(result.Roots, placed);
        Assert.Equal(runs.Count, placed.Count);
        Assert.Equal(runs.Count, new HashSet<string>(placed, StringComparer.Ordinal).Count);
    }

    private static void Collect(IEnumerable<RunTree> nodes, List<string> into)
    {
        foreach (RunTree node in nodes)
        {
            into.Add(node.Run.Key.Canonical);
            Collect(node.Children, into);
        }
    }
}

