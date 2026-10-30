using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The watchlist is the operator's stated intent. Refreshing discovery must never duplicate a
/// member, and a degraded read must stay labelled on the watchlist itself.
/// </summary>
public class WatchlistBuilderTests
{
    private const string ListId = "watchlist-1";
    private const string SolutionId = "44444444-4444-4444-4444-444444444444";
    private const string OtherSolutionId = "45454545-4545-4545-4545-454545454545";
    private const string EnvironmentId = "55555555-5555-5555-5555-555555555555";

    private static SolutionReference Solution(string id = SolutionId, string uniqueName = "staging_team", string displayName = "Staging Team")
    {
        return new SolutionReference(id, uniqueName, displayName, "1.0.0.0", false);
    }

    private static WorkflowReference Flow(string workflowId, string name)
    {
        return new WorkflowReference(
            WorkflowKey.Create(ProviderKind.PowerAutomate, EnvironmentId, workflowId),
            name,
            WorkflowState.Enabled,
            rawStateCode: 1,
            rawCategory: 5);
    }

    private static WatchlistBuilder Builder()
    {
        return new WatchlistBuilder(ListId, "Staging Team", WatchlistMembershipMode.Snapshot);
    }

    [Fact]
    public void FromSolution_BuildsAWatchlistCarryingTheSolutionContext()
    {
        WatchlistBuilder builder = Builder();
        int added = builder.AddSolutionFlows(Solution(), new List<WorkflowReference> { Flow("aaaaaaaa-0000-0000-0000-000000000001", "Daily sync") });

        Watchlist watchlist = builder.Build();

        Assert.Equal(1, added);
        Assert.Equal(WatchlistMembershipMode.Snapshot, watchlist.Mode);
        Assert.Equal("Staging Team", watchlist.Name);
        Assert.Equal(ListId, watchlist.Id);
        Assert.Single(watchlist.Members);
        Assert.Equal(WatchlistMemberSource.Solution, watchlist.Members[0].Source);
        Assert.True(watchlist.Members[0].IsIncluded);
        Assert.Contains(SolutionId, watchlist.Members[0].SourceSolutionIds);
        Assert.Contains(SolutionId, watchlist.SourceSolutionIds);
        Assert.Equal(1, watchlist.MonitoredFlowCount);
        Assert.Equal(ProviderResultState.Complete, watchlist.State);
        Assert.True(watchlist.HasFullCoverage);
        Assert.Equal(string.Empty, watchlist.CoverageDescription);
    }

    [Fact]
    public void EmptyDiscovery_BuildsAnEmptyWatchlistWithoutInventingNotes()
    {
        Watchlist watchlist = Builder().Build();

        Assert.Empty(watchlist.Members);
        Assert.Equal(0, watchlist.MonitoredFlowCount);
        Assert.Equal(ProviderResultState.Complete, watchlist.State);
    }

    [Fact]
    public void AddingTheSameSolutionTwiceDoesNotDuplicateMembers()
    {
        WatchlistBuilder builder = Builder();
        List<WorkflowReference> flows = new List<WorkflowReference> { Flow("aaaaaaaa-0000-0000-0000-000000000001", "Daily sync") };

        int first = builder.AddSolutionFlows(Solution(), flows);
        int second = builder.AddSolutionFlows(Solution(), flows);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(1, builder.MemberCount);
        Assert.Single(builder.Build().SourceSolutionIds);
    }

    [Fact]
    public void AFlowSharedByTwoSolutionsRecordsBothSolutions()
    {
        WatchlistBuilder builder = new WatchlistBuilder(ListId, "Combined", WatchlistMembershipMode.DynamicSolution);
        builder.AddSolutionFlows(Solution(SolutionId, "solution_a"), new List<WorkflowReference> { Flow("aaaaaaaa-0000-0000-0000-000000000001", "Shared") });
        builder.AddSolutionFlows(Solution(OtherSolutionId, "solution_b"), new List<WorkflowReference> { Flow("aaaaaaaa-0000-0000-0000-000000000001", "Shared") });

        Watchlist watchlist = builder.Build();

        Assert.Single(watchlist.Members);
        Assert.Equal(2, watchlist.Members[0].SourceSolutionIds.Count);
        Assert.Equal(2, watchlist.SourceSolutionIds.Count);
        Assert.Equal(WatchlistMembershipMode.DynamicSolution, watchlist.Mode);
    }

    [Fact]
    public void NullSolutionIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Builder().AddSolutionFlows(null!, new List<WorkflowReference>()));
    }

    [Fact]
    public void PartialDiscoveryKeepsTheFlowsVisibleAndTheLabelHonest()
    {
        WatchlistBuilder builder = Builder();
        builder.AddSolutionFlows(
            Solution(),
            new List<WorkflowReference> { Flow("aaaaaaaa-0000-0000-0000-000000000001", "Daily sync") },
            ProviderResultState.Partial,
            "Only the first page of components was read.");

        Watchlist watchlist = builder.Build();

        Assert.Single(watchlist.Members);
        Assert.Equal(1, watchlist.MonitoredFlowCount);
        Assert.Equal(ProviderResultState.Partial, watchlist.State);
        Assert.False(watchlist.HasFullCoverage);
        Assert.Contains("first page", watchlist.CoverageDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorstCoverageSeenWinsAndEveryNoteIsKeptOnce()
    {
        WatchlistBuilder builder = Builder();
        builder.AddSolutionFlows(
            Solution(SolutionId, "solution_a"),
            new List<WorkflowReference> { Flow("aaaaaaaa-0000-0000-0000-000000000001", "Read fine") });
        builder.AddSolutionFlows(
            Solution(OtherSolutionId, "solution_b"),
            null,
            ProviderResultState.Unauthorized,
            "The connection may not read solution_b.");

        // A later, milder statement must not upgrade the watchlist back to "complete".
        builder.RecordCoverage(ProviderResultState.Partial, "The connection may not read solution_b.");

        Watchlist watchlist = builder.Build();

        Assert.Equal(ProviderResultState.Unauthorized, watchlist.State);
        Assert.False(watchlist.HasFullCoverage);
        Assert.Single(watchlist.CoverageNotes);
        Assert.Contains("solution_b", watchlist.CoverageDescription, StringComparison.Ordinal);

        // A failed second source must never discard what the first source already returned.
        Assert.Single(watchlist.Members);
    }

    [Fact]
    public void BuilderRejectsAnIdItCouldNeverPersist()
    {
        Assert.Throws<ArgumentException>(() => new WatchlistBuilder("  ", "name", WatchlistMembershipMode.Snapshot));
    }
}
