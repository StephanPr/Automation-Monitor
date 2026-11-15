using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Query;

namespace AutomationMonitor.Plugin.Connectivity
{
    /// <summary>
    /// The only place in this plugin that touches Dataverse. It implements the core's row seam by
    /// turning one page request into one <see cref="QueryExpression"/> and mapping the returned
    /// entities into core records.
    ///
    /// <para>
    /// Read-only by construction: only <see cref="IOrganizationService.RetrieveMultiple"/> is used,
    /// so no code path here can create, update, delete or execute against the environment.
    /// Exceptions deliberately propagate: the core reader classifies them into an honest state.
    /// </para>
    /// </summary>
    public sealed class DataverseGateway : IDataverseRowSource, IDataverseRunRowSource
    {
        private readonly IOrganizationService service;

        /// <summary>
        /// <paramref name="organizationScope"/> is the kind-qualified scope of the connected organization
        /// (<c>dataverse-organization:&lt;guid&gt;</c>) as <see cref="ConnectedDataverseEnvironment"/> resolved it,
        /// or null when this session has not verified one. It becomes the scope half of every workflow identity
        /// produced from these rows, so it is never a Power Platform environment id, never a connection url and
        /// never a friendly name - and passing null is the honest way to say "we do not know where these rows came
        /// from", which yields unscoped identities that cannot merge with a verified environment's.
        /// </summary>
        public DataverseGateway(IOrganizationService service, string? organizationScope)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            this.service = service;
            EnvironmentId = string.IsNullOrWhiteSpace(organizationScope) ? null : organizationScope!.Trim();
        }

        /// <summary>
        /// The verified organization scope of the rows this gateway reads, or null when none was verified.
        /// The name is the core seam's, for compatibility with the run reader; the value it holds is a
        /// kind-qualified resource scope, never a bare environment identifier.
        /// </summary>
        public string? EnvironmentId { get; }

        public Page<SolutionReference>? ReadSolutions(PageRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();

            QueryExpression query = new QueryExpression(DataverseSchema.SolutionEntity);
            query.ColumnSet = new ColumnSet(
                DataverseSchema.SolutionId,
                DataverseSchema.SolutionUniqueName,
                DataverseSchema.SolutionDisplayName,
                DataverseSchema.SolutionVersion,
                DataverseSchema.SolutionIsManaged,
                DataverseSchema.SolutionIsVisible,
                DataverseSchema.SolutionInstalledOn,
                DataverseSchema.SolutionModifiedOn);
            query.AddOrder(DataverseSchema.SolutionDisplayName, OrderType.Ascending);

            // No criteria: every solution the connected user can read is a legitimate candidate.
            // Filtering by visibility or publisher here would hide flows the operator is looking
            // for, and the search box already narrows the list.
            ApplyPaging(query, request);

            EntityCollection rows = service.RetrieveMultiple(query);
            List<SolutionReference> solutions = new List<SolutionReference>();
            foreach (Entity entity in rows.Entities)
            {
                Guid id = entity.Id;
                if (id == Guid.Empty)
                {
                    // A solution row without an id cannot carry identity; counting happens in
                    // the reader, so it is skipped here rather than invented.
                    continue;
                }

                solutions.Add(new SolutionReference(
                    id.ToString("D"),
                    GetString(entity, DataverseSchema.SolutionUniqueName),
                    GetString(entity, DataverseSchema.SolutionDisplayName),
                    GetString(entity, DataverseSchema.SolutionVersion),
                    GetBool(entity, DataverseSchema.SolutionIsManaged) ?? false,
                    GetUtc(entity, DataverseSchema.SolutionInstalledOn),
                    GetUtc(entity, DataverseSchema.SolutionModifiedOn)));
            }

            return new Page<SolutionReference>(solutions, rows.MoreRecords, rows.PagingCookie);
        }
        public Page<RawWorkflowRecord>? ReadSolutionWorkflows(string solutionId, PageRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            Guid parsed;
            if (!Guid.TryParse(solutionId, out parsed))
            {
                throw new ArgumentException("A solution id Guid is required to discover its flows.", nameof(solutionId));
            }

            cancellationToken.ThrowIfCancellationRequested();

            QueryExpression query = new QueryExpression(DataverseSchema.WorkflowEntity);

            // The projection is a core fact, never a local choice: the platform validates every
            // column against the table's metadata and refuses the whole query when one name is
            // unknown, so the list lives in DataverseSchema where a guard test can refuse an
            // undocumented column. primarycategory/secondarycategory were removed for exactly that
            // reason - see DataverseSchema.WorkflowAttributes and docs/provider-capabilities.md.
            query.ColumnSet = new ColumnSet(DataverseSchema.WorkflowAttributes);

            // Never a bare `true`. Microsoft Learn, remarks on QueryExpression.Distinct: "When the Distinct
            // property is true, the results returned don't include primary key values for each record" - and
            // the primary key of this table is the flow's identity. With it true, every row of the first live
            // session came back with an empty Entity.Id and the operator saw "0 cloud flow(s) - 59 row(s)
            // without an id skipped". The rows this flag appeared to deduplicate are collapsed in Core
            // (DataverseCloudFlowSelector on the WorkflowKey), where a removal is counted and shown instead
            // of disappearing into the query.
            query.Distinct = DataverseSchema.WorkflowQueryDistinct;
            query.AddOrder(DataverseSchema.WorkflowName, OrderType.Ascending);

            // The join is the solution-aware scope: workflows referenced as workflow components
            // (componenttype 29) of this solution. The category is deliberately not filtered here
            // so the core selector can count classic workflows instead of the grid quietly
            // showing fewer rows than the solution contains.
            LinkEntity component = new LinkEntity(
                DataverseSchema.WorkflowEntity,
                DataverseSchema.SolutionComponentEntity,
                DataverseSchema.WorkflowId,
                DataverseSchema.ComponentObjectId,
                JoinOperator.Inner);
            component.Columns = new ColumnSet(false);
            component.LinkCriteria.AddCondition(DataverseSchema.ComponentType, ConditionOperator.Equal, DataverseSchema.ComponentTypeWorkflow);
            component.LinkCriteria.AddCondition(DataverseSchema.ComponentSolutionId, ConditionOperator.Equal, parsed);
            query.LinkEntities.Add(component);

            ApplyPaging(query, request);

            EntityCollection rows = service.RetrieveMultiple(query);
            List<RawWorkflowRecord> records = new List<RawWorkflowRecord>();
            foreach (Entity entity in rows.Entities)
            {
                // Identity is the core's decision, not this adapter's: it is given the projected
                // workflowid (unwrapped, and tagged when it arrived aliased) plus the row id, and it refuses
                // a row rather than pick between two disagreeing ids. The record then carries the source, so
                // an empty grid can be explained from the log without a debugger.
                bool idWasAliased;
                object? projectedId = Unwrap(entity, DataverseSchema.WorkflowId, out idWasAliased);
                string? identity;
                WorkflowIdSource identitySource;
                WorkflowIdentity.TryResolve(projectedId, entity.Id, idWasAliased, out identity, out identitySource);

                records.Add(new RawWorkflowRecord
                {
                    Id = identity,
                    IdSource = identitySource,
                    Name = GetString(entity, DataverseSchema.WorkflowName),
                    RawCategory = GetInt(entity, DataverseSchema.WorkflowCategory),
                    RawStateCode = GetInt(entity, DataverseSchema.WorkflowStateCode),
                    RawStatusCode = GetInt(entity, DataverseSchema.WorkflowStatusCode),
                    Description = GetString(entity, DataverseSchema.WorkflowDescription),
                    CreatedOnUtc = GetUtc(entity, DataverseSchema.WorkflowCreatedOn),
                    ModifiedOnUtc = GetUtc(entity, DataverseSchema.WorkflowModifiedOn)
                });
            }

            return new Page<RawWorkflowRecord>(records, rows.MoreRecords, rows.PagingCookie);
        }

        /// <summary>
        /// One page of <c>flowrun</c> rows for the criteria's flow and window.
        ///
        /// <para>
        /// The window is sent wider than the request on purpose (see
        /// <see cref="RunQueryCriteria.ServerFromUtc"/>): the column's <c>UserLocal</c> behaviour
        /// means a bound expressed in UTC may be interpreted in another zone, and a server that
        /// quietly narrows the read would hide runs. The core re-applies the exact half-open window
        /// afterwards, so a widened read costs a few discarded rows while a narrowed read would cost
        /// the truth.
        /// </para>
        /// <para>
        /// Sorting and paging on an Elastic table are the two facts here that a real tenant has to
        /// confirm; the query is deliberately the same shape discovery already uses, and the core
        /// re-orders rows by start time regardless.
        /// </para>
        /// </summary>
        public Page<RawRunRecord>? ReadFlowRuns(RunQueryCriteria criteria, PageRequest request, CancellationToken cancellationToken)
        {
            if (criteria == null)
            {
                throw new ArgumentNullException(nameof(criteria));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();

            QueryExpression query = new QueryExpression(DataverseSchema.RunEntity);
            query.ColumnSet = new ColumnSet(DataverseSchema.RunAttributes);

            // The workflow filter is what keeps this a per-flow read; the Elastic partition is the
            // current user, so an unbounded run query would pull every flow that user can see.
            query.Criteria = new FilterExpression(LogicalOperator.And);
            query.Criteria.AddCondition(DataverseSchema.RunWorkflowId, ConditionOperator.Equal, criteria.Workflow.WorkflowId);
            query.Criteria.AddCondition(DataverseSchema.RunStartTime, ConditionOperator.OnOrAfter, criteria.ServerFromUtc.UtcDateTime);
            query.Criteria.AddCondition(DataverseSchema.RunStartTime, ConditionOperator.OnOrBefore, criteria.ServerToUtc.UtcDateTime);

            if (criteria.StatusValues.Count > 0)
            {
                FilterExpression statuses = new FilterExpression(LogicalOperator.Or);
                for (int i = 0; i < criteria.StatusValues.Count; i++)
                {
                    statuses.AddCondition(DataverseSchema.RunStatus, ConditionOperator.Equal, criteria.StatusValues[i]);
                }

                query.Criteria.AddFilter(statuses);
            }

            query.AddOrder(DataverseSchema.RunStartTime, OrderType.Descending);
            ApplyPaging(query, request, criteria.PageSize);

            EntityCollection rows = service.RetrieveMultiple(query);
            List<RawRunRecord> records = new List<RawRunRecord>();
            foreach (Entity entity in rows.Entities)
            {
                Guid rowId = entity.Id;
                records.Add(new RawRunRecord
                {
                    RunRecordId = rowId == Guid.Empty ? null : rowId.ToString("D"),
                    RunId = GetString(entity, DataverseSchema.RunName),
                    WorkflowId = GetString(entity, DataverseSchema.RunWorkflowId),
                    RawStatus = GetString(entity, DataverseSchema.RunStatus),
                    StartedAtUtc = GetUtc(entity, DataverseSchema.RunStartTime),
                    EndedAtUtc = GetUtc(entity, DataverseSchema.RunEndTime),
                    ReportedDurationMs = GetLong(entity, DataverseSchema.RunDuration),
                    ErrorCode = GetString(entity, DataverseSchema.RunErrorCode),
                    ErrorText = GetString(entity, DataverseSchema.RunErrorMessage),
                    ParentRunId = GetString(entity, DataverseSchema.RunParentRunId),
                    CorrelationId = GetString(entity, DataverseSchema.RunClientTrackingId),
                    PrimaryRunCode = GetInt(entity, DataverseSchema.RunIsPrimary),
                    // isprimary is a Picklist, so the numeric option is the evidence and the Boolean
                    // field stays null unless the column really is a Boolean. Turning an unrecognised
                    // option value into true would invent a fact the source never stated.
                    PrimaryRunFlag = DataverseValue.AsBooleanWhenBoolean(Unwrap(entity, DataverseSchema.RunIsPrimary)),
                    TriggerType = GetString(entity, DataverseSchema.RunTriggerType)
                });
            }

            return new Page<RawRunRecord>(records, rows.MoreRecords, rows.PagingCookie);
        }

        private static void ApplyPaging(QueryExpression query, PageRequest request)
        {
            ApplyPaging(query, request, PageReader.DefaultPageSize);
        }

        /// <summary>
        /// The one paging rule for every read this plugin makes. Internal rather than private because
        /// <see cref="DataverseWorkflowIdentitySource"/> must page exactly the way discovery does - a second
        /// copy of this logic would let the identity check advance through a window differently from the read
        /// that filled the watchlist it is checking.
        /// </summary>
        internal static void ApplyPaging(QueryExpression query, PageRequest request, int count)
        {
            query.PageInfo = new PagingInfo
            {
                Count = count,
                PageNumber = request.PageNumber
            };

            if (!string.IsNullOrEmpty(request.Cookie))
            {
                query.PageInfo.PagingCookie = request.Cookie;
            }
        }
        /// <summary>
        /// The readers below are thin on purpose: this gateway reduces the SDK's shapes to a plain CLR
        /// value with <see cref="Unwrap"/>, and the core decides what that value means - including
        /// refusing to guess when a column arrives in a shape this tool does not understand.
        /// </summary>
        private static string? GetString(Entity entity, string attribute)
        {
            return DataverseValue.AsText(Unwrap(entity, attribute), attribute);
        }

        private static int? GetInt(Entity entity, string attribute)
        {
            return DataverseValue.AsInt32(Unwrap(entity, attribute), attribute);
        }

        private static bool? GetBool(Entity entity, string attribute)
        {
            return DataverseValue.AsBoolean(Unwrap(entity, attribute), attribute);
        }

        private static DateTimeOffset? GetUtc(Entity entity, string attribute)
        {
            return DataverseValue.AsUtc(Unwrap(entity, attribute), attribute);
        }

        /// <summary>
        /// BigInt reader. Dataverse returns a BigInt column as <see cref="long"/>, so an Int32 read
        /// would overflow on a long-running flow; the value stays milliseconds because the column
        /// (schema name <c>DurationInMs</c>) is milliseconds.
        /// </summary>
        private static long? GetLong(Entity entity, string attribute)
        {
            return DataverseValue.AsInt64(Unwrap(entity, attribute), attribute);
        }

        /// <summary>
        /// The SDK reduction every reader in this plugin shares: alias unwrapped, option set reduced to its
        /// integer, nothing else interpreted. Internal rather than private because
        /// <see cref="DataverseFlowRunProbe"/> reports column shapes and must see exactly the same values the
        /// production reader sees - two copies of this rule would let a diagnostic report a shape the
        /// monitoring path never received.
        /// </summary>
        internal static object? Unwrap(Entity entity, string attribute)
        {
            bool ignored;
            return Unwrap(entity, attribute, out ignored);
        }

        /// <summary>
        /// The overload the identity rule needs: whether a value arrived wrapped in an alias is a fact worth
        /// reporting, because a query with a join can alias every column it returns and a reader that only
        /// understands the plain shape would lose the id without noticing.
        /// </summary>
        internal static object? Unwrap(Entity entity, string attribute, out bool wasAliased)
        {
            wasAliased = false;
            if (!entity.Contains(attribute))
            {
                // A column the environment did not return is "not stated", never a fabricated default.
                return null;
            }

            object? value = entity[attribute];

            // Type tests, never a cast. The SDK's typed accessor casts, so asking it for an
            // AliasedValue on any ordinary column throws InvalidCastException ("Unable to cast object
            // of type 'System.String' to type 'Microsoft.Xrm.Sdk.AliasedValue'") - the exact failure
            // the first real Windows session hit, because a query with no joins legitimately returns
            // plain root values. Proven against the assembly the host loads; see
            // docs/provider-capabilities.md.
            AliasedValue? aliased = value as AliasedValue;
            if (aliased != null)
            {
                value = aliased.Value;
                wasAliased = true;
            }

            // An option set is a wrapper around its integer value, not a value of its own; the core
            // reader sees plain CLR shapes and owns every decision about what they mean.
            OptionSetValue? option = value as OptionSetValue;
            if (option != null)
            {
                value = option.Value;
            }

            return value;
        }
    }
}
