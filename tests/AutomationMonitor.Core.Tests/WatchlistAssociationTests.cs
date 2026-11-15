using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Attaching a verified environment identity to a saved watchlist: what may be previewed, what must be refused,
/// what a confirmation binds to, and - the rule that ordered this checkpoint - the fact that this build must not
/// re-key a stored list at all until it can resolve that scope when it loads watchlists. The refusals are the
/// substance here; an association that applied on weak evidence would silently take the operator's list away
/// rather than improve it.
/// </summary>
public class WatchlistAssociationTests
{
    private const string Organization = "77777777-7777-7777-7777-777777777777";
    private const string OtherOrganization = "88888888-8888-8888-8888-888888888888";
    private const string Qualified = "dataverse-organization:" + Organization;
    private const string OtherQualified = "dataverse-organization:" + OtherOrganization;
    private const string LegacyId = "legacy-list";
    private const string FileName = SettingsLayout.DataFileName;

    private static readonly ResourceScope Connected = ResourceScope.ForDataverseOrganization(Guid.Parse(Organization));
    private static readonly ResourceScope OtherConnected = ResourceScope.ForDataverseOrganization(Guid.Parse(OtherOrganization));
    private static readonly DateTimeOffset Stamp = new DateTimeOffset(2026, 11, 15, 9, 45, 39, TimeSpan.Zero);

    private readonly FakeSettingsFiles _files = new FakeSettingsFiles();
    private readonly FixedClock _clock = new FixedClock { Now = Stamp };
    private readonly SettingsLayout _layout = new SettingsLayout("/settings/AutomationMonitor");
    private readonly JsonWatchlistStore _store;

    public WatchlistAssociationTests()
    {
        _files.HasDirectory = true;
        _store = new JsonWatchlistStore(
            new SettingsFileStore(_layout, _files, new DataContractJsonSettingsSerializer(), _clock), _clock);
    }

    private static string FlowId(int index)
    {
        return index.ToString("x8", CultureInfo.InvariantCulture) + "-1111-2222-3333-444444444444";
    }

    /// <summary>
    /// A saved list in the shape the legacy file has: no environment on the list and none on any member.
    /// </summary>
    private static Watchlist LegacyList(int memberCount, int excludedCount = 0, string? memberScope = null, string environmentId = "")
    {
        WatchlistBuilder builder = new WatchlistBuilder(LegacyId, "My production flows", WatchlistMembershipMode.Snapshot, null, environmentId);
        for (int i = 1; i <= memberCount; i++)
        {
            WorkflowReference flow = FlowRef.Create(FlowId(i), "Flow " + i, ProviderKind.PowerAutomate, memberScope ?? string.Empty);
            builder.AddManualFlow(flow, Stamp);
            if (i <= excludedCount)
            {
                builder.SetIncluded(flow.Key, false);
            }
        }

        return builder.Build();
    }

    /// <summary>Put one list in the store with the legacy unscoped environment selected, then open the manager on it.</summary>
    private WatchlistManager Seed(Watchlist list, ScopeResolutionSupport? support = null)
    {
        ApplicationSettings settings = ApplicationSettings.Defaults
            .WithActiveEnvironment(string.Empty, Stamp)
            .WithActiveWatchlist(string.Empty, list.Id, Stamp);
        _store.Write(new WatchlistCatalog(new List<Watchlist> { list }, list.Id, settings));

        WatchlistManager manager = new WatchlistManager(_store, _clock, new FixedIdGenerator(), support);
        manager.Load();
        return manager;
    }

    private static WorkflowIdentityEvidence Evidence(
        WorkflowIdentityEvidenceKind kind,
        ResourceScope? organization = null,
        int members = 2)
    {
        int resolved;
        if (kind == WorkflowIdentityEvidenceKind.Verified)
        {
            resolved = members;
        }
        else if (kind == WorkflowIdentityEvidenceKind.PartiallyResolved)
        {
            resolved = members - 1;
        }
        else
        {
            resolved = 0;
        }

        // A canned refusal must not carry a tally of ids it never got an answer for: "not found" belongs to a
        // read that finished, and these tests exercise what the planner does with each kind of answer.
        bool finished = kind == WorkflowIdentityEvidenceKind.Verified
            || kind == WorkflowIdentityEvidenceKind.PartiallyResolved
            || kind == WorkflowIdentityEvidenceKind.Unresolved
            || kind == WorkflowIdentityEvidenceKind.NothingToVerify;
        WorkflowIdentityCounts counts = new WorkflowIdentityCounts(
            members,
            members,
            0,
            finished ? members : 0,
            finished ? members : 0,
            resolved,
            0,
            0,
            finished ? 1 : 0,
            finished ? 1 : 0,
            finished ? members : 0);
        return WorkflowIdentityEvidence.From(
            kind,
            organization ?? Connected,
            counts,
            Stamp,
            kind == WorkflowIdentityEvidenceKind.Verified ? null : "canned answer: " + kind);
    }

    [Fact]
    public void ThisBuildShipsWithReassigningAStoredScopeSwitchedOff()
    {
        WatchlistManager plain = Seed(LegacyList(2));

        // Pinned deliberately: the shipped answer must be "not yet", and it must name the checkpoint that
        // changes it. AM-44.3 flips this test in the same commit that makes the connected scope resolvable.
        Assert.False(ScopeResolutionSupport.Shipped.CanReassignStoredScopes);
        Assert.False(plain.ScopeResolution.CanReassignStoredScopes);
        Assert.Contains(ScopeResolutionSupport.RequiredCheckpoint, plain.ScopeResolution.DeferredReason, StringComparison.Ordinal);
        Assert.Contains("AM-44.3", ScopeResolutionSupport.Shipped.DeferredReason, StringComparison.Ordinal);
    }

    [Fact]
    public void APreviewStatesWhatChangesWhatCannotAndCarriesNoIdentifier()
    {
        WatchlistManager manager = Seed(LegacyList(3, excludedCount: 1));

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 3));

        Assert.Equal(WatchlistAssociationDecision.Deferred, preview.Decision);
        Assert.False(preview.CanApply);
        Assert.Equal(3, preview.MemberCount);
        Assert.Equal(2, preview.IncludedCount);
        Assert.Equal(3, preview.RekeyedCount);
        Assert.Equal(0, preview.RecordedScopeCount);
        Assert.True(preview.SelectionMoves);
        Assert.Equal(Qualified, preview.TargetScope.Text);

        string text = preview.Describe();
        Assert.Contains("What would change", text, StringComparison.Ordinal);
        Assert.Contains("What cannot change", text, StringComparison.Ordinal);
        Assert.Contains("watchlists.json.bak", text, StringComparison.Ordinal);
        Assert.Contains("Dataverse organization 77777777", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Organization, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FlowId(1), text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("display name is presentation, never identity", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedBuildDefersTheAssociationAndWritesNothingAtAll()
    {
        WatchlistManager manager = Seed(LegacyList(3));
        string before = _files.ContentOf(FileName)!;

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 3));
        WatchlistOperationResult result = manager.AssociateScope(LegacyId, preview);

        Assert.Equal(WatchlistOperationOutcome.AssociationDeferred, result.Outcome);
        Assert.Contains(ScopeResolutionSupport.RequiredCheckpoint, result.Describe(), StringComparison.Ordinal);
        Assert.False(manager.HasUnsavedChanges);
        Assert.Equal(string.Empty, manager.Catalog.Find(LegacyId)!.EnvironmentId);
        Assert.All(manager.Catalog.Find(LegacyId)!.Members, member => Assert.Equal(string.Empty, member.Key.ScopeId));
        Assert.Equal(before, _files.ContentOf(FileName));
        Assert.False(_files.FileExists(FileName + ".bak"));
    }

    [Fact]
    public void AnAssociationWithoutAnyIdentityReadIsRefusedAndSaysWhy()
    {
        WatchlistManager manager = Seed(LegacyList(2));

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, null);
        WatchlistOperationResult result = manager.AssociateScope(LegacyId, preview);

        Assert.Equal(WatchlistAssociationDecision.Blocked, preview.Decision);
        Assert.Contains(WatchlistAssociationBlocker.EvidenceNotVerified, preview.Blockers);
        Assert.Equal(WatchlistOperationOutcome.AssociationRefused, result.Outcome);
        Assert.Contains("no identity read has been made", result.Describe(), StringComparison.Ordinal);
        Assert.False(manager.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(WorkflowIdentityEvidenceKind.PartiallyResolved)]
    [InlineData(WorkflowIdentityEvidenceKind.Unresolved)]
    [InlineData(WorkflowIdentityEvidenceKind.NothingToVerify)]
    [InlineData(WorkflowIdentityEvidenceKind.Incomplete)]
    [InlineData(WorkflowIdentityEvidenceKind.Unauthorized)]
    [InlineData(WorkflowIdentityEvidenceKind.Unavailable)]
    [InlineData(WorkflowIdentityEvidenceKind.Unsupported)]
    [InlineData(WorkflowIdentityEvidenceKind.NotVerified)]
    public void WeakerEvidenceIsRefusedRatherThanHalfAccepted(WorkflowIdentityEvidenceKind kind)
    {
        WatchlistManager manager = Seed(LegacyList(3));
        WorkflowIdentityEvidence evidence = kind == WorkflowIdentityEvidenceKind.NothingToVerify
            ? Evidence(kind, members: 0)
            : Evidence(kind, members: 3);

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, evidence);
        WatchlistOperationResult result = manager.AssociateScope(LegacyId, preview);

        Assert.False(evidence.CanSupportAssociation);
        Assert.Equal(WatchlistAssociationDecision.Blocked, preview.Decision);
        Assert.Contains(WatchlistAssociationBlocker.EvidenceNotVerified, preview.Blockers);
        Assert.Equal(WatchlistOperationOutcome.AssociationRefused, result.Outcome);
        Assert.Equal(string.Empty, manager.Catalog.Find(LegacyId)!.EnvironmentId);
        Assert.False(manager.HasUnsavedChanges);
    }

    [Fact]
    public void AConnectionThatCannotNameAnOrganizationNamesNoTarget()
    {
        WatchlistManager manager = Seed(LegacyList(2));

        WatchlistAssociationPreview preview = manager.PreviewAssociation(
            LegacyId,
            Evidence(WorkflowIdentityEvidenceKind.NoConnectedOrganization, ResourceScope.Unverified, members: 2));

        Assert.Equal(WatchlistAssociationDecision.Blocked, preview.Decision);
        Assert.Contains(WatchlistAssociationBlocker.NoConnectedOrganization, preview.Blockers);
        Assert.False(preview.TargetScope.IsQualified);
        Assert.Equal(string.Empty, manager.Catalog.Find(LegacyId)!.EnvironmentId);
    }

    [Fact]
    public void AListAlreadyAssociatedWithAnotherOrganizationIsNeverMoved()
    {
        WatchlistManager manager = Seed(LegacyList(2, memberScope: OtherQualified, environmentId: OtherQualified));

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 2));

        Assert.Equal(WatchlistAssociationDecision.Blocked, preview.Decision);
        Assert.Contains(WatchlistAssociationBlocker.AssociatedWithAnotherOrganization, preview.Blockers);
        Assert.Contains("Dataverse organization 88888888", string.Join(" ", preview.Reasons), StringComparison.Ordinal);
        Assert.Equal(OtherQualified, manager.Catalog.Find(LegacyId)!.EnvironmentId);
    }

    [Fact]
    public void AMemberWithARecordedScopeRefusesTheWholeAssociation()
    {
        WatchlistBuilder builder = new WatchlistBuilder(LegacyId, "Mixed list", WatchlistMembershipMode.Snapshot, null, string.Empty);
        builder.AddManualFlow(FlowRef.Create(FlowId(1), "Flow 1", ProviderKind.PowerAutomate, string.Empty), Stamp);
        builder.AddManualFlow(FlowRef.Create(FlowId(2), "Flow 2", ProviderKind.PowerAutomate, OtherQualified), Stamp);
        WatchlistManager manager = Seed(builder.Build());

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 2));

        // Half a list re-keyed and half not is a watchlist nobody can reason about, so nothing moves.
        Assert.Equal(WatchlistAssociationDecision.Blocked, preview.Decision);
        Assert.Contains(WatchlistAssociationBlocker.MemberScopesAlreadyRecorded, preview.Blockers);
        Assert.Equal(1, preview.RekeyedCount);
        Assert.Equal(1, preview.RecordedScopeCount);
        Assert.Equal(string.Empty, manager.Catalog.Find(LegacyId)!.EnvironmentId);
    }

    [Fact]
    public void AnEmptyListHasNothingToAssociateBecauseNothingCouldConfirmIt()
    {
        WatchlistManager manager = Seed(LegacyList(0));

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 0));

        Assert.Equal(WatchlistAssociationDecision.Blocked, preview.Decision);
        Assert.Contains(WatchlistAssociationBlocker.NothingToAssociate, preview.Blockers);
        Assert.Equal(WatchlistOperationOutcome.AssociationRefused, manager.AssociateScope(LegacyId, preview).Outcome);
    }

    [Fact]
    public void AConfirmedAssociationRekeysEveryMemberAndMovesTheSelectionInOneWrite()
    {
        WatchlistManager manager = Seed(LegacyList(3, excludedCount: 1), ScopeResolutionSupport.Resolvable());

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 3));
        Assert.True(preview.CanApply);
        WatchlistOperationResult result = manager.AssociateScope(LegacyId, preview);

        Assert.Equal(WatchlistOperationOutcome.Applied, result.Outcome);
        Watchlist associated = manager.Catalog.Find(LegacyId)!;
        Assert.Equal(Qualified, associated.EnvironmentId);
        Assert.All(associated.Members, member => Assert.Equal(Qualified, member.Key.ScopeId));
        Assert.Equal(3, associated.Members.Count);
        Assert.Equal(2, associated.MonitoredFlowCount);

        // The exclusion moved with its member rather than being re-derived from a key that no longer exists.
        WorkflowKey excludedKey = WorkflowKey.Create(ProviderKind.PowerAutomate, Qualified, FlowId(1));
        WatchlistMember? excluded = associated.FindMember(excludedKey);
        Assert.NotNull(excluded);
        Assert.False(excluded!.IsIncluded);
        Assert.Equal("Flow 1", excluded.Workflow.Name);

        // The environment's saved selection names the new identity, and the catalog-level selection did not move.
        Assert.Equal(LegacyId, manager.Catalog.Settings.ActiveWatchlistIdFor(Qualified));
        Assert.Equal(LegacyId, manager.Catalog.ActiveWatchlistId);
        Assert.Equal(LegacyId, manager.Catalog.ActiveWatchlistFor(Qualified)?.Id);
        Assert.Contains(associated, manager.Catalog.ForEnvironment(Qualified));
        Assert.True(manager.HasUnsavedChanges);

        Assert.True(manager.Save().IsUsable);

        // The save first moved the previous file to watchlists.json.bak, and that copy is the legacy list: no
        // qualified scope anywhere in it. That is what makes this change undoable by the store's own recovery.
        string backup = _files.ContentOf(FileName + ".bak")!;
        Assert.False(string.IsNullOrEmpty(backup));
        Assert.DoesNotContain(Qualified, backup, StringComparison.Ordinal);
        Assert.Contains(FlowId(1), backup, StringComparison.Ordinal);
        Assert.Contains(Qualified, _files.ContentOf(FileName)!, StringComparison.Ordinal);
    }

    [Fact]
    public void ReapplyingAnAssociationThatIsAlreadyInPlaceChangesNothing()
    {
        WatchlistManager manager = Seed(
            LegacyList(2, memberScope: OtherQualified, environmentId: OtherQualified),
            ScopeResolutionSupport.Resolvable());
        string before = _files.ContentOf(FileName)!;

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, OtherConnected, 2));

        // The read found the same organization the list already names: idempotent, and it dirties nothing.
        Assert.Equal(WatchlistAssociationDecision.NoChange, preview.Decision);
        Assert.Equal(WatchlistOperationOutcome.NoChange, manager.AssociateScope(LegacyId, preview).Outcome);
        Assert.False(manager.HasUnsavedChanges);
        Assert.Equal(before, _files.ContentOf(FileName));

        WatchlistAssociationPreview again = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, OtherConnected, 2));
        Assert.Equal(WatchlistAssociationDecision.NoChange, again.Decision);
    }

    [Fact]
    public void AConfirmationThatNoLongerDescribesTheListIsRefused()
    {
        WatchlistManager manager = Seed(LegacyList(2), ScopeResolutionSupport.Resolvable());
        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 2));

        // The operator edited the list between reading and confirming; the preview they hold is stale.
        manager.AddWorkflow(LegacyId, FlowRef.Create(FlowId(9), "Late addition", ProviderKind.PowerAutomate, string.Empty));

        WatchlistOperationResult result = manager.AssociateScope(LegacyId, preview);

        Assert.Equal(WatchlistOperationOutcome.Conflict, result.Outcome);
        Assert.Contains("read the identity again", result.Describe(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, manager.Catalog.Find(LegacyId)!.EnvironmentId);
    }

    [Fact]
    public void AListWithNoScopeOfItsOwnButMembersAlreadyInTargetOrganizationOnlyGainsItsOwnScope()
    {
        // A file a person edited by hand can hold members that name the organization while the list itself does
        // not. The change is then exactly that: the list gains the scope, and no member is rewritten.
        WatchlistManager manager = Seed(LegacyList(2, memberScope: Qualified), ScopeResolutionSupport.Resolvable());

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 2));
        Assert.Equal(WatchlistAssociationDecision.ReadyToConfirm, preview.Decision);
        Assert.Equal(0, preview.RekeyedCount);
        Assert.Equal(2, preview.RecordedScopeCount);

        Assert.Equal(WatchlistOperationOutcome.Applied, manager.AssociateScope(LegacyId, preview).Outcome);
        Watchlist after = manager.Catalog.Find(LegacyId)!;
        Assert.Equal(Qualified, after.EnvironmentId);
        Assert.All(after.Members, member => Assert.Equal(Qualified, member.Key.ScopeId));
    }

    [Fact]
    public void TheUnverifiedEnvironmentsOverridesCarryAcrossAndTheOldEntrySurvives()
    {
        Watchlist list = LegacyList(2);
        ApplicationSettings settings = ApplicationSettings.Defaults
            .WithActiveEnvironment(string.Empty, Stamp)
            .WithActiveWatchlist(string.Empty, list.Id, Stamp);
        settings = settings.WithProfile(settings.ProfileFor(string.Empty)!.WithOverrides(null, MonitoringDateRange.LastSevenDays));
        _store.Write(new WatchlistCatalog(new List<Watchlist> { list }, list.Id, settings));

        WatchlistManager manager = new WatchlistManager(_store, _clock, new FixedIdGenerator(), ScopeResolutionSupport.Resolvable());
        manager.Load();
        WatchlistOperationResult result = manager.AssociateScope(
            LegacyId, manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 2)));

        Assert.Equal(WatchlistOperationOutcome.Applied, result.Outcome);
        EnvironmentProfile? moved = manager.Catalog.Settings.ProfileFor(Qualified);
        Assert.NotNull(moved);
        Assert.Equal(LegacyId, moved!.ActiveWatchlistId);
        Assert.Equal(MonitoringDateRange.LastSevenDays, moved.DateRange);

        // The unscoped entry is copied, not moved: a second legacy list may be the only other thing that
        // remembers it, and deleting stored state nobody asked to delete is not this action's business.
        Assert.NotNull(manager.Catalog.Settings.ProfileFor(string.Empty));
    }

    [Fact]
    public void ACatalogThisBuildCannotWriteBackIsNeverAssociated()
    {
        _files.PlantFile(FileName, UnknownProviderDocument());
        WatchlistManager manager = new WatchlistManager(_store, _clock, new FixedIdGenerator(), ScopeResolutionSupport.Resolvable());
        manager.Load();
        Assert.True(manager.Catalog.HasUnrepresentedMembers);

        WatchlistAssociationPreview preview = manager.PreviewAssociation(LegacyId, Evidence(WorkflowIdentityEvidenceKind.Verified, members: 1));

        Assert.Equal(WatchlistAssociationDecision.Blocked, preview.Decision);
        Assert.Contains(WatchlistAssociationBlocker.UnrepresentableMembers, preview.Blockers);
        Assert.Equal(WatchlistOperationOutcome.AssociationRefused, manager.AssociateScope(LegacyId, preview).Outcome);
        Assert.Contains("cannot read back", string.Join(" ", preview.Reasons), StringComparison.Ordinal);
    }

    [Fact]
    public void AFlowFromAVerifiedOrganizationCannotJoinAnUnverifiedList()
    {
        WatchlistManager manager = Seed(LegacyList(1));

        WatchlistOperationResult result = manager.AddWorkflow(
            LegacyId, FlowRef.Create(FlowId(50), "Scoped flow", ProviderKind.PowerAutomate, Qualified));

        // This is the silent-association path the checkpoint exists to close: an add is not a confirmation.
        Assert.Equal(WatchlistOperationOutcome.AssociationRequired, result.Outcome);
        Assert.Equal(string.Empty, manager.Catalog.Find(LegacyId)!.EnvironmentId);
        Assert.DoesNotContain(manager.Catalog.Find(LegacyId)!.Members, member => member.Key.Scope.IsQualified);
    }

    [Fact]
    public void ImportingScopedFlowsIntoAnUnverifiedListIsRefused()
    {
        WatchlistManager manager = Seed(LegacyList(1));
        SolutionReference solution = new SolutionReference("solution-1", "publisher.solution", "Test solution", "1.0.0.0", false);

        WatchlistOperationResult result = manager.ImportSolutionFlows(
            LegacyId,
            solution,
            new List<WorkflowReference> { FlowRef.Create(FlowId(51), "Scoped flow", ProviderKind.PowerAutomate, Qualified) },
            ProviderResultState.Complete);

        Assert.Equal(WatchlistOperationOutcome.AssociationRequired, result.Outcome);
        Assert.Equal(string.Empty, manager.Catalog.Find(LegacyId)!.EnvironmentId);
        Assert.Single(manager.Catalog.Find(LegacyId)!.Members);
    }

    [Fact]
    public void ALegacyUnqualifiedFlowStillAdoptsTheListScopeAsItAlwaysDid()
    {
        WatchlistManager manager = Seed(LegacyList(0));

        WatchlistOperationResult result = manager.AddWorkflow(
            LegacyId, FlowRef.Create(FlowId(60), "Legacy flow", ProviderKind.PowerAutomate, FlowRef.Environment));

        // Same-scope behaviour is preserved: only a kind-qualified scope is refused.
        Assert.Equal(WatchlistOperationOutcome.Applied, result.Outcome);
        Assert.Equal(FlowRef.Environment, manager.Catalog.Find(LegacyId)!.EnvironmentId);
    }

    /// <summary>A saved file whose one member has a provider this build cannot rebuild.</summary>
    private static string UnknownProviderDocument()
    {
        return "{\"schemaVersion\":1,\"kind\":\"watchlist-store\",\"watchlists\":{\"activeWatchlistId\":\"" + LegacyId + "\",\"items\":[{"
            + "\"id\":\"" + LegacyId + "\",\"name\":\"My production flows\",\"environmentId\":\"\","
            + "\"membershipMode\":\"snapshot\",\"coverageState\":\"complete\",\"members\":[{"
            + "\"provider\":\"logicapps-standard-v2\",\"scopeId\":\"\",\"workflowId\":\"" + FlowId(1) + "\","
            + "\"name\":\"Flow 1\",\"state\":\"enabled\",\"included\":true}]}]}}";
    }
}
