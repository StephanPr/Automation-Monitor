using AutomationMonitor.Core.Models;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Identity is provider-scoped id, never a display name. These tests pin the rules that
/// de-duplication and future watchlist persistence depend on.
/// </summary>
public class WorkflowKeyTests
{
    private const string Env = "11111111-1111-1111-1111-111111111111";
    private const string Flow = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void SameProviderScopeAndId_AreEqual()
    {
        WorkflowKey left = WorkflowKey.Create(ProviderKind.PowerAutomate, Env, Flow);
        WorkflowKey right = WorkflowKey.Create(ProviderKind.PowerAutomate, Env, Flow);

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.Equal(left.Canonical, right.Canonical);
    }

    [Fact]
    public void DifferentProvider_SameId_IsDifferentKey()
    {
        WorkflowKey automation = WorkflowKey.Create(ProviderKind.PowerAutomate, Env, Flow);
        WorkflowKey unknown = WorkflowKey.Create(ProviderKind.Unknown, Env, Flow);

        Assert.False(automation.Equals(unknown));
        Assert.DoesNotContain("powerautomate", unknown.Canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentEnvironment_SameWorkflowId_IsDifferentKey()
    {
        WorkflowKey prod = WorkflowKey.Create(ProviderKind.PowerAutomate, Env, Flow);
        WorkflowKey test = WorkflowKey.Create(ProviderKind.PowerAutomate, "33333333-3333-3333-3333-333333333333", Flow);

        Assert.False(prod.Equals(test));
    }

    [Fact]
    public void UnknownScope_NeverMergesWithKnownScope()
    {
        WorkflowKey scoped = WorkflowKey.Create(ProviderKind.PowerAutomate, Env, Flow);
        WorkflowKey unscoped = WorkflowKey.Create(ProviderKind.PowerAutomate, null, Flow);

        Assert.False(scoped.Equals(unscoped));
        Assert.Equal(string.Empty, unscoped.ScopeId);
    }

    [Fact]
    public void IdIsCaseInsensitiveAcrossGuidFormats()
    {
        WorkflowKey dashed = WorkflowKey.Create(ProviderKind.PowerAutomate, Env, Flow);
        WorkflowKey braces = WorkflowKey.Create(ProviderKind.PowerAutomate, Env, "{" + Flow.ToUpperInvariant() + "}");

        Assert.True(dashed.Equals(braces));
        Assert.Equal(Flow.ToLowerInvariant(), dashed.WorkflowId);
    }

    [Fact]
    public void MissingWorkflowId_CannotBuildIdentity()
    {
        Assert.Throws<ArgumentException>(() => WorkflowKey.Create(ProviderKind.PowerAutomate, Env, "  "));

        WorkflowKey? key;
        Assert.False(WorkflowKey.TryCreate(ProviderKind.PowerAutomate, Env, null, out key));
        Assert.Null(key);
    }

    [Fact]
    public void CanonicalUsesProviderPrefixNotDisplayName()
    {
        WorkflowKey key = WorkflowKey.Create(ProviderKind.PowerAutomate, Env, Flow);

        Assert.StartsWith("powerautomate:", key.Canonical, StringComparison.Ordinal);
        Assert.Equal("powerautomate:" + Env.ToLowerInvariant() + ":" + Flow.ToLowerInvariant(), key.Canonical);
    }
}
