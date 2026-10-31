using System;
using System.Collections.Generic;
using System.Globalization;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Persistence;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// The membership half of <see cref="WatchlistManager"/>: duplicate, delete, select, and the three
    /// things an operator does to a flow in a list - add it, take it out, exclude it while keeping it
    /// visible. Environment rules are enforced here rather than in a view so a second caller cannot mix
    /// two environments into one saved list.
    /// </summary>
    public sealed partial class WatchlistManager
    {
        /// <summary>
        /// Copy a list under a new name and a new id. Members, provenance and source solutions come with
        /// it: a duplicate is a starting point, not an empty list wearing a familiar name.
        /// </summary>
        public WatchlistOperationResult Duplicate(string id, string? name = null)
        {
            Watchlist? current = FindRequired(id, out WatchlistOperationResult? failure);
            if (current == null)
            {
                return failure!;
            }

            string baseName = string.IsNullOrWhiteSpace(name) ? current.Name + " (copy)" : name!.Trim();
            string target = SuggestName(baseName, current.EnvironmentId);
            DateTimeOffset now = _clock.UtcNow;

            // A copy needs a fresh identity, and a builder cannot change its id after construction -
            // which is the point: nothing can quietly re-key a saved list.
            WatchlistBuilder copy = new WatchlistBuilder(_ids.NewId(), target, current.Mode, current.Description, current.EnvironmentId, current.Preferences);
            foreach (WatchlistMember member in current.Members)
            {
                copy.AddStoredMember(member);
            }

            copy.SetSourceSolutions(current.SourceSolutionIds);
            copy.SetCoverage(current.State, current.CoverageNotes);
            copy.SetTimestamps(now, now, current.LastMembershipRefreshUtc);

            Watchlist created = copy.Build();
            return Commit(_catalog.WithWatchlist(created), created, "copied from " + current.Name);
        }

        /// <summary>
        /// Delete a saved list. This removes the local record only: nothing in Dataverse, Power Automate
        /// or Azure is touched - a watchlist is a monitoring intention, not a resource.
        /// </summary>
        public WatchlistOperationResult Delete(string id)
        {
            Watchlist? current = FindRequired(id, out WatchlistOperationResult? failure);
            if (current == null)
            {
                return failure!;
            }

            return Commit(
                _catalog.Without(id),
                null,
                "removed from the saved watchlists; nothing was changed in the environment");
        }

        /// <summary>
        /// Select the list to monitor. The choice is stored per environment, so reopening the tool
        /// against another environment does not inherit it.
        /// </summary>
        public WatchlistOperationResult Select(string? id, string? environmentId)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Commit(_catalog.WithActiveWatchlist(null, environmentId, _clock.UtcNow), null, "no watchlist selected");
            }

            Watchlist? wanted = _catalog.Find(id);
            if (wanted == null)
            {
                return WatchlistOperationResult.Refused(WatchlistOperationOutcome.NotFound, _catalog, null, DescribeMissing(id));
            }

            IReadOnlyList<Watchlist> visible = _catalog.ForEnvironment(environmentId);
            if (!Contains(visible, wanted))
            {
                return WatchlistOperationResult.Refused(
                    WatchlistOperationOutcome.EnvironmentMismatch,
                    _catalog,
                    wanted,
                    "\"" + wanted.Name + "\" was saved for " + DescribeScope(wanted.EnvironmentId)
                        + " and cannot be monitored from " + DescribeScope(environmentId) + ".");
            }

            return Commit(_catalog.WithActiveWatchlist(wanted.Id, environmentId, _clock.UtcNow), wanted, "selected for this environment");
        }

        /// <summary>
        /// Add the flows one solution discovery produced, keeping that read's coverage with them. This is
        /// the M2/M3 discovery path becoming a saved list, using the same merge rule as everywhere else:
        /// a flow reached through several solutions is one member with several provenances.
        /// </summary>
        public WatchlistOperationResult ImportSolutionFlows(
            string id,
            SolutionReference solution,
            IEnumerable<WorkflowReference>? flows,
            ProviderResultState state,
            string? coverageNote = null,
            WatchlistMembershipMode? adoptMode = null)
        {
            Watchlist? current = FindRequired(id, out WatchlistOperationResult? failure);
            if (current == null)
            {
                return failure!;
            }

            if (solution == null)
            {
                return WatchlistOperationResult.Refused(WatchlistOperationOutcome.NotFound, _catalog, current, "No solution was given to import from.");
            }

            WatchlistBuilder builder = WatchlistBuilder.From(current);
            int added = builder.AddSolutionFlows(solution, flows, state, coverageNote, _clock.UtcNow);
            if (adoptMode.HasValue)
            {
                builder.SetMode(adoptMode.Value);
            }

            Watchlist updated = builder.Build().WithModifiedAt(_clock.UtcNow);
            if (added == 0 && updated.State == current.State && updated.Mode == current.Mode)
            {
                return WatchlistOperationResult.Unchanged(_catalog, current, "this solution contributed nothing new");
            }

            return Commit(
                _catalog,
                updated,
                added.ToString(CultureInfo.InvariantCulture) + " new member(s) from " + solution.DisplayName,
                new string[] { SourceStatus.Label(updated.State) });
        }

        private WatchlistOperationResult Commit(WatchlistCatalog next, Watchlist? watchlist, string reason, IEnumerable<string>? notes = null)
        {
            // A caller that hands over a changed list gets it replaced by id: keeping the replace here
            // means no mutation can report "applied" while the catalog still holds the old instance.
            if (watchlist != null)
            {
                next = next.WithWatchlist(watchlist);
            }

            _catalog = next;
            _hasUnsavedChanges = true;
            return WatchlistOperationResult.Applied(_catalog, watchlist, reason, notes);
        }

        private WatchlistOperationResult Replace(Watchlist updated, string? reason, IEnumerable<string>? notes)
        {
            if (_catalog.Find(updated.Id) == null)
            {
                return WatchlistOperationResult.Refused(WatchlistOperationOutcome.NotFound, _catalog, null, DescribeMissing(updated.Id), notes);
            }

            return Commit(_catalog, updated.WithModifiedAt(_clock.UtcNow), reason ?? "membership updated", notes);
        }

        private Watchlist? FindRequired(string? id, out WatchlistOperationResult? failure)
        {
            Watchlist? current = _catalog.Find(id);
            failure = current == null
                ? WatchlistOperationResult.Refused(WatchlistOperationOutcome.NotFound, _catalog, null, DescribeMissing(id))
                : (WatchlistOperationResult?)null;
            return current;
        }

        private WatchlistOperationResult? CheckStale(Watchlist current, DateTimeOffset? expectedModifiedAtUtc)
        {
            if (!expectedModifiedAtUtc.HasValue || !current.ModifiedAtUtc.HasValue)
            {
                return null;
            }

            if (current.ModifiedAtUtc.Value.Equals(expectedModifiedAtUtc.Value.ToUniversalTime()))
            {
                return null;
            }

            return WatchlistOperationResult.Refused(
                WatchlistOperationOutcome.Conflict,
                _catalog,
                current,
                "it changed at " + current.ModifiedAtUtc.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)
                    + "; reload it before editing");
        }

        public WatchlistOperationResult NoteEnvironment(string? environmentId)
        {
            string scope = (environmentId ?? string.Empty).Trim();
            if (string.Equals(_catalog.Settings.ActiveEnvironmentId, scope, StringComparison.OrdinalIgnoreCase))
            {
                return WatchlistOperationResult.Unchanged(_catalog, null, "this environment is already the active scope");
            }

            return Commit(
                _catalog.WithSettings(_catalog.Settings.WithActiveEnvironment(scope, _clock.UtcNow)),
                null,
                scope.Length == 0 ? "no environment recorded" : "active environment is " + scope);
        }

        /// <summary>Add one flow by hand. It survives every later dynamic refresh.</summary>
        public WatchlistOperationResult AddWorkflow(string id, WorkflowReference flow)
        {
            Watchlist? current = FindRequired(id, out WatchlistOperationResult? failure);
            if (current == null)
            {
                return failure!;
            }

            if (flow == null)
            {
                return WatchlistOperationResult.Refused(WatchlistOperationOutcome.NotFound, _catalog, current, "No flow was given to add.");
            }

            string? mismatch = DescribeEnvironmentMismatch(current, flow);
            if (mismatch != null)
            {
                return WatchlistOperationResult.Refused(WatchlistOperationOutcome.EnvironmentMismatch, _catalog, current, mismatch);
            }

            WatchlistBuilder builder = WatchlistBuilder.From(current);
            AdoptEnvironment(builder, current, flow);
            if (!builder.AddManualFlow(flow, _clock.UtcNow))
            {
                return WatchlistOperationResult.Unchanged(_catalog, current, "that flow is already a member of this watchlist");
            }

            return Commit(_catalog, builder.Build().WithModifiedAt(_clock.UtcNow), "added " + flow.Name);
        }

        /// <summary>
        /// Take a flow out of the list entirely. Excluding is usually what is wanted: a removed member can
        /// come back with the next refresh of a dynamic list, while an exclusion is a recorded decision.
        /// </summary>
        public WatchlistOperationResult RemoveWorkflow(string id, WorkflowKey? key)
        {
            Watchlist? current = FindRequired(id, out WatchlistOperationResult? failure);
            if (current == null)
            {
                return failure!;
            }

            WatchlistBuilder builder = WatchlistBuilder.From(current);
            if (key == null || !builder.RemoveMember(key))
            {
                return WatchlistOperationResult.Unchanged(_catalog, current, "that flow is not a member of this watchlist");
            }

            return Commit(_catalog, builder.Build().WithModifiedAt(_clock.UtcNow), "removed " + key.Canonical + " from the members");
        }

        /// <summary>Include or exclude a member. Exclusion keeps it in the list and in the file.</summary>
        public WatchlistOperationResult SetIncluded(string id, WorkflowKey? key, bool isIncluded)
        {
            Watchlist? current = FindRequired(id, out WatchlistOperationResult? failure);
            if (current == null)
            {
                return failure!;
            }

            WatchlistMember? existing = current.FindMember(key);
            if (existing == null)
            {
                return WatchlistOperationResult.Unchanged(_catalog, current, "that flow is not a member of this watchlist");
            }

            if (existing.IsIncluded == isIncluded)
            {
                return WatchlistOperationResult.Unchanged(
                    _catalog,
                    current,
                    isIncluded ? "that flow is already monitored" : "that flow is already excluded");
            }

            WatchlistBuilder builder = WatchlistBuilder.From(current);
            builder.SetIncluded(key!, isIncluded);
            return Commit(
                _catalog,
                builder.Build().WithModifiedAt(_clock.UtcNow),
                isIncluded
                    ? "included " + existing.Workflow.Name
                    : "excluded " + existing.Workflow.Name + "; it stays in the list so you can put it back");
        }

        private static bool TryValidateName(string? name, out string clean, out string? reason)
        {
            clean = SettingsFormat.Text(name, MaximumNameLength);
            if (clean.Length == 0)
            {
                reason = "A watchlist needs a name you will recognise in a dropdown.";
                return false;
            }

            if (clean.Length >= MaximumNameLength)
            {
                reason = "A watchlist name must be shorter than " + MaximumNameLength.ToString(CultureInfo.InvariantCulture) + " characters.";
                return false;
            }

            reason = null;
            return true;
        }

        private static string DescribeClash(string name)
        {
            return "Another watchlist in this environment is already called \"" + name + "\".";
        }

        private static string DescribeMissing(string? id)
        {
            return string.IsNullOrWhiteSpace(id)
                ? "No watchlist is selected."
                : "No saved watchlist has the id \"" + id + "\" - it was deleted, or this reference is from another session.";
        }

        private static string DescribeScope(string? environmentId)
        {
            return string.IsNullOrWhiteSpace(environmentId)
                ? "an environment that was not recorded"
                : "environment " + environmentId!.Trim();
        }

        private static bool Contains(IReadOnlyList<Watchlist> watchlists, Watchlist candidate)
        {
            foreach (Watchlist watchlist in watchlists)
            {
                if (string.Equals(watchlist.Id, candidate.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Why a flow may not join this list, or null when it may. A list that names an environment holds
        /// only flows from it: mixing scopes puts a flow in a list that can never be queried with the
        /// connection that opens it, and the operator reads that as "no runs" instead of "wrong list".
        /// </summary>
        private static string? DescribeEnvironmentMismatch(Watchlist current, WorkflowReference flow)
        {
            string scope = current.EnvironmentId;
            string wanted = flow.Key.ScopeId;
            if (scope.Length == 0 || wanted.Length == 0 || string.Equals(scope, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return "\"" + flow.Name + "\" lives in " + DescribeScope(wanted) + " and cannot join a watchlist saved for "
                + DescribeScope(scope) + ".";
        }

        /// <summary>
        /// Adopt a flow's environment when the list had none recorded. A list built while disconnected
        /// would otherwise stay unscoped forever and start admitting flows from anywhere.
        /// </summary>
        private static void AdoptEnvironment(WatchlistBuilder builder, Watchlist current, WorkflowReference flow)
        {
            if (current.EnvironmentId.Length == 0 && flow.Key.ScopeId.Length > 0)
            {
                builder.SetEnvironment(flow.Key.ScopeId);
            }
        }
    }
}
