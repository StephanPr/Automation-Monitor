using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Plugin.Connectivity;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The workflow-identity filter exactly as the platform receives it. These tests run against the real
/// <c>Microsoft.Xrm.Sdk</c> query objects built by the plugin's own file, because the shape of this one
/// query is a contract with Dataverse rather than an implementation detail: a filter value the platform
/// cannot type makes it refuse the <em>whole</em> query, and a refused query says nothing about which flows
/// exist. The first live run of this read (2026-11-15) was refused for exactly that reason -
/// "Condition for attribute 'workflow.workflowid': expected argument(s) of type 'System.Guid' but received
/// 'System.Guid[]'" - because a <c>Guid[]</c> handed to a <c>params object[]</c> parameter arrives as one
/// nested value rather than as many ids. A core fake cannot see that mistake; only the SDK type can.
/// </summary>
public sealed class DataverseWorkflowIdentityQuerySdkTests
{
    private static readonly Guid OrganizationId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly ResourceScope Connected = ResourceScope.ForDataverseOrganization(OrganizationId);

    private static string FlowId(int index)
    {
        return index.ToString("x8", CultureInfo.InvariantCulture) + "-1111-2222-3333-444444444444";
    }

    private static List<WorkflowKey> Members(int count)
    {
        List<WorkflowKey> keys = new List<WorkflowKey>();
        for (int index = 1; index <= count; index++)
        {
            keys.Add(WorkflowKey.Create(ProviderKind.PowerAutomate, null, FlowId(index)));
        }

        return keys;
    }

    private static ConditionExpression OnlyCondition(QueryExpression query)
    {
        Assert.Equal(DataverseSchema.WorkflowEntity, query.EntityName);
        Assert.Single(query.Criteria.Conditions);
        return query.Criteria.Conditions[0];
    }

    [Fact]
    public void AFilterOfSeveralIdsSendsOneGuidValuePerIdNotOneNestedArray()
    {
        RecordingOrganizationService service = new RecordingOrganizationService();
        DataverseWorkflowIdentitySource source = new DataverseWorkflowIdentitySource(service);

        source.ReadWorkflowsByIds(
            new List<string> { FlowId(1), FlowId(2), FlowId(3) },
            new PageRequest(1, null),
            CancellationToken.None);

        ConditionExpression condition = OnlyCondition(service.SingleQuery);
        Assert.Equal(ConditionOperator.In, condition.Operator);
        Assert.Equal(3, condition.Values.Count);
        foreach (object value in condition.Values)
        {
            // The assertion the live refusal makes necessary: an element that is a Guid[] is a query the
            // platform rejects outright, and it looks identical behind every core-level fake.
            Assert.IsType<Guid>(value);
            Assert.False(value is Array, "a filter value must never be an array");
            Assert.False(value is IEnumerable, "a filter value must never be a collection");
        }

        List<Guid> asked = new List<Guid>();
        foreach (object value in condition.Values)
        {
            asked.Add((Guid)value);
        }

        Assert.Equal(
            new Guid[] { Guid.Parse(FlowId(1)), Guid.Parse(FlowId(2)), Guid.Parse(FlowId(3)) },
            asked.ToArray());
    }

    [Fact]
    public void TheQueryAsksForTheOneDocumentedColumnAndNeverADistinctRead()
    {
        RecordingOrganizationService service = new RecordingOrganizationService();
        DataverseWorkflowIdentitySource source = new DataverseWorkflowIdentitySource(service);

        source.ReadWorkflowsByIds(new List<string> { FlowId(1) }, new PageRequest(1, null), CancellationToken.None);

        QueryExpression query = service.SingleQuery;
        Assert.Equal(
            new string[] { DataverseSchema.WorkflowId },
            new List<string>(query.ColumnSet.Columns).ToArray());
        Assert.False(query.Distinct, "a distinct query returns no primary key values, and identity is the point of this read");
        Assert.Single(query.Orders);
        Assert.Equal(DataverseSchema.WorkflowId, query.Orders[0].AttributeName);
    }

    [Fact]
    public void ThePagingComesFromTheCallersRequestSoTheCeilingIsHonoured()
    {
        RecordingOrganizationService service = new RecordingOrganizationService();
        DataverseWorkflowIdentitySource source = new DataverseWorkflowIdentitySource(service);

        source.ReadWorkflowsByIds(new List<string> { FlowId(1) }, new PageRequest(2, "cookie-2"), CancellationToken.None);

        PagingInfo paging = service.SingleQuery.PageInfo;
        Assert.NotNull(paging);
        Assert.Equal(DataverseWorkflowIdentityProbe.RowsPerRequest, paging.Count);
        Assert.Equal(2, paging.PageNumber);
        Assert.Equal("cookie-2", paging.PagingCookie);
    }

    [Fact]
    public void NothingIsAskedOfThePlatformWhenNoIdCanBeExpressed()
    {
        RecordingOrganizationService service = new RecordingOrganizationService();
        DataverseWorkflowIdentitySource source = new DataverseWorkflowIdentitySource(service);

        Page<RawWorkflowRecord> page = source.ReadWorkflowsByIds(
            new List<string> { "not-a-workflow-guid", Guid.Empty.ToString() },
            new PageRequest(1, null),
            CancellationToken.None);

        // An empty In list is a query shape this read must never send, so the round trip does not happen.
        Assert.Equal(0, service.Calls);
        Assert.Empty(page.Items);
        Assert.False(page.MoreRecords);
    }

    [Fact]
    public void ACancelledTokenSendsNoQuery()
    {
        RecordingOrganizationService service = new RecordingOrganizationService();
        DataverseWorkflowIdentitySource source = new DataverseWorkflowIdentitySource(service);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => source.ReadWorkflowsByIds(
            new List<string> { FlowId(1) },
            new PageRequest(1, null),
            cancellation.Token));

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public void FiftyNineMembersAreAskedInThreeBatchesOfTwentyTwentyAndNineteen()
    {
        RecordingOrganizationService service = new RecordingOrganizationService();
        service.Respond = RowsFor;
        DataverseWorkflowIdentityProbe probe = new DataverseWorkflowIdentityProbe(
            new DataverseWorkflowIdentitySource(service),
            new FixedClock());

        WorkflowIdentityEvidence evidence = probe.Verify(Connected, Members(59), CancellationToken.None);

        List<int> sent = new List<int>();
        foreach (QueryExpression query in service.Queries)
        {
            ConditionExpression condition = OnlyCondition(query);
            Assert.Equal(ConditionOperator.In, condition.Operator);
            int batch = 0;
            foreach (object value in condition.Values)
            {
                Assert.IsType<Guid>(value);
                batch++;
            }

            sent.Add(batch);
        }

        Assert.Equal(new int[] { 20, 20, 19 }, sent.ToArray());
        Assert.Equal(WorkflowIdentityEvidenceKind.Verified, evidence.Kind);
        Assert.Equal(59, evidence.Counts.ResolvedCount);
        Assert.Equal(0, evidence.Counts.MissingCount);
    }

    [Fact]
    public void ASourceThatNeverStopsPromisingPagesStopsAtTheReadsOwnCeiling()
    {
        RecordingOrganizationService service = new RecordingOrganizationService();
        service.Respond = query =>
        {
            EntityCollection rows = RowsFor(query);
            rows.MoreRecords = true;
            rows.PagingCookie = "cookie-" + service.Calls.ToString(CultureInfo.InvariantCulture);
            return rows;
        };
        DataverseWorkflowIdentityProbe probe = new DataverseWorkflowIdentityProbe(
            new DataverseWorkflowIdentitySource(service),
            new FixedClock());

        WorkflowIdentityEvidence evidence = probe.Verify(Connected, Members(3), CancellationToken.None);

        Assert.True(
            service.Calls <= DataverseWorkflowIdentityProbe.MaximumPagesPerBatch,
            "the page ceiling belongs to the read, not to what the source claims: "
                + service.Calls.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(WorkflowIdentityEvidenceKind.Incomplete, evidence.Kind);
        Assert.False(evidence.CanSupportAssociation);
    }

    /// <summary>
    /// One row per id the query actually asked for, which is what makes the batch sizes observable: a value
    /// the platform could not type never reaches here at all.
    /// </summary>
    private static EntityCollection RowsFor(QueryExpression query)
    {
        EntityCollection rows = new EntityCollection();
        foreach (object value in query.Criteria.Conditions[0].Values)
        {
            Guid id = (Guid)value;
            Entity row = new Entity(DataverseSchema.WorkflowEntity, id);
            row[DataverseSchema.WorkflowId] = id;
            rows.Entities.Add(row);
        }

        return rows;
    }
}
