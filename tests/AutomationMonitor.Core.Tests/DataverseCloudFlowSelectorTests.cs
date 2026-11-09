using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The selection rule that decides what Automation Monitor may watch: cloud flows only, and
/// every row that was not selected is counted and described rather than silently hidden.
/// </summary>
public class DataverseCloudFlowSelectorTests
{
    private const string EnvironmentId = "55555555-5555-5555-5555-555555555555";

    /// <param name="source">
    /// Where the row's id came from, exactly as the reader reports it. The default is the honest "not
    /// stated", which is what a row that carried nothing at all looks like.
    /// </param>
    private static RawWorkflowRecord Row(
        string? id,
        int? category,
        string? name = "flow",
        int? state = DataverseSchema.StateCodeEnabled,
        WorkflowIdSource source = WorkflowIdSource.NotStated)
    {
        return new RawWorkflowRecord { Id = id, IdSource = source, RawCategory = category, Name = name, RawStateCode = state };
    }

    private static List<RawWorkflowRecord> Rows(params RawWorkflowRecord[] records)
    {
        return new List<RawWorkflowRecord>(records);
    }

    [Fact]
    public void NoRows_ProducesAnEmptyCompleteResult()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(null, null);

        Assert.Empty(result.Flows);
        Assert.Equal(0, result.RowsRead);
        Assert.False(result.HasWorkflowComponentsWithoutCloudFlows);
        Assert.Equal("0 cloud flow(s)", result.DescribeCoverage());
    }

    [Fact]
    public void OnlyCategoryFiveBecomesACloudFlow()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(
                Row("aaaaaaaa-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "Daily sync"),
                Row("aaaaaaaa-0000-0000-0000-000000000002", 2, "Approve leave")),
            EnvironmentId);

        Assert.Equal(2, result.RowsRead);
        Assert.Single(result.Flows);
        Assert.Equal("Daily sync", result.Flows[0].Name);
        Assert.Equal(DataverseSchema.WorkflowCategoryCloudFlow, result.Flows[0].RawCategory);
        Assert.Equal(WorkflowState.Enabled, result.Flows[0].State);
        Assert.Equal(1, result.NonCloudCount);
        Assert.Contains("1 classic workflow(s) skipped", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicWorkflows_AreExcludedAndCounted()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(
                Row("bbbbbbbb-0000-0000-0000-000000000001", 0),
                Row("bbbbbbbb-0000-0000-0000-000000000002", 1),
                Row("bbbbbbbb-0000-0000-0000-000000000003", 2),
                Row("bbbbbbbb-0000-0000-0000-000000000004", 3),
                Row("bbbbbbbb-0000-0000-0000-000000000005", 4),
                Row("bbbbbbbb-0000-0000-0000-000000000006", 6)),
            EnvironmentId);

        Assert.Empty(result.Flows);
        Assert.Equal(6, result.NonCloudCount);
        Assert.True(result.HasWorkflowComponentsWithoutCloudFlows);
        Assert.Contains("6 classic workflow(s) skipped", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCategory_IsCountedSeparatelyFromClassicWorkflows()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(Row("cccccccc-0000-0000-0000-000000000001", null)),
            EnvironmentId);

        Assert.Empty(result.Flows);
        Assert.Equal(1, result.UnknownCategoryCount);
        Assert.Equal(0, result.NonCloudCount);
        Assert.Contains("1 row(s) with unknown category skipped", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void RowsWithoutProviderId_AreCountedAndNeverGivenAFabricatedKey()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(
                Row(null, DataverseSchema.WorkflowCategoryCloudFlow, "no id"),
                Row("   ", DataverseSchema.WorkflowCategoryCloudFlow, "blank id"),
                Row("dddddddd-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "real")),
            EnvironmentId);

        Assert.Single(result.Flows);
        Assert.Equal(2, result.MissingIdCount);
        Assert.Contains("2 row(s) without an id skipped", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARowThatIsNullIsCountedAsMissingIdentity()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            new List<RawWorkflowRecord> { null! },
            EnvironmentId);

        Assert.Equal(1, result.RowsRead);
        Assert.Equal(1, result.MissingIdCount);
    }

    [Fact]
    public void TheSameWorkflowTwice_CollapsesToOneEntry()
    {
        // The second row is the same id in another GUID format with another display name,
        // which is what an overlapping component page or a re-read produces.
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(
                Row("eeeeeeee-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "Nightly export"),
                Row("{EEEEEEEE-0000-0000-0000-000000000001}", DataverseSchema.WorkflowCategoryCloudFlow, "Nightly export (copy)")),
            EnvironmentId);

        Assert.Equal(2, result.RowsRead);
        Assert.Single(result.Flows);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal("eeeeeeee-0000-0000-0000-000000000001", result.Flows[0].WorkflowId);
        Assert.Contains("1 duplicate(s) removed", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctWorkflowsWithTheSameName_AreBothKept()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(
                Row("ffffffff-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "Same name"),
                Row("ffffffff-0000-0000-0000-000000000002", DataverseSchema.WorkflowCategoryCloudFlow, "Same name")),
            EnvironmentId);

        Assert.Equal(2, result.Flows.Count);
        Assert.Equal(0, result.DuplicateCount);
    }

    [Fact]
    public void FlowsAreOrderedByNameForAStableGrid()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(
                Row("11111111-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "Zebra"),
                Row("22222222-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "apple"),
                Row("33333333-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "Mango")),
            EnvironmentId);

        Assert.Equal("apple", result.Flows[0].Name);
        Assert.Equal("Mango", result.Flows[1].Name);
        Assert.Equal("Zebra", result.Flows[2].Name);
    }

    [Fact]
    public void IdentityCarriesTheEnvironmentScopeInCanonicalForm()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(Row("99999999-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "Flow", state: DataverseSchema.StateCodeDisabled)),
            EnvironmentId.ToUpperInvariant());

        WorkflowReference flow = result.Flows[0];
        Assert.Equal(ProviderKind.PowerAutomate, flow.Provider);
        Assert.Equal(EnvironmentId.ToLowerInvariant(), flow.EnvironmentId);
        Assert.Equal(WorkflowState.Disabled, flow.State);
        Assert.Equal(DataverseSchema.StateCodeDisabled, flow.RawStateCode);
    }

    [Fact]
    public void UnknownEnvironmentStillProducesAnIdentityButAnUnscopedOne()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(Row("77777777-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow)),
            null);

        Assert.Single(result.Flows);
        Assert.Equal(string.Empty, result.Flows[0].EnvironmentId);
    }

    [Fact]
    public void AnUnscopedRowNeverMergesWithAScopedRow()
    {
        CloudFlowSelectionResult scoped = DataverseCloudFlowSelector.Select(
            Rows(Row("12121212-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow)),
            EnvironmentId);
        CloudFlowSelectionResult unscoped = DataverseCloudFlowSelector.Select(
            Rows(Row("12121212-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow)),
            null);

        Assert.NotEqual(scoped.Flows[0].Key.Canonical, unscoped.Flows[0].Key.Canonical);
    }

    /// <summary>
    /// The observed failure reproduced as data: a solution whose read returned 59 rows and in which every
    /// row carried no identity anywhere (a distinct query suppresses primary key values, and the identity
    /// column was not projected). The rule protected here is that all 59 are counted and none is given a
    /// substitute key - an invented id would put 59 unnameable flows into a saved watchlist.
    /// </summary>
    [Fact]
    public void EveryRowWithoutAnyIdentity_IsCountedAndNoneIsInvented()
    {
        List<RawWorkflowRecord> rows = new List<RawWorkflowRecord>();
        for (int i = 0; i < 59; i++)
        {
            rows.Add(Row(null, DataverseSchema.WorkflowCategoryCloudFlow, "flow " + i));
        }

        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(rows, EnvironmentId);

        Assert.Equal(59, result.RowsRead);
        Assert.Empty(result.Flows);
        Assert.Equal(59, result.MissingIdCount);
        Assert.Equal("0 cloud flow(s) - 59 row(s) without an id skipped", result.DescribeCoverage());
        Assert.Contains(
            "identity sources: no workflowid and no row id x59",
            result.DescribeDiagnostics(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityFromTheProjectedColumn_IsKeptAndReportedAsTheColumn()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(Row(
                "aaaaaaaa-0000-0000-0000-000000000009",
                DataverseSchema.WorkflowCategoryCloudFlow,
                "From the column",
                source: WorkflowIdSource.IdColumn)),
            EnvironmentId);

        Assert.Single(result.Flows);
        Assert.Equal(0, result.MissingIdCount);
        Assert.Equal("aaaaaaaa-0000-0000-0000-000000000009", result.Flows[0].WorkflowId);
        Assert.Contains("workflowid column x1", result.DescribeDiagnostics(), StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityFromTheRowId_IsKeptForAReaderThatProjectsNoColumn()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(Row(
                "bbbbbbbb-0000-0000-0000-000000000009",
                DataverseSchema.WorkflowCategoryCloudFlow,
                "From the row id",
                source: WorkflowIdSource.RowId)),
            EnvironmentId);

        Assert.Single(result.Flows);
        Assert.Contains("row id x1", result.DescribeDiagnostics(), StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsReportNoIdentitySourcesOnlyWhenNoRowWasRead()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(null, EnvironmentId);

        Assert.Contains("identity sources: none reported", result.DescribeDiagnostics(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Where a row's id came from must never split one flow into two entries: the same provider id read
    /// through the row id and through the projected column is one workflow, and the second observation is a
    /// duplicate. This is the guard against a second, source-specific identity path.
    /// </summary>
    [Fact]
    public void TheSameFlowSeenThroughTwoIdentitySources_IsOneFlowNotTwo()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(
                Row("abababab-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "Nightly", source: WorkflowIdSource.RowId),
                Row("abababab-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "Nightly", source: WorkflowIdSource.IdColumn)),
            EnvironmentId);

        Assert.Single(result.Flows);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(0, result.MissingIdCount);
        Assert.Contains("1 duplicate(s) removed", result.DescribeCoverage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two ids on one row that do not match is a row this tool cannot name. It is refused, and it is
    /// counted under its own heading: reporting it as "without an id" would describe the symptom wrongly
    /// and send the next reader looking for a missing column instead of a mismatched one.
    /// </summary>
    [Fact]
    public void ARowWithTwoDifferentIds_IsRefusedAndCountedSeparately()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(Row(null, DataverseSchema.WorkflowCategoryCloudFlow, "ambiguous", source: WorkflowIdSource.Conflicting)),
            EnvironmentId);

        Assert.Empty(result.Flows);
        Assert.Equal(1, result.ConflictingIdCount);
        Assert.Equal(0, result.MissingIdCount);
        Assert.Contains("1 row(s) with conflicting ids skipped", result.DescribeCoverage(), StringComparison.Ordinal);
        Assert.Contains("conflicting ids x1", result.DescribeDiagnostics(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A healthy read has to be explainable from the log alone, without a debugger and without the source:
    /// one line carries the projection the query sent, the query shape and where each row's identity came
    /// from. Column names and counts only - never a value.
    /// </summary>
    [Fact]
    public void DiagnosticsNameTheProjectionTheQueryShapeAndTheIdentitySources()
    {
        CloudFlowSelectionResult result = DataverseCloudFlowSelector.Select(
            Rows(
                Row("11111111-0000-0000-0000-000000000001", DataverseSchema.WorkflowCategoryCloudFlow, "One", source: WorkflowIdSource.IdColumn),
                Row("22222222-0000-0000-0000-000000000002", DataverseSchema.WorkflowCategoryCloudFlow, "Two", source: WorkflowIdSource.AliasedIdColumn)),
            EnvironmentId);

        string diagnostics = result.DescribeDiagnostics();

        Assert.Contains("asked for columns: workflowid, name, category", diagnostics, StringComparison.Ordinal);
        Assert.Contains("identity column 'workflowid' projected: yes", diagnostics, StringComparison.Ordinal);
        Assert.Contains("distinct query: no", diagnostics, StringComparison.Ordinal);
        Assert.Contains("scope: workflow rows joined to solutioncomponent componenttype 29", diagnostics, StringComparison.Ordinal);
        Assert.Contains(
            "identity sources: workflowid column x1, aliased workflowid column x1",
            diagnostics,
            StringComparison.Ordinal);
    }
}
