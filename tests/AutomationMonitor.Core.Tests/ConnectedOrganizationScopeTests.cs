using System;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// AM-44.3a: the one mechanism that turns a connected Dataverse organization into the resource scope every
/// consumer keys by. Discovery, the saved-watchlist catalogue, the settings profile and the environment column
/// must take their scope from here and nowhere else, so these cases pin the shape of the token, what a failed
/// read does to it, and that the identity read happens once per connection rather than once per record. The
/// identifiers are invented; none of them is a real environment's.
/// </summary>
public class ConnectedOrganizationScopeTests
{
    private const string OrganizationA = "11111111-1111-1111-1111-111111111111";
    private const string OrganizationB = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void AVerifiedOrganizationBecomesTheQualifiedResourceScope()
    {
        ConnectedOrganization organization = ResolveWith(OrganizationA);

        Assert.True(organization.IsResolved);
        Assert.Equal(ConnectedOrganizationState.Resolved, organization.State);
        Assert.Equal(OrganizationA, organization.Scope.Identifier);
        Assert.Equal("dataverse-organization:" + OrganizationA, organization.ScopeText);
        Assert.Equal(ResourceScopeKind.DataverseOrganization, organization.Scope.Kind);

        // The stored value must be readable by the same contract that reads it back off disk, or a saved list
        // would hold a scope no later build could interpret.
        ResourceScope reread = ResourceScope.Read(organization.ScopeText);
        Assert.True(reread.IsQualified);
        Assert.True(reread.IsSameResourceAs(organization.Scope));
    }

    [Fact]
    public void AGuidInAnotherSpellingIsCanonicalisedBeforeItBecomesAScope()
    {
        ConnectedOrganization organization = ResolveWith("11111111-1111-1111-1111-111111111111".ToUpperInvariant());

        // Casing and braces from a host must not fork one organization into two stored scopes.
        Assert.Equal("dataverse-organization:" + OrganizationA, organization.ScopeText);
    }

    [Fact]
    public void AnUnsuccessfulIdentityReadLeavesTheScopeUnverifiedRatherThanInvented()
    {
        FakeOrganizationIdentity identity = new FakeOrganizationIdentity();
        identity.Fail();
        ConnectedOrganizationResolver resolver = new ConnectedOrganizationResolver(identity, new FixedClock());

        ConnectedOrganization organization = resolver.Resolve(CancellationToken.None);

        Assert.False(organization.IsResolved);
        Assert.Equal(ConnectedOrganizationState.Failed, organization.State);

        // Empty is the spelling of unverified, and the same one a legacy member carries. Substituting a
        // connection url, a tenant id or another organization's value here would be a fabricated identity.
        Assert.Equal(string.Empty, organization.ScopeText);
        Assert.True(organization.Scope.IsUnverified);
        Assert.NotNull(organization.FailureKind);
    }

    [Fact]
    public void AnAllZeroOrganizationIdCannotBecomeAnIdentity()
    {
        // The boundary refuses it, so no caller can hand an all-zero value to the resolver as an answer and have
        // it stored as a scope: Guid.Empty names no organization, and inventing one would key a watchlist to an
        // environment that does not exist.
        Assert.Throws<ArgumentException>(
            () => AutomationMonitor.Core.Abstractions.OrganizationIdentityRead.Identified(Guid.Empty));

        FakeOrganizationIdentity identity = new FakeOrganizationIdentity();
        identity.Fail(ProviderResultState.Unavailable, "OrganizationIdSection.NotFound");
        ConnectedOrganization organization =
            new ConnectedOrganizationResolver(identity, new FixedClock()).Resolve(CancellationToken.None);

        Assert.False(organization.IsResolved);
        Assert.Equal(string.Empty, organization.ScopeText);
    }

    [Fact]
    public void AReadIsTakenOncePerConnectionNotOncePerRecord()
    {
        FakeOrganizationIdentity identity = new FakeOrganizationIdentity();
        identity.Succeed(Guid.Parse(OrganizationA));
        ConnectedOrganizationResolver resolver = new ConnectedOrganizationResolver(identity, new FixedClock());

        ConnectedOrganization first = resolver.Resolve(CancellationToken.None);
        ConnectedOrganization second = resolver.Resolve(CancellationToken.None);
        ConnectedOrganization third = resolver.Resolve(CancellationToken.None);

        Assert.Equal(1, identity.Calls);
        Assert.Equal(first.ScopeText, second.ScopeText);
        Assert.Equal(first.ScopeText, third.ScopeText);
        Assert.Equal(1, resolver.Reads);
    }

    [Fact]
    public void AFailedReadIsNotCachedAsAnAnswerSoTheNextActionMayStillSucceed()
    {
        FakeOrganizationIdentity identity = new FakeOrganizationIdentity();
        identity.Fail();
        identity.Succeed(Guid.Parse(OrganizationA));
        ConnectedOrganizationResolver resolver = new ConnectedOrganizationResolver(identity, new FixedClock());

        Assert.False(resolver.Resolve(CancellationToken.None).IsResolved);
        Assert.True(resolver.Resolve(CancellationToken.None).IsResolved);
        Assert.Equal(2, identity.Calls);
    }

    [Fact]
    public void ACancellationTokenReachesTheIdentityRead()
    {
        FakeOrganizationIdentity identity = new FakeOrganizationIdentity();
        identity.Succeed(Guid.Parse(OrganizationA));
        ConnectedOrganizationResolver resolver = new ConnectedOrganizationResolver(identity, new FixedClock());

        using CancellationTokenSource cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.True(resolver.Resolve(cancelled.Token).IsResolved);
        Assert.True(identity.Tokens[0].IsCancellationRequested);
    }

    [Fact]
    public void ABindingToANewConnectionDropsThePreviousOrganizationsIdentity()
    {
        FakeOrganizationIdentity identity = new FakeOrganizationIdentity();
        identity.Succeed(Guid.Parse(OrganizationA));
        identity.Succeed(Guid.Parse(OrganizationB));
        ConnectedOrganizationResolver resolver = new ConnectedOrganizationResolver(identity, new FixedClock());

        Assert.Equal("dataverse-organization:" + OrganizationA, resolver.Resolve(CancellationToken.None).ScopeText);

        resolver.Bind("connection-two");
        Assert.Equal(ConnectedOrganizationState.NotAttempted, resolver.Current.State);
        Assert.Equal("dataverse-organization:" + OrganizationB, resolver.Resolve(CancellationToken.None).ScopeText);
        Assert.Equal(2, identity.Calls);

        // Re-binding the same connection must not spend another round trip.
        resolver.Bind("connection-two");
        Assert.Equal("dataverse-organization:" + OrganizationB, resolver.Resolve(CancellationToken.None).ScopeText);
        Assert.Equal(2, identity.Calls);
    }

    [Fact]
    public void TheSameWorkflowIdInTwoOrganizationsIsTwoWorkflowIdentities()
    {
        const string workflowId = "99999999-9999-9999-9999-999999999999";
        ConnectedOrganization organizationA = ResolveWith(OrganizationA);
        ConnectedOrganization organizationB = ResolveWith(OrganizationB);

        WorkflowKey keyA = WorkflowKey.Create(ProviderKind.PowerAutomate, organizationA.ScopeText, workflowId);
        WorkflowKey keyB = WorkflowKey.Create(ProviderKind.PowerAutomate, organizationB.ScopeText, workflowId);

        Assert.NotEqual(keyA, keyB);
        Assert.NotEqual(keyA.Canonical, keyB.Canonical);

        // The provider's own id is preserved exactly: qualifying the scope must never disturb the value the
        // flowrun.workflowid text filter is measured against.
        Assert.Equal(workflowId, keyA.WorkflowId);
        Assert.Equal(workflowId, keyB.WorkflowId);
    }

    [Fact]
    public void AnUnverifiedMemberNeverMatchesAVerifiedMemberOfTheSameWorkflow()
    {
        const string workflowId = "33333333-3333-3333-3333-333333333333";
        ConnectedOrganization verified = ResolveWith(OrganizationA);

        WorkflowKey unverified = WorkflowKey.Create(ProviderKind.PowerAutomate, string.Empty, workflowId);
        WorkflowKey qualified = WorkflowKey.Create(ProviderKind.PowerAutomate, verified.ScopeText, workflowId);

        // The whole isolation rule in one place: a legacy member and a freshly discovered flow with the same id
        // are different identities, so no import, add, refresh or save can pair them silently.
        Assert.NotEqual(unverified, qualified);
        Assert.False(unverified.Scope.IsSameResourceAs(qualified.Scope));
        Assert.False(verified.IsSameOrganizationAs(unverified.Scope));
        Assert.False(ResourceScope.Unverified.IsSameResourceAs(verified.Scope));
    }

    [Fact]
    public void TwoUnverifiedScopesAreNotAnIdentityMatch()
    {
        // Two unscoped lists share the absence of evidence, and the absence of evidence is not evidence of being
        // the same environment. Nothing may treat them as connected.
        Assert.False(ResourceScope.Unverified.IsSameResourceAs(ResourceScope.Read("   ")));
    }

    [Fact]
    public void TheConnectedOrganizationCarriesItsDisplayContextSeparatelyFromItsIdentity()
    {
        ConnectedOrganization organization = ResolveWith(OrganizationA).WithDisplayName("Test Organization");

        Assert.Equal("Test Organization", organization.DisplayName);
        Assert.Equal("dataverse-organization:" + OrganizationA, organization.ScopeText);

        // Renaming the organization changes no key, and a display name never becomes the scope.
        Assert.Equal(organization.ScopeText, organization.WithDisplayName("Renamed").ScopeText);
        Assert.NotEqual("Test Organization", organization.ScopeText);
    }

    [Fact]
    public void ADisplayContextIsSanitisedAndClampedBecauseItCameFromTheHost()
    {
        ConnectedOrganization organization = ResolveWith(OrganizationA).WithDisplayName("  Tab\tand\rnewline  ");

        Assert.Equal("Tab and newline", organization.DisplayName);

        char[] longName = new char[ConnectedOrganization.MaximumDisplayNameLength + 40];
        for (int index = 0; index < longName.Length; index++)
        {
            longName[index] = 'a';
        }

        Assert.Equal(
            ConnectedOrganization.MaximumDisplayNameLength,
            organization.WithDisplayName(new string(longName)).DisplayName!.Length);
        Assert.Null(organization.WithDisplayName("   ").DisplayName);
    }

    private static ConnectedOrganization ResolveWith(string organizationIdText)
    {
        FakeOrganizationIdentity identity = new FakeOrganizationIdentity();
        identity.Succeed(Guid.Parse(organizationIdText));
        return new ConnectedOrganizationResolver(identity, new FixedClock()).Resolve(CancellationToken.None);
    }
}
