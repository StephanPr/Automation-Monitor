using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Building the in-memory watchlist source: what one solution's rows turn into, and how the
/// result is described when the rows are less than perfect.
/// </summary>
public class DataverseFlowDiscoveryTests
{
    private const string SolutionId = "44444444-4444-4444-4444-444444444444";

    private static DataverseSolutionFlowSource Source(FakeRowSource rows)
    {
        return new DataverseSolutionFlowSource(rows, new FixedClock());
    }

    [Fact]
    public void BlankSolutionIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Source(new FakeRowSource()).GetSolutionFlows("  ", CancellationToken.None));
    }

    [Fact]
    public void SolutionIsPassedThroughAndCancellationStopsTheNextRoundTrip()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.WorkflowPages.Add(FakeRowSource.Of(true, "cookie-1", FakeRowSource.Row("aaaaaaaa-0000-0000-0000-000000000001", 5, "First")));
        rows.WorkflowPages.Add(FakeRowSource.Of(false, null, FakeRowSource.Row("aaaaaaaa-0000-0000-0000-000000000002", 5, "Second")));
        using CancellationTokenSource tokens = new CancellationTokenSource();

        ProviderResult<CloudFlowSelectionResult> result = Source(rows).GetSolutionFlows(SolutionId, tokens.Token);

        Assert.Equal(SolutionId, rows.ObservedSolutionId);
        Assert.Equal(2, rows.WorkflowCalls);
        Assert.Equal(2, result.Value!.Flows.Count);
        Assert.Equal("cookie-1", rows.WorkflowRequests[1].Cookie);
        Assert.Null(rows.WorkflowRequests[0].Cookie);

        tokens.Cancel();
        int callsBefore = rows.WorkflowCalls;
        Assert.Throws<OperationCanceledException>(() => Source(rows).GetSolutionFlows(SolutionId, tokens.Token));
        Assert.Equal(callsBefore, rows.WorkflowCalls);
    }

    [Fact]
    public void DuplicatesAndOtherComponentKindsAreCountedNotHidden()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.WorkflowPages.Add(FakeRowSource.Of(
            false,
            null,
            FakeRowSource.Row("bbbbbbbb-0000-0000-0000-000000000001", 5, "Nightly export"),
            FakeRowSource.Row("{BBBBBBBB-0000-0000-0000-000000000001}", 5, "Nightly export (copy)"),
            FakeRowSource.Row("bbbbbbbb-0000-0000-0000-000000000002", 1, "Approve leave"),
            FakeRowSource.Row(null, 5, "Orphan row")));

        CloudFlowSelectionResult selection = Source(rows).GetSolutionFlows(SolutionId, CancellationToken.None).Value!;

        Assert.Single(selection.Flows);
        Assert.Equal(4, selection.RowsRead);
        Assert.Equal(1, selection.DuplicateCount);
        Assert.Equal(1, selection.NonCloudCount);
        Assert.Equal(1, selection.MissingIdCount);
        Assert.Contains("1 duplicate(s) removed", selection.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void SolutionWithOnlyClassicWorkflowsIsCompleteAndExplainsItself()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.WorkflowPages.Add(FakeRowSource.Of(false, null, FakeRowSource.Row("cccccccc-0000-0000-0000-000000000001", 2, "Case BPF")));

        ProviderResult<CloudFlowSelectionResult> result = Source(rows).GetSolutionFlows(SolutionId, CancellationToken.None);

        Assert.Equal(ProviderResultState.Complete, result.State);
        Assert.Empty(result.Value!.Flows);
        Assert.True(result.Value!.HasWorkflowComponentsWithoutCloudFlows);
        Assert.Equal("0 cloud flow(s) - 1 classic workflow(s) skipped", result.Value!.DescribeCoverage());
    }

    [Fact]
    public void SolutionWithNoWorkflowComponentsAtAllIsEmptyNotPartial()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.WorkflowPages.Add(FakeRowSource.Of<RawWorkflowRecord>(false, null));

        ProviderResult<IReadOnlyList<WorkflowReference>> result = Source(rows).GetCloudFlows(SolutionId, CancellationToken.None);

        Assert.Equal(ProviderResultState.Complete, result.State);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public void AMissingLaterPageIsPartialButStillShowsWhatWasRead()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.WorkflowPages.Add(FakeRowSource.Of(true, "cookie-1", FakeRowSource.Row("dddddddd-0000-0000-0000-000000000001", 5, "Read")));

        ProviderResult<IReadOnlyList<WorkflowReference>> result =
            new DataverseSolutionFlowSource(rows, new FixedClock()).GetCloudFlows("44444444-4444-4444-4444-444444444444", CancellationToken.None);

        Assert.Equal(ProviderResultState.Partial, result.State);
        Assert.True(result.IsUsable);
        Assert.Single(result.Value!);
        Assert.Contains("returned no page", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailureStateAndReasonSurviveTheProjectionToTheGenericContract()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.ThrowForWorkflows = new Exception("The caller does not have permission to read solutioncomponent.");
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());

        ProviderResult<CloudFlowSelectionResult> rich = source.GetSolutionFlows("44444444-4444-4444-4444-444444444444", CancellationToken.None);
        ProviderResult<IReadOnlyList<WorkflowReference>> generic = source.GetCloudFlows("44444444-4444-4444-4444-444444444444", CancellationToken.None);

        Assert.Equal(ProviderResultState.Unauthorized, rich.State);
        Assert.Equal(rich.State, generic.State);
        Assert.Equal(rich.Message, generic.Message);
        Assert.Equal(rich.CheckedAtUtc, generic.CheckedAtUtc);
        Assert.Null(generic.Value);
    }

    [Fact]
    public void FlowIdentityUsesTheConnectionsEnvironmentAsItsScope()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.EnvironmentId = "55555555-5555-5555-5555-555555555555".ToUpperInvariant();
        rows.WorkflowPages.Add(FakeRowSource.Of(false, null, FakeRowSource.Row("eeeeeeee-0000-0000-0000-000000000001", 5, "Scoped")));

        WorkflowReference flow = new DataverseSolutionFlowSource(rows, new FixedClock())
            .GetCloudFlows("44444444-4444-4444-4444-444444444444", CancellationToken.None).Value![0];

        Assert.Equal("55555555-5555-5555-5555-555555555555", flow.EnvironmentId);
        Assert.StartsWith("powerautomate:55555555-5555-5555-5555-555555555555:", flow.Key.Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownEnvironmentProducesUnscopedIdentitiesNotGuessedOnes()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.EnvironmentId = null;
        rows.WorkflowPages.Add(FakeRowSource.Of(false, null, FakeRowSource.Row("ffffffff-0000-0000-0000-000000000001", 5, "Unscoped")));

        WorkflowReference flow = new DataverseSolutionFlowSource(rows, new FixedClock())
            .GetCloudFlows("44444444-4444-4444-4444-444444444444", CancellationToken.None).Value![0];

        Assert.Equal(string.Empty, flow.EnvironmentId);
        Assert.StartsWith("powerautomate::", flow.Key.Canonical, StringComparison.Ordinal);
    }
}
