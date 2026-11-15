using System;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;


namespace AutomationMonitor.Core.Tests;

/// <summary>
/// AM-42 measures which stable environment identifiers a connection really offers. What these tests guard is the
/// measurement's two promises: that the four answers ("it is there", "it is blank", "it is not an identifier",
/// "this build could not ask") stay four separate answers, and that a comparison is only ever drawn between two
/// sources of the SAME identity kind - a Dataverse organization id and a Power Platform environment id are never
/// treated as equal or as a mismatch, because they name different things.
/// </summary>
public class IdentityObservationTests
{
    private const string First = "11111111-1111-1111-1111-111111111111";
    private const string Second = "22222222-2222-2222-2222-222222222222";

    private static IdentityObservation Environment(string? value, bool reachable = true)
    {
        return IdentityObservation.Observe(IdentityKind.PowerPlatformEnvironmentId, "the test source", value, reachable);
    }

    private static IdentityObservation Organization(string? value)
    {
        return IdentityObservation.Observe(IdentityKind.DataverseOrganizationId, "the test source", value);
    }

    [Fact]
    public void APresentIdentifierIsRecordedAsItsShapeAndNothingElse()
    {
        IdentityObservation observed = Environment(First);

        Assert.Equal(IdentityAvailability.Present, observed.Availability);
        Assert.True(observed.IsIdentifierShape);
        Assert.True(observed.IsUsable);
        Assert.Equal(36, observed.CharacterCount);
        Assert.Equal("present, GUID shape, 36 character(s)", observed.Describe());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ASourceThatOfferedNothingIsMissingRatherThanInvalid(string? value)
    {
        IdentityObservation observed = Environment(value);

        Assert.Equal(IdentityAvailability.Missing, observed.Availability);
        Assert.False(observed.IsUsable);
        Assert.False(observed.IsAllZeroIdentifier);
        Assert.Equal("not stated", observed.Describe());
    }

    [Fact]
    public void TheAllZeroIdentifierIsNotAnIdentifier()
    {
        IdentityObservation observed = Environment(Zero());

        Assert.Equal(IdentityAvailability.Missing, observed.Availability);
        Assert.True(observed.IsAllZeroIdentifier);
        Assert.Contains("all-zero", observed.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AValueThatIsNotAnIdentifierIsItsOwnAnswer()
    {
        IdentityObservation observed = Environment("contoso-production");

        // Not "missing": something did arrive, and calling it blank would hide a shape this build cannot read.
        Assert.Equal(IdentityAvailability.Invalid, observed.Availability);
        Assert.False(observed.IsIdentifierShape);
        Assert.False(observed.IsUsable);
        Assert.Equal(18, observed.CharacterCount);
    }

    [Fact]
    public void AnUnreachableSourceIsNeverWrittenUpAsABlank()
    {
        IdentityObservation observed = Environment(First, reachable: false);

        Assert.Equal(IdentityAvailability.Unavailable, observed.Availability);
        Assert.Equal("not read by this build", observed.Describe());
    }

    [Fact]
    public void TwoSourcesOfTheSameKindAreComparedOnTheirValue()
    {
        Assert.Equal(IdentityMatch.Same, IdentityComparison.Evaluate(Environment(First), Environment(First), true));
        Assert.Equal(IdentityMatch.Different, IdentityComparison.Evaluate(Environment(First), Environment(Second), false));
    }

    [Theory]
    [InlineData(IdentityAvailability.Missing)]
    [InlineData(IdentityAvailability.Invalid)]
    [InlineData(IdentityAvailability.Unavailable)]
    public void ASideThatIsNotAUsableIdentifierMakesNoComparison(IdentityAvailability availability)
    {
        IdentityObservation unusable = availability switch
        {
            IdentityAvailability.Missing => Environment(null),
            IdentityAvailability.Invalid => Environment("not-an-id"),
            _ => Environment(First, reachable: false)
        };

        // Even when the caller claims the values match: agreement between one known id and one unknown source is
        // not a fact that may be reported.
        Assert.Equal(IdentityMatch.NotComparable, IdentityComparison.Evaluate(Environment(First), unusable, true));
    }

    [Fact]
    public void AnOrganizationIdAndAnEnvironmentIdAreNeverCompared()
    {
        // The same text on both sides, on purpose: even then the kinds differ, so the answer is not comparable.
        Assert.Equal(IdentityMatch.NotComparable, IdentityComparison.Evaluate(Environment(First), Organization(First), true));
    }

    [Fact]
    public void ANullSideIsNotComparable()
    {
        Assert.Equal(IdentityMatch.NotComparable, IdentityComparison.Evaluate(null, Environment(First), true));
    }

    private static string Zero()
    {
        return Guid.Empty.ToString("D");
    }
}
