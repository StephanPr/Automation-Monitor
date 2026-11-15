using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The precondition AM-44.3b required before a stored identity could be allowed to move: opening, listing,
/// naming and per-environment settings lookup must answer "is this the same environment?" with one rule.
/// AM-44.3a gave the listing rule identity matching (<see cref="WatchlistCatalog.ForEnvironment"/>); the two
/// places that still compared scope text - the name-clash check and the per-environment profile - would
/// otherwise disagree with it the moment a file named the connected organization in a spelling the reader
/// canonicalises differently, producing a duplicate name in one environment or a second profile for one
/// organization. These cases pin the shared rule and its two limits: an unverified scope matches only another
/// unverified scope, and a preserved value this build cannot canonicalise matches only exactly what it says.
/// </summary>
public class WatchlistEnvironmentMatchingTests
{
    private const string Organization = "77777777-7777-7777-7777-777777777777";
    private const string Qualified = "dataverse-organization:" + Organization;
    private const string Braced = "dataverse-organization:{" + Organization + "}";
    private const string LettersQualified = "dataverse-organization:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UpperLetters = "dataverse-organization:AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";
    private const string OtherQualified = "dataverse-organization:88888888-8888-8888-8888-888888888888";
    private const string ReservedKind = "powerplatform-environment:" + Organization;
    private const string Damaged = "dataverse-organization:not-a-guid";
    private static readonly DateTimeOffset Stamp = new DateTimeOffset(2026, 11, 15, 9, 45, 39, TimeSpan.Zero);

    [Theory]
    [InlineData("", "", true)]
    [InlineData("   ", "", true)]
    [InlineData("", Qualified, false)]
    [InlineData(Qualified, "", false)]
    [InlineData(Qualified, Qualified, true)]
    [InlineData(Qualified, Braced, true)]
    [InlineData(LettersQualified, UpperLetters, true)]
    [InlineData(" " + Qualified + " ", Qualified, true)]
    [InlineData(Qualified, UpperLetters, false)]
    [InlineData(Qualified, OtherQualified, false)]
    [InlineData(Qualified, ReservedKind, false)]
    [InlineData(ReservedKind, ReservedKind, true)]
    [InlineData(ReservedKind, Qualified, false)]
    [InlineData(Damaged, Damaged, true)]
    [InlineData(Damaged, "dataverse-organization:also-broken", false)]
    [InlineData(Damaged, Qualified, false)]
    [InlineData("contoso-env", "contoso-env", true)]
    [InlineData("contoso-env", "CONTOSO-ENV", true)]
    [InlineData("contoso-env", "fabrikam-env", false)]
    [InlineData("contoso-env", Qualified, false)]
    public void OneEnvironmentRuleAnswersForEveryConsumer(string? stored, string? connected, bool same)
    {
        Assert.Equal(same, ResourceScope.NamesSameEnvironment(stored, connected));
        Assert.Equal(same, ResourceScope.NamesSameEnvironment(connected, stored));
    }

    [Fact]
    public void ANameClashIsFoundAcrossSpellingsOfOneOrganization()
    {
        WatchlistCatalog catalog = CatalogWith(List("list-braced", "Go-live flows", Braced));

        Watchlist? clash = catalog.FindSameName("Go-live flows", Qualified);

        Assert.NotNull(clash);
        Assert.Equal("list-braced", clash!.Id);
    }

    [Fact]
    public void AnUnverifiedListNeverSharesANamespaceWithAQualifiedOne()
    {
        WatchlistCatalog catalog = CatalogWith(List("list-legacy", "Go-live flows", string.Empty));

        Assert.Null(catalog.FindSameName("Go-live flows", Qualified));
        Assert.NotNull(catalog.FindSameName("Go-live flows", string.Empty));
    }

    [Fact]
    public void AListInAnotherOrganizationNeverSharesANamespaceWithThisOne()
    {
        WatchlistCatalog catalog = CatalogWith(List("list-other", "Go-live flows", OtherQualified));

        Assert.Null(catalog.FindSameName("Go-live flows", Qualified));
        Assert.NotNull(catalog.FindSameName("Go-live flows", OtherQualified));
    }

    [Fact]
    public void AProfileStoredInAnotherSpellingIsTheConnectedEnvironmentProfile()
    {
        ApplicationSettings settings = Settings()
            .WithProfile(new EnvironmentProfile(Braced, "list-braced", null, MonitoringDateRange.LastSevenDays, Stamp));

        EnvironmentProfile? profile = settings.ProfileFor(Qualified);

        Assert.NotNull(profile);
        Assert.Equal("list-braced", profile!.ActiveWatchlistId);
        Assert.Equal("list-braced", settings.ActiveWatchlistIdFor(Qualified));
    }

    [Fact]
    public void WritingAProfileForOneOrganizationReplacesTheOtherSpellingRatherThanAddingASecond()
    {
        ApplicationSettings settings = ApplicationSettings.Defaults
            .WithProfile(new EnvironmentProfile(Braced, "list-braced", null, MonitoringDateRange.LastSevenDays, Stamp));

        ApplicationSettings next = settings.WithProfile(new EnvironmentProfile(Qualified, "list-canonical", null, null, Stamp));

        Assert.Single(next.Environments);
        Assert.Equal("list-canonical", next.ActiveWatchlistIdFor(Qualified));
        Assert.Null(next.ActiveWatchlistIdFor(OtherQualified));
    }

    [Fact]
    public void TwoOrganizationsKeepSeparateProfilesAndSeparateSelections()
    {
        ApplicationSettings settings = ApplicationSettings.Defaults
            .WithProfile(new EnvironmentProfile(Qualified, "list-one", null, null, Stamp))
            .WithProfile(new EnvironmentProfile(OtherQualified, "list-two", null, null, Stamp));

        Assert.Equal(2, settings.Environments.Count);
        Assert.Equal("list-one", settings.ActiveWatchlistIdFor(Qualified));
        Assert.Equal("list-two", settings.ActiveWatchlistIdFor(OtherQualified));

        ApplicationSettings moved = settings.WithActiveWatchlist(OtherQualified, "list-three", Stamp);
        Assert.Equal("list-one", moved.ActiveWatchlistIdFor(Qualified));
        Assert.Equal("list-three", moved.ActiveWatchlistIdFor(OtherQualified));
    }

    [Fact]
    public void TheUnverifiedProfileStaysSeparateFromEveryQualifiedOne()
    {
        ApplicationSettings settings = Settings()
            .WithProfile(new EnvironmentProfile(string.Empty, "list-legacy", null, null, Stamp))
            .WithProfile(new EnvironmentProfile(Qualified, "list-qualified", null, null, Stamp));

        Assert.Equal("list-legacy", settings.ActiveWatchlistIdFor(string.Empty));
        Assert.Equal("list-qualified", settings.ActiveWatchlistIdFor(Qualified));
        Assert.Equal(2, settings.Environments.Count);
    }

    private static ApplicationSettings Settings()
    {
        return ApplicationSettings.Defaults.WithActiveEnvironment(string.Empty, Stamp);
    }

    private static WatchlistCatalog CatalogWith(Watchlist list)
    {
        return new WatchlistCatalog(new List<Watchlist> { list }, list.Id, Settings());
    }

    private static Watchlist List(string id, string name, string environmentId)
    {
        WatchlistBuilder builder = new WatchlistBuilder(id, name, WatchlistMembershipMode.Snapshot, null, environmentId);
        builder.AddManualFlow(
            FlowRef.Create(id + "-flow", name + " flow", ProviderKind.PowerAutomate, environmentId),
            Stamp);
        return builder.Build();
    }
}
