using System;
using AutomationMonitor.Core.Models;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The one environment-scope renderer. Two properties are load-bearing: the three identity states must read
/// differently, so an operator can tell "we never checked" from "we know which organization" from "this
/// connection answered"; and no label may carry a whole identifier, because these strings end up in a grid,
/// a status line and the log.
/// </summary>
public class ResourceScopeLabelTests
{
    private const string Organization = "3f9c1a2b-3d4e-4f50-9a8b-1c2d3e4f5a6b";
    private const string Other = "77777777-4444-4444-8888-888888888888";
    private const string LegacyEnvironment = "55555555-5555-5555-5555-555555555555";

    private static string Qualified(string identifier)
    {
        return ResourceScope.DataverseOrganizationToken + ":" + identifier;
    }

    [Fact]
    public void AnUnverifiedScopeSaysNobodyChecked()
    {
        Assert.Equal("no verified environment", ResourceScopeLabel.DescribeStored(null));
        Assert.Equal("no verified environment", ResourceScopeLabel.DescribeStored("   "));
        Assert.Equal("no verified environment", ResourceScopeLabel.Describe(ResourceScope.Unverified));
    }

    [Fact]
    public void ARecognizedOrganizationIsNamedByKindAndFingerprint()
    {
        Assert.Equal(
            "Dataverse organization 3f9c1a2b",
            ResourceScopeLabel.DescribeStored(Qualified(Organization)));
    }

    [Fact]
    public void AnUnqualifiedLegacyValueIsNotReportedAsAnAbsence()
    {
        string label = ResourceScopeLabel.DescribeStored(LegacyEnvironment);

        Assert.Equal("unqualified environment id 55555555", label);
        Assert.DoesNotContain("no verified environment", label, StringComparison.Ordinal);
        Assert.Equal(
            "unqualified environment scope",
            ResourceScopeLabel.DescribeStored("contoso-onprems"));
    }

    [Fact]
    public void AnUnknownKindIsNamedByItsTokenNotByItsValue()
    {
        string label = ResourceScopeLabel.DescribeStored("powerplatform-environment:" + Organization);

        Assert.Contains("environment scope of kind", label, StringComparison.Ordinal);
        Assert.Contains("powerplatform-environment", label, StringComparison.Ordinal);
        Assert.DoesNotContain(Organization, label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADamagedScopeIsNeverQuietAndNeverAnEmptyScope()
    {
        string label = ResourceScopeLabel.DescribeStored(Qualified("not-a-guid"));

        Assert.Contains("unreadable environment scope", label, StringComparison.Ordinal);
        Assert.DoesNotContain("no verified environment", label, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(LegacyEnvironment)]
    [InlineData("dataverse-organization:" + Organization)]
    [InlineData("datasense-organization:{3F9C1A2B-3D4E-4F50-9A8B-1C2D3E4F5A6B}")]
    [InlineData("powerplatform-environment:" + Organization)]
    [InlineData("dataverse-organization:not-a-guid")]
    public void NoLabelEverCarriesAWholeIdentifier(string stored)
    {
        string label = ResourceScopeLabel.DescribeStored(stored);

        Assert.DoesNotContain(Organization, label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacyEnvironment, label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":", label, StringComparison.Ordinal);
        Assert.True(label.Length <= 64, "a label that long is not a label: " + label);
    }

    [Fact]
    public void AConnectionThatWasNeverReadClaimsNothing()
    {
        ResourceScope scope = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));

        Assert.Equal(ResourceScopeLabel.Describe(scope), ResourceScopeLabel.Describe(scope, null));
        Assert.Equal(ResourceScopeLabel.Describe(scope), ResourceScopeLabel.Describe(scope, ResourceScope.Unverified));
        Assert.DoesNotContain("connection", ResourceScopeLabel.Describe(scope, null), StringComparison.Ordinal);
    }

    [Fact]
    public void ARecognizedScopeIsNotCalledConnectedUntilEvidenceArrives()
    {
        ResourceScope stored = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));
        ResourceScope connected = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));

        Assert.DoesNotContain("this connection", ResourceScopeLabel.Describe(stored), StringComparison.Ordinal);
        Assert.Equal(
            "Dataverse organization 3f9c1a2b (this connection)",
            ResourceScopeLabel.Describe(stored, connected));
    }

    [Fact]
    public void AScopeFromAnotherOrganizationSaysItIsNotThisOne()
    {
        ResourceScope stored = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));
        ResourceScope connected = ResourceScope.ForDataverseOrganization(Guid.Parse(Other));

        Assert.Equal(
            "Dataverse organization 3f9c1a2b (not this connection)",
            ResourceScopeLabel.Describe(stored, connected));
    }

    [Fact]
    public void AnUnverifiedScopeShowsTheConnectedOrganizationAsSomethingElse()
    {
        Assert.Equal(
            "no verified environment (connected organization 77777777)",
            ResourceScopeLabel.Describe(
                ResourceScope.Unverified,
                ResourceScope.ForDataverseOrganization(Guid.Parse(Other))));
    }

    [Fact]
    public void APreservedScopeOfAnUnknownKindIsNotReclassifiedByConnectionEvidence()
    {
        ResourceScope stored = ResourceScope.Read("powerplatform-environment:" + Organization);
        ResourceScope connected = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));
        string label = ResourceScopeLabel.Describe(stored, connected);

        Assert.Equal(ResourceScopeLabel.Describe(stored), label);
        Assert.DoesNotContain("this connection", label, StringComparison.Ordinal);
    }

    [Fact]
    public void AControlCharacterInAStoredTokenCannotReachTheScreen()
    {
        string label = ResourceScopeLabel.DescribeStored("powerplatform-env\u0007ironment:" + Organization);

        Assert.DoesNotContain("\u0007", label, StringComparison.Ordinal);
        Assert.True(label.Length <= 64, label);
    }
}
