using System;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// The one place a Dataverse workflow row becomes a normalized <see cref="WorkflowReference"/>.
    /// Callers must not re-interpret raw codes themselves. Unknown values stay unknown instead of
    /// being coerced to a plausible default, and the raw codes are always carried through.
    /// </summary>
    public static class DataverseWorkflowNormalizer
    {
        /// <summary>True when the row is a modern cloud flow rather than a classic workflow.</summary>
        public static bool IsCloudFlow(RawWorkflowRecord? record)
        {
            return record != null && record.RawCategory == DataverseSchema.WorkflowCategoryCloudFlow;
        }


        public static WorkflowState NormalizeState(int? rawStateCode)
        {
            if (!rawStateCode.HasValue)
            {
                return WorkflowState.Unknown;
            }

            switch (rawStateCode.Value)
            {
                case DataverseSchema.StateCodeEnabled:
                    return WorkflowState.Enabled;
                case DataverseSchema.StateCodeDisabled:
                    return WorkflowState.Disabled;
                default:
                    return WorkflowState.Unknown;
            }
        }

        /// <summary>
        /// Builds the normalized reference. Returns false when the row cannot carry an identity
        /// (no provider id): such a row must never enter a watchlist under a made-up key.
        /// </summary>
        public static bool TryToReference(RawWorkflowRecord? record, string? environmentId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WorkflowReference? reference)
        {
            reference = null;
            if (record == null)
            {
                return false;
            }

            WorkflowKey? key;
            if (!WorkflowKey.TryCreate(ProviderKind.PowerAutomate, environmentId, record.Id, out key))
            {
                return false;
            }

            reference = new WorkflowReference(
                key,
                record.Name,
                NormalizeState(record.RawStateCode),
                record.RawStateCode,
                record.RawStatusCode,
                record.RawCategory,
                record.Description,
                record.CreatedOnUtc,
                record.ModifiedOnUtc);

            return true;
        }
    }
}
