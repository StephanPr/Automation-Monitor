using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using AutomationMonitor.Plugin.Storage;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// AM-44.3a: what the verified organization does to the watchlist contract, proved against real files on a
/// real disk. Discovery qualifies the identity it produces and a new list is created with that scope; an
/// existing unverified list is still untouched: import, add, save, selection and restart must never borrow the
/// connected organization for a list whose environment was never verified. The 59-member legacy file is
/// <see cref="LegacyWatchlistAssociationFileTests"/>; here the same shape (empty scope on the list and on every
/// member, unscoped profile holding the selection) is built with this build so both shapes share one document.
/// </summary>
public sealed class ConnectedOrganizationScopeIntegrationTests : IDisposable
{
    private const string Organization = "88888888-8888-8888-8888-888888888888";
    private const string OtherOrganization = "99999999-9999-9999-9999-999999999999";
    private const string Qualified = "dataverse-organization:" + Organization;
    private const string QualifiedElsewhere = "dataverse-organization:" + OtherOrganization;
    private const string SolutionId = "11111111-1111-1111-1111-111111111111";
    private const string FlowId = "44444444-4444-4444-4444-444444444444";
    private const int LegacyMemberCount = 5;

    private static readonly DateTimeOffset From = new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(1);

    private readonly string _root;

    public ConnectedOrganizationScopeIntegrationTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "AutomationMonitor-ConnectedScopeTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // A temp folder the OS holds for a moment is not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string DataPath
    {
        get { return Path.Combine(_root, SettingsLayout.DataFileName); }
    }

    /// <summary>
    /// The shipped composition: the real store over the real disk with the shipped scope-support gate, which is
    /// what refuses to let this build reassign stored scopes. No test here is handed the wider permission.
    /// </summary>
    private WatchlistManager Open()
    {
        SettingsLayout layout = new SettingsLayout(_root);
        FixedClock clock = new FixedClock { Now = From };
        SettingsFileStore storage = new SettingsFileStore(
            layout, new DiskSettingsFileAccess(layout), new DataContractJsonSettingsSerializer(), clock);
        return new WatchlistManager(new JsonWatchlistStore(storage, clock), clock, new GuidIdGenerator());
    }

    private static string LegacyFlowId(int index)
    {
        return index.ToString("x8", CultureInfo.InvariantCulture) + "-2222-3333-4444-444444444444";
    }

    /// <summary>
    /// An unverified watchlist as a legacy build left it: no scope on the list, no scope on any member, one
    /// member excluded, and the unscoped profile holding it as the selection.
    /// </summary>
    private string WriteLegacyList()
    {
        WatchlistManager manager = Open();
        WatchlistOperationResult created = manager.Create(
            "My production flows", WatchlistMembershipMode.Snapshot, null, "Built while disconnected");
        Assert.True(created.IsApplied);
        string id = created.Watchlist!.Id;

        for (int index = 1; index <= LegacyMemberCount; index++)
        {
            Assert.True(manager.AddWorkflow(id, Flow(LegacyFlowId(index), "Legacy flow " + index, null)).IsApplied);
        }

        Assert.True(manager.SetIncluded(id, UnverifiedKey(LegacyFlowId(1)), false).IsApplied);

        // The selection lives in the unscoped environment profile, which is the shape a legacy build wrote.
        Assert.True(manager.Select(id, string.Empty).IsApplied);
        Assert.True(manager.Save().IsUsable);
        return id;
    }

    private static WorkflowKey UnverifiedKey(string workflowId)
    {
        return WorkflowKey.Create(ProviderKind.PowerAutomate, string.Empty, workflowId);
    }

    /// <summary>A discovered flow as the Dataverse normalizer produces it, under the scope the gateway reports.</summary>
    private static WorkflowReference Flow(string workflowId, string name, string? scopeId)
    {
        RawWorkflowRecord record = FakeRowSource.Row(workflowId, DataverseSchema.WorkflowCategoryCloudFlow, name);
        WorkflowReference? reference;
        Assert.True(DataverseWorkflowNormalizer.TryToReference(record, scopeId, out reference));
        return reference!;
    }

    private static SolutionReference Solution()
    {
        return FakeRowSource.Solution(SolutionId, "Contoso process automation");
    }

    [Fact]
    public void ADiscoveredFlowIsQualifiedByTheVerifiedOrganization()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.EnvironmentId = Qualified;
        rows.WorkflowPages.Add(FakeRowSource.Of(false, null, FakeRowSource.Row(FlowId, DataverseSchema.WorkflowCategoryCloudFlow, "Approval flow")));

        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());
        ProviderResult<IReadOnlyList<WorkflowReference>> flows = source.GetCloudFlows(SolutionId, CancellationToken.None);

        Assert.Equal(ProviderResultState.Complete, flows.State);
        WorkflowReference flow = Assert.Single(flows.Value!);

        // The organization qualifies the identity; the provider's own id travels through untouched, because the
        // very same value is what the run-history filter is measured against.
        Assert.Equal(Qualified, flow.Key.ScopeId);
        Assert.Equal(ResourceScopeKind.DataverseOrganization, flow.Key.Scope.Kind);
        Assert.Equal(FlowId, flow.Key.WorkflowId);
        Assert.Equal(SolutionId, rows.ObservedSolutionId);
    }

    [Fact]
    public void ADiscoveryWithNoVerifiedOrganizationRecordsNoScopeAnywhere()
    {
        FakeRowSource rows = new FakeRowSource();
        rows.EnvironmentId = null;
        rows.WorkflowPages.Add(FakeRowSource.Of(false, null, FakeRowSource.Row(FlowId, DataverseSchema.WorkflowCategoryCloudFlow, "Approval flow")));

        DataverseSolutionFlowSource source = new DataverseSolutionFlowSource(rows, new FixedClock());
        ProviderResult<IReadOnlyList<WorkflowReference>> flows = source.GetCloudFlows(SolutionId, CancellationToken.None);

        WorkflowReference flow = Assert.Single(flows.Value!);
        Assert.True(flow.Key.Scope.IsUnverified);
        Assert.Equal(string.Empty, flow.Key.ScopeId);
        Assert.Equal(ProviderKind.PowerAutomate, flow.Key.Provider);
    }

    [Fact]
    public void ANewListUnderAQualifiedDiscoveryCarriesThatScopeIntoTheFileAndBackOut()
    {
        WatchlistManager manager = Open();
        WatchlistOperationResult created = manager.Create(
            "Verified list", WatchlistMembershipMode.Snapshot, Qualified);
        Assert.True(created.IsApplied);

        WatchlistOperationResult imported = manager.ImportSolutionFlows(
            created.Watchlist!.Id,
            Solution(),
            new WorkflowReference[] { Flow(FlowId, "Approval flow", Qualified) },
            ProviderResultState.Complete,
            "Read 1 flow from the solution.");

        Assert.True(imported.IsApplied);
        Assert.True(manager.Save().IsUsable);

        WatchlistManager reopened = Open();
        Assert.True(reopened.Load().IsUsable);
        Watchlist list = reopened.Find(created.Watchlist.Id)!;

        Assert.Equal(Qualified, list.EnvironmentId);
        Assert.All(list.Members, member => Assert.Equal(Qualified, member.Key.ScopeId));

        // Bytes, not just objects: the qualified token is what reached the file. The document is still the shape a
        // version-1 build reads and re-saves - that compatibility is what LegacyWatchlistAssociationFileTests and
        // the previous-build worktree run prove, not anything asserted here.
        string text = File.ReadAllText(DataPath);
        Assert.Contains(Qualified, text, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingANewQualifiedListLeavesTheUnverifiedListExactlyAsItWas()
    {
        string legacyId = WriteLegacyList();
        WatchlistManager before = Open();
        before.Load();
        string legacyScope = before.Find(legacyId)!.EnvironmentId;

        WatchlistManager manager = Open();
        manager.Load();
        WatchlistOperationResult created = manager.Create(
            "Verified list", WatchlistMembershipMode.Snapshot, Qualified);
        manager.ImportSolutionFlows(
            created.Watchlist!.Id,
            Solution(),
            new WorkflowReference[] { Flow(FlowId, "Approval flow", Qualified) },
            ProviderResultState.Complete,
            "Read 1 flow from the solution.");
        Assert.True(manager.Save().IsUsable);

        WatchlistManager reopened = Open();
        Assert.True(reopened.Load().IsUsable);
        Watchlist legacy = reopened.Find(legacyId)!;

        // Neither the list nor any of its members picked up the organization that happened to be connected.
        Assert.Equal(legacyScope, legacy.EnvironmentId);
        Assert.Equal(string.Empty, legacy.EnvironmentId);
        Assert.Equal(LegacyMemberCount, legacy.Members.Count);
        Assert.All(legacy.Members, member => Assert.True(member.Key.Scope.IsUnverified));
        Assert.Equal(LegacyMemberCount - 1, legacy.Members.Count(member => member.IsIncluded));

        // Only the new list names an organization in the bytes; the legacy list's content is still there in full,
        // and the fresh read above shows every one of its scopes still empty.
        string text = File.ReadAllText(DataPath);
        Assert.Contains(Qualified, text, StringComparison.Ordinal);
        Assert.Contains("My production flows", text, StringComparison.Ordinal);
        Assert.Contains(LegacyFlowId(LegacyMemberCount), text, StringComparison.Ordinal);

        // Two lists, two identities and two selections: the qualified environment selects the new list while the
        // unscoped profile still selects the legacy one. Nothing moved the operator's selection.
        Assert.Equal(2, reopened.Catalog.Watchlists.Count);
        Assert.Equal(created.Watchlist.Id, reopened.Catalog.Settings.ActiveWatchlistIdFor(Qualified));
        Assert.Equal(legacyId, reopened.Catalog.Settings.ActiveWatchlistIdFor(string.Empty));
    }

    [Fact]
    public void AnImportIntoAnUnverifiedListNeverBorrowsTheConnectedOrganization()
    {
        string legacyId = WriteLegacyList();
        string bytes = File.ReadAllText(DataPath);

        WatchlistManager manager = Open();
        manager.Load();
        WatchlistOperationResult result = manager.ImportSolutionFlows(
            legacyId,
            Solution(),
            new WorkflowReference[] { Flow(FlowId, "Approval flow", Qualified) },
            ProviderResultState.Complete,
            "Read 1 flow from the solution.");

        Assert.Equal(WatchlistOperationOutcome.AssociationRequired, result.Outcome);
        Assert.False(manager.HasUnsavedChanges);
        Assert.Equal(bytes, File.ReadAllText(DataPath));
        Assert.All(manager.Find(legacyId)!.Members, member => Assert.True(member.Key.Scope.IsUnverified));
    }

    [Fact]
    public void AddingAQualifiedFlowToAnUnverifiedListIsRefusedAsAnAssociation()
    {
        string legacyId = WriteLegacyList();
        string bytes = File.ReadAllText(DataPath);

        WatchlistManager manager = Open();
        manager.Load();
        WatchlistOperationResult refused = manager.AddWorkflow(legacyId, Flow(FlowId, "Approval flow", Qualified));

        Assert.Equal(WatchlistOperationOutcome.AssociationRequired, refused.Outcome);
        Assert.Equal(bytes, File.ReadAllText(DataPath));

        // The list's own scope decides, never the connection that happens to be open: the same flow joins a list
        // that does claim the organization.
        WatchlistOperationResult created = manager.Create(
            "Verified list", WatchlistMembershipMode.Snapshot, Qualified);
        Assert.True(manager.AddWorkflow(created.Watchlist!.Id, Flow(FlowId, "Approval flow", Qualified)).IsApplied);
        Assert.Equal(Qualified, manager.Find(created.Watchlist.Id)!.Members.Single().Key.ScopeId);
    }

    [Fact]
    public void ACatalogueForAQualifiedEnvironmentStillOffersTheUnverifiedList()
    {
        string legacyId = WriteLegacyList();
        WatchlistManager manager = Open();
        manager.Load();
        manager.Create("Verified list", WatchlistMembershipMode.Snapshot, Qualified);

        // Visibility is deliberate: a list built while disconnected must not vanish the moment an environment is
        // connected. Being offered is not being rewritten - its stored scope is still empty.
        Assert.Contains(manager.Lists(Qualified), list => list.Id == legacyId);
        Assert.Contains(manager.Lists(QualifiedElsewhere), list => list.Id == legacyId);
        Assert.Contains(manager.Lists(null), list => list.Id == legacyId);
        Assert.Equal(string.Empty, manager.Find(legacyId)!.EnvironmentId);
    }

    [Fact]
    public void RunHistoryIsStillFilteredOnTheProvidersOwnWorkflowId()
    {
        FakeRunRowSource rows = new FakeRunRowSource();
        rows.EnvironmentId = Qualified;
        rows.AddPage(FakeRunRowSource.RunRow("run-1", "Succeeded", From.AddHours(1), From.AddHours(2), 3600000));

        DataverseFlowRunSource source = new DataverseFlowRunSource(rows, new FixedClock());

        // The run row names the flow it belongs to, and a row naming another flow is refused - so the flow this
        // read asks for is the fake's own provider id, qualified by the organization the gateway reported.
        string providerWorkflowId = FakeRunRowSource.Workflow;
        WorkflowKey qualified = WorkflowKey.Create(ProviderKind.PowerAutomate, Qualified, providerWorkflowId);
        MonitoringQuery query = new MonitoringQuery(
            new[] { qualified }, From, To, null, MonitoringQuery.DefaultMaxRows);

        ProviderResult<RunReadResult> read = source.GetRunRead(qualified, "Approval flow", query, CancellationToken.None);

        Assert.Equal(ProviderResultState.Complete, read.State);
        RunQueryCriteria criteria = Assert.Single(rows.ObservedCriteria);

        // The organization qualifies the plugin-side identity; the value the flowrun.workflowid text filter is
        // measured against stays the provider's own id, exactly as the 2026-11-12 probe established.
        Assert.Equal(providerWorkflowId, criteria.Workflow.WorkflowId);
        Assert.Equal(Qualified, criteria.Workflow.ScopeId);
        Assert.Equal(Qualified, source.ScopeId);
        Assert.Equal(providerWorkflowId, Assert.Single(read.Value!.Runs).Key.WorkflowId);
    }

    [Fact]
    public void AnUnverifiedRowIsNeverLabelledWithTheConnectedOrganization()
    {
        ConnectedOrganization connected = ResolveWith(Organization).WithDisplayName("Test Organization");

        // A legacy row opened while connected must not read as though it belonged to this connection.
        Assert.Equal(EnvironmentDisplay.UnverifiedText, EnvironmentDisplay.Describe(string.Empty, connected));
        Assert.Equal(EnvironmentDisplay.UnverifiedText, EnvironmentDisplay.Describe((string?)null, connected));
        Assert.DoesNotContain("Test Organization", EnvironmentDisplay.Describe(string.Empty, connected), StringComparison.Ordinal);
    }

    [Fact]
    public void AQualifiedRowNamesItsIdentityAndOnlyBorrowsTheDisplayNameOfThisConnection()
    {
        ConnectedOrganization connected = ResolveWith(Organization).WithDisplayName("Test Organization");
        string thisOrganization = ResourceScopeLabel.Describe(ResourceScope.ForDataverseOrganization(Guid.Parse(Organization)));
        string otherOrganization = ResourceScopeLabel.Describe(ResourceScope.ForDataverseOrganization(Guid.Parse(OtherOrganization)));

        // The identity is always present; the friendly name is only ever shown beside the connection it came from.
        string here = EnvironmentDisplay.Describe(Qualified, connected);
        Assert.Contains("Test Organization", here, StringComparison.Ordinal);
        Assert.Contains(thisOrganization, here, StringComparison.Ordinal);

        string elsewhere = EnvironmentDisplay.Describe(QualifiedElsewhere, connected);
        Assert.Contains(otherOrganization, elsewhere, StringComparison.Ordinal);
        Assert.DoesNotContain("Test Organization", elsewhere, StringComparison.Ordinal);
        Assert.Contains("not this connection", elsewhere, StringComparison.Ordinal);

        // A failed verification read shows identity only: a friendly name it could not confirm is not shown, and
        // the connected line says the environment was not verified rather than naming something unproven.
        FakeOrganizationIdentity failing = new FakeOrganizationIdentity();
        failing.Fail();
        ConnectedOrganization unresolved = new ConnectedOrganizationResolver(failing, new FixedClock())
            .Resolve(CancellationToken.None);

        Assert.Equal(EnvironmentDisplay.UnverifiedText, EnvironmentDisplay.DescribeConnected(unresolved));
        Assert.DoesNotContain(
            "Test Organization",
            EnvironmentDisplay.Describe(Qualified, unresolved.WithDisplayName("Test Organization")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ATicketIsCurrentOnlyForTheConnectionItWasCapturedAgainst()
    {
        ConnectedOrganization organization = ResolveWith(Organization);
        ConnectedOrganization elsewhere = ResolveWith(OtherOrganization);
        ConnectionContext connections = new ConnectionContext();

        ConnectionTicket first = connections.Capture("connection-one", organization);
        Assert.True(connections.IsCurrent(first));
        Assert.Equal(Qualified, first.ScopeText);

        // Two actions on the same connection are both current: one refresh does not stale another.
        Assert.True(connections.IsCurrent(connections.Capture("connection-one", organization)));

        // A switch makes the earlier ticket stale, and switching back does not revive it.
        ConnectionTicket second = connections.Capture("connection-two", elsewhere);
        Assert.True(connections.IsCurrent(second));
        Assert.Equal(QualifiedElsewhere, second.ScopeText);
        Assert.False(connections.IsCurrent(first));
        ConnectionTicket returned = connections.Capture("connection-one", organization);
        Assert.True(connections.IsCurrent(returned));
        Assert.False(connections.IsCurrent(second));

        // A disconnect makes everything stale, including the ticket of the connection that just closed.
        ConnectionTicket beforeDisconnect = connections.Capture("connection-one", organization);
        connections.Invalidate();
        Assert.False(connections.IsCurrent(beforeDisconnect));
        Assert.False(connections.IsConnected);
        Assert.False(connections.IsCurrent(null));

        // An action launched with no service can never be current: it could not have reached anything.
        Assert.False(connections.IsCurrent(connections.Capture(null, ConnectedOrganization.NotAttempted)));
        Assert.Contains("discarded", ConnectionContext.DescribeDiscard("flow read"), StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredListIsMatchedByTheOrganizationItNamesRatherThanByTheSpellingInTheFile()
    {
        WatchlistManager manager = Open();
        WatchlistOperationResult created = manager.Create(
            "My production flows", WatchlistMembershipMode.Snapshot, Qualified, "Scoped by the connected organization");
        Assert.True(created.IsApplied);
        Watchlist list = created.Watchlist!;

        // A file may reach this build with the same organization written another way - hand-edited, or written by a
        // tool that did not canonicalise. It names one organization, so it must be listed for it: matching is on what
        // a scope names, never on how the bytes spell it.
        Watchlist spelled = list.WithEnvironment(
            "dataverse-organization:{" + Organization.ToUpperInvariant() + "}");
        WatchlistCatalog catalog = new WatchlistCatalog(new[] { spelled }, spelled.Id, ApplicationSettings.Defaults);

        Assert.Contains(catalog.ForEnvironment(Qualified), candidate => candidate.Id == spelled.Id);
        Watchlist? active = catalog.ActiveWatchlistFor(Qualified);
        Assert.NotNull(active);
        Assert.Equal(spelled.Id, active!.Id);

        // The other organization is still not this list's environment, however the file spells it.
        Assert.DoesNotContain(catalog.ForEnvironment(QualifiedElsewhere), candidate => candidate.Id == spelled.Id);
    }

    [Fact]
    public void AScopeThisBuildCannotInterpretIsNeverEvidenceThatAListBelongsHere()
    {
        WatchlistManager manager = Open();
        WatchlistOperationResult created = manager.Create(
            "My production flows", WatchlistMembershipMode.Snapshot, Qualified, "Scoped by the connected organization");
        Assert.True(created.IsApplied);
        Watchlist list = created.Watchlist!;

        // A kind this build does not implement, and a damaged identifier, are preserved rather than interpreted - so
        // neither may be read as a match for the connected organization. The list is not lost: while nothing is
        // verified it stays visible, exactly as an unscoped list does.
        string[] unreadable =
        {
            "powerplatform-environment:" + Organization,
            "dataverse-organization:not-a-guid",
            "just-a-name"
        };

        foreach (string stored in unreadable)
        {
            Watchlist other = list.WithEnvironment(stored);
            WatchlistCatalog catalog = new WatchlistCatalog(new[] { other }, other.Id, ApplicationSettings.Defaults);

            Assert.DoesNotContain(catalog.ForEnvironment(Qualified), candidate => candidate.Id == other.Id);
            Assert.Contains(catalog.ForEnvironment(string.Empty), candidate => candidate.Id == other.Id);
        }
    }

    private static ConnectedOrganization ResolveWith(string organizationIdText)
    {
        FakeOrganizationIdentity identity = new FakeOrganizationIdentity();
        identity.Succeed(Guid.Parse(organizationIdText));
        return new ConnectedOrganizationResolver(identity, new FixedClock()).Resolve(CancellationToken.None);
    }
}
