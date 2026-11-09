using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
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

    // ---- WF-A: a defect inside this tool is not the environment going quiet ---------------------

    /// <summary>
    /// The exact message the first real Windows session showed. It used to classify as Unavailable,
    /// which told the operator to check a connection that was working: the environment had answered,
    /// and the cast that failed was ours.
    /// </summary>
    [Fact]
    public void TheCastWeUsedToDoThis_IsNotReportedAsTheEnvironmentNotAnswering()
    {
        Exception cast = new InvalidCastException(
            "Unable to cast object of type 'System.String' to type 'Microsoft.Xrm.Sdk.AliasedValue'.");

        ProviderResultState state = ProviderErrorClassifier.Classify(cast.GetType().Name, cast.Message);

        Assert.Equal(ProviderResultState.Unsupported, state);
        Assert.True(ProviderErrorClassifier.IsLocalDefect(cast.GetType().Name, cast.Message));
        Assert.DoesNotContain("did not answer", ProviderErrorClassifier.AdviceFor(state, true), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("InvalidCastException", "Unable to cast object of type 'System.String' to type 'Microsoft.Xrm.Sdk.AliasedValue'.")]
    [InlineData("InvalidCastException", "Specified cast is not valid.")]
    [InlineData("DataverseMappingException", "Automation Monitor could not read the 'uniquename' value returned by the environment (expected text, received Guid).")]
    [InlineData("Exception", "Automation Monitor could not read the 'name' value returned by the environment.")]
    public void LocalMappingFailures_AreUnsupportedAndMarkedLocal(string? typeName, string? message)
    {
        Assert.Equal(ProviderResultState.Unsupported, ProviderErrorClassifier.Classify(typeName, message));
        Assert.True(ProviderErrorClassifier.IsLocalDefect(typeName, message));
    }

    /// <summary>
    /// The type name has to be enough on its own. A message can be sanitised away, wrapped, or replaced
    /// by a transport layer, and a marker spelled from memory can quietly stop matching - which is exactly
    /// how a misspelled "DataverseMappingException" marker would have left a mapping defect looking like an
    /// outage while every paired test above still passed.
    /// </summary>
    [Theory]
    [InlineData(nameof(DataverseMappingException))]
    [InlineData(nameof(InvalidCastException))]
    [InlineData(nameof(OverflowException))]
    [InlineData(nameof(FormatException))]
    public void TheTypeNameAloneMarksALocalDefect(string typeName)
    {
        Assert.True(ProviderErrorClassifier.IsLocalDefect(typeName, null));
        Assert.Equal(ProviderResultState.Unsupported, ProviderErrorClassifier.Classify(typeName, null));
    }

    [Fact]
    public void AMappingFailureWhoseMessageWasLostIsStillKnownToBeOurs()
    {
        DataverseMappingException mapping = new DataverseMappingException("uniquename", "text", "Guid");

        Assert.True(ProviderErrorClassifier.IsLocalDefect(mapping.GetType().Name, "a message that no longer says anything"));
        Assert.Equal(ProviderResultState.Unsupported, ProviderErrorClassifier.Classify(mapping.GetType().Name, null));
    }

    [Fact]
    public void ALocalFailureOutranksAnAccessWordInOneMessage()
    {
        string message = "Automation Monitor could not read the 'name' value: the caller does not have permission? no - a cast failed.";

        Assert.Equal(ProviderResultState.Unsupported, ProviderErrorClassifier.Classify("DataverseMappingException", message));
        Assert.True(ProviderErrorClassifier.IsLocalDefect("DataverseMappingException", message));
    }

    [Theory]
    [InlineData("HttpRequestException", "The remote name could not be resolved")]
    [InlineData("TimeoutException", "The request timed out after 30000 ms")]
    [InlineData(null, "Insufficient privilege to read solutioncomponent")]
    [InlineData("OrganizationServiceFault", "The attribute does not exist on the entity")]
    [InlineData(null, null)]
    public void EnvironmentFailuresAreNotLocalDefects(string? typeName, string? message)
    {
        Assert.False(ProviderErrorClassifier.IsLocalDefect(typeName, message));
    }

    [Theory]
    [InlineData(ProviderResultState.Unauthorized)]
    [InlineData(ProviderResultState.Unsupported)]
    [InlineData(ProviderResultState.Unavailable)]
    [InlineData(ProviderResultState.Partial)]
    [InlineData(ProviderResultState.Stale)]
    public void TheSingleArgumentAdviceIsTheEnvironmentWording(ProviderResultState state)
    {
        Assert.Equal(ProviderErrorClassifier.AdviceFor(state, false), ProviderErrorClassifier.AdviceFor(state));
    }

    [Fact]
    public void LocalDefectAdviceSaysWhoIsAtFault()
    {
        string advice = ProviderErrorClassifier.AdviceFor(ProviderResultState.Unsupported, true);

        Assert.Contains("Automation Monitor", advice, StringComparison.Ordinal);
        Assert.Contains("connection", advice, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(ProviderErrorClassifier.AdviceFor(ProviderResultState.Unavailable, true)));
    }

    [Fact]
    public void EnvironmentUnsupportedAdviceStillNamesTheTableProblem()
    {
        string advice = ProviderErrorClassifier.AdviceFor(ProviderResultState.Unsupported, false);

        Assert.Contains("table or column", advice, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Automation Monitor", advice, StringComparison.Ordinal);
    }
}
