using System;
using System.Linq;
using AutomationMonitor.Core.Providers.Dataverse;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The column lists this build sends to Dataverse are a contract with the platform, not an
/// implementation detail. Dataverse validates every projected column against the table's metadata and
/// refuses the <em>whole</em> query when one name is unknown, so a single speculative column costs
/// every row - which is exactly what "primarycategory" did to the flow list in the first live session
/// on 2026-11-09. These guards sit on the projection the gateway builds its ColumnSet from, because
/// nothing else in this repository can see the SDK query.
/// </summary>
public class DataverseSchemaTests
{
    [Fact]
    public void TheWorkflowProjectionIsExactlyTheColumnsWeUnderstand()
    {
        Assert.Equal(
            new[] { "name", "category", "statecode", "statuscode", "description", "createdon", "modifiedon" },
            DataverseSchema.WorkflowAttributes);
    }

    /// <summary>
    /// The regression that emptied the flow list. These names are not attributes of the workflow table
    /// and no code reads their values, so there is nothing to gain by projecting them and everything to
    /// lose: the read fails for every solution in the environment.
    /// </summary>
    [Theory]
    [InlineData("primarycategory")]
    [InlineData("secondarycategory")]
    [InlineData("primaryCategory")]
    [InlineData("SecondaryCategory")]
    public void TheWorkflowProjectionNeverAsksForAColumnWeCannotJustify(string column)
    {
        Assert.DoesNotContain(DataverseSchema.WorkflowAttributes, name => string.Equals(name, column, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheWorkflowProjectionKeepsEveryColumnADecisionDependsOn()
    {
        // Identity comes from the row id, so it is not projected. These four carry the cloud-flow
        // decision, the enablement state and the grid; losing one turns a Complete result into a wrong
        // one rather than a visible failure.
        Assert.Contains("name", DataverseSchema.WorkflowAttributes);
        Assert.Contains("category", DataverseSchema.WorkflowAttributes);
        Assert.Contains("statecode", DataverseSchema.WorkflowAttributes);
        Assert.Contains("statuscode", DataverseSchema.WorkflowAttributes);
    }

    [Fact]
    public void TheWorkflowProjectionHasNoDuplicates()
    {
        Assert.Equal(
            DataverseSchema.WorkflowAttributes.Length,
            DataverseSchema.WorkflowAttributes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Dataverse logical names are lowercase; a display or schema name in a query fails the read the
    /// same way an unknown column does. This is the only check available for the run projection until a
    /// real environment confirms its names - see the open question on RunDuration in
    /// docs/provider-capabilities.md - so it pins the shape, not the truth.
    /// </summary>
    [Fact]
    public void EveryProjectedColumnIsALowercaseLogicalName()
    {
        foreach (string column in DataverseSchema.WorkflowAttributes.Concat(DataverseSchema.RunAttributes))
        {
            Assert.False(string.IsNullOrWhiteSpace(column), "A projection entry must not be blank.");
            Assert.Equal(column.ToLowerInvariant(), column);
        }
    }
}
