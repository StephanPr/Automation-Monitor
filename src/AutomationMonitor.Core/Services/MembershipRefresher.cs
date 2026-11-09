using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Reads a saved watchlist's source solutions and reconciles membership against what comes back.
    /// This is the only place M4 contacts a provider, and it owns no policy of its own: each solution is
    /// read through the same <see cref="ISolutionAwareFlowSource"/> discovery already used on screen,
    /// turned into <see cref="MembershipEvidence"/>, and handed to <see cref="MembershipReconciler"/>,
    /// which is where the add/remove rules live.
    ///
    /// <para>
    /// Synchronous on purpose, like the rest of the provider seams: the XrmToolBox host runs this on a
    /// worker thread. Cancellation is honoured between solutions and between pages inside the source, and
    /// an unobserved partial read of one solution never discards what another one delivered.
    /// </para>
    /// </summary>
    public sealed class MembershipRefresher
    {
        private readonly ISolutionAwareFlowSource _source;
        private readonly IClock _clock;

        public MembershipRefresher(ISolutionAwareFlowSource source, IClock clock)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            _source = source;
            _clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Read every source solution of this watchlist and reconcile against the result.
        /// A preview (<paramref name="applyChanges"/> false) changes nothing, not even the stored
        /// coverage, so a preview cannot quietly make a stale list look current.
        /// </summary>
        public MembershipReconciliationResult Refresh(
            Watchlist watchlist,
            bool applyChanges,
            CancellationToken cancellationToken)
        {
            if (watchlist == null)
            {
                throw new ArgumentNullException(nameof(watchlist));
            }

            DateTimeOffset startedUtc = _clock.UtcNow;
            List<MembershipEvidence> evidence = new List<MembershipEvidence>();
            foreach (string solutionId in watchlist.SourceSolutionIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                evidence.Add(ReadOne(solutionId, cancellationToken));
            }

            return MembershipReconciler.Reconcile(watchlist, evidence, applyChanges, startedUtc);
        }

        /// <summary>
        /// Read the solutions a watchlist has never had, so an operator can attach a source to an
        /// existing snapshot and make it dynamic without losing the members already in it.
        /// </summary>
        public IReadOnlyList<MembershipEvidence> Read(IEnumerable<string>? solutionIds, CancellationToken cancellationToken)
        {
            List<MembershipEvidence> evidence = new List<MembershipEvidence>();
            if (solutionIds == null)
            {
                return evidence;
            }

            foreach (string solutionId in solutionIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(solutionId))
                {
                    evidence.Add(ReadOne(solutionId.Trim(), cancellationToken));
                }
            }

            return evidence;
        }

        /// <summary>
        /// One solution read, with its coverage kept. An exception is not allowed to look like an empty
        /// solution: it becomes <see cref="ProviderResultState.Unavailable"/> (or whatever the classifier
        /// says) and reconciliation then refuses to remove anything that came from it.
        /// </summary>
        private MembershipEvidence ReadOne(string solutionId, CancellationToken cancellationToken)
        {
            DateTimeOffset checkedAtUtc = _clock.UtcNow;
            try
            {
                ProviderResult<IReadOnlyList<WorkflowReference>> read = _source.GetCloudFlows(solutionId, cancellationToken);
                if (read == null)
                {
                    return new MembershipEvidence(
                        solutionId,
                        null,
                        ProviderResultState.Unavailable,
                        null,
                        0,
                        0,
                        checkedAtUtc,
                        "the source returned no result at all");
                }

                IReadOnlyList<WorkflowReference> flows = read.Value ?? new List<WorkflowReference>();
                return new MembershipEvidence(
                    solutionId,
                    null,
                    read.State,
                    flows,
                    read.PagesRead,
                    read.ItemsRead,
                    read.CheckedAtUtc,
                    read.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                ProviderResultState state = ProviderErrorClassifier.Classify(error.GetType().Name, error.Message);
                bool local = ProviderErrorClassifier.IsLocalDefect(error.GetType().Name, error.Message);
                string reason = DiagnosticsSanitizer.Describe(
                    ProviderErrorClassifier.AdviceFor(state, local),
                    DiagnosticsSanitizer.Sanitize(error.Message));
                return new MembershipEvidence(solutionId, null, state, null, 0, 0, checkedAtUtc, reason);
            }
        }
    }
}
