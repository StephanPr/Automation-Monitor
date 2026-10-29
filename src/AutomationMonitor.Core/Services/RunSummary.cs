using System;
using System.Collections.Generic;
using System.Linq;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    public sealed class RunSummary
    {
        public int Total { get; private set; }
        public int Succeeded { get; private set; }
        public int Failed { get; private set; }
        public int Running { get; private set; }

        public static RunSummary FromRuns(IEnumerable<RunInfo> runs)
        {
            if (runs == null) throw new ArgumentNullException(nameof(runs));
            var list = runs.ToList();
            return new RunSummary
            {
                Total = list.Count,
                Succeeded = list.Count(r => r.Status == RunStatus.Succeeded),
                Failed = list.Count(r => r.Status == RunStatus.Failed),
                Running = list.Count(r => r.Status == RunStatus.Running)
            };
        }
    }
}
