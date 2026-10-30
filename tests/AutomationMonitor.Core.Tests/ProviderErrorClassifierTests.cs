using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// "No data" and "not allowed" and "broken" must never collapse into the same answer. The
/// classifier is deliberately conservative: only an explicit access complaint is Unauthorized.
/// </summary>
public class ProviderErrorClassifierTests
{
    [Theory]
    [InlineData(null, "Insufficient privilege to read solutioncomponent")]
    [InlineData("UnauthorizedAccessException", null)]
    [InlineData(null, "The caller does not have permission")]
    [InlineData(null, "Response status code does not indicate success: 403 (Forbidden).")]
    [InlineData(null, "HTTP 401 Unauthorized")]
    public void AccessComplaints_AreUnauthorized(string? typeName, string? message)
    {
        Assert.Equal(ProviderResultState.Unauthorized, ProviderErrorClassifier.Classify(typeName, message));
    }

    [Theory]
    [InlineData(null, "The attribute does not exist on the entity")]
    [InlineData(null, "'primaryagent' is not a valid attribute")]
    [InlineData("OrganizationServiceFault", "no entity defined by the name workflow")]
    public void MissingSchema_AreUnsupported(string? typeName, string? message)
    {
        Assert.Equal(ProviderResultState.Unsupported, ProviderErrorClassifier.Classify(typeName, message));
    }

    [Theory]
    [InlineData("HttpRequestException", "The remote name could not be resolved")]
    [InlineData("TimeoutException", "The request timed out after 30000 ms")]
    [InlineData(null, "Response status code does not indicate success: 503 (Server Error).")]
    [InlineData(null, "The underlying connection was closed")]
    [InlineData(null, null)]
    [InlineData(null, "Something unexpected happened")]
    public void EverythingElse_IsUnavailableNeverEmptySuccess(string? typeName, string? message)
    {
        Assert.Equal(ProviderResultState.Unavailable, ProviderErrorClassifier.Classify(typeName, message));
    }

    [Fact]
    public void SchemaProblemsOutrankAccessComplaintsInOneMessage()
    {
        // "does not have" is an access marker, "attribute does not exist" is a schema marker.
        ProviderResultState state = ProviderErrorClassifier.Classify(null, "The caller does not have permission and the attribute does not exist.");

        Assert.Equal(ProviderResultState.Unsupported, state);
    }

    [Fact]
    public void ClassificationNeverClaimsComplete()
    {
        Assert.NotEqual(ProviderResultState.Complete, ProviderErrorClassifier.Classify("Exception", "unknown failure"));
    }

    [Theory]
    [InlineData(ProviderResultState.Unauthorized)]
    [InlineData(ProviderResultState.Unsupported)]
    [InlineData(ProviderResultState.Unavailable)]
    [InlineData(ProviderResultState.Partial)]
    [InlineData(ProviderResultState.Stale)]
    public void EveryDegradedStateHasAdviceTheOperatorCanActOn(ProviderResultState state)
    {
        Assert.False(string.IsNullOrWhiteSpace(ProviderErrorClassifier.AdviceFor(state)));
    }

    [Fact]
    public void CompleteStateNeedsNoAdvice()
    {
        Assert.Equal(string.Empty, ProviderErrorClassifier.AdviceFor(ProviderResultState.Complete));
    }
}
