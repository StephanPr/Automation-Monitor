using System;
using System.Collections.Generic;
using System.Threading;
using AutomationMonitor.Core.Abstractions;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;

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

/// <summary>
/// Canned <c>flowrun</c> pages behind the run seam. Faking the boundary and not the logic: every
/// paging, coverage, identity and status decision under test is the core's own.
/// </summary>
internal sealed class FakeRunRowSource : IDataverseRunRowSource
{
    public const string Environment = "55555555-5555-5555-5555-555555555555";
    public const string Workflow = "22222222-2222-2222-2222-222222222222";

    private readonly List<Page<RawRunRecord>> pages = new List<Page<RawRunRecord>>();

    public string? EnvironmentId { get; set; } = Environment;

    public Exception? ThrowOnRead { get; set; }

    public List<RunQueryCriteria> ObservedCriteria { get; } = new List<RunQueryCriteria>();

    public List<PageRequest> ObservedRequests { get; } = new List<PageRequest>();

    public int Calls { get; private set; }

    public void AddPage(params RawRunRecord[] items)
    {
        pages.Add(new Page<RawRunRecord>(new List<RawRunRecord>(items), false, null));
    }

    public void AddPage(bool moreRecords, string? cookie, params RawRunRecord[] items)
    {
        pages.Add(new Page<RawRunRecord>(new List<RawRunRecord>(items), moreRecords, cookie));
    }

    public Page<RawRunRecord>? ReadFlowRuns(RunQueryCriteria criteria, PageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        ObservedCriteria.Add(criteria);
        ObservedRequests.Add(request);
        if (ThrowOnRead != null)
        {
            throw ThrowOnRead;
        }

        return request.PageNumber <= pages.Count ? pages[request.PageNumber - 1] : null;
    }

    public static WorkflowKey Key(string workflowId = Workflow, string? environmentId = Environment, ProviderKind provider = ProviderKind.PowerAutomate)
    {
        return WorkflowKey.Create(provider, environmentId, workflowId);
    }

    /// <summary>One run row as Dataverse would return it, with the fields a test does not name left absent.</summary>
    public static RawRunRecord RunRow(
        string? runId,
        string? status = "Succeeded",
        DateTimeOffset? startedUtc = null,
        DateTimeOffset? endedUtc = null,
        long? durationMs = null,
        string? workflowId = Workflow,
        string? rowId = null,
        string? parentRunId = null)
    {
        return new RawRunRecord
        {
            RunId = runId,
            RunRecordId = rowId,
            WorkflowId = workflowId,
            RawStatus = status,
            StartedAtUtc = startedUtc ?? new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero),
            EndedAtUtc = endedUtc,
            ReportedDurationMs = durationMs,
            ParentRunId = parentRunId
        };
    }
}

/// <summary>
/// A run provider that answers from a canned table, so the query service can be tested across many
/// workflows and many coverage states with no Dataverse type in sight.
/// </summary>
internal sealed class FakeRunSource : IWorkflowRunSource
{
    private readonly Dictionary<string, ProviderResult<IReadOnlyList<WorkflowRun>>> results =
        new Dictionary<string, ProviderResult<IReadOnlyList<WorkflowRun>>>(StringComparer.Ordinal);

    public FakeRunSource()
    {
        Provider = ProviderKind.PowerAutomate;
        ScopeId = FakeRunRowSource.Environment;
        UnlistedState = ProviderResultState.Complete;
    }

    public ProviderKind Provider { get; set; }

    public string? ScopeId { get; set; }

    /// <summary>What a workflow with no canned answer gets. Complete-and-empty unless a test says so.</summary>
    public ProviderResultState UnlistedState { get; set; }

    public List<WorkflowKey> Requested { get; } = new List<WorkflowKey>();

    public List<MonitoringQuery> ObservedQueries { get; } = new List<MonitoringQuery>();

    /// <summary>Hook so a test can cancel mid-refresh, the way the host would.</summary>
    public Action<WorkflowKey>? OnRead { get; set; }

    /// <summary>When true, every read throws OperationCanceledException like a stopped worker.</summary>
    public bool ThrowCancellation { get; set; }

    public void Supply(WorkflowKey workflow, params WorkflowRun[] runs)
    {
        Supply(workflow, ProviderResult<IReadOnlyList<WorkflowRun>>.Complete(
            new List<WorkflowRun>(runs),
            1,
            runs.Length,
            new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero)));
    }

    public void Supply(WorkflowKey workflow, ProviderResult<IReadOnlyList<WorkflowRun>> result)
    {
        results[workflow.Canonical] = result;
    }

    public ProviderResult<IReadOnlyList<WorkflowRun>> GetRuns(WorkflowKey workflow, MonitoringQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requested.Add(workflow);
        ObservedQueries.Add(query);
        if (OnRead != null)
        {
            OnRead(workflow);
        }

        if (ThrowCancellation)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        ProviderResult<IReadOnlyList<WorkflowRun>> canned;
        if (results.TryGetValue(workflow.Canonical, out canned))
        {
            return canned;
        }

        DateTimeOffset checkedAtUtc = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        IReadOnlyList<WorkflowRun> none = new List<WorkflowRun>();
        switch (UnlistedState)
        {
            case ProviderResultState.Complete:
                return ProviderResult<IReadOnlyList<WorkflowRun>>.Complete(none, 0, 0, checkedAtUtc);
            case ProviderResultState.Partial:
                return ProviderResult<IReadOnlyList<WorkflowRun>>.Partial(none, 1, 0, checkedAtUtc, "canned pages exhausted");
            case ProviderResultState.Unauthorized:
                return ProviderResult<IReadOnlyList<WorkflowRun>>.Unauthorized(0, checkedAtUtc, "no canned answer for " + workflow.Canonical + ".");
            case ProviderResultState.Unsupported:
                return ProviderResult<IReadOnlyList<WorkflowRun>>.Unsupported(checkedAtUtc, "no canned answer for " + workflow.Canonical + ".");
            case ProviderResultState.Stale:
                return ProviderResult<IReadOnlyList<WorkflowRun>>.Stale(none, 0, 0, checkedAtUtc, "canned answer is stale");
            default:
                return ProviderResult<IReadOnlyList<WorkflowRun>>.Unavailable(0, checkedAtUtc, "no canned answer for " + workflow.Canonical + ".");
        }
    }
}

/// <summary>Builds run records without repeating the nine required constructor arguments per test.</summary>
internal static class RunBuilder
{
    /// <summary>The instant a row is stamped with when a test does not care about its timing.</summary>
    public static readonly DateTimeOffset DefaultStart = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    public static WorkflowRun Create(
        string runId,
        RunStatus status = RunStatus.Succeeded,
        string? rawStatus = null,
        DateTimeOffset? startedUtc = null,
        DateTimeOffset? endedUtc = null,
        long? durationMs = null,
        WorkflowKey? workflow = null,
        string? parentRunId = null,
        DateTimeOffset? checkedAtUtc = null,
        ProviderResultState coverageState = ProviderResultState.Complete,
        string? correlationId = null)
    {
        RunKey key = RunKey.Create(workflow ?? FakeRunRowSource.Key(), runId);
        return new WorkflowRun(
            key,
            status,
            rawStatus ?? status.ToString(),
            startedUtc ?? DefaultStart,
            endedUtc,
            durationMs,
            "test source",
            checkedAtUtc ?? new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero),
            coverageState,
            parentRunId: parentRunId,
            correlationId: correlationId);
    }

    /// <summary>
    /// A row the source gave no instants for at all. Separate because <see cref="Create"/> defaults
    /// its start: passing null there means "do not care", not "the source was silent".
    /// </summary>
    public static WorkflowRun WithoutInstants(
        string runId,
        RunStatus status = RunStatus.Succeeded,
        WorkflowKey? workflow = null,
        string? parentRunId = null)
    {
        RunKey key = RunKey.Create(workflow ?? FakeRunRowSource.Key(), runId);
        return new WorkflowRun(
            key,
            status,
            status.ToString(),
            null,
            null,
            null,
            "test source",
            new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero),
            ProviderResultState.Complete,
            parentRunId: parentRunId);
    }
}


