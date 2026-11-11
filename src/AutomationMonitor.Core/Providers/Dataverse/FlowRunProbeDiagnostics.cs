using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// What a probe column actually carried, expressed as a shape and never as a value.
    ///
    /// <para>
    /// The spike this serves asks "which of the two documented <c>flowrun</c> columns holds the id
    /// discovery produced". That question is answerable from shapes and counts, and answering it that way
    /// is the only reason the answer can be pasted into a log, a document or a chat window without moving
    /// tenant data. A run id, a flow name or a workflow id never appears in any of it.
    /// </para>
    /// </summary>
    public enum ProbeValueShape
    {
        /// <summary>The read carried no rows, so the column said nothing at all.</summary>
        NoValues,

        /// <summary>Rows arrived and every value in the column was null or blank.</summary>
        Absent,

        /// <summary>Every value present parses as a GUID - an id column, not a name column.</summary>
        GuidLike,

        /// <summary>Every value present is text that is not a GUID - a name or an opaque token.</summary>
        NonGuidText,

        /// <summary>The rows disagreed about the shape, which is itself the finding.</summary>
        Mixed
    }

    /// <summary>How the values of one column relate to the identity this build was asked to match.</summary>
    public enum ProbeMatch
    {
        /// <summary>The query was never sent, so there is nothing to compare.</summary>
        NotAttempted,

        /// <summary>The query answered with no rows.</summary>
        NoRows,

        /// <summary>Rows arrived but none of them carried the column.</summary>
        ColumnAbsent,

        /// <summary>Every value present equals the requested identity.</summary>
        AllMatch,

        /// <summary>Some values match and some do not - the column is not one consistent thing.</summary>
        SomeMatch,

        /// <summary>No value matches the requested identity.</summary>
        NoneMatch
    }

    /// <summary>
    /// What the two-diagnostic-query experiment settled, as a verdict rather than a pair of counts.
    /// Every member states an observation; none of them states a cause.
    /// </summary>
    public enum ProbeVerdict
    {
        /// <summary>Neither query could be sent, so the experiment did not run.</summary>
        NotAttempted,

        /// <summary>
        /// Only one of the two queries could be sent. One column answering on its own says nothing about
        /// which of the two is authoritative, so this is never reported as a one-sided result.
        /// </summary>
        Incomplete,

        /// <summary>At least one query was refused. A refused read is not an empty read.</summary>
        Refused,

        /// <summary>Both queries answered with no rows: the identity relationship stays open.</summary>
        BothEmpty,

        /// <summary>Only the text-column filter returned rows.</summary>
        TextOnly,

        /// <summary>Only the lookup-column filter returned rows.</summary>
        LookupOnly,

        /// <summary>Both filters returned rows and at least one run id came back through both.</summary>
        BothAgree,

        /// <summary>Both filters returned rows, but not one run id was shared between them.</summary>
        BothDiffer
    }

    /// <summary>
    /// One diagnostic read exactly as the probe observed it: counts, shapes, returned CLR type names and a
    /// classified failure. The id lists exist so the two reads can be compared in memory and are never
    /// rendered - see <see cref="FlowRunProbeOutcome.Describe"/>.
    /// </summary>
    public sealed class FlowRunProbeRead
    {
        public FlowRunProbeRead(string filterColumn, string filterKind)
        {
            FilterColumn = string.IsNullOrWhiteSpace(filterColumn) ? "not named" : filterColumn!.Trim();
            FilterKind = string.IsNullOrWhiteSpace(filterKind) ? "text" : filterKind!.Trim();
            RunIds = new string?[0];
            WorkflowIdValues = new string?[0];
            ColumnTypes = new string[0];
        }

        /// <summary>The logical name this query filtered on. Schema text, never tenant data.</summary>
        public string FilterColumn { get; }

        /// <summary>"text" or "lookup": how the filter value had to be shaped for that column.</summary>
        public string FilterKind { get; }

        /// <summary>False when the probe could not build a legal query, with the reason beside it.</summary>
        public bool WasAttempted { get; set; }

        /// <summary>Why the query was not sent, in codes. Never an exception message.</summary>
        public string? NotAttemptedReason { get; set; }

        public int Rows { get; set; }

        public int PagesRead { get; set; }

        /// <summary>True when the ceiling stopped the read while the source still had rows.</summary>
        public bool CeilingReached { get; set; }

        /// <summary>Whether any returned row carried the <c>workflow</c> lookup column at all.</summary>
        public bool LookupColumnArrived { get; set; }

        /// <summary>Whether any returned row carried the <c>workflowid</c> text column at all.</summary>
        public bool WorkflowIdColumnArrived { get; set; }

        /// <summary>The classified state of a refused read; null when the read succeeded.</summary>
        public ProviderResultState? Failure { get; set; }

        /// <summary>The exception's type name only. The message is dropped where it is caught.</summary>
        public string? FailureKind { get; set; }

        /// <summary>Run ids the rows carried. Compared in memory; never rendered.</summary>
        public IReadOnlyList<string?> RunIds { get; set; }

        /// <summary>The <c>workflowid</c> values the rows carried. Compared in memory; never rendered.</summary>
        public IReadOnlyList<string?> WorkflowIdValues { get; set; }

        /// <summary>Attribute name plus CLR type name per returned column, tallied by the gateway.</summary>
        public IReadOnlyList<string> ColumnTypes { get; set; }

        /// <summary>Whether the environment answered this query with at least one row.</summary>
        public bool HasRows
        {
            get { return WasAttempted && Failure == null && Rows > 0; }
        }

        /// <summary>
        /// The one wording for what happened to this query: "answered", "not sent", or "refused: &lt;state&gt;".
        /// The report and the log line both take it from here, so a state can never be described two ways -
        /// and a refused query can never be rendered as an empty one.
        /// </summary>
        public string StateLabel
        {
            get
            {
                if (!WasAttempted)
                {
                    return "not sent";
                }

                if (Failure != null)
                {
                    return "refused: " + ProviderResultStates.Token(Failure.Value);
                }

                return "answered";
            }
        }

        /// <summary>The shape of the run ids this read returned.</summary>
        public ProbeValueShape RunIdShape
        {
            get { return FlowRunProbeDiagnostics.ShapeOf(RunIds); }
        }

        /// <summary>How this read's <c>workflowid</c> values relate to the identity that was asked for.</summary>
        public ProbeMatch WorkflowIdMatch
        {
            get { return FlowRunProbeDiagnostics.MatchOf(RequestedId, this, WorkflowIdValues); }
        }

        /// <summary>
        /// Set by the outcome that owns the read, so a read can judge itself against the request without
        /// every caller having to remember to pass the identity along.
        /// </summary>
        internal string? RequestedId { get; set; }
    }

    /// <summary>
    /// The result of the AM-35 experiment: one flow, one window, one ceiling, read twice - once filtered on
    /// the <c>workflowid</c> text column (what production does today) and once on the <c>workflow</c>
    /// lookup column (the documented join) - plus the verdict those two answers support.
    ///
    /// <para>
    /// Everything rendered is structural by construction: counts, column names, CLR type names and state
    /// codes. The requested identity, the run ids and the error text are compared in memory and cannot reach
    /// a rendered line; a unit test asserts exactly that.
    /// </para>
    ///
    /// <para>
    /// AM-36 added the two members the hand-back actually depends on: <see cref="VerdictLabel"/> with
    /// <see cref="Explanation"/> (what the maintainer reads on screen, worded differently for every state so
    /// an empty answer is never mistaken for a cause) and <see cref="Severity"/> (how loudly the host should
    /// speak about it). <see cref="FlowRunProbeReport.Build"/> turns all of it into the copyable block;
    /// <see cref="Describe"/> stays the single-line form for the host log.
    /// </para>
    /// </summary>
    public sealed class FlowRunProbeOutcome
    {
        public FlowRunProbeOutcome(
            FlowRunProbeRead stringFilter,
            FlowRunProbeRead lookupFilter,
            string? requestedWorkflowId,
            DateTimeOffset? windowFromUtc = null,
            DateTimeOffset? windowToUtc = null)
        {
            if (stringFilter == null)
            {
                throw new ArgumentNullException(nameof(stringFilter));
            }

            if (lookupFilter == null)
            {
                throw new ArgumentNullException(nameof(lookupFilter));
            }

            StringFilter = stringFilter;
            LookupFilter = lookupFilter;
            RequestedWorkflowId = string.IsNullOrWhiteSpace(requestedWorkflowId) ? null : requestedWorkflowId!.Trim();

            // The reads judge themselves against the request, so the comparison rule lives in one place.
            stringFilter.RequestedId = RequestedWorkflowId;
            lookupFilter.RequestedId = RequestedWorkflowId;

            Guid parsed;
            RequestedIdentityIsGuid = RequestedWorkflowId != null && Guid.TryParse(RequestedWorkflowId, out parsed);
            WindowFromUtc = windowFromUtc == null ? (DateTimeOffset?)null : windowFromUtc.Value.ToUniversalTime();
            WindowToUtc = windowToUtc == null ? (DateTimeOffset?)null : windowToUtc.Value.ToUniversalTime();
        }

        /// <summary>Query A: filtered on the <c>workflowid</c> text column.</summary>
        public FlowRunProbeRead StringFilter { get; }

        /// <summary>Query B: filtered on the <c>workflow</c> lookup column.</summary>
        public FlowRunProbeRead LookupFilter { get; }

        /// <summary>The identity discovery produced. Compared, never rendered.</summary>
        public string? RequestedWorkflowId { get; }

        /// <summary>Whether that identity can be used as a lookup value at all.</summary>
        public bool RequestedIdentityIsGuid { get; }

        /// <summary>
        /// Start of the window the queries were given, in UTC, when the caller recorded it. A probe run
        /// without a window cannot be interpreted later, so the report says "not recorded" rather than
        /// inventing one.
        /// </summary>
        public DateTimeOffset? WindowFromUtc { get; }

        /// <summary>End of the window the queries were given, in UTC. Also the instant the read happened.</summary>
        public DateTimeOffset? WindowToUtc { get; }

        /// <summary>Distinct run ids that came back from both queries.</summary>
        public int OverlapCount
        {
            get { return FlowRunProbeDiagnostics.CountOverlap(StringFilter.RunIds, LookupFilter.RunIds); }
        }

        /// <summary>The observation this experiment supports. It never names a cause.</summary>
        public ProbeVerdict Verdict
        {
            get
            {
                bool asked = StringFilter.WasAttempted;
                bool askedTheOther = LookupFilter.WasAttempted;
                if (!asked && !askedTheOther)
                {
                    return ProbeVerdict.NotAttempted;
                }

                if (StringFilter.Failure != null || LookupFilter.Failure != null)
                {
                    return ProbeVerdict.Refused;
                }

                // One column answering by itself cannot rank the two, however many rows it returned.
                if (!asked || !askedTheOther)
                {
                    return ProbeVerdict.Incomplete;
                }

                bool left = StringFilter.HasRows;
                bool right = LookupFilter.HasRows;
                if (!left && !right)
                {
                    return ProbeVerdict.BothEmpty;
                }

                if (left && !right)
                {
                    return ProbeVerdict.TextOnly;
                }

                if (!left && right)
                {
                    return ProbeVerdict.LookupOnly;
                }

                return OverlapCount > 0 ? ProbeVerdict.BothAgree : ProbeVerdict.BothDiffer;
            }
        }

        /// <summary>
        /// Whether this run says anything about which column is authoritative. A refused read, a read that
        /// could not be sent and an empty read all leave the question open, and none of them may be
        /// reported as an answer. Two reads that returned rows but not one run id in common are also
        /// inconclusive: something other than identity separated them, and this probe cannot see what.
        /// </summary>
        public bool IsInconclusive
        {
            get
            {
                ProbeVerdict verdict = Verdict;
                return verdict == ProbeVerdict.NotAttempted
                    || verdict == ProbeVerdict.Incomplete
                    || verdict == ProbeVerdict.Refused
                    || verdict == ProbeVerdict.BothEmpty
                    || verdict == ProbeVerdict.BothDiffer;
            }
        }

        /// <summary>
        /// The verdict in words, for the status line and the result window. Every state has its own label:
        /// "no rows either way", "a query was refused" and "only one query could be sent" must never collapse
        /// into one "inconclusive" that the operator has to interpret.
        /// </summary>
        public string VerdictLabel
        {
            get { return LabelFor(Verdict); }
        }

        /// <summary>
        /// What this result does and does not establish, as plain text the plugin can show on its own. Two
        /// wording rules hold across every state: no state may suggest that zero rows means the flow has
        /// never executed, and no state may name a cause this read cannot actually see.
        /// </summary>
        public string Explanation
        {
            get { return Explain(Verdict); }
        }

        /// <summary>
        /// How loudly the host should speak about this result. The levels mean what they normally mean: an
        /// empty answer the environment really did give is information, a query that could not be sent or two
        /// reads that came back disagreeing is a warning, and a refusal that says "you may not read this" is
        /// an error. Nothing here is promoted merely to make a log file appear - the copyable result window
        /// AM-36 added is what makes the answer reachable, independent of the host's log level.
        /// </summary>
        public ProbeReportSeverity Severity
        {
            get
            {
                if (StringFilter.Failure == ProviderResultState.Unauthorized
                    || LookupFilter.Failure == ProviderResultState.Unauthorized)
                {
                    return ProbeReportSeverity.Error;
                }

                if (StringFilter.Failure != null || LookupFilter.Failure != null)
                {
                    return ProbeReportSeverity.Warning;
                }

                ProbeVerdict verdict = Verdict;
                if (verdict == ProbeVerdict.NotAttempted || verdict == ProbeVerdict.Incomplete)
                {
                    // The experiment could not be run as designed. That is a fault in the question this build
                    // asked, not a routine empty answer, and reading it as one would hide a defect.
                    return ProbeReportSeverity.Warning;
                }

                if (verdict == ProbeVerdict.BothDiffer)
                {
                    // Two filters on one table disagreeing about the same flow is the anomaly this probe
                    // exists to catch; it deserves a louder entry than a settled answer.
                    return ProbeReportSeverity.Warning;
                }

                return ProbeReportSeverity.Information;
            }
        }

        /// <summary>
        /// The single line the maintainer copies back. Everything on it is a count, a column name, a CLR
        /// type name or a state code - never an id, a name, an error message or a payload.
        /// </summary>
        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("Run-history identity probe (")
                .Append(FlowRunProbeDiagnostics.WindowDays.ToString(CultureInfo.InvariantCulture))
                .Append("-day window, ")
                .Append(FlowRunProbeDiagnostics.RowCeiling.ToString(CultureInfo.InvariantCulture))
                .Append(" row ceiling, newest first): ")
                .Append(FlowRunProbeDiagnostics.Describe(StringFilter))
                .Append("; ")
                .Append(FlowRunProbeDiagnostics.Describe(LookupFilter))
                .Append("; comparison: ")
                .Append(OverlapCount.ToString(CultureInfo.InvariantCulture))
                .Append(" run id(s) returned by both queries; requested identity is ")
                .Append(RequestedIdentityIsGuid ? "guid-shaped" : "not guid-shaped")
                .Append(". Verdict: ")
                .Append(LabelFor(Verdict));

            string? caveat = CaveatFor(Verdict);
            if (caveat != null)
            {
                text.Append(". ").Append(caveat);
            }

            return text.ToString();
        }

        private static string LabelFor(ProbeVerdict verdict)
        {
            switch (verdict)
            {
                case ProbeVerdict.NotAttempted:
                    return "not attempted";
                case ProbeVerdict.Incomplete:
                    return "incomplete - only one of the two queries could be sent";
                case ProbeVerdict.Refused:
                    return "a query was refused";
                case ProbeVerdict.BothEmpty:
                    return "inconclusive - no rows either way";
                case ProbeVerdict.TextOnly:
                    return "only the text-column filter returned rows";
                case ProbeVerdict.LookupOnly:
                    return "only the lookup-column filter returned rows";
                case ProbeVerdict.BothAgree:
                    return "both filters return the same runs";
                default:
                    return "both filters returned rows but none in common";
            }
        }

        /// <summary>
        /// What the maintainer reads on screen (AM-36). Every state gets its own sentence, and three states in
        /// particular must not read alike: an empty answer, a refused query and an experiment that could not be
        /// sent are three different problems with three different next actions.
        ///
        /// <para>
        /// Standing wording rule: no state may suggest that zero rows proves the flow has never executed, and
        /// no state may name a cause this read cannot see - ingestion settings, retention and permissions are
        /// all invisible to a two-query probe.
        /// </para>
        /// </summary>
        private static string Explain(ProbeVerdict verdict)
        {
            switch (verdict)
            {
                case ProbeVerdict.NotAttempted:
                    return "Neither query was sent, so the experiment did not run. Nothing here says anything "
                        + "about either column, and the reason is in how the probe was asked for, not in the "
                        + "environment's data.";
                case ProbeVerdict.Incomplete:
                    return "Only one of the two queries could be sent, so this is not a comparison yet. One "
                        + "column answering by itself cannot say which of the two is authoritative, whatever "
                        + "the count on that side was.";
                case ProbeVerdict.Refused:
                    return "At least one query was refused. A refused read is not an empty read: the counts on "
                        + "this report do not measure what this flow has run, and the identity question stays "
                        + "open until both queries answer.";
                case ProbeVerdict.BothEmpty:
                    return "Both queries were sent and the environment answered with no rows for this window. "
                        + "That is not evidence that the flow has never run, and it is not evidence about "
                        + "which column is authoritative: a flow that did not run in this window, a retention "
                        + "window shorter than this one, run history not being stored in this environment and a "
                        + "read this build cannot see would all answer exactly the same way here.";
                case ProbeVerdict.TextOnly:
                    return "Only the text-column filter returned rows. For this flow the filter the monitoring "
                        + "query uses today does find run rows; the lookup column being empty in this window "
                        + "does not by itself show that it is never populated.";
                case ProbeVerdict.LookupOnly:
                    return "Only the lookup-column filter returned rows, so the filter the monitoring query uses "
                        + "today would find nothing for this flow. That is evidence the production filter needs "
                        + "its own change task with its own tests; this probe does not change it.";
                case ProbeVerdict.BothAgree:
                    return "Both filters returned rows and at least one run came back through both, so both "
                        + "columns carry this flow's identity. The filter the monitoring query uses today can "
                        + "find runs for this flow.";
                default:
                    return "Both filters returned rows but not one run id in common. Two filters on one table "
                        + "disagreeing about the same flow is not something the schema explains on its own; the "
                        + "column shapes reported below are the evidence to act on, and this result does not say "
                        + "which filter is the wrong one.";
            }
        }

        /// <summary>
        /// What the verdict does not establish. The possibilities are listed so an empty answer is not read
        /// as a cause: this probe cannot see ingestion settings, retention, permissions, or whether the flow
        /// simply did not run, and naming one of them would be a guess dressed up as a finding.
        /// </summary>
        private static string? CaveatFor(ProbeVerdict verdict)
        {
            switch (verdict)
            {
                case ProbeVerdict.NotAttempted:
                    return "Nothing was asked of the environment, so this says nothing about the columns.";
                case ProbeVerdict.Incomplete:
                    return "Both queries have to answer before the two columns can be compared; this run asked only one of them.";
                case ProbeVerdict.Refused:
                    return "A refused query is not an empty query: the identity question stays open until both reads answer.";
                case ProbeVerdict.BothEmpty:
                    return "This does not say which column is authoritative. A row-less answer is consistent with run-history "
                        + "ingestion not being enabled for this flow, a retention window shorter than the probe window, the "
                        + "connected user not being able to read run rows, or the flow genuinely not having run in the window; "
                        + "this read cannot tell those apart.";
                case ProbeVerdict.BothDiffer:
                    return "Two filters on one table answered with different runs, which the schema does not explain on its "
                        + "own; the shapes reported above are the evidence to act on.";
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// The comparison rules behind the probe: pure functions of (requested identity, returned values) so
    /// they are testable with fixtures and so the plugin's job stays "ask the question, report the shapes".
    /// </summary>
    public static class FlowRunProbeDiagnostics
    {
        /// <summary>How far back a probe looks. A week is long enough to find a recent run.</summary>
        public const int WindowDays = 7;

        /// <summary>
        /// Hard ceiling on rows per probe query. The probe samples, it never reads a window, and every
        /// rendering of a read that hit the ceiling says so instead of implying completeness.
        /// </summary>
        public const int RowCeiling = 5;

        /// <summary>Cap on how many returned column shapes are named, so the line stays readable.</summary>
        public const int MaxReportedColumns = 24;

        /// <summary>
        /// The start of the window a probe asks for, given the instant it runs. UTC in and UTC out: the
        /// <c>starttime</c> column behaves <c>UserLocal</c>, so the gateway widens this before sending it and
        /// the probe never reasons in local time.
        /// </summary>
        public static DateTimeOffset WindowStart(DateTimeOffset toUtc)
        {
            return toUtc.ToUniversalTime().AddDays(-WindowDays);
        }

        /// <summary>
        /// The value the text-column filter has to be given. That column is text, so the identity is sent in
        /// the same canonical dashed form the model uses for every workflow id; a braced, padded or bare
        /// spelling would match nothing and would then be read as "this flow has no runs" - the difference
        /// between an empty answer and a wrong one.
        /// </summary>
        public static string RequestIdText(Guid workflowId)
        {
            return workflowId == Guid.Empty ? string.Empty : workflowId.ToString("D");
        }

        /// <summary>
        /// The "<c>column</c>=<c>Type</c>" name the report shows for one returned attribute. The value has
        /// already been reduced to a plain CLR type by the gateway, so the name states the shape this build
        /// actually read - <c>workflow=EntityReference</c> versus <c>workflow=String</c> is the difference
        /// the spike is looking for - and never states the value.
        /// </summary>
        public static string ColumnTypeText(string attribute, object? plainValue)
        {
            string name = string.IsNullOrWhiteSpace(attribute) ? "unnamed" : attribute.Trim();
            return name + "=" + (plainValue == null ? "empty" : plainValue.GetType().Name);
        }

        /// <summary>
        /// The shape a column's values had. Missing values are counted separately by
        /// <see cref="CountAbsent"/> and never folded into a shape, because "the column was not there" and
        /// "the column held something else" are different findings.
        /// </summary>
        public static ProbeValueShape ShapeOf(IEnumerable<string?> values)
        {
            bool anyGuid = false;
            bool anyText = false;
            bool anyValue = false;

            if (values != null)
            {
                foreach (string? value in values)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    anyValue = true;
                    Guid parsed;
                    if (Guid.TryParse(value!.Trim(), out parsed))
                    {
                        anyGuid = true;
                    }
                    else
                    {
                        anyText = true;
                    }
                }
            }

            if (anyValue)
            {
                if (anyGuid && anyText)
                {
                    return ProbeValueShape.Mixed;
                }

                return anyGuid ? ProbeValueShape.GuidLike : ProbeValueShape.NonGuidText;
            }

            // Distinguishes "no rows to look at" from "rows that said nothing in this column".
            return Count(values) == 0 ? ProbeValueShape.NoValues : ProbeValueShape.Absent;
        }

        /// <summary>How many entries in the list carried nothing.</summary>
        public static int CountAbsent(IEnumerable<string?> values)
        {
            int absent = 0;
            if (values == null)
            {
                return absent;
            }

            foreach (string? value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    absent++;
                }
            }

            return absent;
        }

        /// <summary>Distinct non-empty ids, canonicalised by the one identity rule the whole model uses.</summary>
        public static int CountDistinct(IEnumerable<string?> values)
        {
            return ToCanonicalSet(values).Count;
        }

        /// <summary>
        /// Run ids present on both sides. Canonicalisation is the shared <see cref="CanonicalId"/> rule, so
        /// a GUID spelled three ways is still one run and a duplicate observation of the same run cannot
        /// inflate the overlap - the same de-duplication discipline the merge pipeline uses.
        /// </summary>
        public static int CountOverlap(IEnumerable<string?> left, IEnumerable<string?> right)
        {
            HashSet<string> leftIds = ToCanonicalSet(left);
            if (leftIds.Count == 0)
            {
                return 0;
            }

            int shared = 0;
            foreach (string id in ToCanonicalSet(right))
            {
                if (leftIds.Contains(id))
                {
                    shared++;
                }
            }

            return shared;
        }

        /// <summary>
        /// Whether one read's <c>workflowid</c> values are the identity that was asked for. An absent column
        /// is reported as absent rather than as a mismatch: the server already filtered the read, so a
        /// column the source left out states nothing about which flow a row belongs to.
        /// </summary>
        public static ProbeMatch MatchOf(string? requested, FlowRunProbeRead? read, IEnumerable<string?> values)
        {
            if (read == null || !read.WasAttempted)
            {
                return ProbeMatch.NotAttempted;
            }

            if (read.Failure != null || read.Rows == 0)
            {
                return ProbeMatch.NoRows;
            }

            if (string.IsNullOrWhiteSpace(requested))
            {
                // Nothing was asked for, so no value can be said to match or to differ.
                return ProbeMatch.NotAttempted;
            }

            int total = Count(values);
            if (total == 0 || CountAbsent(values) == total)
            {
                return ProbeMatch.ColumnAbsent;
            }

            string wanted = CanonicalId.Identifier(requested);
            int matched = 0;
            int considered = 0;
            foreach (string? value in values!)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                considered++;
                if (string.Equals(CanonicalId.Identifier(value), wanted, StringComparison.Ordinal))
                {
                    matched++;
                }
            }

            if (matched == 0)
            {
                return ProbeMatch.NoneMatch;
            }

            return matched == considered ? ProbeMatch.AllMatch : ProbeMatch.SomeMatch;
        }

        /// <summary>One read's segment of the report line.</summary>
        public static string Describe(FlowRunProbeRead? read)
        {
            if (read == null)
            {
                return "query: no result object at all";
            }

            StringBuilder text = new StringBuilder();
            text.Append("query on '")
                .Append(Safe(read.FilterColumn))
                .Append("' (")
                .Append(Safe(read.FilterKind))
                .Append(" filter): ");

            if (!read.WasAttempted)
            {
                text.Append(read.StateLabel);
                if (!string.IsNullOrWhiteSpace(read.NotAttemptedReason))
                {
                    text.Append(" (").Append(Safe(read.NotAttemptedReason)).Append(')');
                }

                return text.ToString();
            }

            if (read.Failure != null)
            {
                text.Append(read.StateLabel);
                if (!string.IsNullOrWhiteSpace(read.FailureKind))
                {
                    text.Append(" (").Append(Safe(read.FailureKind)).Append(')');
                }

                return text.ToString();
            }

            text.Append(read.Rows.ToString(CultureInfo.InvariantCulture)).Append(" row(s), ")
                .Append(read.PagesRead.ToString(CultureInfo.InvariantCulture)).Append(" page(s), ")
                .Append(CountDistinct(read.RunIds).ToString(CultureInfo.InvariantCulture))
                .Append(" distinct run id(s), run id shape ")
                .Append(ShapeLabel(read.RunIdShape));

            if (read.CeilingReached)
            {
                text.Append("; stopped at the row ceiling with more rows pending, so this is a sample");
            }

            text.Append("; lookup column arrived: ").Append(read.LookupColumnArrived ? "yes" : "no")
                .Append("; text id column arrived: ").Append(read.WorkflowIdColumnArrived ? "yes" : "no")
                .Append("; text id vs requested: ").Append(MatchLabel(read.WorkflowIdMatch));

            int absent = CountAbsent(read.WorkflowIdValues);
            if (absent > 0)
            {
                text.Append(" (absent in ").Append(absent.ToString(CultureInfo.InvariantCulture)).Append(" row(s))");
            }

            text.Append("; columns returned: ").Append(DescribeColumnTypes(read.ColumnTypes));
            return text.ToString();
        }

        public static string ShapeLabel(ProbeValueShape shape)
        {
            switch (shape)
            {
                case ProbeValueShape.NoValues:
                    return "no rows to judge";
                case ProbeValueShape.Absent:
                    return "carried no value";
                case ProbeValueShape.GuidLike:
                    return "guid-shaped";
                case ProbeValueShape.NonGuidText:
                    return "text that is not a guid";
                default:
                    return "mixed";
            }
        }

        public static string MatchLabel(ProbeMatch match)
        {
            switch (match)
            {
                case ProbeMatch.NotAttempted:
                    return "not attempted";
                case ProbeMatch.NoRows:
                    return "no rows to compare";
                case ProbeMatch.ColumnAbsent:
                    return "column carried nothing";
                case ProbeMatch.AllMatch:
                    return "all values are the requested identity";
                case ProbeMatch.SomeMatch:
                    return "some values match, some do not";
                default:
                    return "no value is the requested identity";
            }
        }

        private static string DescribeColumnTypes(IEnumerable<string> columnTypes)
        {
            if (columnTypes == null)
            {
                return "not reported";
            }

            List<string> named = new List<string>();
            foreach (string column in columnTypes)
            {
                if (named.Count >= MaxReportedColumns)
                {
                    named.Add("...");
                    break;
                }

                named.Add(Safe(column));
            }

            return named.Count == 0 ? "none named" : string.Join(", ", named);
        }

        /// <summary>Column and type names come from the platform, so they are sanitized like any provider text.</summary>
        private static string Safe(string? value)
        {
            string cleaned = DiagnosticsSanitizer.Sanitize(value);
            return cleaned.Length == 0 ? "not reported" : cleaned;
        }

        private static HashSet<string> ToCanonicalSet(IEnumerable<string?> values)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (values == null)
            {
                return ids;
            }

            foreach (string? value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                ids.Add(CanonicalId.Identifier(value));
            }

            return ids;
        }

        private static int Count(IEnumerable<string?>? values)
        {
            if (values == null)
            {
                return 0;
            }

            int count = 0;
            foreach (string? unused in values)
            {
                count++;
            }

            return count;
        }
    }
}






