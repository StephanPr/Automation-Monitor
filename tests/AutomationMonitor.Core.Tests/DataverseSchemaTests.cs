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
            new[] { "workflowid", "name", "category", "statecode", "statuscode", "description", "createdon", "modifiedon" },
            DataverseSchema.WorkflowAttributes);
    }

    /// <summary>
    /// The second regression that emptied the flow list, and the mirror image of the first: the read
    /// succeeded, 59 rows arrived, and every one of them was dropped as "without an id". Identity is the
    /// primary key, and a query is allowed to leave primary keys out (see
    /// <see cref="DataverseSchema.WorkflowQueryDistinct"/>), so the id has to be a column this build names
    /// and reads - not a value the SDK happens to fill in on a favourable query shape.
    /// </summary>
    [Fact]
    public void TheWorkflowProjectionCarriesTheIdentityColumn()
    {
        Assert.Contains(DataverseSchema.WorkflowId, DataverseSchema.WorkflowAttributes);
        Assert.Equal("workflowid", DataverseSchema.WorkflowId);
    }

    /// <summary>
    /// A distinct query does not carry primary key values - Learn says so in the remarks on
    /// <c>QueryExpression.Distinct</c> - so turning it on costs every workflow row its identity. The
    /// de-duplication it appeared to provide belongs in Core, where removing a row is counted and shown.
    /// </summary>
    [Fact]
    public void WorkflowDiscoveryIsNeverADistinctQuery()
    {
        Assert.False(DataverseSchema.WorkflowQueryDistinct);
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
        // These carry the identity, the cloud-flow decision, the enablement state and the grid. Losing one
        // turns a Complete result into a wrong one rather than a visible failure, which is why they are
        // pinned here and not left to the query.
        Assert.Contains("workflowid", DataverseSchema.WorkflowAttributes);
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
