using System;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// The single rule that decides which value is a workflow row's identity.
    ///
    /// <para>
    /// The gateway hands over the two candidates it can see - the value of the projected
    /// <c>workflowid</c> column (already reduced to a plain CLR value, plus whether it arrived aliased)
    /// and the SDK's row id - and this is the only place that chooses between them. The projection
    /// deliberately names the column because a query is allowed to suppress primary keys: Microsoft Learn
    /// states for <c>QueryExpression.Distinct</c> that "the results returned don't include primary key
    /// values for each record", which is how the first live session read 59 rows of a solution and lost
    /// every one of them at identity validation. The documented primary key of the workflow table is
    /// <c>workflowid</c>, so that is the authority; <c>Entity.Id</c> stays a fallback for a reader that
    /// legitimately has no such column to project.
    /// </para>
    /// <para>
    /// An all-zero <see cref="Guid"/> states nothing and is never turned into an identity, and two sources
    /// that disagree refuse the row rather than pick one: identity keys merging, de-duplication, run
    /// filtering and saved watchlists, so a guessed id is a wrong watchlist, not a cosmetic defect.
    /// </para>
    /// </summary>
    public static class WorkflowIdentity
    {
        /// <summary>
        /// Resolves the provider id for one row. Returns false - with <paramref name="id"/> null - when the
        /// row cannot carry identity; the caller must then drop the row and count it, never substitute a
        /// name, a component id or a generated value.
        /// </summary>
        /// <param name="projectedValue">
        /// Value of <see cref="DataverseSchema.WorkflowId"/> as the SDK adapter unwrapped it: null when the
        /// environment did not return the column at all.
        /// </param>
        /// <param name="rowId">The reader's row id, <c>Guid.Empty</c> when it was not returned.</param>
        /// <param name="projectedValueWasAliased">True when <paramref name="projectedValue"/> arrived wrapped in an alias.</param>
        public static bool TryResolve(object? projectedValue, Guid rowId, bool projectedValueWasAliased, out string? id, out WorkflowIdSource source)
        {
            Guid? column = DataverseValue.AsGuid(projectedValue, DataverseSchema.WorkflowId);
            bool columnStated = column.HasValue && column.Value != Guid.Empty;
            bool rowStated = rowId != Guid.Empty;

            if (columnStated && rowStated && column!.Value != rowId)
            {
                id = null;
                source = WorkflowIdSource.Conflicting;
                return false;
            }

            if (columnStated)
            {
                id = column!.Value.ToString("D");
                source = projectedValueWasAliased ? WorkflowIdSource.AliasedIdColumn : WorkflowIdSource.IdColumn;
                return true;
            }

            if (rowStated)
            {
                id = rowId.ToString("D");
                source = WorkflowIdSource.RowId;
                return true;
            }

            id = null;
            source = WorkflowIdSource.NotStated;
            return false;
        }

        /// <summary>
        /// The label a diagnostic uses for one source. Every state of <see cref="WorkflowIdSource"/> has
        /// wording here, so a log line can never print a bare number or fall back to "unknown" and leave
        /// the next reader guessing. Column names only - never a value, a name or an id.
        /// </summary>
        public static string Describe(WorkflowIdSource source)
        {
            switch (source)
            {
                case WorkflowIdSource.RowId:
                    return "row id";
                case WorkflowIdSource.IdColumn:
                    return DataverseSchema.WorkflowId + " column";
                case WorkflowIdSource.AliasedIdColumn:
                    return "aliased " + DataverseSchema.WorkflowId + " column";
                case WorkflowIdSource.Conflicting:
                    return "conflicting ids";
                default:
                    return "no " + DataverseSchema.WorkflowId + " and no row id";
            }
        }
    }
}
