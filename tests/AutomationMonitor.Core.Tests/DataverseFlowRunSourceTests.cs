using System;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The Dataverse run provider: one paging rule, an honest coverage state per read, and a run identity
/// that only ever comes from the flow that was asked for. Every case is a fake at the row seam - no
/// Dataverse type, no tenant, no network.
/// </summary>
public class DataverseFlowRunSourceTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(1);

    private static DataverseFlowRunSource Source(FakeRunRowSource rows)
    {
        return new DataverseFlowRunSource(rows, new FixedClock());
    }

    private static MonitoringQuery Query(WorkflowKey workflow, int maxRows = MonitoringQuery.DefaultMaxRows)
    {
        return new MonitoringQuery(new[] { workflow }, From, To, null, maxRows);
    }

    [Fact]
    public void ASinglePageReadIsCompleteAndCarriesTheProvenance()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage(FakeRunRowSource.RunRow("run-1", "Succeeded", From.AddHours(1), From.AddHours(1).AddMinutes(2), 120000));

        ProviderResult<RunReadResult> result = Source(rows).GetRunRead(FakeRunRowSource.Key(), "Approvals", Query(FakeRunRowSource.Key()), CancellationToken.None);

        Assert.Equal(ProviderResultState.Complete, result.State);
        Assert.True(result.IsUsable);
        Assert.Equal(1, result.ItemsRead);
        Assert.Equal(1, result.PagesRead);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero), result.CheckedAtUtc);

        WorkflowRun run = Assert.Single(result.Value!.Runs);
        Assert.Equal("Approvals", run.WorkflowName);
        Assert.Equal("Dataverse FlowRun", run.DataSource);
        Assert.Equal(ProviderResultState.Complete, run.CoverageState);
    }

    [Fact]
    public void EveryPageIsFollowedUsingTheSharedPagingRule()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage(true, "cookie-1", FakeRunRowSource.RunRow("run-1"));
        rows.AddPage(true, "cookie-2", FakeRunRowSource.RunRow("run-2"));
        rows.AddPage(false, null, FakeRunRowSource.RunRow("run-3"));

        ProviderResult<RunReadResult> result = Source(rows).GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);

        Assert.Equal(3, rows.Calls);
        Assert.Equal(3, result.PagesRead);
        Assert.Equal(3, result.Value!.Runs.Count);
        Assert.Equal("cookie-1", rows.ObservedRequests[1].Cookie);
        Assert.Equal("cookie-2", rows.ObservedRequests[2].Cookie);
        Assert.Null(rows.ObservedRequests[0].Cookie);
    }

    [Fact]
    public void ASourceThatStopsPromisingNothingMoreIsCompleteEvenWithZeroRows()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage();

        ProviderResult<RunReadResult> result = Source(rows).GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);

        Assert.Equal(ProviderResultState.Complete, result.State);
        Assert.Empty(result.Value!.Runs);
        Assert.Null(result.Message);
    }

    [Fact]
    public void AStalledContinuationIsPartialAndKeepsTheRowsItDidGet()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage(true, "cookie-1", FakeRunRowSource.RunRow("run-1"));
        rows.AddPage(true, "cookie-2");

        ProviderResult<RunReadResult> result = Source(rows).GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);

        Assert.Equal(ProviderResultState.Partial, result.State);
        Assert.Equal(1, result.Value!.Runs.Count);
        Assert.Contains("empty page", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRowCeilingStopsTheReadAndIsReportedAsPartial()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage(true, "cookie-1", FakeRunRowSource.RunRow("run-1"), FakeRunRowSource.RunRow("run-2"));
        rows.AddPage(false, null, FakeRunRowSource.RunRow("run-3"));

        ProviderResult<RunReadResult> result = Source(rows).GetRunRead(
            FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key(), maxRows: 2), CancellationToken.None);

        Assert.Equal(ProviderResultState.Partial, result.State);
        Assert.Equal(1, rows.Calls);
        Assert.Contains("row ceiling", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWindowAndWorkflowReachTheSourceAsCriteria()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage();

        Source(rows).GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);

        RunQueryCriteria criteria = Assert.Single(rows.ObservedCriteria);
        Assert.Equal(From, criteria.FromUtc);
        Assert.Equal(To, criteria.ToUtc);
        Assert.Equal(FakeRunRowSource.Workflow, criteria.Workflow.WorkflowId);
        Assert.Empty(criteria.StatusValues);
    }


    [Fact]
    public void AStatusSelectionReachesTheSourceAsProviderSpellings()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage();
        MonitoringQuery query = new MonitoringQuery(
            new[] { FakeRunRowSource.Key() }, From, To, new[] { RunStatus.Failed, RunStatus.TimedOut });

        Source(rows).GetRunRead(FakeRunRowSource.Key(), null, query, CancellationToken.None);

        RunQueryCriteria criteria = Assert.Single(rows.ObservedCriteria);
        Assert.Contains("failed", criteria.StatusValues, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("timedout", criteria.StatusValues, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("succeeded", criteria.StatusValues, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheServerWindowIsWiderThanTheRequestSoANarrowingSourceCannotHideRuns()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage();

        Source(rows).GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);

        RunQueryCriteria criteria = Assert.Single(rows.ObservedCriteria);
        Assert.True(criteria.ServerFromUtc < criteria.FromUtc);
        Assert.True(criteria.ServerToUtc > criteria.ToUtc);
    }

    [Fact]
    public void DuplicateRowsInsideOneReadCollapseAndAreCounted()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage(FakeRunRowSource.RunRow("run-1"), FakeRunRowSource.RunRow("run-1"), FakeRunRowSource.RunRow("run-2"));

        RunReadResult result = Source(rows).GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None).Value!;

        Assert.Equal(2, result.Runs.Count);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(3, result.RowsRead);
        Assert.Contains("duplicate(s) removed", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void RowsWithoutIdentityAndForOtherFlowsAreCountedNotMerged()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        RawRunRecord orphan = FakeRunRowSource.RunRow(null);
        orphan.RunRecordId = null;
        rows.AddPage(
            orphan,
            FakeRunRowSource.RunRow("run-foreign", workflowId: "99999999-9999-9999-9999-999999999999"),
            FakeRunRowSource.RunRow("run-1"));

        RunReadResult result = Source(rows).GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None).Value!;

        Assert.Equal(1, result.Runs.Count);
        Assert.Equal(1, result.MissingIdentityCount);
        Assert.Equal(1, result.ForeignWorkflowCount);
        Assert.Contains("without a run id", result.DescribeCoverage(), StringComparison.Ordinal);
        Assert.Contains("another flow", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void CancellationReachesTheSourceAndIsNotSwallowedAsAnEmptyResult()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage(FakeRunRowSource.RunRow("run-1"));
        using CancellationTokenSource tokens = new CancellationTokenSource();
        tokens.Cancel();

        Assert.Throws<OperationCanceledException>(() => Source(rows).GetRunRead(
            FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), tokens.Token));
        Assert.Equal(0, rows.Calls);
    }

    [Fact]
    public void AFailedReadKeepsItsOwnShapeInsteadOfAnEmptyHistory()
    {
        ProviderResult<RunReadResult> denied = Source(new FakeRunRowSource { ThrowOnRead = new Exception("insufficient privilege to read flowrun") })
            .GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);
        ProviderResult<RunReadResult> absent = Source(new FakeRunRowSource { ThrowOnRead = new Exception("entity does not exist by name 'flowrun'") })
            .GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);
        ProviderResult<RunReadResult> broken = Source(new FakeRunRowSource { ThrowOnRead = new Exception("The underlying connection was closed") })
            .GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);

        Assert.Equal(ProviderResultState.Unauthorized, denied.State);
        Assert.Equal(ProviderResultState.Unsupported, absent.State);
        Assert.Equal(ProviderResultState.Unavailable, broken.State);
        Assert.False(denied.IsUsable);
        Assert.Null(absent.Value);
        Assert.Contains("FlowRun", denied.Message, StringComparison.Ordinal);
        Assert.Contains("solution-aware", absent.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// WF-A on the run path: a column this build cannot map is a defect here, not the run table being
    /// absent and not an environment that failed to answer. The two wordings must not be confused.
    /// </summary>
    [Fact]
    public void AColumnThisBuildCannotMapIsNotReportedAsAnAbsentRunTable()
    {
        ProviderResult<RunReadResult> local = Source(new FakeRunRowSource { ThrowOnRead = new DataverseMappingException("starttime", "a date and time", "String") })
            .GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);

        Assert.Equal(ProviderResultState.Unsupported, local.State);
        Assert.False(local.IsUsable);
        Assert.Contains("Automation Monitor could not read a value", local.Message, StringComparison.Ordinal);
        Assert.Contains("starttime", local.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("does not expose the flow run table", local.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("did not answer", local.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheFailureMessageIsSanitizedAndNeverCarriesAUrlOrAToken()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.ThrowOnRead = new Exception("Attaching to https://contoso.crm.dynamics.com/api failed with bearer abc.def.ghi");

        ProviderResult<RunReadResult> result = Source(rows).GetRunRead(FakeRunRowSource.Key(), null, Query(FakeRunRowSource.Key()), CancellationToken.None);

        Assert.DoesNotContain("contoso.crm.dynamics.com", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def.ghi", result.Message, StringComparison.Ordinal);
        Assert.Contains("[url]", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWorkflowFromAnotherProviderIsUnsupportedRatherThanEmpty()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        WorkflowKey logicApp = FakeRunRowSource.Key(provider: ProviderKind.LogicAppsStandard);

        ProviderResult<RunReadResult> result = Source(rows).GetRunRead(logicApp, null, Query(logicApp), CancellationToken.None);

        Assert.Equal(ProviderResultState.Unsupported, result.State);
        Assert.Contains("logicapps-standard", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, rows.Calls);
    }

    [Fact]
    public void TheContractFormKeepsTheStateOfTheRichRead()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.AddPage(true, "cookie-1", FakeRunRowSource.RunRow("run-1"));

        ProviderResult<IReadOnlyList<WorkflowRun>> result = Source(rows).GetRuns(FakeRunRowSource.Key(), Query(FakeRunRowSource.Key()), CancellationToken.None);

        Assert.Equal(ProviderResultState.Partial, result.State);
        Assert.Equal(1, result.Value!.Count);
        Assert.Equal(FakeRunRowSource.Environment, Assert.Single(result.Value).EnvironmentId);
    }

    [Fact]
    public void TheProviderAdvertisesItsKindAndScopeAndRejectsAMissingSource()
    {
        Assert.Equal(ProviderKind.PowerAutomate, Source(new FakeRunRowSource()).Provider);
        Assert.Equal(FakeRunRowSource.Environment, Source(new FakeRunRowSource()).ScopeId);
        Assert.Null(new DataverseFlowRunSource(new FakeRunRowSource { EnvironmentId = null }, null).ScopeId);
        Assert.Throws<ArgumentNullException>(() => new DataverseFlowRunSource(null!, null));
        Assert.Throws<ArgumentNullException>(() => Source(new FakeRunRowSource()).GetRuns(null!, Query(FakeRunRowSource.Key()), CancellationToken.None));
    }

    [Fact]
    public void TheRunColumnsAreDeclaredOnceAndCoverWhatTheNormalizerReads()
    {
        Assert.Equal(13, DataverseSchema.RunAttributes.Length);
        Assert.Equal(13, new HashSet<string>(DataverseSchema.RunAttributes, StringComparer.Ordinal).Count);
        Assert.Contains(DataverseSchema.RunParentRunId, DataverseSchema.RunAttributes);
        Assert.Contains(DataverseSchema.RunClientTrackingId, DataverseSchema.RunAttributes);
        Assert.Contains(DataverseSchema.RunIsPrimary, DataverseSchema.RunAttributes);
        Assert.Contains(DataverseSchema.RunDuration, DataverseSchema.RunAttributes);
    }

    [Fact]
    public void NoRunColumnIsNamedForAFieldTheTableDoesNotHave()
    {
        // correlationid and flowname are not FlowRun columns: asking for one would fail the whole
        // query, and taking the flow's display name from the request is the honest alternative.
        Assert.DoesNotContain("correlationid", DataverseSchema.RunAttributes, StringComparer.Ordinal);
        Assert.DoesNotContain("flowname", DataverseSchema.RunAttributes, StringComparer.Ordinal);
        Assert.Equal("clienttrackingid", DataverseSchema.RunClientTrackingId);
    }
}

