using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Operator intent beats discovery: a manual pick or a deliberate exclusion must survive the
/// next discovery pass, and identity (not the name) decides what counts as the same flow.
/// </summary>
public class WatchlistIntentTests
{
    private const string SolutionId = "44444444-4444-4444-4444-444444444444";
    private const string EnvironmentId = "55555555-5555-5555-5555-555555555555";

    private static SolutionReference Solution()
    {
        return new SolutionReference(SolutionId, "staging_team", "Staging Team", "1.0.0.0", false);
    }

    private static WorkflowReference Flow(string workflowId, string name, ProviderKind provider = ProviderKind.PowerAutomate)
    {
        return new WorkflowReference(
            WorkflowKey.Create(provider, EnvironmentId, workflowId),
            name,
            WorkflowState.Enabled,
            rawStateCode: 1,
            rawCategory: 5);
    }

    private static List<WorkflowReference> One(WorkflowReference flow)
    {
        return new List<WorkflowReference> { flow };
    }

    [Fact]
    public void ExcludedFlow_StaysExcludedAfterTheNextDiscovery()
    {
        WorkflowReference daily = Flow("aaaaaaaa-0000-0000-0000-000000000001", "Daily sync");
        WatchlistBuilder builder = new WatchlistBuilder("list", "Staging Team", WatchlistMembershipMode.DynamicSolution);
        builder.AddSolutionFlows(Solution(), One(daily));

        Assert.True(builder.SetIncluded(daily.Key, isIncluded: false));
        int addedAgain = builder.AddSolutionFlows(Solution(), One(daily));

        Watchlist watchlist = builder.Build();
        Assert.Equal(0, addedAgain);
        Assert.Single(watchlist.Members);
        Assert.False(watchlist.Members[0].IsIncluded);
        Assert.Equal(0, watchlist.MonitoredFlowCount);
        Assert.Empty(watchlist.IncludedFlows);
    }

    [Fact]
    public void ReIncludingAFlowRestoresItWithoutReDiscovering()
    {
        WorkflowReference flow = Flow("aaaaaaaa-0000-0000-0000-000000000002", "Weekly report");
        WatchlistBuilder builder = new WatchlistBuilder("list", "Staging Team", WatchlistMembershipMode.Snapshot);
        builder.AddSolutionFlows(Solution(), One(flow));
        builder.SetIncluded(flow.Key, isIncluded: false);

        Assert.True(builder.SetIncluded(flow.Key, isIncluded: true));

        Assert.Equal(1, builder.Build().MonitoredFlowCount);
    }

    [Fact]
    public void AFlowFromBothSourcesRecordsBothOrigins()
    {
        WorkflowReference flow = Flow("aaaaaaaa-0000-0000-0000-000000000003", "Hand picked");
        WatchlistBuilder builder = new WatchlistBuilder("list", "Mixed", WatchlistMembershipMode.Snapshot);

        Assert.True(builder.AddManualFlow(flow));
        Assert.Equal(0, builder.AddSolutionFlows(Solution(), One(flow)));

        Watchlist watchlist = builder.Build();
        Assert.Single(watchlist.Members);
        Assert.True(watchlist.Members[0].Source.HasFlag(WatchlistMemberSource.Manual));
        Assert.True(watchlist.Members[0].Source.HasFlag(WatchlistMemberSource.Solution));
        Assert.Contains(SolutionId, watchlist.Members[0].SourceSolutionIds);
    }

    [Fact]
    public void UnknownFlowCannotBeIncludedOrExcluded()
    {
        WatchlistBuilder builder = new WatchlistBuilder("list", "Mixed", WatchlistMembershipMode.Snapshot);

        Assert.False(builder.SetIncluded(Flow("bbbbbbbb-0000-0000-0000-000000000001", "Elsewhere").Key, isIncluded: false));
    }

    [Fact]
    public void ManualFlowWithoutASolutionStillMonitors()
    {
        WatchlistBuilder builder = new WatchlistBuilder("list", "Custom", WatchlistMembershipMode.Snapshot);
        builder.AddManualFlow(Flow("abababab-0000-0000-0000-000000000001", "Loose flow"));

        Watchlist watchlist = builder.Build();

        Assert.Empty(watchlist.SourceSolutionIds);
        Assert.Empty(watchlist.Members[0].SourceSolutionIds);
        Assert.Single(watchlist.IncludedFlows);
    }
}
