using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;

namespace AutomationMonitor.Core.Tests;

/// <summary>Deterministic time so "last checked" is assertable.</summary>
internal sealed class FixedClock : IClock
{
    public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get { return Now; } }
}

/// <summary>
/// Canned rows behind the Dataverse seam. Stands in for the plugin's SDK gateway so discovery
/// can be verified for paging, coverage and failure handling without a live tenant.
/// </summary>
internal sealed class FakeRowSource : IDataverseRowSource
{
    public List<Page<SolutionReference>> SolutionPages { get; } = new List<Page<SolutionReference>>();
    public List<Page<RawWorkflowRecord>> WorkflowPages { get; } = new List<Page<RawWorkflowRecord>>();
    public Exception? ThrowForSolutions { get; set; }
    public Exception? ThrowForWorkflows { get; set; }
    public List<PageRequest> SolutionRequests { get; } = new List<PageRequest>();
    public List<PageRequest> WorkflowRequests { get; } = new List<PageRequest>();
    public int SolutionCalls { get; private set; }
    public int WorkflowCalls { get; private set; }
    public string? ObservedSolutionId { get; private set; }
    public string? EnvironmentId { get; set; } = "55555555-5555-5555-5555-555555555555";

    public Page<SolutionReference>? ReadSolutions(PageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SolutionCalls++;
        SolutionRequests.Add(request);
        if (ThrowForSolutions != null)
        {
            throw ThrowForSolutions;
        }

        return request.PageNumber <= SolutionPages.Count ? SolutionPages[request.PageNumber - 1] : null;
    }

    public Page<RawWorkflowRecord>? ReadSolutionWorkflows(string solutionId, PageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkflowCalls++;
        WorkflowRequests.Add(request);
        ObservedSolutionId = solutionId;
        if (ThrowForWorkflows != null)
        {
            throw ThrowForWorkflows;
        }

        return request.PageNumber <= WorkflowPages.Count ? WorkflowPages[request.PageNumber - 1] : null;
    }

    public static Page<T> Of<T>(bool moreRecords, string? cookie, params T[] items)
    {
        return new Page<T>(new List<T>(items), moreRecords, cookie);
    }

    public static SolutionReference Solution(string id, string displayName)
    {
        return new SolutionReference(id, "publisher." + id, displayName, "1.0.0.0", false);
    }

    public static RawWorkflowRecord Row(string? id, int? category, string name = "flow")
    {
        return new RawWorkflowRecord { Id = id, RawCategory = category, Name = name, RawStateCode = 1 };
    }
}
