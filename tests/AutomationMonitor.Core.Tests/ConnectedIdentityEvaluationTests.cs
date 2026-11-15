using System;
using System.Linq;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Tests;


/// <summary>
/// The AM-42 evaluation: three sources measured in one call, and the answer that comes out the other side.
/// These cases keep "the connection has no environment id" apart from "this build could not ask", pin the one
/// comparison that is allowed, and refuse a wording that would quietly endorse using an organization id as a
/// watchlist scope.
/// </summary>
public class ConnectedIdentityEvaluationTests
{
    private const string First = "11111111-1111-1111-1111-111111111111";
    private const string Second = "22222222-2222-2222-2222-222222222222";
    private const string Organization = "33333333-3333-3333-3333-333333333333";

    private static readonly DateTimeOffset WhenUtc = new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void OneGuidInTwoSpellingsIsOneIdentifier()
    {
        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            First, "{" + First + "}", true, Organization, true, null, null, WhenUtc);

        Assert.Equal(IdentityMatch.Same, outcome.EnvironmentIdMatch);
    }

    [Fact]
    public void SurroundingWhitespaceIsNotPartOfAnIdentifier()
    {
        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            "  " + First + " ", First, true, Organization, true, null, null, WhenUtc);

        Assert.Equal(IdentityAvailability.Present, outcome.ConnectionEnvironmentId.Availability);
        Assert.Equal(IdentityMatch.Same, outcome.EnvironmentIdMatch);
    }

    [Fact]
    public void AConnectionThatReportsBothKindsIsPlainInformation()
    {
        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            First, Second, true, Organization, true, null, null, WhenUtc);

        Assert.True(outcome.HasEnvironmentIdentity);
        Assert.True(outcome.HasOrganizationIdentity);
        Assert.Equal(IdentityMatch.Different, outcome.EnvironmentIdMatch);
        Assert.Equal(ProbeReportSeverity.Information, outcome.Severity);
        Assert.Contains("both identifier kinds", ConnectedIdentityEvaluator.VerdictFor(outcome), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreachableClientIsNotReportedAsAnEmptyEnvironmentId()
    {
        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            First, null, false, Organization, true, null, null, WhenUtc);

        Assert.Equal(IdentityAvailability.Unavailable, outcome.ClientEnvironmentId.Availability);
        Assert.Equal(IdentityMatch.NotComparable, outcome.EnvironmentIdMatch);
        Assert.True(outcome.HasEnvironmentIdentity);
    }

    [Fact]
    public void AnOrganizationIdAloneIsNotAnInvitationToRescopeAWatchlist()
    {
        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            null, "   ", true, Organization, true, null, null, WhenUtc);

        Assert.False(outcome.HasEnvironmentIdentity);
        Assert.True(outcome.HasOrganizationIdentity);
        Assert.Equal(ProbeReportSeverity.Warning, outcome.Severity);

        string verdict = ConnectedIdentityEvaluator.VerdictFor(outcome);
        Assert.Contains("no Power Platform environment id was available", verdict, StringComparison.Ordinal);
        Assert.Contains("design decision", verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedWhoAmIIsTheOperatorsToFix()
    {
        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            First, First, true, null, false, ProviderResultState.Unauthorized, "SecurityException", WhenUtc);

        Assert.False(outcome.WhoAmISucceeded);
        Assert.Equal(ProbeReportSeverity.Error, outcome.Severity);
        Assert.Equal("SecurityException", outcome.WhoAmIFailureKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAllZeroEnvironmentIdIsNeverTreatedAsAgreement(bool fromClient)
    {
        string zero = Guid.Empty.ToString("D");
        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            fromClient ? First : zero, fromClient ? zero : First, true, Organization, true, null, null, WhenUtc);

        Assert.Equal(IdentityMatch.NotComparable, outcome.EnvironmentIdMatch);
    }

    [Fact]
    public void NothingMeasuredSaysSoRatherThanNamingAnUnusableIdentifier()
    {
        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            null, null, false, null, false, ProviderResultState.Unavailable, "InvalidOperationException", WhenUtc);

        Assert.False(outcome.HasEnvironmentIdentity);
        Assert.False(outcome.HasOrganizationIdentity);
        Assert.Equal(ProbeReportSeverity.Warning, outcome.Severity);
        Assert.Contains("no usable identifier of either kind", ConnectedIdentityEvaluator.VerdictFor(outcome), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMeasurementInstantIsStoredAsUtc()
    {
        DateTimeOffset shifted = new DateTimeOffset(2026, 11, 15, 14, 30, 0, TimeSpan.FromHours(5));

        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            First, First, true, Organization, true, null, null, shifted);

        Assert.Equal(0, outcome.CheckedAtUtc.Offset.TotalMinutes);
        Assert.Equal(WhenUtc, outcome.CheckedAtUtc);
    }

    [Fact]
    public void TheOutcomeCarriesTheThreeSourcesInOrder()
    {
        ConnectedIdentityOutcome outcome = ConnectedIdentityEvaluator.Evaluate(
            First, Second, true, Organization, true, null, null, WhenUtc);

        Assert.Equal(3, outcome.Observations.Count);
        Assert.Equal(
            new[] { ConnectedIdentityEvaluator.ConnectionSource, ConnectedIdentityEvaluator.ClientSource, ConnectedIdentityEvaluator.WhoAmISource },
            outcome.Observations.Select(observation => observation.Source).ToArray());
    }
}


