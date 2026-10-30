using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Membership, lookup and labelling all go through the provider-scoped identity. A display
/// name is mutable and collides, so it must never decide what a watchlist contains.
/// </summary>
public class WatchlistIdentityTests
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

    [Fact]
    public void TwoFlowsWithTheSameNameAreTwoMembers()
    {
        WatchlistBuilder builder = new WatchlistBuilder("list", "Ops", WatchlistMembershipMode.Snapshot);
        builder.AddSolutionFlows(Solution(), new List<WorkflowReference>
        {
            Flow("cccccccc-0000-0000-0000-000000000001", "Same name"),
            Flow("cccccccc-0000-0000-0000-000000000002", "Same name"),
        });

        Watchlist watchlist = builder.Build();

        Assert.Equal(2, watchlist.Members.Count);
        Assert.Same(watchlist.Members[1].Workflow, watchlist.FindMember(watchlist.Members[1].Key)!.Workflow);
    }

    [Fact]
    public void TheSameIdOnAnotherProviderIsAnotherFlow()
    {
        WatchlistBuilder builder = new WatchlistBuilder("list", "Ops", WatchlistMembershipMode.Snapshot);
        builder.AddManualFlow(Flow("dddddddd-0000-0000-0000-000000000001", "Mirror"));
        builder.AddManualFlow(Flow("dddddddd-0000-0000-0000-000000000001", "Mirror", ProviderKind.Unknown));

        Assert.Equal(2, builder.MemberCount);
    }

    [Fact]
    public void AFlowIdSeenInAnotherEnvironmentIsAnotherFlow()
    {
        WorkflowReference inTest = Flow("edededed-0000-0000-0000-000000000001", "Promoted flow");
        WorkflowReference inProd = new WorkflowReference(
            WorkflowKey.Create(ProviderKind.PowerAutomate, "99999999-9999-9999-9999-999999999999", "edededed-0000-0000-0000-000000000001"),
            "Promoted flow",
            WorkflowState.Enabled,
            rawStateCode: 1,
            rawCategory: 5);
        WatchlistBuilder builder = new WatchlistBuilder("list", "Ops", WatchlistMembershipMode.Snapshot);

        builder.AddManualFlow(inTest);
        builder.AddManualFlow(inProd);

        Assert.Equal(2, builder.MemberCount);
        Assert.NotEqual(inTest.Key.Canonical, inProd.Key.Canonical);
    }

    [Fact]
    public void LookupToleratesAMissingOrUnknownKey()
    {
        WatchlistBuilder builder = new WatchlistBuilder("list", "Ops", WatchlistMembershipMode.Snapshot);
        builder.AddManualFlow(Flow("eeeeeeee-0000-0000-0000-000000000001", "Known"));

        Watchlist watchlist = builder.Build();

        Assert.Null(watchlist.FindMember(null));
        Assert.Null(watchlist.FindMember(Flow("eeeeeeee-0000-0000-0000-000000000009", "Unknown").Key));
        Assert.NotNull(watchlist.FindMember(Flow("eeeeeeee-0000-0000-0000-000000000001", "Renamed since").Key));
    }

    [Fact]
    public void RenamingAndChangingModeNeverTouchMembership()
    {
        WatchlistBuilder builder = new WatchlistBuilder("list", "Old name", WatchlistMembershipMode.Snapshot);
        builder.AddSolutionFlows(Solution(), new List<WorkflowReference> { Flow("ffffffff-0000-0000-0000-000000000001", "Keep me") });

        builder.Rename("Ops watchlist");
        builder.Rename("   ");
        builder.SetMode(WatchlistMembershipMode.DynamicSolution);

        Watchlist watchlist = builder.Build();

        Assert.Equal("Ops watchlist", watchlist.Name);
        Assert.Equal(WatchlistMembershipMode.DynamicSolution, watchlist.Mode);
        Assert.Single(watchlist.Members);
    }

    [Fact]
    public void MembersExposeTheirIdentitySoViewsNeverNeedTheName()
    {
        WorkflowReference flow = Flow("abcdefab-0000-0000-0000-000000000001", "Approve invoice");
        WatchlistBuilder builder = new WatchlistBuilder("list", "Ops", WatchlistMembershipMode.Snapshot);
        builder.AddSolutionFlows(Solution(), new List<WorkflowReference> { flow });

        WatchlistMember member = builder.Build().Members[0];

        Assert.Equal(flow.Key, member.Key);
        Assert.Equal("abcdefab-0000-0000-0000-000000000001", member.Key.WorkflowId);
        Assert.Equal(flow.Name, member.Workflow.Name);
    }
}
