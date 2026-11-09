using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The one rule that decides where a workflow row's identity came from. This is the rule whose absence
/// let the first live session read 59 rows of a solution and lose every one of them: the query was
/// distinct, the platform therefore returned no primary key values, and identity had nowhere else to
/// come from. Each case below is a shape the SDK adapter can really hand over - and the point of every
/// refusal is that no row is ever given an identity the source did not state.
/// </summary>
public class WorkflowIdentityTests
{
    private static readonly Guid RowId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ColumnId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void ColumnAndRowIdThatAgree_UseTheColumnAndSaySo()
    {
        // The column is what the read asked for, so it is the value kept; the row id only agrees with it.
        bool resolved = WorkflowIdentity.TryResolve(ColumnId, ColumnId, false, out string? id, out WorkflowIdSource source);

        Assert.True(resolved);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", id);
        Assert.Equal(WorkflowIdSource.IdColumn, source);
    }

    [Fact]
    public void TheRowIdIsTheFallbackWhenTheColumnWasNotReturned()
    {
        bool resolved = WorkflowIdentity.TryResolve(null, RowId, false, out string? id, out WorkflowIdSource source);

        Assert.True(resolved);
        Assert.Equal("11111111-2222-3333-4444-555555555555", id);
        Assert.Equal(WorkflowIdSource.RowId, source);
    }

    [Fact]
    public void AnAliasedIdentityColumn_IsRecognisedAndReportedAsOne()
    {
        // A joined query can alias every column it returns. The adapter unwraps the value; the fact that
        // it had to be unwrapped is what a diagnostic reports, so "the id came from a column" and
        // "the id came from an aliased column" stay distinguishable.
        bool resolved = WorkflowIdentity.TryResolve(ColumnId, Guid.Empty, true, out string? id, out WorkflowIdSource source);

        Assert.True(resolved);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", id);
        Assert.Equal(WorkflowIdSource.AliasedIdColumn, source);
    }

    [Fact]
    public void AnAllZeroIdentityColumn_StatesNothingAndFallsBackToTheRowId()
    {
        bool resolved = WorkflowIdentity.TryResolve(Guid.Empty, RowId, false, out string? id, out WorkflowIdSource source);

        Assert.True(resolved);
        Assert.Equal("11111111-2222-3333-4444-555555555555", id);
        Assert.Equal(WorkflowIdSource.RowId, source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankIdentityColumn_StatesNothing(string? value)
    {
        bool resolved = WorkflowIdentity.TryResolve(value, Guid.Empty, false, out string? id, out WorkflowIdSource source);

        Assert.False(resolved);
        Assert.Null(id);
        Assert.Equal(WorkflowIdSource.NotStated, source);
    }

    /// <summary>
    /// The exact shape the first live session produced: distinct query, so no primary key on the row, and
    /// no identity column in the projection. Nothing may be invented here.
    /// </summary>
    [Fact]
    public void NoColumnAndNoRowId_IsRefusedRatherThanInvented()
    {
        bool resolved = WorkflowIdentity.TryResolve(null, Guid.Empty, false, out string? id, out WorkflowIdSource source);

        Assert.False(resolved);
        Assert.Null(id);
        Assert.Equal(WorkflowIdSource.NotStated, source);
    }

    [Fact]
    public void AnEmptyGuidInBothPlaces_IsRefusedAndNotTreatedAsAnId()
    {
        bool resolved = WorkflowIdentity.TryResolve(Guid.Empty, Guid.Empty, false, out string? id, out WorkflowIdSource source);

        Assert.False(resolved);
        Assert.Null(id);
        Assert.Equal(WorkflowIdSource.NotStated, source);
    }

    /// <summary>
    /// Two ids on one row that disagree cannot both be right, and picking one would attach runs, filters
    /// and saved watchlist membership to a flow this tool cannot prove. Refusing is the only honest option.
    /// </summary>
    [Fact]
    public void ColumnAndRowIdThatDisagree_AreRefused()
    {
        bool resolved = WorkflowIdentity.TryResolve(ColumnId, RowId, false, out string? id, out WorkflowIdSource source);

        Assert.False(resolved);
        Assert.Null(id);
        Assert.Equal(WorkflowIdSource.Conflicting, source);
    }

    [Fact]
    public void ATextColumnCarryingAnIdentifier_IsAcceptedBecauseTheSpellingIsNotThePoint()
    {
        bool resolved = WorkflowIdentity.TryResolve(
            "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
            Guid.Empty,
            false,
            out string? id,
            out WorkflowIdSource source);

        Assert.True(resolved);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", id);
        Assert.Equal(WorkflowIdSource.IdColumn, source);
    }

    [Fact]
    public void AnIdentityInAShapeThisBuildDoesNotUnderstand_IsNamedNotGuessed()
    {
        DataverseMappingException error = Assert.Throws<DataverseMappingException>(
            () => WorkflowIdentity.TryResolve(7, Guid.Empty, false, out _, out _));

        Assert.Equal("workflowid", error.Attribute);
        Assert.Equal("Int32", error.ActualType);
    }

    /// <summary>
    /// Every state of the enum has wording, so a diagnostic can never print a bare number or let the next
    /// reader guess what an unlabelled source meant.
    /// </summary>
    [Theory]
    [InlineData(WorkflowIdSource.NotStated, "no workflowid and no row id")]
    [InlineData(WorkflowIdSource.RowId, "row id")]
    [InlineData(WorkflowIdSource.IdColumn, "workflowid column")]
    [InlineData(WorkflowIdSource.AliasedIdColumn, "aliased workflowid column")]
    [InlineData(WorkflowIdSource.Conflicting, "conflicting ids")]
    public void EveryIdentitySourceIsDescribed(WorkflowIdSource source, string expected)
    {
        Assert.Equal(expected, WorkflowIdentity.Describe(source));
    }

    [Fact]
    public void EveryIdentitySourceHasDistinctWording()
    {
        string[] labels =
        {
            WorkflowIdentity.Describe(WorkflowIdSource.NotStated),
            WorkflowIdentity.Describe(WorkflowIdSource.RowId),
            WorkflowIdentity.Describe(WorkflowIdSource.IdColumn),
            WorkflowIdentity.Describe(WorkflowIdSource.AliasedIdColumn),
            WorkflowIdentity.Describe(WorkflowIdSource.Conflicting)
        };

        Assert.Equal(labels.Length, new HashSet<string>(labels, StringComparer.Ordinal).Count);
    }
}
