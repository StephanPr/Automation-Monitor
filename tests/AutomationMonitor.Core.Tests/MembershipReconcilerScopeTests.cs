using System;
using System.Collections.Generic;
using System.Linq;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The vocabulary rule in the one membership rule: a discovery that keys its flows by a verified organization
/// cannot express the identity of a member recorded without one, and neither can it express a member that lives
/// in a <em>different</em> organization. Absence from such a read is not evidence that the flow is gone, so the
/// member is kept and labelled unresolved; and a flow whose identity this list does not claim is never added to
/// it. This is what stops the first refresh after AM-44.3a from emptying a dynamic legacy list and rebuilding it
/// under an identity nobody confirmed.
/// </summary>
public sealed class MembershipReconcilerScopeTests
{
    private const string Organization = "88888888-8888-8888-8888-888888888888";
    private const string OtherOrganization = "99999999-9999-9999-9999-999999999999";
    private const string Qualified = "dataverse-organization:" + Organization;
    private const string QualifiedElsewhere = "dataverse-organization:" + OtherOrganization;
    private const string SolutionId = "11111111-1111-1111-1111-111111111111";
    private const string FlowId = "44444444-4444-4444-4444-444444444444";
    private const string OtherFlowId = "55555555-5555-5555-5555-555555555555";

    private static readonly DateTimeOffset CheckedAt = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private static SolutionReference Solution()
    {
        return FakeRowSource.Solution(SolutionId, "Contoso process automation");
    }

    private static WorkflowReference Flow(string workflowId, string? scopeId)
    {
        RawWorkflowRecord record = FakeRowSource.Row(workflowId, DataverseSchema.WorkflowCategoryCloudFlow, "Approval flow");
        WorkflowReference reference;
        Assert.True(DataverseWorkflowNormalizer.TryToReference(record, scopeId, out reference));
        return reference!;
    }

    private static Watchlist List(string? environmentId, params WorkflowReference[] flows)
    {
        WatchlistBuilder builder = new WatchlistBuilder("list", "Scope test", WatchlistMembershipMode.DynamicSolution, null, environmentId);
        builder.AddSolutionFlows(Solution(), flows, ProviderResultState.Complete, "canned discovery", CheckedAt);
        builder.SetCoverage(ProviderResultState.Complete, new[] { "canned discovery" });
        return builder.Build();
    }

    private static MembershipEvidence Read(ProviderResultState state, params WorkflowReference[] flows)
    {
        return new MembershipEvidence(SolutionId, "Contoso process automation", state, flows, 1, flows.Length, CheckedAt);
    }

    [Fact]
    public void AMemberWithoutAScopeSurvivesACompleteReadThatNamesAnOrganization()
    {
        Watchlist list = List(null, Flow(FlowId, null));

        MembershipReconciliationResult result = MembershipReconciler.Reconcile(
            list, new[] { Read(ProviderResultState.Complete, Flow(FlowId, Qualified)) }, true, CheckedAt);

        // The read looked complete, and it still could not have reported this member: different vocabularies.
        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.UnresolvedCount);
        Assert.Equal(1, result.NotAppliedCount);
        WatchlistMember kept = Assert.Single(result.After.Members);
        Assert.True(kept.Key.Scope.IsUnverified);
        Assert.Contains("associate", result.DescribeChanges(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSameQualifiedIdentityIsMatchedAndRetained()
    {
        Watchlist list = List(Qualified, Flow(FlowId, Qualified));

        MembershipReconciliationResult result = MembershipReconciler.Reconcile(
            list, new[] { Read(ProviderResultState.Complete, Flow(FlowId, Qualified)) }, true, CheckedAt);

        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(0, result.UnresolvedCount);
        Assert.Equal(0, result.NotAppliedCount);
        Assert.Equal(1, result.RetainedCount);
    }

    [Fact]
    public void AFlowTheSameOrganizationReportsJoinsTheListThatNamesIt()
    {
        Watchlist list = List(Qualified, Flow(FlowId, Qualified));

        MembershipReconciliationResult result = MembershipReconciler.Reconcile(
            list,
            new[] { Read(ProviderResultState.Complete, Flow(FlowId, Qualified), Flow(OtherFlowId, Qualified)) },
            true,
            CheckedAt);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(0, result.NotAppliedCount);
        Assert.Equal(2, result.After.Members.Count);
        Assert.All(result.After.Members, member => Assert.Equal(Qualified, member.Key.ScopeId));
    }

    [Fact]
    public void AMemberLivingInAnotherOrganizationIsNotRemovedByACompleteReadOfThisOne()
    {
        // The stored list names one organization and holds a member that names another - possible whenever the
        // bytes on disk say so, and the whole point of the vocabulary rule: this read could not have reported
        // that member, so its absence establishes nothing about the flow still existing.
        Watchlist list = List(Qualified, Flow(FlowId, QualifiedElsewhere));

        MembershipReconciliationResult result = MembershipReconciler.Reconcile(
            list, new[] { Read(ProviderResultState.Complete, Flow(FlowId, Qualified)) }, true, CheckedAt);

        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(1, result.UnresolvedCount);
        Assert.Equal(1, result.AddedCount);

        // The member that this read could not express is still there, and the flow it did report joined
        // normally: the vocabulary rule protects the stored identity without freezing the list.
        WatchlistMember foreign = Assert.Single(result.After.Members, member => member.Key.ScopeId == QualifiedElsewhere);
        Assert.Equal(FlowId, foreign.Key.WorkflowId);
    }

    [Fact]
    public void AFlowLivingInAnotherOrganizationIsNotAddedToThisList()
    {
        Watchlist list = List(Qualified, Flow(FlowId, Qualified));

        MembershipReconciliationResult result = MembershipReconciler.Reconcile(
            list,
            new[] { Read(ProviderResultState.Complete, Flow(FlowId, Qualified), Flow(OtherFlowId, QualifiedElsewhere)) },
            true,
            CheckedAt);

        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.NotAppliedCount);
        Assert.DoesNotContain(result.After.Members, member => member.Key.ScopeId == QualifiedElsewhere);
    }
}
