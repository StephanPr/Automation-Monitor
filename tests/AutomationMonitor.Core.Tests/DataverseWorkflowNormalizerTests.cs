using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Normalization is lossy on purpose, so both the raw provider codes and the normalized state
/// have to survive on the reference for diagnosis and portal links.
/// </summary>
public class DataverseWorkflowNormalizerTests
{
    private const string EnvironmentId = "55555555-5555-5555-5555-555555555555";

    private static RawWorkflowRecord Row(string? id, int? category, string? name = "flow", int? state = null)
    {
        return new RawWorkflowRecord { Id = id, RawCategory = category, Name = name, RawStateCode = state };
    }

    [Theory]
    [InlineData(DataverseSchema.StateCodeEnabled, WorkflowState.Enabled)]
    [InlineData(DataverseSchema.StateCodeDisabled, WorkflowState.Disabled)]
    [InlineData(9, WorkflowState.Unknown)]
    [InlineData(null, WorkflowState.Unknown)]
    public void StateCodeMapping_IsExact(int? rawStateCode, WorkflowState expected)
    {
        Assert.Equal(expected, DataverseWorkflowNormalizer.NormalizeState(rawStateCode));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(6, false)]
    [InlineData(42, false)]
    [InlineData(null, false)]
    public void OnlyCategoryFiveIsACloudFlow(int? category, bool expected)
    {
        Assert.Equal(expected, DataverseWorkflowNormalizer.IsCloudFlow(Row("11111111-1111-1111-1111-111111111111", category)));
    }

    [Fact]
    public void NullRecordIsNotACloudFlow()
    {
        Assert.False(DataverseWorkflowNormalizer.IsCloudFlow(null));
    }

    [Fact]
    public void RowsWithoutAnIdentityCannotBeReferenced()
    {
        Assert.False(DataverseWorkflowNormalizer.TryToReference(null, EnvironmentId, out WorkflowReference? missing));
        Assert.Null(missing);

        Assert.False(DataverseWorkflowNormalizer.TryToReference(Row(null, 5), EnvironmentId, out WorkflowReference? noId));
        Assert.Null(noId);

        Assert.False(DataverseWorkflowNormalizer.TryToReference(Row("   ", 5), EnvironmentId, out WorkflowReference? blankId));
        Assert.Null(blankId);
    }

    [Fact]
    public void EveryRawValueSurvivesNormalization()
    {
        DateTimeOffset created = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        DateTimeOffset modified = new DateTimeOffset(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);
        RawWorkflowRecord record = Row("66666666-0000-0000-0000-000000000001", 5, "Audit", state: 9);
        record.RawStatusCode = 20;
        record.Description = "Runs nightly";
        record.CreatedOnUtc = created;
        record.ModifiedOnUtc = modified;

        Assert.True(DataverseWorkflowNormalizer.TryToReference(record, EnvironmentId, out WorkflowReference? reference));

        Assert.NotNull(reference);
        Assert.Equal(9, reference!.RawStateCode);
        Assert.Equal(20, reference.RawStatusCode);
        Assert.Equal(5, reference.RawCategory);
        // No primary/secondary category by design: those are not attributes of the workflow table, and
        // the model carries only what the platform really publishes for a projected column. The
        // projection guard in DataverseSchemaTests keeps the query matching that.
        Assert.Equal("Runs nightly", reference.Description);
        Assert.Equal(created, reference.CreatedOnUtc);
        Assert.Equal(modified, reference.ModifiedOnUtc);
        Assert.Equal(WorkflowState.Unknown, reference.State);
        Assert.Equal("Unknown (9)", reference.StateLabel);
    }

    [Fact]
    public void MissingOptionalFieldsBecomeNullNotFabricatedValues()
    {
        RawWorkflowRecord record = new RawWorkflowRecord();
        record.Id = "12121212-0000-0000-0000-000000000001";
        record.Name = "   ";

        Assert.True(DataverseWorkflowNormalizer.TryToReference(record, null, out WorkflowReference? reference));

        Assert.NotNull(reference);
        Assert.Equal("(unnamed workflow)", reference!.Name);
        Assert.Null(reference.Description);
        Assert.Null(reference.CreatedOnUtc);
        Assert.Null(reference.RawCategory);
        Assert.Equal(WorkflowState.Unknown, reference.State);
        Assert.Equal("Unknown", reference.StateLabel);
        Assert.Equal(string.Empty, reference.EnvironmentId);
    }

    [Fact]
    public void ProviderIdIsCanonicalisedSoOneFlowCannotBecomeTwo()
    {
        RawWorkflowRecord record = Row("{AAAABBBB-CCCC-DDDD-EEEE-FFFF00000001}", 5);

        Assert.True(DataverseWorkflowNormalizer.TryToReference(record, EnvironmentId, out WorkflowReference? reference));

        Assert.Equal("aaaabbbb-cccc-dddd-eeee-ffff00000001", reference!.WorkflowId);
    }
}
