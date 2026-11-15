using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// The Dataverse service seam as the plugin really calls it, for tests that must see a real
/// <see cref="QueryExpression"/> rather than a core fake. It records every query and answers with canned
/// rows; every write and every <c>Execute</c> throws, so a test of a read path cannot quietly turn into a
/// write, and an <c>Execute</c> here would also break the read-only rule this plugin is built on.
/// </summary>
internal sealed class RecordingOrganizationService : IOrganizationService
{
    public List<QueryExpression> Queries { get; } = new List<QueryExpression>();

    public int Calls { get; private set; }

    public Func<QueryExpression, EntityCollection>? Respond { get; set; }

    /// <summary>The one query this test expected, failing loudly if the read sent more or fewer.</summary>
    public QueryExpression SingleQuery
    {
        get
        {
            Xunit.Assert.Single(Queries);
            return Queries[0];
        }
    }

    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        Calls++;
        QueryExpression expression = (QueryExpression)query;
        Queries.Add(expression);
        return Respond == null ? new EntityCollection() : Respond(expression);
    }

    public OrganizationResponse Execute(OrganizationRequest request)
    {
        throw new NotSupportedException("the workflow-identity read is RetrieveMultiple only");
    }

    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
    {
        throw new NotSupportedException("this plugin never writes");
    }

    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
    {
        throw new NotSupportedException("this plugin never writes");
    }

    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    {
        throw new NotSupportedException("the workflow-identity read is RetrieveMultiple only");
    }

    public Guid Create(Entity entity)
    {
        throw new NotSupportedException("this plugin never writes");
    }

    public void Update(Entity entity)
    {
        throw new NotSupportedException("this plugin never writes");
    }

    public void Delete(string entityName, Guid id)
    {
        throw new NotSupportedException("this plugin never writes");
    }
}
