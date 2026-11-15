using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// Canned answers behind the workflow-identity seam. Like every other fake here, it stands at the boundary and
/// not inside the logic: batching, paging, ceiling behaviour, identity tallies and the evidence classification
/// under test are all the core's own.
/// </summary>
internal sealed class FakeWorkflowIdentitySource : IWorkflowIdentityRowSource
{
    /// <summary>The id batches the probe actually sent, in order.</summary>
    public List<IReadOnlyList<string>> Batches { get; } = new List<IReadOnlyList<string>>();

    public List<PageRequest> Requests { get; } = new List<PageRequest>();

    public int Calls { get; private set; }

    /// <summary>Rows returned for a batch. Null means "the source returned no rows".</summary>
    public Func<IReadOnlyList<string>, IEnumerable<RawWorkflowRecord>>? Respond { get; set; }

    /// <summary>True to report more records on every page, so a read has to stop at its ceiling.</summary>
    public bool ClaimsMoreRecords { get; set; }

    public Exception? Throw { get; set; }

    public Page<RawWorkflowRecord>? ReadWorkflowsByIds(IReadOnlyList<string> workflowIds, PageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        Batches.Add(workflowIds);
        Requests.Add(request);
        if (Throw != null)
        {
            throw Throw;
        }

        List<RawWorkflowRecord> rows = new List<RawWorkflowRecord>();
        if (Respond != null)
        {
            rows.AddRange(Respond(workflowIds));
        }

        // Every page promises another, which is the shape of a source that keeps claiming without finishing -
        // exactly what an incomplete read means.
        string? cookie = ClaimsMoreRecords ? "cookie-" + request.PageNumber.ToString(CultureInfo.InvariantCulture) : null;
        return new Page<RawWorkflowRecord>(rows, ClaimsMoreRecords, cookie);
    }

    /// <summary>A row carrying one workflow id, as the gateway would hand it over.</summary>
    public static RawWorkflowRecord Row(string? id)
    {
        return new RawWorkflowRecord
        {
            Id = id,
            IdSource = id == null ? WorkflowIdSource.NotStated : WorkflowIdSource.IdColumn
        };
    }

    /// <summary>A workflow key in the shape a saved Dataverse member has.</summary>
    public static WorkflowKey Key(string workflowId, string? scope = null)
    {
        return WorkflowKey.Create(ProviderKind.PowerAutomate, scope, workflowId);
    }
}
