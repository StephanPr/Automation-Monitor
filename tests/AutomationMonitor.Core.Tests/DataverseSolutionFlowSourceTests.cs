using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The discovery provider over a fake row source: every page is consumed, a stall is reported as
/// partial, and a failure is never rendered as "there are no solutions".
/// </summary>
public class DataverseSolutionFlowSourceTests
{
    [Fact]
    public void ConstructorRejectsAMissingRowSource()
    {
        Assert.Throws<ArgumentNullException>(() => new DataverseSolutionFlowSource(null!, new FixedClock()));
    }

    [Fact]
    public void ProviderIsPowerAutomateAndEnvironmentPassesThrough()
    {
        FakeRowSource rows = new FakeRowSource();
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());

        Assert.Equal(ProviderKind.PowerAutomate, source.Provider);
        Assert.Equal("55555555-5555-5555-5555-555555555555", source.EnvironmentId);
    }

    [Fact]
    public void GetSolutions_ConsumesEveryPageAndStampsTheCheckTime()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.SolutionPages.Add(FakeRowSource.Of(true, "cookie-1", FakeRowSource.Solution("s2", "Zebra"), FakeRowSource.Solution("s1", "Apple")));
        rows.SolutionPages.Add(FakeRowSource.Of(false, null, FakeRowSource.Solution("s3", "Mango")));
        FixedClock clock = new FixedClock();
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, clock);

        ProviderResult<IReadOnlyList<SolutionReference>> result = source.GetSolutions(CancellationToken.None);

        Assert.Equal(ProviderResultState.Complete, result.State);
        Assert.True(result.IsUsable);
        Assert.Equal(2, result.PagesRead);
        Assert.Equal(3, result.ItemsRead);
        Assert.Equal(clock.Now, result.CheckedAtUtc);
        Assert.Equal("Apple", result.Value![0].DisplayName);
        Assert.Equal("Mango", result.Value![1].DisplayName);
        Assert.Equal("Zebra", result.Value![2].DisplayName);
        Assert.Null(rows.SolutionRequests[0].Cookie);
        Assert.Equal("cookie-1", rows.SolutionRequests[1].Cookie);
    }

    [Fact]
    public void GetSolutions_StalledSourceIsPartialNotEmpty()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.SolutionPages.Add(FakeRowSource.Of<SolutionReference>(true, "same-cookie"));
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());

        ProviderResult<IReadOnlyList<SolutionReference>> result = source.GetSolutions(CancellationToken.None);

        Assert.Equal(ProviderResultState.Partial, result.State);
        Assert.True(result.IsUsable);
        Assert.Empty(result.Value!);
        Assert.Contains("empty page", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSolutions_PermissionProblemIsUnauthorizedWithAdvice()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.ThrowForSolutions = new Exception("Insufficient privilege to read solutions.");
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());

        ProviderResult<IReadOnlyList<SolutionReference>> result = source.GetSolutions(CancellationToken.None);

        Assert.Equal(ProviderResultState.Unauthorized, result.State);
        Assert.False(result.IsUsable);
        Assert.Null(result.Value);
        Assert.Contains("security role", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Insufficient privilege", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSolutions_SchemaProblemIsUnsupportedNotUnavailable()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.ThrowForSolutions = new Exception("The attribute does not exist on the entity.");
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());

        ProviderResult<IReadOnlyList<SolutionReference>> result = source.GetSolutions(CancellationToken.None);

        Assert.Equal(ProviderResultState.Unsupported, result.State);
        Assert.Contains("does not expose the expected table", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The WF-A regression: a value this tool could not map happens after the environment has
    /// answered, so it must never be described as a connection that went quiet. The state stays
    /// Unsupported (no new coverage label), the wording names this tool and the column, and the
    /// shared renderer still shows the honest coverage label.
    /// </summary>
    [Fact]
    public void GetSolutions_MappingDefectNamesThisToolInsteadOfTheConnection()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.ThrowForSolutions = new DataverseMappingException("uniquename", "text", "Guid");
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());

        ProviderResult<IReadOnlyList<SolutionReference>> result = source.GetSolutions(CancellationToken.None);

        Assert.Equal(ProviderResultState.Unsupported, result.State);
        Assert.False(result.IsUsable);
        Assert.Null(result.Value);
        Assert.Contains("Automation Monitor could not read a value", result.Message, StringComparison.Ordinal);
        Assert.Contains("uniquename", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("did not answer", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("does not expose the expected table", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT SUPPORTED", SourceStatus.Describe("Solutions", result), StringComparison.Ordinal);
    }

    [Fact]
    public void GetSolutions_RawCastFailureIsNoLongerReportedAsAnOutage()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.ThrowForSolutions = new InvalidCastException(
            "Unable to cast object of type 'System.String' to type 'Microsoft.Xrm.Sdk.AliasedValue'.");
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());

        ProviderResult<IReadOnlyList<SolutionReference>> result = source.GetSolutions(CancellationToken.None);

        Assert.Equal(ProviderResultState.Unsupported, result.State);
        Assert.DoesNotContain("Check the connection", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSolutions_TransportFailureIsUnavailableAndNeverRepeatsAUrl()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.ThrowForSolutions = new Exception("The underlying connection was closed: https://contoso.crm.dynamics.com/api");
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());

        ProviderResult<IReadOnlyList<SolutionReference>> result = source.GetSolutions(CancellationToken.None);

        Assert.Equal(ProviderResultState.Unavailable, result.State);
        Assert.Null(result.Value);
        Assert.DoesNotContain("contoso.crm.dynamics.com", result.Message, StringComparison.Ordinal);
        Assert.Contains("[url]", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSolutions_CancellationPropagatesInsteadOfBecomingAnEmptyResult()
    {
        using CancellationTokenSource tokens = new CancellationTokenSource();
        tokens.Cancel();
        FakeRowSource rows = new FakeRowSource();
        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());

        Assert.Throws<OperationCanceledException>(() => source.GetSolutions(tokens.Token));
        Assert.Equal(0, rows.SolutionCalls);
    }
}
