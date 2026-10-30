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
    public sealed class DataverseGateway : IDataverseRowSource
    {
        private readonly IOrganizationService service;

        public DataverseGateway(IOrganizationService service, string? environmentId)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            this.service = service;
            EnvironmentId = string.IsNullOrWhiteSpace(environmentId) ? null : environmentId!.Trim();
        }

        /// <summary>Environment id from the XrmToolBox connection; null when it is not reported.</summary>
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
            query.ColumnSet = new ColumnSet(
                DataverseSchema.WorkflowName,
                DataverseSchema.WorkflowCategory,
                DataverseSchema.WorkflowStateCode,
                DataverseSchema.WorkflowStatusCode,
                DataverseSchema.WorkflowPrimaryCategory,
                DataverseSchema.WorkflowSecondaryCategory,
                DataverseSchema.WorkflowDescription,
                DataverseSchema.WorkflowCreatedOn,
                DataverseSchema.WorkflowModifiedOn);
            query.Distinct = true;
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
                records.Add(new RawWorkflowRecord
                {
                    Id = entity.Id == Guid.Empty ? null : entity.Id.ToString("D"),
                    Name = GetString(entity, DataverseSchema.WorkflowName),
                    RawCategory = GetInt(entity, DataverseSchema.WorkflowCategory),
                    RawStateCode = GetInt(entity, DataverseSchema.WorkflowStateCode),
                    RawStatusCode = GetInt(entity, DataverseSchema.WorkflowStatusCode),
                    RawPrimaryCategory = GetInt(entity, DataverseSchema.WorkflowPrimaryCategory),
                    RawSecondaryCategory = GetInt(entity, DataverseSchema.WorkflowSecondaryCategory),
                    Description = GetString(entity, DataverseSchema.WorkflowDescription),
                    CreatedOnUtc = GetUtc(entity, DataverseSchema.WorkflowCreatedOn),
                    ModifiedOnUtc = GetUtc(entity, DataverseSchema.WorkflowModifiedOn)
                });
            }

            return new Page<RawWorkflowRecord>(records, rows.MoreRecords, rows.PagingCookie);
        }

        private static void ApplyPaging(QueryExpression query, PageRequest request)
        {
            query.PageInfo = new PagingInfo
            {
                Count = PageReader.DefaultPageSize,
                PageNumber = request.PageNumber
            };

            if (!string.IsNullOrEmpty(request.Cookie))
            {
                query.PageInfo.PagingCookie = request.Cookie;
            }
        }
        /// <summary>
        /// Attribute readers below tolerate the shapes Dataverse actually returns: option sets for
        /// choice columns, and a missing column as null rather than a fabricated default.
        /// </summary>
        private static string? GetString(Entity entity, string attribute)
        {
            object? raw = Unwrap(entity, attribute);
            string? text = raw as string;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static int? GetInt(Entity entity, string attribute)
        {
            object? raw = Unwrap(entity, attribute);
            OptionSetValue? option = raw as OptionSetValue;
            if (option != null)
            {
                return option.Value;
            }

            if (raw is int)
            {
                return (int)raw;
            }

            if (raw is long)
            {
                return checked((int)(long)raw);
            }

            return null;
        }

        private static bool? GetBool(Entity entity, string attribute)
        {
            object? raw = Unwrap(entity, attribute);
            if (raw is bool)
            {
                return (bool)raw;
            }

            return null;
        }

        private static DateTimeOffset? GetUtc(Entity entity, string attribute)
        {
            object? raw = Unwrap(entity, attribute);
            if (!(raw is DateTime))
            {
                return null;
            }

            // Dataverse persists these instants in UTC and hands them back with Kind Unspecified.
            return new DateTimeOffset(DateTime.SpecifyKind((DateTime)raw, DateTimeKind.Utc));
        }

        private static object? Unwrap(Entity entity, string attribute)
        {
            if (!entity.Contains(attribute))
            {
                return null;
            }

            AliasedValue? aliased = entity.GetAttributeValue<AliasedValue>(attribute);
            return aliased == null ? entity[attribute] : aliased.Value;
        }
    }
}
