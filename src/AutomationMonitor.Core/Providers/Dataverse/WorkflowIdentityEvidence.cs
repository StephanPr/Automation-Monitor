using System;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// What a completed workflow-identity read established - or why it could not. Every state but
    /// <see cref="Verified"/> is a refusal reason, and the distinction between them is the point:
    /// "the environment said no", "the environment was not reached", "this build could not read the
    /// answer" and "there was nothing to ask" must never be collapsed into one blank verdict.
    /// </summary>
    public enum WorkflowIdentityEvidenceKind
    {
        /// <summary>Nothing was established. The zero value is deliberately the weakest claim.</summary>
        NotVerified = 0,

        /// <summary>Every stored member id was found by a read that ran to the end.</summary>
        Verified = 1,

        /// <summary>Some member ids were found and some were not, or some could not be asked about.</summary>
        PartiallyResolved = 2,

        /// <summary>The read finished and found none of the member ids in this organization.</summary>
        Unresolved = 3,

        /// <summary>There was no member id a Dataverse read could ask about, so there is no evidence.</summary>
        NothingToVerify = 4,

        /// <summary>The read stopped before it finished (paging or a ceiling), so it proves nothing.</summary>
        Incomplete = 5,

        /// <summary>The connection may not read the workflow table.</summary>
        Unauthorized = 6,

        /// <summary>The workflow table could not be reached or refused the read.</summary>
        Unavailable = 7,

        /// <summary>The environment answered with a shape this build could not interpret.</summary>
        Unsupported = 8,

        /// <summary>The connection could not name an organization, so nothing was asked of it.</summary>
        NoConnectedOrganization = 9
    }

    /// <summary>
    /// The tallies a verification read produced. Counts only: no workflow id, no flow name and no
    /// payload, so an evidence object is safe to show, log and keep.
    /// </summary>
    public readonly struct WorkflowIdentityCounts
    {
        public WorkflowIdentityCounts(
            int memberCount,
            int queryableCount,
            int notQueryableCount,
            int resolvedCount,
            int unidentifiableRows,
            int duplicateRows,
            int readsIssued,
            int pagesRead,
            int rowsRead)
        {
            MemberCount = memberCount;
            QueryableCount = queryableCount;
            NotQueryableCount = notQueryableCount;
            ResolvedCount = resolvedCount;
            UnidentifiableRows = unidentifiableRows;
            DuplicateRows = duplicateRows;
            ReadsIssued = readsIssued;
            PagesRead = pagesRead;
            RowsRead = rowsRead;
        }

        /// <summary>Members the read was asked about, including ones it cannot express as a query.</summary>
        public int MemberCount { get; }

        /// <summary>Distinct member ids the read could actually ask the organization about.</summary>
        public int QueryableCount { get; }

        /// <summary>Members whose id is not a Dataverse workflow id (another provider, or malformed).</summary>
        public int NotQueryableCount { get; }

        /// <summary>Queryable ids the organization confirmed it holds.</summary>
        public int ResolvedCount { get; }

        /// <summary>Rows that came back without an id this build could resolve.</summary>
        public int UnidentifiableRows { get; }

        /// <summary>Rows that repeated an id already counted (overlapping pages, re-reads).</summary>
        public int DuplicateRows { get; }

        public int ReadsIssued { get; }

        public int PagesRead { get; }

        public int RowsRead { get; }

        /// <summary>Queryable ids the organization did not return.</summary>
        public int MissingCount
        {
            get
            {
                int missing = QueryableCount - ResolvedCount;
                return missing < 0 ? 0 : missing;
            }
        }

        public static WorkflowIdentityCounts None
        {
            get { return new WorkflowIdentityCounts(0, 0, 0, 0, 0, 0, 0, 0, 0); }
        }
    }

    /// <summary>
    /// The evidence an environment-identity association may or may not rest on: the connected
    /// organization, and what a completed read said about the workflow ids stored in a watchlist.
    ///
    /// <para>
    /// A workflow-id hit is evidence about that id. It is not proof that the list was created here -
    /// importing a solution preserves workflow ids - so the value exists to let an operator confirm an
    /// association with the numbers in front of them, never to let the tool infer one. Nothing in here
    /// holds an identifier beyond the scope itself: the report renders the shared fingerprint label from
    /// <see cref="ResourceScopeLabel"/>, so a copied block cannot leak more of an environment than a
    /// grid cell already does.
    /// </para>
    /// </summary>
    public sealed class WorkflowIdentityEvidence
    {
        private WorkflowIdentityEvidence(
            WorkflowIdentityEvidenceKind kind,
            ResourceScope? organization,
            WorkflowIdentityCounts counts,
            DateTimeOffset checkedAtUtc,
            string? reason,
            ProviderResultState? providerState)
        {
            Kind = kind;
            Organization = organization ?? ResourceScope.Unverified;
            Counts = counts;
            CheckedAtUtc = checkedAtUtc.ToUniversalTime();
            Reason = reason ?? string.Empty;
            ProviderState = providerState;
        }

        public WorkflowIdentityEvidenceKind Kind { get; }

        /// <summary>The connected organization the read ran against; unverified when none was reported.</summary>
        public ResourceScope Organization { get; }

        public WorkflowIdentityCounts Counts { get; }

        /// <summary>When this read was made. UTC, and always a real instant - never a placeholder.</summary>
        public DateTimeOffset CheckedAtUtc { get; }

        /// <summary>Sanitized, actionable reason for anything that is not <see cref="Verified"/>.</summary>
        public string Reason { get; }

        /// <summary>The provider state behind a read failure, for the one shared source wording.</summary>
        public ProviderResultState? ProviderState { get; }

        /// <summary>True when the read ran to the end, whatever it concluded.</summary>
        public bool IsCompleted
        {
            get
            {
                return Kind == WorkflowIdentityEvidenceKind.Verified
                    || Kind == WorkflowIdentityEvidenceKind.PartiallyResolved
                    || Kind == WorkflowIdentityEvidenceKind.Unresolved
                    || Kind == WorkflowIdentityEvidenceKind.NothingToVerify;
            }
        }

        /// <summary>
        /// The only gate a caller may consult before associating: a completed read that found every
        /// stored id. Partial, stopped, refused, unreachable, unreadable and empty all answer no.
        /// </summary>
        public bool CanSupportAssociation
        {
            get { return Kind == WorkflowIdentityEvidenceKind.Verified; }
        }

        /// <summary>The short verdict for a status line.</summary>
        public string Verdict
        {
            get
            {
                switch (Kind)
                {
                    case WorkflowIdentityEvidenceKind.Verified:
                        return "verified - every stored member id was found in the connected organization";
                    case WorkflowIdentityEvidenceKind.PartiallyResolved:
                        return "partially resolved - only some stored member ids were found";
                    case WorkflowIdentityEvidenceKind.Unresolved:
                        return "unresolved - none of the stored member ids were found here";
                    case WorkflowIdentityEvidenceKind.NothingToVerify:
                        return "nothing to verify - no member has an id this read can ask about";
                    case WorkflowIdentityEvidenceKind.Incomplete:
                        return "incomplete - the read stopped before it finished, so it proves nothing";
                    case WorkflowIdentityEvidenceKind.Unauthorized:
                        return "unauthorized - this connection cannot read the workflow table";
                    case WorkflowIdentityEvidenceKind.Unsupported:
                        return "unsupported - the environment answered with a shape this build cannot read";
                    case WorkflowIdentityEvidenceKind.NoConnectedOrganization:
                        return "no connected organization - the connection could not name one";
                    case WorkflowIdentityEvidenceKind.NotVerified:
                        return "not verified - no identity read has been made";
                    default:
                        return "unavailable - the workflow table could not be read";
                }
            }
        }

        public static WorkflowIdentityEvidence From(
            WorkflowIdentityEvidenceKind kind,
            ResourceScope? organization,
            WorkflowIdentityCounts counts,
            DateTimeOffset checkedAtUtc,
            string? reason = null,
            ProviderResultState? providerState = null)
        {
            return new WorkflowIdentityEvidence(kind, organization, counts, checkedAtUtc, reason, providerState);
        }

        /// <summary>The copyable block. Counts and labels only - never an id, a name or a payload.</summary>
        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("Workflow-identity evidence").Append(Environment.NewLine);
            text.Append("  Connected organization : ")
                .Append(ResourceScopeLabel.Describe(Organization)).Append(Environment.NewLine);
            text.Append("  Verdict                : ").Append(Verdict).Append(Environment.NewLine);

            if (ProviderState.HasValue)
            {
                text.Append("  Source state           : ").Append(SourceStatus.Label(ProviderState.Value)).Append(Environment.NewLine);
            }

            text.Append("  Members considered     : ").Append(Num(Counts.MemberCount)).Append(Environment.NewLine);
            text.Append("  Ids asked about        : ").Append(Num(Counts.QueryableCount)).Append(Environment.NewLine);
            text.Append("  Found in organization  : ").Append(Num(Counts.ResolvedCount)).Append(Environment.NewLine);
            text.Append("  Not found              : ").Append(Num(Counts.MissingCount)).Append(Environment.NewLine);
            text.Append("  Not a Dataverse id     : ").Append(Num(Counts.NotQueryableCount)).Append(Environment.NewLine);
            text.Append("  Rows with no usable id : ").Append(Num(Counts.UnidentifiableRows)).Append(Environment.NewLine);
            text.Append("  Repeated rows ignored  : ").Append(Num(Counts.DuplicateRows)).Append(Environment.NewLine);
            text.Append("  Reads / pages / rows   : ")
                .Append(Num(Counts.ReadsIssued)).Append(" / ")
                .Append(Num(Counts.PagesRead)).Append(" / ")
                .Append(Num(Counts.RowsRead)).Append(Environment.NewLine);
            text.Append("  Read at (UTC)          : ").Append(CheckedAtUtc.ToString("u", CultureInfo.InvariantCulture)).Append(Environment.NewLine);

            if (Reason.Length > 0)
            {
                text.Append("  Reason                 : ").Append(Reason).Append(Environment.NewLine);
            }

            text.Append("  What this proves       : these workflow ids exist in the organization named above.").Append(Environment.NewLine);
            text.Append("  What this does not prove: that this watchlist was created there. Importing a solution").Append(Environment.NewLine);
            text.Append("                            preserves workflow ids, so only you can confirm it is this one.").Append(Environment.NewLine);

            return text.ToString();
        }

        public override string ToString()
        {
            return Verdict;
        }

        private static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
