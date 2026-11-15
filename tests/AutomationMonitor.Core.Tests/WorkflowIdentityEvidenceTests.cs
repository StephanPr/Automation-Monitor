using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The bounded workflow-identity read and the evidence it produces. This is the only thing allowed to say
/// "verified", so what matters here is not that a happy path works but that every weaker answer - none found,
/// some found, the read stopped, the connection refused, the shape unreadable, nothing to ask - refuses just as
/// firmly as a failure does, and that a copied report can carry no identifier.
/// </summary>
public sealed class WorkflowIdentityEvidenceTests
{
    private const string Organization = "77777777-7777-7777-7777-777777777777";
    private const string NotADataverseId = "not-a-workflow-guid";

    private static readonly ResourceScope Connected = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));

    private static string FlowId(int index)
    {
        return index.ToString("x8", CultureInfo.InvariantCulture) + "-1111-2222-3333-444444444444";
    }

    private static List<WorkflowKey> Members(int count)
    {
        List<WorkflowKey> keys = new List<WorkflowKey>();
        for (int i = 1; i <= count; i++)
        {
            keys.Add(FakeWorkflowIdentitySource.Key(FlowId(i)));
        }

        return keys;
    }

    private static WorkflowIdentityEvidence Run(FakeWorkflowIdentitySource source, ResourceScope? organization, IEnumerable<WorkflowKey> members)
    {
        FixedClock clock = new FixedClock();
        return new DataverseWorkflowIdentityProbe(source, clock).Verify(organization, members, CancellationToken.None);
    }

    [Fact]
    public void ACompletedReadThatFoundEveryStoredIdIsVerified()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList();

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(3));

        Assert.Equal(WorkflowIdentityEvidenceKind.Verified, evidence.Kind);
        Assert.True(evidence.CanSupportAssociation);
        Assert.True(evidence.IsCompleted);
        Assert.Equal(3, evidence.Counts.QueryableCount);
        Assert.Equal(3, evidence.Counts.ResolvedCount);
        Assert.Equal(0, evidence.Counts.MissingCount);
        Assert.Equal(Connected, evidence.Organization);
    }

    [Fact]
    public void AReadThatFoundOnlySomeStoredIdsIsPartiallyResolved()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => new List<RawWorkflowRecord> { FakeWorkflowIdentitySource.Row(ids[0]) };

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(3));

        Assert.Equal(WorkflowIdentityEvidenceKind.PartiallyResolved, evidence.Kind);
        Assert.True(evidence.IsCompleted);
        Assert.False(evidence.CanSupportAssociation);
        Assert.Contains("partially resolved", evidence.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadThatFoundNoStoredIdIsUnresolvedAndLicensesNothing()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => new List<RawWorkflowRecord>();

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(2));

        Assert.Equal(WorkflowIdentityEvidenceKind.Unresolved, evidence.Kind);
        Assert.False(evidence.CanSupportAssociation);
        Assert.Equal(2, evidence.Counts.MissingCount);
        Assert.Contains("none of the workflow ids", evidence.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectionThatCannotNameAnOrganizationSendsNoRequestAtAll()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList();

        WorkflowIdentityEvidence evidence = Run(source, ResourceScope.Unverified, Members(3));

        Assert.Equal(WorkflowIdentityEvidenceKind.NoConnectedOrganization, evidence.Kind);
        Assert.False(evidence.CanSupportAssociation);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void AnEmptyListIsNothingToVerifyAndIsNeverTreatedAsVerified()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();

        WorkflowIdentityEvidence evidence = Run(source, Connected, new List<WorkflowKey>());

        Assert.Equal(WorkflowIdentityEvidenceKind.NothingToVerify, evidence.Kind);
        Assert.False(evidence.CanSupportAssociation);
        Assert.Equal(0, source.Calls);
        Assert.Contains("no members", evidence.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdThatIsNotADataverseWorkflowIdIsCountedAndBlocksVerification()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList();
        List<WorkflowKey> members = Members(2);
        members.Add(FakeWorkflowIdentitySource.Key(NotADataverseId));

        WorkflowIdentityEvidence evidence = Run(source, Connected, members);

        Assert.Equal(WorkflowIdentityEvidenceKind.PartiallyResolved, evidence.Kind);
        Assert.False(evidence.CanSupportAssociation);
        Assert.Equal(1, evidence.Counts.NotQueryableCount);
        Assert.Equal(2, evidence.Counts.QueryableCount);
    }

    [Fact]
    public void AMemberOfAnotherProviderIsNeverAskedOfDataverse()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList();
        List<WorkflowKey> members = Members(1);
        members.Add(WorkflowKey.Create(ProviderKind.LogicAppsStandard, null, FlowId(99)));

        WorkflowIdentityEvidence evidence = Run(source, Connected, members);

        Assert.Equal(1, evidence.Counts.NotQueryableCount);
        Assert.False(evidence.CanSupportAssociation);
        Assert.All(source.Batches, batch => Assert.DoesNotContain(FlowId(99), batch));
    }

    [Fact]
    public void AStoredIdHeldByTwoMembersIsAskedAboutOnce()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList();
        List<WorkflowKey> members = new List<WorkflowKey>
        {
            FakeWorkflowIdentitySource.Key(FlowId(1)),
            FakeWorkflowIdentitySource.Key(FlowId(1).ToUpperInvariant())
        };

        WorkflowIdentityEvidence evidence = Run(source, Connected, members);

        Assert.Equal(2, evidence.Counts.MemberCount);
        Assert.Equal(1, evidence.Counts.QueryableCount);
        Assert.Equal(1, source.Batches.Sum(b => b.Count));
        Assert.Equal(WorkflowIdentityEvidenceKind.Verified, evidence.Kind);
    }

    [Fact]
    public void AFullLegacyListIsSplitIntoBatchesThatStayInsideTheCeiling()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList();

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(59));

        Assert.Equal(WorkflowIdentityEvidenceKind.Verified, evidence.Kind);
        Assert.Equal(3, source.Batches.Count);
        Assert.All(source.Batches, batch => Assert.True(batch.Count <= DataverseWorkflowIdentityProbe.MaximumIdsPerBatch));
        Assert.Equal(59, source.Batches.Sum(b => b.Count));
        Assert.Equal(59, evidence.Counts.ResolvedCount);
    }

    [Fact]
    public void AListBeyondTheCeilingIsRefusedBeforeARequestIsSent()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList();

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(DataverseWorkflowIdentityProbe.MaximumVerifiableIds + 1));

        Assert.Equal(WorkflowIdentityEvidenceKind.Incomplete, evidence.Kind);
        Assert.False(evidence.CanSupportAssociation);
        Assert.Equal(0, source.Calls);
        Assert.Contains("than one read may verify", evidence.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ASourceThatKeepsPromisingPagesMakesTheEvidenceIncomplete()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource
        {
            ClaimsMoreRecords = true,
            Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList()
        };

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(2));

        Assert.Equal(WorkflowIdentityEvidenceKind.Incomplete, evidence.Kind);
        Assert.False(evidence.IsCompleted);
        Assert.False(evidence.CanSupportAssociation);
        Assert.True(source.Calls <= DataverseWorkflowIdentityProbe.MaximumPagesPerBatch,
            "a bounded read must stop at its own page ceiling instead of following the source");
    }

    [Fact]
    public void ARowWithNoIdIsCountedAndNeverInvented()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => new List<RawWorkflowRecord>
        {
            FakeWorkflowIdentitySource.Row(ids[0]),
            FakeWorkflowIdentitySource.Row(null)
        };

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(1));

        Assert.Equal(WorkflowIdentityEvidenceKind.Verified, evidence.Kind);
        Assert.Equal(1, evidence.Counts.UnidentifiableRows);
        Assert.Equal(2, evidence.Counts.RowsRead);
    }

    [Fact]
    public void ARepeatedRowIsCountedOnceAndNotResolvedTwice()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => new List<RawWorkflowRecord>
        {
            FakeWorkflowIdentitySource.Row(ids[0]),
            FakeWorkflowIdentitySource.Row(ids[0])
        };

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(1));

        Assert.Equal(WorkflowIdentityEvidenceKind.Verified, evidence.Kind);
        Assert.Equal(1, evidence.Counts.ResolvedCount);
        Assert.Equal(1, evidence.Counts.DuplicateRows);
    }

    [Fact]
    public void ARowTheReadDidNotAskForMakesTheWholeAnswerUntrustworthy()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => new List<RawWorkflowRecord>
        {
            FakeWorkflowIdentitySource.Row(ids[0]),
            FakeWorkflowIdentitySource.Row(FlowId(777))
        };

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(1));

        Assert.Equal(WorkflowIdentityEvidenceKind.Incomplete, evidence.Kind);
        Assert.False(evidence.CanSupportAssociation);
        Assert.Contains("did not ask about", evidence.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("The user does not have sufficient privileges to read workflow records.", WorkflowIdentityEvidenceKind.Unauthorized)]
    [InlineData("The 'workflow' entity does not exist in this environment.", WorkflowIdentityEvidenceKind.Unsupported)]
    [InlineData("A connection attempt failed because the server did not respond.", WorkflowIdentityEvidenceKind.Unavailable)]
    public void EveryRefusedReadKeepsItsOwnReasonInsteadOfOneBlankVerdict(string message, WorkflowIdentityEvidenceKind expected)
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource { Throw = new InvalidOperationException(message) };

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(2));

        Assert.Equal(expected, evidence.Kind);
        Assert.False(evidence.CanSupportAssociation);
        Assert.False(evidence.IsCompleted);
        Assert.NotEmpty(evidence.Reason);
    }

    [Fact]
    public void AShapeThisBuildCannotReadIsBlamedOnThisBuildAndNotOnTheConnection()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource
        {
            Throw = new DataverseMappingException("workflowid", "Guid", "Int32")
        };

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(2));

        Assert.Equal(WorkflowIdentityEvidenceKind.Unsupported, evidence.Kind);
        Assert.Contains("Automation Monitor", evidence.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Check the connection", evidence.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ATransportFailureNeverCarriesTheProvidersOwnTextIntoTheReason()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource
        {
            Throw = new InvalidOperationException("Failed executing GET https://contoso.api.crm11.dynamics.com/workflows?token=supersecret 500")
        };

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(1));

        Assert.Equal(WorkflowIdentityEvidenceKind.Unavailable, evidence.Kind);
        Assert.DoesNotContain("contoso.api.crm11.dynamics.com", evidence.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supersecret", evidence.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellationPropagatesInsteadOfBecomingAFailedRead()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource { Throw = new OperationCanceledException() };
        DataverseWorkflowIdentityProbe probe = new DataverseWorkflowIdentityProbe(source, new FixedClock());

        Assert.ThrowsAny<OperationCanceledException>(() => probe.Verify(Connected, Members(2), CancellationToken.None));
    }

    [Fact]
    public void AReportAnyoneCanCopyCarriesNoIdentifierAtAll()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList();

        WorkflowIdentityEvidence evidence = Run(source, Connected, Members(2));
        string report = evidence.Describe();

        // The organization id and every workflow id are identity, and identity is not something a status window
        // hands to the clipboard. Only a fingerprint and counts may appear.
        Assert.DoesNotContain(Organization, report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FlowId(1), report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dataverse organization 77777777", report, StringComparison.Ordinal);
        Assert.Contains("What this does not prove", report, StringComparison.Ordinal);
    }
}
