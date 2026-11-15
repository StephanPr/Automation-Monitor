using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AutomationMonitor.Plugin.Connectivity
{
    /// <summary>
    /// The Dataverse half of the workflow-identity verification: one <see cref="RetrieveMultiple"/> per page,
    /// asking whether this organization holds the workflow ids a saved watchlist carries.
    ///
    /// <para>
    /// Read-only by construction, exactly like <see cref="DataverseGateway"/>: only <c>RetrieveMultiple</c> is
    /// used, so no path here can create, update, cancel or reconfigure anything. The projection is a single
    /// documented column (<c>workflowid</c>, the table's primary id attribute) because a column the table does
    /// not have makes the platform refuse the <em>whole</em> query - the <c>primarycategory</c> failure that
    /// emptied the flow list on 2026-11-09. <c>Distinct</c> stays as <see cref="DataverseSchema.WorkflowQueryDistinct"/>
    /// (false), because a distinct query returns no primary key values and identity is the whole point of this read.
    /// </para>
    ///
    /// <para>
    /// The filter uses the documented <see cref="ConditionOperator.In"/> ("the value exists in a list of values")
    /// over Guid values - one boxed Guid per id, never an array as one value, because the platform refuses the
    /// whole query when a filter value is not the type the column takes. Ordering by the primary key makes paging
    /// stable, and the batch size and page ceiling are the core probe's, not this adapter's, so the bound cannot be
    /// widened by whoever wires the next caller. Exceptions deliberately propagate: the core classifies them into
    /// an honest state.
    /// </para>
    /// </summary>
    public sealed class DataverseWorkflowIdentitySource : IWorkflowIdentityRowSource
    {
        private readonly IOrganizationService service;

        public DataverseWorkflowIdentitySource(IOrganizationService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            this.service = service;
        }

        public Page<RawWorkflowRecord>? ReadWorkflowsByIds(IReadOnlyList<string> workflowIds, PageRequest request, CancellationToken cancellationToken)
        {
            if (workflowIds == null)
            {
                throw new ArgumentNullException(nameof(workflowIds));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();

            List<Guid> ids = new List<Guid>();
            foreach (string value in workflowIds)
            {
                Guid parsed;
                // The core only sends Guid-shaped ids; one that is not, or an empty Guid, is dropped here rather
                // than becoming a filter value that would make the platform reject the entire batch.
                if (Guid.TryParse(value, out parsed) && parsed != Guid.Empty)
                {
                    ids.Add(parsed);
                }
            }

            if (ids.Count == 0)
            {
                // Nothing to ask, and no round trip. The reader counts this as a complete empty page; the caller
                // has already refused any id that could not be expressed, so this is a belt-and-braces branch.
                return new Page<RawWorkflowRecord>(new List<RawWorkflowRecord>(), false, null);
            }

            QueryExpression query = new QueryExpression(DataverseSchema.WorkflowEntity);
            query.ColumnSet = new ColumnSet(DataverseSchema.WorkflowId);
            query.Distinct = DataverseSchema.WorkflowQueryDistinct;
            query.AddOrder(DataverseSchema.WorkflowId, OrderType.Ascending);

            // Each id is boxed on its own. AddCondition's third parameter is `params object[]`, and a Guid[]
            // is not an object[] because value-type arrays are not covariant, so handing it the array binds
            // the whole array as ONE value: Dataverse then refuses the entire query with
            // "Condition for attribute 'workflow.workflowid': expected argument(s) of type 'System.Guid' but
            // received 'System.Guid[]'" - which is what the first live run of this read did on 2026-11-15, and
            // it answered with no rows at all. A refused query establishes nothing, so the shape of this list
            // is part of the contract with the platform; DataverseWorkflowIdentityQuerySdkTests asserts it on
            // the real ConditionExpression.
            object[] idValues = new object[ids.Count];
            for (int index = 0; index < ids.Count; index++)
            {
                idValues[index] = ids[index];
            }

            query.Criteria.AddCondition(DataverseSchema.WorkflowId, ConditionOperator.In, idValues);
            DataverseGateway.ApplyPaging(query, request, DataverseWorkflowIdentityProbe.RowsPerRequest);

            EntityCollection rows = service.RetrieveMultiple(query);
            List<RawWorkflowRecord> records = new List<RawWorkflowRecord>();
            foreach (Entity entity in rows.Entities)
            {
                // Identity stays the core's decision: it is given the projected column plus the row id and
                // refuses a row whose two candidates disagree, rather than this adapter picking one.
                bool idWasAliased;
                object? projectedId = DataverseGateway.Unwrap(entity, DataverseSchema.WorkflowId, out idWasAliased);
                string? identity;
                WorkflowIdSource identitySource;
                WorkflowIdentity.TryResolve(projectedId, entity.Id, idWasAliased, out identity, out identitySource);

                records.Add(new RawWorkflowRecord
                {
                    Id = identity,
                    IdSource = identitySource
                });
            }

            return new Page<RawWorkflowRecord>(records, rows.MoreRecords, rows.PagingCookie);
        }
    }
}
