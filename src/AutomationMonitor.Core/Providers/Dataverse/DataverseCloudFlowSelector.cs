using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// Categorises and de-duplicates the workflow rows found through a solution's components.
    /// Identity is the provider-scoped key, never the display name, so a flow that appears in
    /// several component rows collapses to exactly one entry.
    /// </summary>
    public static class DataverseCloudFlowSelector
    {
        public static CloudFlowSelectionResult Select(IEnumerable<RawWorkflowRecord>? records, string? environmentId)
        {
            List<WorkflowReference> flows = new List<WorkflowReference>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int rowsRead = 0;
            int duplicates = 0;
            int nonCloud = 0;
            int unknownCategory = 0;
            int missingId = 0;

            if (records != null)
            {
                foreach (RawWorkflowRecord record in records)
                {
                    rowsRead++;

                    if (record == null)
                    {
                        missingId++;
                        continue;
                    }

                    WorkflowKey? probe;
                    if (!WorkflowKey.TryCreate(ProviderKind.PowerAutomate, environmentId, record.Id, out probe))
                    {
                        missingId++;
                        continue;
                    }

                    if (!seen.Add(probe.Canonical))
                    {
                        duplicates++;
                        continue;
                    }

                    if (!record.RawCategory.HasValue)
                    {
                        unknownCategory++;
                        continue;
                    }

                    if (!DataverseWorkflowNormalizer.IsCloudFlow(record))
                    {
                        nonCloud++;
                        continue;
                    }

                    WorkflowReference? reference;
                    if (DataverseWorkflowNormalizer.TryToReference(record, environmentId, out reference))
                    {
                        flows.Add(reference);
                    }
                    else
                    {
                        missingId++;
                    }
                }
            }

            flows.Sort(delegate(WorkflowReference left, WorkflowReference right)
            {
                int byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
                if (byName != 0)
                {
                    return byName;
                }

                return string.Compare(left.WorkflowId, right.WorkflowId, StringComparison.Ordinal);
            });

            return new CloudFlowSelectionResult(flows, rowsRead, duplicates, nonCloud, unknownCategory, missingId);
        }
    }
}
