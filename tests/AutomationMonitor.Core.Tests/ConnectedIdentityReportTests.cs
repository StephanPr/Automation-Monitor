using System;
using System.Reflection;
using System.Text.RegularExpressions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The copyable AM-42 block is the only thing this probe hands to an operator, so it is the surface that must
/// provably carry no identifier, no url and no provider message. These tests feed in realistic-looking values -
/// including a value that is not an identifier at all - and then look for any trace of them in the text.
/// </summary>
public class ConnectedIdentityReportTests
{
    private const string First = "11111111-1111-1111-1111-111111111111";
    private const string Second = "22222222-2222-2222-2222-222222222222";
    private const string Organization = "33333333-3333-3333-3333-333333333333";
    private const string UrlShapedValue = "https://contoso-eu.crm.dynamics.com";

    private static readonly DateTimeOffset WhenUtc = new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Any spelling of a GUID: dashed, braced or bare.</summary>
    private static readonly Regex Identifier = new Regex(
        "[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}", RegexOptions.Compiled);

    private static ConnectedIdentityOutcome Measure(
        string? connection = First,
        string? client = Second,
        bool clientReachable = true,
        string? organization = Organization,
        bool whoAmISucceeded = true,
        ProviderResultState? failure = null,
        string? failureKind = null)
    {
        return ConnectedIdentityEvaluator.Evaluate(
            connection, client, clientReachable, organization, whoAmISucceeded, failure, failureKind, WhenUtc);
    }

    [Fact]
    public void EverySourceGetsItsOwnLineAndTheComparisonItsOwnVerdict()
    {
        string report = ConnectedIdentityReport.Build(Measure(client: First));

        Assert.Contains(ConnectedIdentityReport.Heading, report, StringComparison.Ordinal);
        Assert.Contains("Environment id from " + ConnectedIdentityEvaluator.ConnectionSource + ": present", report, StringComparison.Ordinal);
        Assert.Contains("Environment id from " + ConnectedIdentityEvaluator.ClientSource + ": present", report, StringComparison.Ordinal);
        Assert.Contains("Organization id from " + ConnectedIdentityEvaluator.WhoAmISource + ": present", report, StringComparison.Ordinal);
        Assert.Contains("Comparison of the two environment-id sources: the same identifier", report, StringComparison.Ordinal);
        Assert.Contains("WhoAmI request: succeeded", report, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreachableSourceExplainsWhyItWasNotRead()
    {
        string report = ConnectedIdentityReport.Build(Measure(client: null, clientReachable: false));

        Assert.Contains("not read by this build", report, StringComparison.Ordinal);
        Assert.Contains("Why that source was not read:", report, StringComparison.Ordinal);
        Assert.Contains("creating a separate connection", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedRequestIsNamedByItsStateAndItsKind()
    {
        string report = ConnectedIdentityReport.Build(
            Measure(organization: null, whoAmISucceeded: false, failure: ProviderResultState.Unauthorized, failureKind: "SecurityException"));

        Assert.Contains("WhoAmI request: " + ProviderResultStates.Token(ProviderResultState.Unauthorized) + " (SecurityException)", report, StringComparison.Ordinal);
        Assert.Contains("What to do:", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlockCarriesNoIdentifierOfAnyKind()
    {
        string report = ConnectedIdentityReport.Build(Measure());

        Assert.DoesNotContain(First, report, StringComparison.Ordinal);
        Assert.DoesNotContain(Second, report, StringComparison.Ordinal);
        Assert.DoesNotContain(Organization, report, StringComparison.Ordinal);
        Assert.False(Identifier.IsMatch(report));
        Assert.DoesNotContain("https", report, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueThatIsNotAnIdentifierIsStillNeverEchoed()
    {
        string report = ConnectedIdentityReport.Build(Measure(client: UrlShapedValue));

        // The shape is the finding; the text that arrived in it is not.
        Assert.Contains("present but not a valid identifier", report, StringComparison.Ordinal);
        Assert.DoesNotContain("contoso", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dynamics", report, StringComparison.OrdinalIgnoreCase);
        Assert.False(Identifier.IsMatch(report));
    }

    [Fact]
    public void EveryBlockStatesWhatWasNotComparedAndThatNothingWasWritten()
    {
        string report = ConnectedIdentityReport.Build(Measure());

        Assert.Contains(ConnectedIdentityReport.NotCompared, report, StringComparison.Ordinal);
        Assert.Contains(ConnectedIdentityReport.NothingWritten, report, StringComparison.Ordinal);
        Assert.Contains(ConnectedIdentityReport.CopyInstruction, report, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutcomeThatNeverArrivedIsReportedAsNothingMeasured()
    {
        string report = ConnectedIdentityReport.Build(null);

        Assert.Contains("no result object at all", report, StringComparison.Ordinal);
        Assert.Contains(ConnectedIdentityReport.CopyInstruction, report, StringComparison.Ordinal);
    }

    [Fact]
    public void NeitherResultTypeHasAPropertyThatCouldHoldAnIdentifier()
    {
        ConnectedIdentityOutcome outcome = Measure();

        AssertNoIdentifier(outcome.ConnectionEnvironmentId);
        AssertNoIdentifier(outcome.ClientEnvironmentId);
        AssertNoIdentifier(outcome.OrganizationId);
        AssertNoIdentifier(outcome);
    }

    /// <summary>
    /// The privacy rule of AM-42 is structural rather than a convention: the types that survive the measurement
    /// have no member a value could be kept in. This walks them at run time so a later field added in good
    /// intentions has to be noticed here first.
    /// </summary>
    private static void AssertNoIdentifier(object measured)
    {
        foreach (PropertyInfo property in measured.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(string) || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            string? value = (string?)property.GetValue(measured);
            Assert.True(value == null || !Identifier.IsMatch(value),
                "The public property " + measured.GetType().Name + "." + property.Name + " would carry an identifier into a report.");
        }
    }
}
