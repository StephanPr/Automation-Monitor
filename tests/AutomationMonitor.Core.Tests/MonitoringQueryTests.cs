using System;
using AutomationMonitor.Core.Models;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// What the operator asked for: a half-open UTC window, an explicit status selection and a bounded
/// row ceiling. The query reads no clock and holds no booleans that hide a decision.
/// </summary>
public class MonitoringQueryTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(1);

    [Fact]
    public void TheWindowMustMoveForward()
    {
        Assert.Throws<ArgumentException>(() => new MonitoringQuery(new[] { FakeRunRowSource.Key() }, From, From));
        Assert.Throws<ArgumentException>(() => new MonitoringQuery(new[] { FakeRunRowSource.Key() }, To, From));
    }

    [Fact]
    public void WindowBoundsAreStoredAsUtc()
    {
        MonitoringQuery query = new MonitoringQuery(
            new[] { FakeRunRowSource.Key() },
            new DateTimeOffset(2026, 10, 29, 2, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 10, 30, 2, 0, 0, TimeSpan.FromHours(2)));

        Assert.Equal(From, query.FromUtc);
        Assert.Equal(To, query.ToUtc);
    }

    [Fact]
    public void TheWindowIsHalfOpen()
    {
        MonitoringQuery query = new MonitoringQuery(new[] { FakeRunRowSource.Key() }, From, To);

        Assert.True(query.MatchesWindow(From));
        Assert.True(query.MatchesWindow(From.AddMilliseconds(1)));
        Assert.True(query.MatchesWindow(To.AddMilliseconds(-1)));
        Assert.False(query.MatchesWindow(To));
        Assert.False(query.MatchesWindow(From.AddMilliseconds(-1)));
    }

    [Fact]
    public void AMissingInstantIsNeitherInsideNorOutside()
    {
        MonitoringQuery query = new MonitoringQuery(new[] { FakeRunRowSource.Key() }, From, To);

        Assert.False(query.MatchesWindow(null));
    }

    [Fact]
    public void WorkflowsAreDeDuplicatedOnIdentityNotOnSpelling()
    {
        WorkflowKey first = FakeRunRowSource.Key();
        WorkflowKey sameFlow = WorkflowKey.Create(ProviderKind.PowerAutomate, FakeRunRowSource.Environment, "{" + FakeRunRowSource.Workflow.ToUpperInvariant() + "}");

        MonitoringQuery query = new MonitoringQuery(new[] { first, sameFlow, FakeRunRowSource.Key() }, From, To);

        Assert.Single(query.Workflows);
    }

    [Fact]
    public void NullWorkflowsAreIgnoredAndAnEmptySetIsLegal()
    {
        MonitoringQuery query = new MonitoringQuery(new WorkflowKey?[] { null, FakeRunRowSource.Key() }.Length == 0 ? null : new[] { FakeRunRowSource.Key() }, From, To);

        Assert.Single(query.Workflows);
        Assert.Empty(new MonitoringQuery(null, From, To).Workflows);
    }

    [Fact]
    public void NoStatusSelectionMeansEveryGroupIncludingUnknown()
    {
        MonitoringQuery query = new MonitoringQuery(new[] { FakeRunRowSource.Key() }, From, To);

        Assert.False(query.HasStatusFilter);
        Assert.True(query.MatchesStatus(RunStatus.Unknown));
        Assert.True(query.MatchesStatus(RunStatus.Succeeded));
    }

    [Fact]
    public void AStatusSelectionKeepsOnlyThoseGroups()
    {
        MonitoringQuery query = new MonitoringQuery(
            new[] { FakeRunRowSource.Key() }, From, To, new[] { RunStatus.Failed, RunStatus.TimedOut });

        Assert.True(query.HasStatusFilter);
        Assert.True(query.MatchesStatus(RunStatus.Failed));
        Assert.True(query.MatchesStatus(RunStatus.TimedOut));
        Assert.False(query.MatchesStatus(RunStatus.Succeeded));
        Assert.False(query.MatchesStatus(RunStatus.Unknown));
    }

    [Fact]
    public void RowCeilingIsClampedToTheHardLimit()
    {
        Assert.Equal(MonitoringQuery.HardMaxRows, new MonitoringQuery(new[] { FakeRunRowSource.Key() }, From, To, null, int.MaxValue).MaxRows);
        Assert.Equal(MonitoringQuery.DefaultMaxRows, new MonitoringQuery(new[] { FakeRunRowSource.Key() }, From, To, null, 0).MaxRows);
        Assert.Equal(250, new MonitoringQuery(new[] { FakeRunRowSource.Key() }, From, To, null, 250).MaxRows);
    }

    [Fact]
    public void SingleWorkflowQueriesCarryTheirIdentity()
    {
        WorkflowKey key = FakeRunRowSource.Key();

        MonitoringQuery query = MonitoringQuery.ForWorkflow(key, From, To);

        Assert.Single(query.Workflows);
        Assert.Same(query.Workflows[0], key);
        Assert.Throws<ArgumentNullException>(() => MonitoringQuery.ForWorkflow(null!, From, To));
    }
}
