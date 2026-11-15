using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The host's organization-identity read as a scripted answer, so the resolver's rules - one read per
/// connection, a failure never cached as an answer, a new connection dropping the old identity - are measured
/// on the seam rather than on a fake of the logic under test. Nothing here reaches a tenant: the identifiers are
/// invented, and none of them is a real environment's.
/// </summary>
internal sealed class FakeOrganizationIdentity : IOrganizationIdentitySource
{
    private readonly Queue<OrganizationIdentityRead> answers = new Queue<OrganizationIdentityRead>();

    /// <summary>How many reads the resolver actually asked for. The caching rule is asserted on this.</summary>
    public int Calls { get; private set; }

    /// <summary>The cancellation token each read was handed, in order.</summary>
    public List<CancellationToken> Tokens { get; } = new List<CancellationToken>();

    public void Succeed(Guid organizationId)
    {
        answers.Enqueue(OrganizationIdentityRead.Identified(organizationId));
    }

    public void Fail(
        ProviderResultState state = ProviderResultState.Unavailable,
        string kind = "FaultException")
    {
        answers.Enqueue(OrganizationIdentityRead.Failed(state, kind));
    }

    public OrganizationIdentityRead Read(CancellationToken cancellationToken)
    {
        Calls++;
        Tokens.Add(cancellationToken);

        if (answers.Count == 0)
        {
            throw new InvalidOperationException("the test scripted no further organization-identity answer");
        }

        return answers.Dequeue();
    }
}
