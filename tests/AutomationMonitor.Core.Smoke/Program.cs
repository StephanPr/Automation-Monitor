using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

// No test framework packages required for the first Linux smoke test.
var runs = new[]
{
    new RunInfo { Provider = "PowerAutomate", RunId = "1", Status = RunStatus.Failed },
    new RunInfo { Provider = "LogicApps", RunId = "2", Status = RunStatus.Succeeded },
    new RunInfo { Provider = "LogicApps", RunId = "3", Status = RunStatus.Running }
};

var summary = RunSummary.FromRuns(runs);
if (summary.Total != 3 || summary.Failed != 1 || summary.Succeeded != 1 || summary.Running != 1)
{
    throw new Exception("Run summary smoke test failed.");
}

Console.WriteLine("PASS: cross-platform monitoring core smoke test");
