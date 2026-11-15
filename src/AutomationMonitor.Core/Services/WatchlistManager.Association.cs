using System;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// The environment-identity half of the manager: show what attaching a verified organization to a saved
    /// list would do, and apply exactly that on an explicit confirmation. It is a separate file because the
    /// rules here are the ones an operator's data is judged by - every refusal leaves the catalog and the file
    /// untouched, and every applied change is the previewed change and nothing else.
    /// </summary>
    public sealed partial class WatchlistManager
    {
        /// <summary>
        /// What associating this list with the connected organization would mean, given one identity read.
        /// Reads nothing, writes nothing, and answers even when the answer is a refusal.
        /// </summary>
        public WatchlistAssociationPreview PreviewAssociation(string? id, WorkflowIdentityEvidence? evidence)
        {
            Watchlist? current = _catalog.Find(id);
            return WatchlistAssociationPlanner.Plan(current, evidence, _scopeResolution, !_catalog.HasUnrepresentedMembers);
        }

        /// <summary>
        /// Apply the association the operator confirmed.
        ///
        /// <para>
        /// The plan is rebuilt from the live catalog and the confirmed preview's own evidence, then compared to
        /// what was shown: an edit, a reload or a second read between preview and confirmation makes the two
        /// differ, and that is a refusal rather than a best-effort guess. When it does apply, the list, its
        /// member keys and that environment's active-list selection move together in one mutation, so there is
        /// no moment in which a saved list names an environment its members do not.
        /// </para>
        /// </summary>
        public WatchlistOperationResult AssociateScope(
            string? id,
            WatchlistAssociationPreview? confirmed,
            DateTimeOffset? expectedModifiedAtUtc = null)
        {
            Watchlist? current = FindRequired(id, out WatchlistOperationResult? failure);
            if (current == null)
            {
                return failure!;
            }

            WatchlistOperationResult? stale = CheckStale(current, expectedModifiedAtUtc);
            if (stale != null)
            {
                return stale;
            }

            WorkflowIdentityEvidence? evidence = confirmed == null ? null : confirmed.Evidence;
            WatchlistAssociationPreview plan = WatchlistAssociationPlanner.Plan(
                current, evidence, _scopeResolution, !_catalog.HasUnrepresentedMembers);

            switch (plan.Decision)
            {
                case WatchlistAssociationDecision.Blocked:
                    return WatchlistOperationResult.Refused(
                        WatchlistOperationOutcome.AssociationRefused,
                        _catalog,
                        current,
                        plan.DecisionText());

                case WatchlistAssociationDecision.NoChange:
                    return WatchlistOperationResult.Unchanged(_catalog, current, plan.DecisionText());

                case WatchlistAssociationDecision.Deferred:
                    return WatchlistOperationResult.Refused(
                        WatchlistOperationOutcome.AssociationDeferred,
                        _catalog,
                        current,
                        plan.DecisionText());
            }

            if (confirmed == null || !plan.IsSamePlanAs(confirmed))
            {
                return WatchlistOperationResult.Refused(
                    WatchlistOperationOutcome.Conflict,
                    _catalog,
                    current,
                    "the preview you confirmed is not the change that applies now - the list or the evidence moved, so read the identity again and confirm that");
            }

            DateTimeOffset appliedAtUtc = _clock.UtcNow;
            string fromScope = current.EnvironmentId;
            string toScope = plan.TargetScope.Text;
            Watchlist associated = WatchlistAssociationPlanner.Apply(current, plan.TargetScope, appliedAtUtc);

            ApplicationSettings settings = CarryEnvironmentProfile(_catalog.Settings, fromScope, toScope, associated.Id, appliedAtUtc);
            settings = settings.WithActiveWatchlist(toScope, associated.Id, appliedAtUtc);
            WatchlistCatalog next = _catalog.WithWatchlist(associated).WithSettings(settings);

            string[] notes = evidence == null
                ? new string[] { "the read that licensed this is no longer in hand" }
                : new string[] { "verified by the read of " + evidence.CheckedAtUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture) };

            return Commit(next, associated, "associated with " + ResourceScopeLabel.Describe(plan.TargetScope), notes);
        }

        /// <summary>
        /// Give the new identity the environment settings the unverified scope was carrying, but only when the
        /// attribution is unambiguous: that profile's active list must be the list being associated, and the
        /// organization must not already have a profile of its own. The old entry is copied, never deleted,
        /// because a second unscoped legacy list may be the only other thing that remembers it.
        /// </summary>
        private static ApplicationSettings CarryEnvironmentProfile(
            ApplicationSettings settings,
            string fromScope,
            string toScope,
            string listId,
            DateTimeOffset usedAtUtc)
        {
            EnvironmentProfile? source = settings.ProfileFor(fromScope);
            if (source == null)
            {
                return settings;
            }

            if (!string.Equals(source.ActiveWatchlistId, listId, StringComparison.OrdinalIgnoreCase))
            {
                return settings;
            }

            if (settings.ProfileFor(toScope) != null)
            {
                return settings;
            }

            return settings.WithProfile(new EnvironmentProfile(toScope, listId, source.AutoRefresh, source.DateRange, usedAtUtc));
        }
    }
}
