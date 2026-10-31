using System;
using AutomationMonitor.Core.Models;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Run identity is provider + environment + workflow + run id, and nothing else. These tests pin the
/// rules that de-duplication, correlation and any later cache depend on.
/// </summary>
public class RunKeyTests
{
    private const string Env = "11111111-1111-1111-1111-111111111111";
    private const string Flow = "22222222-2222-2222-2222-222222222222";
    private const string Run = "08583000124706209406282721132cu56";

    [Fact]
    public void SameWorkflowAndRunId_AreTheSameRun()
    {
        RunKey left = RunKey.Create(FakeRunRowSource.Key(), Run);
        RunKey right = RunKey.Create(FakeRunRowSource.Key(), Run);

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.Equal(left.Canonical, right.Canonical);
    }

    [Fact]
    public void RunIdSpellingVariants_Collapse()
    {
        RunKey plain = RunKey.Create(FakeRunRowSource.Key(), Run);
        RunKey upper = RunKey.Create(FakeRunRowSource.Key(), Run.ToUpperInvariant());
        RunKey padded = RunKey.Create(FakeRunRowSource.Key(), "  " + Run + "  ");

        Assert.True(plain.Equals(upper));
        Assert.True(plain.Equals(padded));
    }

    [Fact]
    public void GuidSpellingVariants_CollapseToTheDashedForm()
    {
        string guid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        RunKey dashed = RunKey.Create(FakeRunRowSource.Key(), guid);
        RunKey braced = RunKey.Create(FakeRunRowSource.Key(), "{" + guid.ToUpperInvariant() + "}");
        RunKey bare = RunKey.Create(FakeRunRowSource.Key(), guid.Replace("-", string.Empty));

        Assert.True(dashed.Equals(braced));
        Assert.True(dashed.Equals(bare));
        Assert.EndsWith(":" + guid, dashed.Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void SameRunId_DifferentWorkflow_IsADifferentRun()
    {
        RunKey first = RunKey.Create(FakeRunRowSource.Key("22222222-2222-2222-2222-222222222222"), Run);
        RunKey second = RunKey.Create(FakeRunRowSource.Key("33333333-3333-3333-3333-333333333333"), Run);

        Assert.False(first.Equals(second));
    }

    [Fact]
    public void SameRunId_DifferentEnvironment_IsADifferentRun()
    {
        RunKey prod = RunKey.Create(FakeRunRowSource.Key(environmentId: Env), Run);
        RunKey test = RunKey.Create(FakeRunRowSource.Key(environmentId: "44444444-4444-4444-4444-444444444444"), Run);

        Assert.False(prod.Equals(test));
    }

    [Fact]
    public void UnscopedEnvironment_NeverMergesWithAScopedOne()
    {
        RunKey scoped = RunKey.Create(FakeRunRowSource.Key(), Run);
        RunKey unscoped = RunKey.Create(FakeRunRowSource.Key(environmentId: null), Run);

        Assert.False(scoped.Equals(unscoped));
        Assert.Equal(string.Empty, unscoped.EnvironmentId);
    }

    [Fact]
    public void SameRunId_OnAnotherProvider_IsADifferentRun()
    {
        RunKey automation = RunKey.Create(FakeRunRowSource.Key(provider: ProviderKind.PowerAutomate), Run);
        RunKey standard = RunKey.Create(FakeRunRowSource.Key(provider: ProviderKind.LogicAppsStandard), Run);

        Assert.False(automation.Equals(standard));
        Assert.StartsWith("logicapps-standard:", standard.Canonical, StringComparison.Ordinal);
        Assert.DoesNotContain(automation.Canonical, standard.Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryProviderKind_HasItsOwnNamespace()
    {
        Assert.Equal("powerautomate", ProviderKinds.Prefix(ProviderKind.PowerAutomate));
        Assert.Equal("logicapps-consumption", ProviderKinds.Prefix(ProviderKind.LogicAppsConsumption));
        Assert.Equal("logicapps-standard", ProviderKinds.Prefix(ProviderKind.LogicAppsStandard));
        Assert.Equal("unknown", ProviderKinds.Prefix(ProviderKind.Unknown));
    }

    [Fact]
    public void OnlyPowerAutomateRunsAreImplementedToday()
    {
        Assert.True(ProviderKinds.IsImplemented(ProviderKind.PowerAutomate));
        Assert.False(ProviderKinds.IsImplemented(ProviderKind.LogicAppsConsumption));
        Assert.False(ProviderKinds.IsImplemented(ProviderKind.LogicAppsStandard));
        Assert.False(ProviderKinds.IsImplemented(ProviderKind.Unknown));
    }

    [Fact]
    public void MissingRunId_CannotBuildIdentity()
    {
        Assert.Throws<ArgumentException>(() => RunKey.Create(FakeRunRowSource.Key(), "   "));

        RunKey? key;
        Assert.False(RunKey.TryCreate(FakeRunRowSource.Key(), null, out key));
        Assert.Null(key);
        Assert.False(RunKey.TryCreate(null, Run, out key));
        Assert.Null(key);
    }

    [Fact]
    public void StatusAndName_NeverParticipateInIdentity()
    {
        RunKey key = RunKey.Create(FakeRunRowSource.Key(), Run);
        WorkflowRun succeeded = new WorkflowRun(
            key, RunStatus.Succeeded, "Succeeded", null, null, null, "s", new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero),
            ProviderResultState.Complete, workflowName: "Nightly sync");
        WorkflowRun failed = new WorkflowRun(
            key, RunStatus.Failed, "Failed", null, null, null, "s", new DateTimeOffset(2026, 10, 29, 13, 0, 0, TimeSpan.Zero),
            ProviderResultState.Partial, workflowName: "Renamed sync");

        Assert.True(succeeded.Key.Equals(failed.Key));
        Assert.Equal(succeeded.Key.Canonical, failed.Key.Canonical);
    }
}
