using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// Asks a Dataverse organization whether it holds the workflow ids a saved watchlist carries, and
    /// turns the answer into <see cref="WorkflowIdentityEvidence"/>. This is the read an operator-confirmed
    /// association is allowed to rest on, and it is the only thing that may say "verified".
    ///
    /// <para>
    /// <b>Bounded on purpose.</b> It runs against a live tenant on an explicit operator request, so it asks
    /// for one column (<see cref="DataverseSchema.WorkflowId"/>), never a distinct query, in batches of
    /// <see cref="MaximumIdsPerBatch"/> ids, at most <see cref="MaximumBatches"/> batches,
    /// <see cref="MaximumPagesPerBatch"/> pages each and <see cref="MaximumRows"/> rows in total. A list
    /// larger than that is reported <see cref="WorkflowIdentityEvidenceKind.Incomplete"/> rather than read
    /// to death: an incomplete read licenses nothing, so refusing to grow the ceiling is safe.
    /// These ceilings are this tool's own bounds, not a documented platform limit.
    /// </para>
    ///
    /// <para>
    /// <b>What the answer means.</b> A hit says the organization holds that workflow id. It does not say the
    /// list came from there - a solution import preserves ids - so this class produces evidence for a human
    /// to confirm and never a decision. Cancellation propagates; every other failure becomes an honest state
    /// with a sanitized, actionable reason, and the exception itself never escapes.
    /// </para>
    /// </summary>
    public sealed class DataverseWorkflowIdentityProbe
    {
        /// <summary>Ids in one request. Small, because the answer must stay attributable to one batch.</summary>
        public const int MaximumIdsPerBatch = 20;

        /// <summary>Batches one verification may issue, so the whole read stays inside a few round trips.</summary>
        public const int MaximumBatches = 12;

        /// <summary>Pages per batch. The batch holds at most 20 ids, so four pages is generous headroom.</summary>
        public const int MaximumPagesPerBatch = 4;

        /// <summary>Rows asked for per round trip.</summary>
        public const int RowsPerRequest = 50;

        /// <summary>Total row ceiling across the whole read.</summary>
        public const int MaximumRows = 500;

        /// <summary>The most ids one read will attempt; a bigger list is refused before any request is sent.</summary>
        public static readonly int MaximumVerifiableIds = MaximumIdsPerBatch * MaximumBatches;

        private readonly IWorkflowIdentityRowSource rows;
        private readonly IClock clock;

        public DataverseWorkflowIdentityProbe(IWorkflowIdentityRowSource rowSource, IClock? clock)
        {
            rows = rowSource ?? throw new ArgumentNullException(nameof(rowSource));
            this.clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Run the read. Only cancellation propagates: a refused, partial or empty answer is a result this
        /// probe has to report, not one it may hide behind an exception.
        /// </summary>
        public WorkflowIdentityEvidence Verify(
            ResourceScope? connectedOrganization,
            IEnumerable<WorkflowKey>? members,
            CancellationToken cancellationToken)
        {
            DateTimeOffset checkedAtUtc = clock.UtcNow;
            ResourceScope organization = connectedOrganization ?? ResourceScope.Unverified;

            if (!organization.IsQualified)
            {
                // No request is sent. A connection that cannot name an organization has nothing to verify
                // against, and an unverified scope must never be dressed up as an answer.
                return WorkflowIdentityEvidence.From(
                    WorkflowIdentityEvidenceKind.NoConnectedOrganization,
                    organization,
                    WorkflowIdentityCounts.None,
                    checkedAtUtc,
                    "The connection did not report a Dataverse organization identifier, so no read was made and nothing was verified.");
            }

            List<WorkflowKey> memberList = new List<WorkflowKey>();
            if (members != null)
            {
                foreach (WorkflowKey key in members)
                {
                    if (key != null)
                    {
                        memberList.Add(key);
                    }
                }
            }

            List<string> requested = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int notQueryable = 0;
            foreach (WorkflowKey key in memberList)
            {
                Guid parsed;
                if (key.Provider != ProviderKind.PowerAutomate || !Guid.TryParse(key.WorkflowId, out parsed))
                {
                    // A member this read cannot express as a query: counted and reported, never guessed at.
                    notQueryable++;
                    continue;
                }

                string canonical = parsed.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
                if (seen.Add(canonical))
                {
                    requested.Add(canonical);
                }
            }

            int memberCount = memberList.Count;
            if (requested.Count == 0)
            {
                return WorkflowIdentityEvidence.From(
                    WorkflowIdentityEvidenceKind.NothingToVerify,
                    organization,
                    new WorkflowIdentityCounts(memberCount, 0, notQueryable, 0, 0, 0, 0, 0, 0, 0, 0),
                    checkedAtUtc,
                    memberCount == 0
                        ? "This watchlist has no members, so nothing could confirm it belongs to this organization."
                        : "None of this watchlist's members carry a workflow id this read can ask Dataverse about.");
            }

            if (requested.Count > MaximumVerifiableIds)
            {
                return WorkflowIdentityEvidence.From(
                    WorkflowIdentityEvidenceKind.Incomplete,
                    organization,
                    new WorkflowIdentityCounts(memberCount, requested.Count, notQueryable, 0, 0, 0, 0, 0, 0, 0, 0),
                    checkedAtUtc,
                    "This list holds more workflow ids than one read may verify (limit "
                        + MaximumVerifiableIds.ToString(CultureInfo.InvariantCulture)
                        + "), so nothing was established and nothing was changed.",
                    ProviderResultState.Partial);
            }

            return Read(organization, memberCount, requested, notQueryable, checkedAtUtc, cancellationToken);
        }

        /// <summary>
        /// Issue the bounded batches and classify what came back. The tallies are kept even when the read
        /// fails halfway, because "three batches of twelve" is the sentence an operator needs.
        /// </summary>
        private WorkflowIdentityEvidence Read(
            ResourceScope organization,
            int memberCount,
            List<string> requested,
            int notQueryable,
            DateTimeOffset checkedAtUtc,
            CancellationToken cancellationToken)
        {
            HashSet<string> wanted = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);
            HashSet<string> resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> submitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int reads = 0;
            int pages = 0;
            int rowCount = 0;
            int unidentifiable = 0;
            int duplicates = 0;
            int unexpected = 0;

            try
            {
                string? stopped = null;
                for (int offset = 0; offset < requested.Count; offset += MaximumIdsPerBatch)
                {
                    List<string> batch = requested.GetRange(offset, Math.Min(MaximumIdsPerBatch, requested.Count - offset));
                    PagedReadResult<RawWorkflowRecord> read;
                    try
                    {
                        read = PageReader.Read(
                            request =>
                            {
                                reads++;

                                // These ids are now in front of the platform. Whether it answers for them is a
                                // separate fact, and the two only part company when a read fails - which is the
                                // moment the distinction matters, because an id that was asked about and never
                                // answered has not been shown to be missing.
                                foreach (string id in batch)
                                {
                                    submitted.Add(id);
                                }

                                return rows.ReadWorkflowsByIds(batch, request, cancellationToken);
                            },
                            RowsPerRequest,
                            MaximumRows,
                            MaximumPagesPerBatch,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        // The tallies this far are still the truth: what was answered stays answered, and
                        // everything else - this batch included - stays unresolved rather than missing.
                        return Failed(
                            error,
                            organization,
                            Counts(memberCount, requested, notQueryable, submitted, answered, resolved, unidentifiable, duplicates, reads, pages, rowCount),
                            checkedAtUtc);
                    }

                    pages += read.PagesRead;
                    HashSet<string> foundHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (RawWorkflowRecord row in read.Items)
                    {
                        rowCount++;
                        string rowId = CanonicalId.Identifier(row == null ? null : row.Id);
                        if (rowId.Length == 0)
                        {
                            unidentifiable++;
                            continue;
                        }

                        if (!wanted.Contains(rowId))
                        {
                            // The environment returned a row this read did not ask for. That contradicts its
                            // own filter, so the answer stops being trustworthy instead of being filtered into
                            // the shape that suits the request.
                            unexpected++;
                            continue;
                        }

                        if (!resolved.Add(rowId))
                        {
                            duplicates++;
                        }
                        else
                        {
                            foundHere.Add(rowId);
                        }
                    }

                    if (!read.IsComplete)
                    {
                        // A read that stopped mid-window still settled the ids it returned: those exist.
                        // The rest of the batch was never answered, so they are unresolved - not missing.
                        foreach (string id in foundHere)
                        {
                            answered.Add(id);
                        }

                        stopped = DiagnosticsSanitizer.Describe("Only part of the identity read completed", read.StopReason);
                        break;
                    }

                    // Every page of this batch arrived, so the organization has answered for every id in it:
                    // the ones that came back exist here, and the ones that did not are absent from it.
                    foreach (string id in batch)
                    {
                        answered.Add(id);
                    }
                }

                WorkflowIdentityCounts counts = Counts(
                    memberCount, requested, notQueryable, submitted, answered, resolved, unidentifiable, duplicates, reads, pages, rowCount);

                if (stopped != null)
                {
                    return WorkflowIdentityEvidence.From(
                        WorkflowIdentityEvidenceKind.Incomplete, organization, counts, checkedAtUtc, stopped, ProviderResultState.Partial);
                }

                if (unexpected > 0)
                {
                    return WorkflowIdentityEvidence.From(
                        WorkflowIdentityEvidenceKind.Incomplete,
                        organization,
                        counts,
                        checkedAtUtc,
                        unexpected.ToString(CultureInfo.InvariantCulture)
                            + " row(s) carried a workflow id this read did not ask about, so its answer cannot be trusted. Nothing was changed.",
                        ProviderResultState.Partial);
                }

                if (notQueryable > 0)
                {
                    return WorkflowIdentityEvidence.From(
                        WorkflowIdentityEvidenceKind.PartiallyResolved,
                        organization,
                        counts,
                        checkedAtUtc,
                        notQueryable.ToString(CultureInfo.InvariantCulture)
                            + " member(s) hold an id this read cannot ask Dataverse about, so the list is not fully verified.");
                }

                if (resolved.Count == requested.Count)
                {
                    return WorkflowIdentityEvidence.From(
                        WorkflowIdentityEvidenceKind.Verified, organization, counts, checkedAtUtc, null, ProviderResultState.Complete);
                }

                return resolved.Count == 0
                    ? WorkflowIdentityEvidence.From(
                        WorkflowIdentityEvidenceKind.Unresolved,
                        organization,
                        counts,
                        checkedAtUtc,
                        "This organization returned none of the workflow ids in this list.",
                        ProviderResultState.Complete)
                    : WorkflowIdentityEvidence.From(
                        WorkflowIdentityEvidenceKind.PartiallyResolved,
                        organization,
                        counts,
                        checkedAtUtc,
                        "This organization returned " + resolved.Count.ToString(CultureInfo.InvariantCulture)
                            + " of " + requested.Count.ToString(CultureInfo.InvariantCulture) + " workflow ids.",
                        ProviderResultState.Complete);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                // Anything the loop itself did not expect still reports the tallies it reached.
                return Failed(
                    error,
                    organization,
                    Counts(memberCount, requested, notQueryable, submitted, answered, resolved, unidentifiable, duplicates, reads, pages, rowCount),
                    checkedAtUtc);
            }
        }

        /// <summary>
        /// The one place the five different id tallies are assembled, so a read that ended early and a read
        /// that ran to the end cannot report them in two different ways.
        /// </summary>
        private static WorkflowIdentityCounts Counts(
            int memberCount,
            List<string> requested,
            int notQueryable,
            HashSet<string> submitted,
            HashSet<string> answered,
            HashSet<string> resolved,
            int unidentifiableRows,
            int duplicateRows,
            int reads,
            int pages,
            int rows)
        {
            return new WorkflowIdentityCounts(
                memberCount,
                requested.Count,
                notQueryable,
                submitted.Count,
                answered.Count,
                resolved.Count,
                unidentifiableRows,
                duplicateRows,
                reads,
                pages,
                rows);
        }

        /// <summary>
        /// A transport or mapping failure, in the one classification the rest of the product already uses.
        /// The exception object never escapes and its message is sanitized before anyone sees it.
        /// </summary>
        private static WorkflowIdentityEvidence Failed(
            Exception error,
            ResourceScope organization,
            WorkflowIdentityCounts counts,
            DateTimeOffset checkedAtUtc)
        {
            ProviderResultState state = ProviderErrorClassifier.Classify(error.GetType().Name, error.Message);
            ProviderErrorClassifier.DefectKind defect = ProviderErrorClassifier.DefectKindOf(error.GetType().Name, error.Message);
            string reason = DiagnosticsSanitizer.Describe(
                ProviderErrorClassifier.AdviceFor(state, defect),
                DiagnosticsSanitizer.Sanitize(error.Message));

            WorkflowIdentityEvidenceKind kind;
            if (state == ProviderResultState.Unauthorized)
            {
                kind = WorkflowIdentityEvidenceKind.Unauthorized;
            }
            else if (state == ProviderResultState.Unsupported || defect != ProviderErrorClassifier.DefectKind.None)
            {
                // A local defect means the environment answered - with a refusal of this build's query, or
                // with a shape this build could not read. Unavailable would claim the opposite, and its advice
                // would send the operator to check a connection that is working.
                kind = WorkflowIdentityEvidenceKind.Unsupported;
            }
            else
            {
                kind = WorkflowIdentityEvidenceKind.Unavailable;
            }

            return WorkflowIdentityEvidence.From(kind, organization, counts, checkedAtUtc, reason, state, defect);
        }
    }
}
