using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AutomationMonitor.Plugin.Connectivity
{
    /// <summary>
    /// AM-35: the run-history identity spike, and nothing else.
    ///
    /// <para>
    /// The <c>flowrun</c> table documents two workflow columns: <c>workflow</c>, a Lookup to the workflow
    /// table whose referenced primary key is <c>workflow.workflowid</c>, and <c>workflowid</c>, a String 100
    /// described as "unique identifier of the workflow associated with this run". The production run query
    /// filters on the text column because that is the column it was written against. Which of the two
    /// actually carries the id discovery produced is not a question the schema answers, and a monitoring view
    /// must not be built on an assumption about it. So this class asks the environment instead - twice, for
    /// one flow, inside one seven-day window, at a hard ceiling of five rows per query - and reports the two
    /// answers as shapes and counts.
    /// </para>
    ///
    /// <para>
    /// Read-only by construction: <see cref="IOrganizationService.RetrieveMultiple"/> is the only service
    /// call reachable from here. Deliberately separate from <see cref="DataverseGateway"/>: the approved scope
    /// keeps the production criteria, normalizer and correlation rules untouched, so a diagnostic can never
    /// leak into the monitoring path it is meant to inform.
    /// </para>
    /// </summary>
    public sealed class DataverseFlowRunProbe
    {
        /// <summary>
        /// Round trips one probe query may make. A single ceiling-sized page is the whole answer; the second
        /// exists only so a source that claims more records while returning none cannot stall the read -
        /// <see cref="PageReader"/> stops on that case and reports it as truncated.
        /// </summary>
        private const int MaxPages = 2;

        private readonly IOrganizationService service;

        public DataverseFlowRunProbe(IOrganizationService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            this.service = service;
        }

        /// <summary>
        /// Run the experiment for one flow identity. Never throws for a data reason: a refused query is
        /// recorded as a state on that query's read, because "the environment would not answer" and "the
        /// environment answered with nothing" are the two answers this spike must not confuse.
        /// </summary>
        public FlowRunProbeOutcome Run(string? workflowIdentity, DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
        {
            FlowRunProbeRead text = new FlowRunProbeRead(DataverseSchema.RunWorkflowId, "text");
            FlowRunProbeRead lookup = new FlowRunProbeRead(DataverseSchema.RunWorkflowLookup, "lookup");

            // The window belongs to the run rather than to the queries: even a probe that could not ask
            // anything has to report the window it was going to ask about, or the next run's report cannot be
            // compared with this one.
            DateTimeOffset toUtc = checkedAtUtc.ToUniversalTime();
            DateTimeOffset fromUtc = FlowRunProbeDiagnostics.WindowStart(toUtc);

            string identity = (workflowIdentity ?? string.Empty).Trim();
            if (identity.Length == 0)
            {
                text.NotAttemptedReason = "no workflow identity was selected";
                lookup.NotAttemptedReason = "no workflow identity was selected";
                return new FlowRunProbeOutcome(text, lookup, null, fromUtc, toUtc);
            }

            Guid workflowId;
            bool isGuid = Guid.TryParse(identity, out workflowId);

            // The same widening the monitoring path uses: starttime is a UserLocal column, so a bound in UTC
            // may be interpreted in the connecting user's zone. The probe compares two reads against each
            // other, so both must be widened identically or the comparison measures the window instead of
            // the identity.
            DateTimeOffset serverFrom = fromUtc - RunQueryCriteria.ServerSkewAllowance;
            DateTimeOffset serverTo = toUtc + RunQueryCriteria.ServerSkewAllowance;

            // Query A: the text column, sent as a string - what production does today.
            Read(text, DataverseSchema.RunWorkflowId, isGuid ? FlowRunProbeDiagnostics.RequestIdText(workflowId) : identity, serverFrom, serverTo, cancellationToken);

            // Query B: the lookup column, sent as a reference to the workflow table's primary key. A text
            // identity cannot be turned into a lookup value without inventing one, so it is not attempted.
            if (!isGuid)
            {
                lookup.NotAttemptedReason = "requested identity is not guid-shaped";
            }
            else
            {
                Read(lookup, DataverseSchema.RunWorkflowLookup, new EntityReference(DataverseSchema.WorkflowEntity, workflowId), serverFrom, serverTo, cancellationToken);
            }

            return new FlowRunProbeOutcome(text, lookup, identity, fromUtc, toUtc);
        }

        /// <summary>
        /// One bounded read. The query is the same shape the production run query uses - late-bound,
        /// projected from <see cref="DataverseSchema.RunProbeAttributes"/>, newest first, window widened - so
        /// the only difference between the two queries is the column they filter on, which is the variable
        /// under test.
        /// </summary>
        private void Read(
            FlowRunProbeRead target,
            string filterColumn,
            object filterValue,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken)
        {
            target.WasAttempted = true;

            List<string?> workflowIdValues = new List<string?>();
            Dictionary<string, string> columnTypes = new Dictionary<string, string>(StringComparer.Ordinal);

            QueryExpression query = new QueryExpression(DataverseSchema.RunEntity);
            query.ColumnSet = new ColumnSet(DataverseSchema.RunProbeAttributes);
            query.Criteria = new FilterExpression(LogicalOperator.And);
            query.Criteria.AddCondition(filterColumn, ConditionOperator.Equal, filterValue);
            query.Criteria.AddCondition(DataverseSchema.RunStartTime, ConditionOperator.OnOrAfter, fromUtc.UtcDateTime);
            query.Criteria.AddCondition(DataverseSchema.RunStartTime, ConditionOperator.OnOrBefore, toUtc.UtcDateTime);
            query.AddOrder(DataverseSchema.RunStartTime, OrderType.Descending);

            try
            {
                PagedReadResult<string?> paged = PageReader.Read<string?>(
                    request =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // No TopCount. TopCount makes the platform report that there are no more records,
                        // which would let a five-row sample come back claiming a complete window. Asking for
                        // one ceiling-sized page instead leaves MoreRecords honest and lets the shared reader
                        // stop the read and label it a sample.
                        query.PageInfo = new PagingInfo
                        {
                            Count = FlowRunProbeDiagnostics.RowCeiling,
                            PageNumber = request.PageNumber
                        };

                        if (!string.IsNullOrEmpty(request.Cookie))
                        {
                            query.PageInfo.PagingCookie = request.Cookie;
                        }

                        EntityCollection rows = service.RetrieveMultiple(query);
                        List<string?> items = new List<string?>();
                        foreach (Entity entity in rows.Entities)
                        {
                            items.Add(Tally(entity, columnTypes));

                            object? workflowValue = DataverseGateway.Unwrap(entity, DataverseSchema.RunWorkflowId);
                            workflowIdValues.Add(TextOf(workflowValue));
                            if (workflowValue != null)
                            {
                                target.WorkflowIdColumnArrived = true;
                            }

                            if (entity.Contains(DataverseSchema.RunWorkflowLookup))
                            {
                                target.LookupColumnArrived = true;
                            }
                        }

                        return new Page<string?>(items, rows.MoreRecords, rows.PagingCookie);
                    },
                    FlowRunProbeDiagnostics.RowCeiling,
                    FlowRunProbeDiagnostics.RowCeiling,
                    MaxPages,
                    cancellationToken);

                target.Rows = paged.Items.Count;
                target.PagesRead = paged.PagesRead;
                target.CeilingReached = paged.IsTruncated;
                target.RunIds = paged.Items;
                target.WorkflowIdValues = workflowIdValues;
                target.ColumnTypes = new List<string>(columnTypes.Values);
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                // The message is dropped where it is caught: provider text can carry a tenant url, an
                // environment name or a payload. A type name plus a classified state is all the report needs,
                // and the classifier is the one the monitoring path uses too, so an absent run table reads as
                // Unsupported rather than as an empty history.
                target.Failure = ProviderErrorClassifier.Classify(error.GetType().Name, error.Message);
                target.FailureKind = error.GetType().Name;
            }
        }

        /// <summary>
        /// Record the shape of every column this row carried and hand back the row's run id as text.
        /// Presence is read from the attribute dictionary and the value obtained with the gateway's own
        /// reduction, so a column that arrived aliased or as an option set is named by the type the
        /// monitoring path would actually see.
        /// </summary>
        private static string? Tally(Entity entity, Dictionary<string, string> columnTypes)
        {
            string? runId = null;
            foreach (string attribute in entity.Attributes.Keys)
            {
                object? value = DataverseGateway.Unwrap(entity, attribute);
                columnTypes[attribute] = FlowRunProbeDiagnostics.ColumnTypeText(attribute, value);
                if (string.Equals(attribute, DataverseSchema.RunFlowRunId, StringComparison.Ordinal))
                {
                    runId = TextOf(value);
                }
            }

            if (runId == null && entity.Id != Guid.Empty)
            {
                // The key can sit on the Entity wrapper instead of in the projected attributes. Whether it did
                // is itself part of what this spike reports, so it is named as a column shape too.
                columnTypes[DataverseSchema.RunFlowRunId] = FlowRunProbeDiagnostics.ColumnTypeText(DataverseSchema.RunFlowRunId, entity.Id);
                runId = entity.Id.ToString("D");
            }

            return runId;
        }

        /// <summary>
        /// The text an identity column offers for comparison: a GUID in its canonical dashed form, or trimmed
        /// text. Anything else contributes nothing - its CLR type is already in the column tally, and that
        /// type name is the finding rather than a value coerced into a shape it does not have.
        /// </summary>
        private static string? TextOf(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is Guid)
            {
                return ((Guid)value).ToString("D");
            }

            string? text = value as string;
            if (text != null)
            {
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            return null;
        }
    }
}

//__APPEND__
