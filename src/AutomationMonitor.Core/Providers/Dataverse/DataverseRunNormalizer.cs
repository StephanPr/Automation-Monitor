using System;
using AutomationMonitor.Core.Models;
using AutomationMonitor.Core.Services;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// What happened to one <c>flowrun</c> row. A row that could not be used is counted and
    /// reported, never dropped in silence and never normalised under an invented identity.
    /// </summary>
    public enum RunRowDisposition
    {
        /// <summary>Turned into a <see cref="WorkflowRun"/>.</summary>
        Normalized = 0,

        /// <summary>No run id and no row id: the row cannot carry identity.</summary>
        MissingIdentity = 1,

        /// <summary>The row names a different flow than the one that was asked for.</summary>
        BelongsToAnotherWorkflow = 2
    }

    /// <summary>
    /// The one place a Dataverse <c>flowrun</c> row becomes a normalized
    /// <see cref="WorkflowRun"/>, the run counterpart of <see cref="DataverseWorkflowNormalizer"/>.
    /// Callers must not re-interpret status text, durations or flags themselves.
    ///
    /// <para>
    /// Identity rule: <c>parentrunid</c> refers to a run by its Logic Apps run name, which is the
    /// <c>name</c> column - not the GUID of the Dataverse row. So the model keys on the run name
    /// when the row carries one and only falls back to the row id, while both values stay on the
    /// record for deep links and support. Status text is free-form (String, RequiredLevel None), so
    /// absent or unrecognised text is <see cref="RunStatus.Unknown"/> and stays visible.
    /// </para>
    /// </summary>
    public static class DataverseRunNormalizer
    {
        /// <summary>
        /// Ceiling on the error description kept in memory. The column is a 20 000 character memo;
        /// a grid or an export never needs that much, and holding every row's full payload turns a
        /// refresh of a failure-heavy window into a memory problem.
        /// </summary>
        public const int MaxErrorSummaryLength = 2000;

        /// <summary>Group a <c>flowrun.status</c> value. Shared vocabulary, one table for all providers.</summary>
        public static RunStatus NormalizeStatus(string? providerStatus)
        {
            return StatusNormalizer.Normalize(providerStatus);
        }

        /// <summary>
        /// Read the primary-run marker, which is a <c>Picklist</c> (0 false, 1 true) rather than a
        /// Boolean. A Boolean from another reader is honoured; anything unrecognised stays null,
        /// because "the source did not say" is not the same as "the source said no".
        /// </summary>
        public static bool? NormalizePrimaryFlag(int? primaryRunCode, bool? primaryRunFlag)
        {
            if (primaryRunFlag.HasValue)
            {
                return primaryRunFlag.Value;
            }

            if (!primaryRunCode.HasValue)
            {
                return null;
            }

            if (primaryRunCode.Value == DataverseSchema.IsPrimaryValueTrue)
            {
                return true;
            }

            return primaryRunCode.Value == DataverseSchema.IsPrimaryValueFalse ? false : (bool?)null;
        }

        /// <summary>
        /// Whether the row belongs to the flow that was asked for. A blank <c>workflowid</c> is
        /// accepted - the server-side filter already restricted the read, and an absent column is
        /// not evidence of a different flow. A value naming another flow is.
        /// </summary>
        public static bool BelongsToWorkflow(RawRunRecord? record, WorkflowKey requested)
        {
            if (requested == null)
            {
                throw new ArgumentNullException(nameof(requested));
            }

            if (record == null || string.IsNullOrWhiteSpace(record.WorkflowId))
            {
                return true;
            }

            return string.Equals(CanonicalId.Identifier(record.WorkflowId), requested.WorkflowId, StringComparison.Ordinal);
        }

        /// <summary>Trim an error description to a usable size without rewriting its content.</summary>
        public static string? TruncateError(string? errorText)
        {
            if (string.IsNullOrWhiteSpace(errorText))
            {
                return null;
            }

            string text = errorText!.Trim();
            return text.Length <= MaxErrorSummaryLength ? text : text.Substring(0, MaxErrorSummaryLength) + "...";
        }
        /// <summary>
        /// Build one normalized run from one row. The requested workflow supplies the identity
        /// scope, so a row can never land under a key nobody asked for.
        /// </summary>
        public static RunRowDisposition TryToRun(
            RawRunRecord? record,
            WorkflowKey requested,
            string? workflowName,
            string? dataSource,
            DateTimeOffset checkedAtUtc,
            ProviderResultState coverageState,
            out WorkflowRun? run)
        {
            run = null;
            if (requested == null)
            {
                throw new ArgumentNullException(nameof(requested));
            }

            if (record == null)
            {
                return RunRowDisposition.MissingIdentity;
            }

            if (!BelongsToWorkflow(record, requested))
            {
                return RunRowDisposition.BelongsToAnotherWorkflow;
            }

            // The run name is what parentrunid points at, so it wins; the row id is the fallback
            // and is kept on the record either way for links and support.
            string? runId = string.IsNullOrWhiteSpace(record.RunId) ? record.RunRecordId : record.RunId;
            RunKey? key;
            if (!RunKey.TryCreate(requested, runId, out key))
            {
                return RunRowDisposition.MissingIdentity;
            }

            run = new WorkflowRun(
                key,
                NormalizeStatus(record.RawStatus),
                record.RawStatus,
                record.StartedAtUtc,
                record.EndedAtUtc,
                record.ReportedDurationMs,
                dataSource,
                checkedAtUtc,
                coverageState,
                runRecordId: record.RunRecordId,
                workflowName: workflowName,
                errorCode: record.ErrorCode,
                errorSummary: TruncateError(record.ErrorText),
                parentRunId: record.ParentRunId,
                correlationId: record.CorrelationId,
                isPrimaryRun: NormalizePrimaryFlag(record.PrimaryRunCode, record.PrimaryRunFlag),
                triggerType: record.TriggerType);

            return RunRowDisposition.Normalized;
        }
    }
}

