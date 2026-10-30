using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Provider error text is untrusted and can carry tenant urls and credentials. Nothing may
/// reach the screen, a log or an export with those still inside it.
/// </summary>
public class DiagnosticsSanitizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToSayStaysEmpty(string? message)
    {
        Assert.Equal(string.Empty, DiagnosticsSanitizer.Sanitize(message));
    }

    [Fact]
    public void PlainTextIsPreservedSoTheOperatorGetsAReason()
    {
        Assert.Equal("The connection was closed.", DiagnosticsSanitizer.Sanitize("The connection was closed."));
    }

    [Fact]
    public void TenantUrlIsReplacedNotReproduced()
    {
        string cleaned = DiagnosticsSanitizer.Sanitize("Failed reading https://contoso.crm.dynamics.com/api/data/v9.2/solutions");

        Assert.DoesNotContain("contoso.crm.dynamics.com", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", cleaned, StringComparison.Ordinal);
        Assert.Contains("[url]", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void BearerTokenIsRedacted()
    {
        string cleaned = DiagnosticsSanitizer.Sanitize("Authorization rejected: bearer eyJhbGciOi.ABC123-xyz_~+/=");

        Assert.DoesNotContain("eyJhbGciOi", cleaned, StringComparison.Ordinal);
        Assert.Contains("bearer [redacted]", cleaned, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("password=SuperSecret123")]
    [InlineData("password: SuperSecret123")]
    [InlineData("token=abc.def.ghi")]
    [InlineData("api_key=abc.def.ghi")]
    [InlineData("sessionid=abc.def.ghi")]
    public void SecretLookingPairsAreRedacted(string input)
    {
        string cleaned = DiagnosticsSanitizer.Sanitize(input);

        Assert.DoesNotContain("SuperSecret123", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def.ghi", cleaned, StringComparison.Ordinal);
        Assert.Contains("[redacted]", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void StackTraceIsDroppedBecauseItCarriesAssemblyAndTenantDetail()
    {
        string cleaned = DiagnosticsSanitizer.Sanitize("The plug-in executed and failed.\n   at Microsoft.Xrm.Customization.Step.Run()");

        Assert.Equal("The plug-in executed and failed.", cleaned);
    }

    [Fact]
    public void NewlinesAndTabsBecomeSingleSpacesSoOneLineIsOneMessage()
    {
        string cleaned = DiagnosticsSanitizer.Sanitize("  line one\n\tline two  \r\nline three  ");

        Assert.Equal("line one line two line three", cleaned);
    }

    [Fact]
    public void LongPayloadsAreTruncatedInsteadOfPasted()
    {
        string cleaned = DiagnosticsSanitizer.Sanitize(new string('x', 4000));

        Assert.True(cleaned.Length < 400, "Sanitized text was " + cleaned.Length + " characters.");
        Assert.EndsWith("...", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeJoinsContextAndReason()
    {
        Assert.Equal("Reading solutions: timed out", DiagnosticsSanitizer.Describe("Reading solutions", "timed out"));
        Assert.Equal("Reading solutions", DiagnosticsSanitizer.Describe("Reading solutions", null));
        Assert.Equal("timed out", DiagnosticsSanitizer.Describe(null, "timed out"));
        Assert.Equal(string.Empty, DiagnosticsSanitizer.Describe(null, null));
    }

    [Fact]
    public void DescribeDoesNotInventAReasonForAnEmptyMessage()
    {
        Assert.Equal("Reading solutions", DiagnosticsSanitizer.Describe("Reading solutions", "   "));
    }
}
