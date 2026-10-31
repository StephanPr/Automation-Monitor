using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Providers.Dataverse;
using AutomationMonitor.Core.Services;

// No test framework packages required: this stays the dependency-light floor that proves the run
// model, its normalization, the merge rule and correlation on any machine. The detailed coverage
// lives in tests/AutomationMonitor.Core.Tests.
const string Env = "11111111-1111-1111-1111-111111111111";
const string Flow = "22222222-2222-2222-2222-222222222222";
WorkflowKey flowKey = WorkflowKey.Create(ProviderKind.PowerAutomate, Env, Flow);
DateTimeOffset start = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

// 1. Run identity is provider + environment + workflow + run id, never a name.
RunKey left = RunKey.Create(flowKey, "08583000124706209406282721132CU56");
RunKey sameSpelling = RunKey.Create(flowKey, "08583000124706209406282721132cu56");
RunKey otherEnvironment = RunKey.Create(
    WorkflowKey.Create(ProviderKind.PowerAutomate, "33333333-3333-3333-3333-333333333333", Flow), "08583000124706209406282721132cu56");
if (!left.Equals(sameSpelling) || left.Equals(otherEnvironment))
{
    throw new Exception("Run identity rules failed.");
}

if (!left.Canonical.StartsWith("powerautomate:", StringComparison.Ordinal))
{
    throw new Exception("Canonical run key must carry the provider namespace: " + left.Canonical);
}

// 2. Status grouping keeps the raw value and never treats an unknown status as success.
if (DataverseRunNormalizer.NormalizeStatus("Succeeded") != RunStatus.Succeeded
    || DataverseRunNormalizer.NormalizeStatus("Waiting") != RunStatus.Running
    || DataverseRunNormalizer.NormalizeStatus("TimedOut") != RunStatus.TimedOut
    || DataverseRunNormalizer.NormalizeStatus("Faulted") != RunStatus.Failed)
{
    throw new Exception("Status normalization failed.");
}

if (DataverseRunNormalizer.NormalizeStatus("Teleported") != RunStatus.Unknown
    || DataverseRunNormalizer.NormalizeStatus(null) != RunStatus.Unknown
    || DataverseRunNormalizer.NormalizeStatus("   ") != RunStatus.Unknown)
{
    throw new Exception("An unrecognised or absent status must stay unknown, never become a success.");
}

// 3. The primary-run marker is a Picklist, not a Boolean.
if (DataverseRunNormalizer.NormalizePrimaryFlag(1, null) != true
    || DataverseRunNormalizer.NormalizePrimaryFlag(0, null) != false
    || DataverseRunNormalizer.NormalizePrimaryFlag(null, null) != null
    || DataverseRunNormalizer.NormalizePrimaryFlag(7, null) != null)
{
    throw new Exception("Primary-run picklist reading failed.");
}

// 4. Duration: measured from the stored UTC instants, with the source's own value kept beside it.
RawRunRecord row = new RawRunRecord
{
    RunRecordId = "44444444-4444-4444-4444-444444444444",
    RunId = "08583000124706209406282721132cu56",
    WorkflowId = Flow,
    RawStatus = "Succeeded",
    StartedAtUtc = start,
    EndedAtUtc = start.AddSeconds(90),
    ReportedDurationMs = 90000,
    PrimaryRunCode = 1
};

WorkflowRun? ok;
if (DataverseRunNormalizer.TryToRun(row, flowKey, "Approvals", DataverseFlowRunSource.SourceName, start, ProviderResultState.Complete, out ok)
        != RunRowDisposition.Normalized || ok == null)
{
    throw new Exception("Run normalization failed.");
}

if (ok.DurationMs != 90000 || ok.DurationBasis != RunDurationBasis.Measured || ok.HasTimingGap || ok.WorkflowName != "Approvals")
{
    throw new Exception("Duration or provenance failed: " + ok.DurationMs + " / " + ok.DurationBasis + " / " + ok.WorkflowName);
}

WorkflowRun? mismatched;
DataverseRunNormalizer.TryToRun(
    new RawRunRecord { RunId = "duration-mismatch", WorkflowId = Flow, RawStatus = "Succeeded", StartedAtUtc = start, EndedAtUtc = start.AddSeconds(90), ReportedDurationMs = 120000 },
    flowKey, null, DataverseFlowRunSource.SourceName, start, ProviderResultState.Complete, out mismatched);
if (mismatched == null || !mismatched.DurationConflictsWithTimestamps || mismatched.DurationMs != 90000)
{
    throw new Exception("A source duration that disagrees with its own timestamps must be reported, not hidden.");
}

// 5. A terminal row with no end instant is a gap, never a zero duration.
WorkflowRun? gap;
DataverseRunNormalizer.TryToRun(
    new RawRunRecord { RunId = "run-gap", WorkflowId = Flow, RawStatus = "Failed", StartedAtUtc = start },
    flowKey, null, DataverseFlowRunSource.SourceName, start, ProviderResultState.Complete, out gap);
if (gap == null || gap.DurationMs != null || gap.IsCompleteOutcome || !gap.HasTimingGap)
{
    throw new Exception("A missing end time must be reported, not zeroed.");
}

// 6. A running row has no stored duration but does have an elapsed time against a given instant.
WorkflowRun? live;
DataverseRunNormalizer.TryToRun(
    new RawRunRecord { RunId = "run-live", WorkflowId = Flow, RawStatus = "Running", StartedAtUtc = start },
    flowKey, null, DataverseFlowRunSource.SourceName, start, ProviderResultState.Complete, out live);
if (live == null || live.DurationMs != null || !live.IsRunning || live.ElapsedMsAt(start.AddMinutes(3)) != 180000)
{
    throw new Exception("A running run must report elapsed time, never zero.");
}

// 7. Merging collapses repeat observations of one identity onto the honest terminal status.
IReadOnlyList<WorkflowRun> merged = RunQueryService.MergeRuns(new[]
{
    new WorkflowRun(left, RunStatus.Running, "Running", start, null, null, "test", start.AddMinutes(1), ProviderResultState.Partial),
    new WorkflowRun(left, RunStatus.Succeeded, "Succeeded", start, start.AddMinutes(2), 120000, "test", start.AddMinutes(5), ProviderResultState.Complete)
});
if (merged.Count != 1 || merged[0].Status != RunStatus.Succeeded)
{
    throw new Exception("Duplicate run observations must collapse to the terminal one.");
}

// 8. Correlation links on the recorded parent id only, and a cycle surfaces instead of hanging.
WorkflowRun parent = new WorkflowRun(
    RunKey.Create(flowKey, "parent-run"), RunStatus.Succeeded, "Succeeded", start, start.AddMinutes(1), 60000, "test", start, ProviderResultState.Complete);
WorkflowRun child = new WorkflowRun(
    RunKey.Create(flowKey, "child-run"), RunStatus.Failed, "Failed", start.AddSeconds(30), start.AddMinutes(1), 30000, "test", start, ProviderResultState.Complete,
    parentRunId: "parent-run");
WorkflowRun stranger = new WorkflowRun(
    RunKey.Create(flowKey, "stranger-run"), RunStatus.Succeeded, "Succeeded", start, start.AddMinutes(1), 1000, "test", start, ProviderResultState.Complete);

RunCorrelationResult trees = RunCorrelationResolver.Resolve(new[] { child, parent, stranger });
if (trees.Roots.Count != 2 || trees.LinkedRunCount != 1 || trees.Roots[0].DescendantCount != 1)
{
    throw new Exception("Parent/child resolution failed: " + trees.DescribeCoverage());
}

WorkflowRun cycleA = new WorkflowRun(
    RunKey.Create(flowKey, "cyclic-a"), RunStatus.Running, "Running", start, null, null, "test", start, ProviderResultState.Complete,
    parentRunId: "cyclic-b");
WorkflowRun cycleB = new WorkflowRun(
    RunKey.Create(flowKey, "cyclic-b"), RunStatus.Running, "Running", start, null, null, "test", start, ProviderResultState.Complete,
    parentRunId: "cyclic-a");
RunCorrelationResult broken = RunCorrelationResolver.Resolve(new[] { cycleA, cycleB });
if (broken.Roots.Count != 2 || broken.CyclicRunIds.Count != 2)
{
    throw new Exception("A parent cycle must surface as reported roots, not as a hang: " + broken.DescribeCoverage());
}

// 9. Counters state their own denominator, and a running row is not a failure.
RunSummary summary = RunSummary.FromRuns(new[] { parent, child, live }, true);
if (summary.TotalRuns != 3 || summary.Succeeded != 1 || summary.Failed != 1 || summary.Running != 1
    || summary.CompletedRuns != 2 || summary.FailureRate != 0.5d || !summary.IsPartial)
{
    throw new Exception("Summary or failure-rate denominator failed: " + summary);
}

Console.WriteLine("PASS: run history model, normalization, merge and correlation smoke test");


