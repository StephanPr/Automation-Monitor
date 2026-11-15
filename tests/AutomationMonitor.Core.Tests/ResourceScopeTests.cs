using System;
using AutomationMonitor.Core.Models;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The resource-scope contract: what a stored scope text is allowed to mean, and what it is never allowed to
/// claim. The grammar is the identity decision behind multi-environment monitoring, so every case here is a
/// rule about a value that outlives this build: reading must never lose a scope, authoring must never invent
/// one, and recognition must never be mistaken for a verified connection.
/// </summary>
public class ResourceScopeTests
{
    private const string Organization = "3f9c1a2b-3d4e-4f50-9a8b-1c2d3e4f5a6b";
    private const string Other = "77777777-4444-4444-8888-888888888888";
    private const string LegacyEnvironment = "55555555-5555-5555-5555-555555555555";

    private static string Qualified(string identifier)
    {
        return ResourceScope.DataverseOrganizationToken + ":" + identifier;
    }

    [Fact]
    public void AnAbsentScopeIsUnverifiedAndNeverAWildcard()
    {
        Assert.Equal(ResourceScopeState.Unverified, ResourceScope.Read(null).State);
        Assert.Equal(ResourceScopeState.Unverified, ResourceScope.Read(string.Empty).State);
        Assert.Equal(ResourceScopeState.Unverified, ResourceScope.Read("   ").State);
        Assert.True(ResourceScope.Unverified.IsUnverified);
        Assert.Equal(string.Empty, ResourceScope.Unverified.Text);
        Assert.False(ResourceScope.Unverified.IsPreservedUnreadable);
        Assert.False(ResourceScope.Unverified.IsSameResourceAs(null));
        Assert.False(ResourceScope.TryQualified(string.Empty, out _, out string? problem));
        Assert.Contains("no resource scope", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("3f9c1a2b-3d4e-4f50-9a8b-1c2d3e4f5a6b")]
    [InlineData("3F9C1A2B-3D4E-4F50-9A8B-1C2D3E4F5A6B")]
    [InlineData("{3F9C1A2B-3D4E-4F50-9A8B-1C2D3E4F5A6B}")]
    [InlineData("{3f9c1a2b-3d4e-4f50-9a8b-1c2d3e4f5a6b}")]
    [InlineData("3f9c1a2b3d4e4f509a8b1c2d3e4f5a6b")]
    public void EverySpellingOfOneOrganizationIsOneScope(string spelling)
    {
        ResourceScope scope = ResourceScope.Read(Qualified(spelling));

        Assert.True(scope.IsQualified);
        Assert.Equal(ResourceScopeKind.DataverseOrganization, scope.Kind);
        Assert.Equal(Qualified(Organization), scope.Text);
        Assert.Equal(Organization, scope.Identifier);
        Assert.Equal(ResourceScope.ForDataverseOrganization(Guid.Parse(spelling)), scope);
    }

    [Theory]
    [InlineData("DATAVERSE-ORGANIZATION:" + Organization)]
    [InlineData("Dataverse-Organization:" + Organization)]
    [InlineData("  dataverse-organization:" + Organization + "  ")]
    public void TheKindTokenIsRecognisedCaseInsensitivelyAndWithoutWhitespace(string stored)
    {
        ResourceScope scope = ResourceScope.Read(stored);

        Assert.True(scope.IsQualified);
        Assert.Equal(Qualified(Organization), scope.Text);
    }

    [Theory]
    [InlineData("powerplatform-environment:" + Organization)]
    [InlineData("azure-subscription:/subscriptions/contoso")]
    [InlineData("dataverse-tenant:" + Organization)]
    public void AnUnknownKindIsPreservedAndIsNeverCalledUnverified(string stored)
    {
        ResourceScope scope = ResourceScope.Read(stored);

        Assert.Equal(ResourceScopeState.UnrecognizedKind, scope.State);
        Assert.True(scope.IsPreservedUnreadable);
        Assert.False(scope.IsUnverified);
        Assert.Equal(stored.Trim().ToLowerInvariant(), scope.Text);
        Assert.False(scope.IsSameResourceAs(ResourceScope.Unverified));
        Assert.False(scope.IsSameResourceAs(ResourceScope.ForDataverseOrganization(Guid.Parse(Organization))));
        Assert.False(ResourceScope.TryQualified(stored, out _, out _));
    }

    [Fact]
    public void ALegacyUnqualifiedScopeStaysTextAndIsNeverPromoted()
    {
        ResourceScope legacy = ResourceScope.Read(LegacyEnvironment);

        Assert.Equal(ResourceScopeState.Unqualified, legacy.State);
        Assert.Equal(LegacyEnvironment, legacy.Text);
        Assert.True(legacy.IsPreservedUnreadable);

        // The same GUID behind two different kind claims is two different scopes: a bare value does not say
        // whether it names an environment or an organization, and guessing is the identity defect itself.
        Assert.False(legacy.IsSameResourceAs(ResourceScope.ForDataverseOrganization(Guid.Parse(LegacyEnvironment))));
    }

    [Theory]
    [InlineData("dataverse-organization:")]
    [InlineData("dataverse-organization:not-a-guid")]
    [InlineData("dataverse-organization:{not-a-guid}")]
    [InlineData("dataverse-organization:00000000-0000-0000-0000-000000000000")]
    [InlineData(":" + Organization)]
    [InlineData("dataverse-organization:" + Organization + ":" + Other)]
    public void ADamagedScopeIsRefusedAsAnIdentityButNeverRewritten(string stored)
    {
        ResourceScope scope = ResourceScope.Read(stored);

        Assert.Equal(ResourceScopeState.Invalid, scope.State);
        Assert.False(scope.IsQualified);
        Assert.True(scope.IsPreservedUnreadable);
        Assert.Equal(stored.Trim().ToLowerInvariant(), scope.Text);
        Assert.False(ResourceScope.TryQualified(stored, out _, out string? problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void TwoOrganizationsAreTwoScopes()
    {
        ResourceScope one = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));
        ResourceScope two = ResourceScope.ForDataverseOrganization(Guid.Parse(Other));

        Assert.False(one.IsSameResourceAs(two));
        Assert.NotEqual(one, two);
    }

    [Fact]
    public void ReadingAScopeTwiceChangesNothing()
    {
        ResourceScope once = ResourceScope.Read("DATAVERSE-ORGANIZATION:{" + Organization.ToUpperInvariant() + "}");
        ResourceScope twice = ResourceScope.Read(once.Text);

        Assert.Equal(once, twice);
        Assert.Equal(once.Text, twice.Text);
        Assert.Equal(once.GetHashCode(), twice.GetHashCode());
    }

    [Fact]
    public void OnlyARecognizedScopeIsCanonicalisedSoNoStoredIdentityCanMove()
    {
        // Everything this build writes today - empty, or an opaque environment value - keeps exactly the
        // spelling CanonicalId.Text gave it. Canonicalising anything else would re-key stored members.
        Assert.Equal(string.Empty, ResourceScope.NormalizeStored(null));
        Assert.Equal(string.Empty, ResourceScope.NormalizeStored("   "));
        Assert.Equal(LegacyEnvironment, ResourceScope.NormalizeStored(" " + LegacyEnvironment.ToUpperInvariant() + " "));
        Assert.Equal("{" + LegacyEnvironment + "}", ResourceScope.NormalizeStored("{" + LegacyEnvironment.ToUpperInvariant() + "}"));
        Assert.Equal(
            "powerplatform-environment:" + Organization,
            ResourceScope.NormalizeStored("POWERPLATFORM-ENVIRONMENT:" + Organization.ToUpperInvariant()));
        Assert.Equal(
            Qualified(Organization),
            ResourceScope.NormalizeStored("dataverse-organization:{" + Organization.ToUpperInvariant() + "}"));
    }

    [Fact]
    public void WorkflowKeyStillKeysOnTheScopeTextItStores()
    {
        WorkflowKey key = WorkflowKey.Create(ProviderKind.PowerAutomate, "ENV-1", "wf-1");

        Assert.Equal("env-1", key.ScopeId);
        Assert.Equal(ResourceScopeState.Unqualified, key.Scope.State);
        Assert.Equal(key.ScopeId, key.Scope.Text);
        Assert.Equal("powerautomate:env-1:wf-1", key.Canonical);
    }

    [Fact]
    public void EveryGuidSpellingOfOneOrganizationKeysOneWorkflow()
    {
        WorkflowKey dashed = WorkflowKey.Create(ProviderKind.PowerAutomate, Qualified(Organization), "wf-1");
        WorkflowKey braced = WorkflowKey.Create(
            ProviderKind.PowerAutomate, Qualified("{" + Organization.ToUpperInvariant() + "}"), "wf-1");
        WorkflowKey bare = WorkflowKey.Create(
            ProviderKind.PowerAutomate, Qualified(Organization.Replace("-", string.Empty)), "wf-1");

        Assert.Equal(Qualified(Organization), dashed.ScopeId);
        Assert.Equal(dashed, braced);
        Assert.Equal(dashed, bare);
        Assert.Equal(dashed.GetHashCode(), braced.GetHashCode());
    }

    [Fact]
    public void AnUnverifiedScopeNeverMergesWithAKnownOne()
    {
        WorkflowKey unscoped = WorkflowKey.Create(ProviderKind.PowerAutomate, null, "wf-1");
        WorkflowKey scoped = WorkflowKey.Create(ProviderKind.PowerAutomate, Qualified(Organization), "wf-1");

        Assert.True(unscoped.Scope.IsUnverified);
        Assert.True(scoped.Scope.IsQualified);
        Assert.NotEqual(unscoped, scoped);
    }

    [Fact]
    public void TheSameFlowUnderTwoOrganizationsIsTwoKeysAndTwoRuns()
    {
        WorkflowKey one = WorkflowKey.Create(
            ProviderKind.PowerAutomate, ResourceScope.ForDataverseOrganization(Guid.Parse(Organization)).Text, "wf-1");
        WorkflowKey two = WorkflowKey.Create(
            ProviderKind.PowerAutomate, ResourceScope.ForDataverseOrganization(Guid.Parse(Other)).Text, "wf-1");

        Assert.NotEqual(one, two);
        Assert.NotEqual(RunKey.Create(one, "run-1"), RunKey.Create(two, "run-1"));
    }

    [Fact]
    public void ARunKeyCarriesItsWorkflowsScopeWithoutASecondRule()
    {
        RunKey run = RunKey.Create(
            WorkflowKey.Create(
                ProviderKind.PowerAutomate,
                "DATAVERSE-ORGANIZATION:{" + Organization.ToUpperInvariant() + "}",
                "wf-1"),
            "run-1");

        Assert.Equal(ResourceScopeKind.DataverseOrganization, run.Scope.Kind);
        Assert.Equal(Organization, run.Scope.Identifier);
        Assert.Equal(run.Workflow.ScopeId, run.EnvironmentId);
    }
}
