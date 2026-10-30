using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Solution selection search: an empty query means "everything", never "nothing", and the
/// label that carries managed/version is built in exactly one place.
/// </summary>
public class SolutionSearchTests
{
    private static SolutionReference Solution(string id, string displayName, string uniqueName, string version = "1.0.0.0", bool isManaged = false)
    {
        return new SolutionReference(id, uniqueName, displayName, version, isManaged);
    }

    private static List<SolutionReference> Catalogue()
    {
        return new List<SolutionReference>
        {
            Solution("s3", "Accounting", "acme_ledger", "2.1.0.0", isManaged: true),
            Solution("s1", "Core Product", "contoso_core", "7.0.0.1"),
            Solution("s2", "core extras", "contoso_extras", "1.4.0.0"),
        };
    }

    [Fact]
    public void EmptyOrNullQueryReturnsEverything()
    {
        Assert.Equal(3, SolutionSearch.Filter(Catalogue(), null).Count);
        Assert.Equal(3, SolutionSearch.Filter(Catalogue(), string.Empty).Count);
        Assert.Equal(3, SolutionSearch.Filter(Catalogue(), "   ").Count);
    }

    [Fact]
    public void NullCatalogueIsAnEmptyResultNotACrash()
    {
        Assert.Empty(SolutionSearch.Filter(null, "core"));
    }

    [Fact]
    public void ResultsAreOrderedByDisplayNameThenPublisher()
    {
        IReadOnlyList<SolutionReference> all = SolutionSearch.Filter(Catalogue(), null);

        Assert.Equal("Accounting", all[0].DisplayName);
        Assert.Equal("core extras", all[1].DisplayName);
        Assert.Equal("Core Product", all[2].DisplayName);
    }

    [Fact]
    public void QueryMatchesDisplayNameCaseInsensitively()
    {
        IReadOnlyList<SolutionReference> matches = SolutionSearch.Filter(Catalogue(), "CORE PRODUCT");

        Assert.Single(matches);
        Assert.Equal("s1", matches[0].Id);
    }

    [Fact]
    public void QueryMatchesPublisherName()
    {
        IReadOnlyList<SolutionReference> matches = SolutionSearch.Filter(Catalogue(), "acme");

        Assert.Single(matches);
        Assert.Equal("Accounting", matches[0].DisplayName);
    }

    [Fact]
    public void QueryIsTrimmedBeforeMatching()
    {
        Assert.Single(SolutionSearch.Filter(Catalogue(), "  ledger  "));
    }

    [Fact]
    public void UnmatchedQueryReturnsNothing()
    {
        Assert.Empty(SolutionSearch.Filter(Catalogue(), "does-not-exist"));
    }

    [Fact]
    public void NullEntriesAreSkipped()
    {
        List<SolutionReference> withHoles = Catalogue();
        withHoles.Add(null!);

        Assert.Equal(3, SolutionSearch.Filter(withHoles, null).Count);
    }

    [Fact]
    public void LabelCarriesPublisherVersionAndManagedFlag()
    {
        string label = SolutionSearch.FormatLabel(Solution("s3", "Accounting", "acme_ledger", "2.1.0.0", isManaged: true));

        Assert.Equal("Accounting (acme_ledger) - v2.1.0.0 - managed", label);
    }

    [Fact]
    public void LabelOmitsMissingPartsInsteadOfPrintingBlanks()
    {
        string label = SolutionSearch.FormatLabel(new SolutionReference("s1", null, "Core", null, false));

        Assert.Equal("Core", label);
    }

    [Fact]
    public void LabelForNullSolutionIsEmpty()
    {
        Assert.Equal(string.Empty, SolutionSearch.FormatLabel(null!));
    }

    [Fact]
    public void SolutionFallsBackToPublisherNameWhenDisplayNameIsMissing()
    {
        SolutionReference solution = new SolutionReference("s1", "contoso_core", " ", "1.0", false);

        Assert.Equal("contoso_core", solution.DisplayName);
        Assert.Equal("contoso_core", solution.ToString());
    }

    [Fact]
    public void SolutionWithoutIdCannotExist()
    {
        Assert.Throws<ArgumentException>(() => new SolutionReference("  ", "pub", "name", "1.0", false));
    }
}
