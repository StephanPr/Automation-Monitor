using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// What a verification read is allowed to say about itself. The first live run (2026-11-15) sent a query the
/// platform refused, reported 59 members as "not found", and told the operator the environment had not
/// answered. Each of those was a reporting defect: a refused read has established nothing, so it may not name
/// a missing workflow, may not claim any membership is proven, and must blame the query this build wrote
/// rather than the connection. These tests hold that line for every way a read can fail to finish.
/// </summary>
public sealed class WorkflowIdentityEvidenceReportingTests
{
    private const string Organization = "77777777-7777-7777-7777-777777777777";

    private static readonly ResourceScope Connected = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));

    private static string FlowId(int index)
    {
        return index.ToString("x8", CultureInfo.InvariantCulture) + "-1111-2222-3333-444444444444";
    }

    private static List<WorkflowKey> Members(int count)
    {
        List<WorkflowKey> keys = new List<WorkflowKey>();
        for (int index = 1; index <= count; index++)
        {
            keys.Add(WorkflowKey.Create(ProviderKind.PowerAutomate, null, FlowId(index)));
        }

        return keys;
    }

    private static WorkflowIdentityEvidence Run(FakeWorkflowIdentitySource source, int members)
    {
        return new DataverseWorkflowIdentityProbe(source, new FixedClock())
            .Verify(Connected, Members(members), CancellationToken.None);
    }

    /// <summary>The one line of the copied report that states what the read established.</summary>
    private static string ProvesLine(string report)
    {
        return report.Split('\n').First(line => line.Contains("What this proves", StringComparison.Ordinal)).Trim();
    }

    [Fact]
    public void ARefusedQueryReportsNoMissingWorkflowAtAll()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource
        {
            Throw = FakeWorkflowIdentitySource.RefusedQuery()
        };

        WorkflowIdentityEvidence evidence = Run(source, 59);

        // Nothing was answered, so nothing is absent. This is the misreport the live run produced: 59 members
        // counted as missing by a query that never ran.
        Assert.Equal(WorkflowIdentityEvidenceKind.Unsupported, evidence.Kind);
        Assert.Equal(0, evidence.Counts.MissingCount);
        Assert.Equal(0, evidence.Counts.ResolvedCount);
        Assert.Equal(59, evidence.Counts.UnresolvedCount);
        Assert.False(evidence.IsCompleted);
        Assert.False(evidence.CanSupportAssociation);
    }

    [Fact]
    public void ARefusedQueryStillTellsApartMembersIdsAndWhatWentOverTheWire()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource
        {
            Throw = FakeWorkflowIdentitySource.RefusedQuery()
        };

        WorkflowIdentityEvidence evidence = Run(source, 59);

        // The read stopped at the first refusal, so 39 of these ids were never even asked about. One number
        // for "asked about" would hide the difference between a refusal and fifty-nine answers.
        Assert.Equal(59, evidence.Counts.MemberCount);
        Assert.Equal(59, evidence.Counts.QueryableCount);
        Assert.Equal(DataverseWorkflowIdentityProbe.MaximumIdsPerBatch, evidence.Counts.SubmittedCount);
        Assert.Equal(0, evidence.Counts.AnsweredCount);

        string report = evidence.Describe();
        Assert.Contains("Ids it could ask about : 59", report, StringComparison.Ordinal);
        Assert.Contains("Ids put in a query     : 20", report, StringComparison.Ordinal);
        Assert.Contains("Not found (answered)   : 0", report, StringComparison.Ordinal);
        Assert.Contains("Unanswered (unresolved): 59", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedQueryIsBlamedOnThisBuildsQueryAndNotOnTheConnection()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource
        {
            Throw = FakeWorkflowIdentitySource.RefusedQuery()
        };

        WorkflowIdentityEvidence evidence = Run(source, 59);

        Assert.Equal(ProviderErrorClassifier.DefectKind.QueryConstruction, evidence.Defect);
        Assert.Contains("Automation Monitor", evidence.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("did not answer", evidence.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Check the connection", evidence.Reason, StringComparison.Ordinal);
        Assert.Contains("this build sent a query the environment refused", evidence.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedQueryCannotClaimThatAnyMembershipIsProven()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource
        {
            Throw = FakeWorkflowIdentitySource.RefusedQuery()
        };

        WorkflowIdentityEvidence evidence = Run(source, 59);
        string report = evidence.Describe();

        Assert.Contains("nothing yet", ProvesLine(report), StringComparison.Ordinal);
        Assert.DoesNotContain("these workflow ids exist", report, StringComparison.OrdinalIgnoreCase);

        // And the block stays safe to copy: no id, no organization identifier, no provider payload.
        Assert.DoesNotContain(FlowId(1), report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Organization, report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARefusedSecondBatchKeepsTheAnswersTheFirstOneReallyGot()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource
        {
            ThrowFromCall = 2,
            Respond = ids => ids.Select(id => FakeWorkflowIdentitySource.Row(id)).ToList()
        };

        WorkflowIdentityEvidence evidence = Run(source, 59);

        // Twenty ids came back from a read that finished, and that is evidence. The other thirty-nine were
        // refused or never reached, and none of them is "missing".
        Assert.Equal(WorkflowIdentityEvidenceKind.Unsupported, evidence.Kind);
        Assert.Equal(20, evidence.Counts.ResolvedCount);
        Assert.Equal(0, evidence.Counts.MissingCount);
        Assert.Equal(39, evidence.Counts.UnresolvedCount);
        Assert.Equal(40, evidence.Counts.SubmittedCount);
        Assert.Equal(20, evidence.Counts.AnsweredCount);

        // A found id is still a found id, and the report is allowed to say that much and no more.
        Assert.Contains("only the ids counted as found", ProvesLine(evidence.Describe()), StringComparison.Ordinal);
    }

    [Fact]
    public void ACompletedReadStillReportsAnIdItAnsweredForAsNotFound()
    {
        FakeWorkflowIdentitySource source = new FakeWorkflowIdentitySource();
        source.Respond = ids => new List<RawWorkflowRecord> { FakeWorkflowIdentitySource.Row(ids[0]) };

        WorkflowIdentityEvidence evidence = Run(source, 3);

        // Fixing the refusal misreport must not swing the other way: a read that finished and returned one of
        // three ids really did establish that the other two are not here.
        Assert.Equal(WorkflowIdentityEvidenceKind.PartiallyResolved, evidence.Kind);
        Assert.Equal(3, evidence.Counts.AnsweredCount);
        Assert.Equal(1, evidence.Counts.ResolvedCount);
        Assert.Equal(2, evidence.Counts.MissingCount);
        Assert.Equal(0, evidence.Counts.UnresolvedCount);
        Assert.Contains(
            "were absent from a read that answered for them",
            ProvesLine(evidence.Describe()),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WorkflowIdentityEvidenceKind.Incomplete)]
    [InlineData(WorkflowIdentityEvidenceKind.Unauthorized)]
    [InlineData(WorkflowIdentityEvidenceKind.Unavailable)]
    [InlineData(WorkflowIdentityEvidenceKind.Unsupported)]
    [InlineData(WorkflowIdentityEvidenceKind.NothingToVerify)]
    [InlineData(WorkflowIdentityEvidenceKind.NoConnectedOrganization)]
    [InlineData(WorkflowIdentityEvidenceKind.NotVerified)]
    public void AnUnfinishedReadNeverDescribesItsCountsAsProof(WorkflowIdentityEvidenceKind kind)
    {
        WorkflowIdentityEvidence evidence = WorkflowIdentityEvidence.From(
            kind,
            Connected,
            new WorkflowIdentityCounts(59, 59, 0, 20, 0, 0, 0, 0, 1, 1, 0),
            new FixedClock().UtcNow,
            "canned answer: " + kind);

        Assert.False(evidence.CanSupportAssociation);
        Assert.Contains("nothing yet", evidence.Proves, StringComparison.Ordinal);
        Assert.DoesNotContain("these workflow ids exist", evidence.Describe(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheLiveRefusalIsRecognisedAsAQueryThisBuildWroteWrong()
    {
        string message = FakeWorkflowIdentitySource.RefusedQueryMessage;

        Assert.Equal(ProviderErrorClassifier.DefectKind.QueryConstruction, ProviderErrorClassifier.DefectKindOf("FaultException`1", message));
        Assert.Equal(ProviderResultState.Unsupported, ProviderErrorClassifier.Classify("FaultException`1", message));
        Assert.DoesNotContain(
            "Check the connection",
            ProviderErrorClassifier.AdviceFor(ProviderResultState.Unsupported, ProviderErrorClassifier.DefectKind.QueryConstruction),
            StringComparison.Ordinal);

        // A genuine transport failure must still read as the environment not answering, or this marker set has
        // swallowed a real outage.
        Assert.Equal(
            ProviderResultState.Unavailable,
            ProviderErrorClassifier.Classify("IOException", "A connection attempt failed because the server did not respond."));
    }
}

