namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Where a raw workflow row actually carried its provider id.
    ///
    /// <para>
    /// Identity is the one value a monitoring row cannot be without, and the source of it is a fact about
    /// the reader, not about the flow. A row id and a projected id column normally hold the same
    /// <c>Guid</c>; they stop agreeing when a query changes shape, which is precisely how a flow list can
    /// come back full of rows and yield nothing (<c>QueryExpression.Distinct</c> suppresses primary keys -
    /// see <see cref="AutomationMonitor.Core.Providers.Dataverse.WorkflowIdentity"/>). Recording the source
    /// per row is what lets a diagnostic say "the id never arrived" instead of "the environment has no
    /// flows", and it is why no row is ever given a substitute identity.
    /// </para>
    /// </summary>
    public enum WorkflowIdSource
    {
        /// <summary>Neither the row id nor the projected id column stated a value. The row cannot carry identity.</summary>
        NotStated = 0,

        /// <summary>Taken from the reader's row id (Dataverse <c>Entity.Id</c>).</summary>
        RowId = 1,

        /// <summary>Taken from the projected identity column itself (Dataverse <c>workflowid</c>).</summary>
        IdColumn = 2,

        /// <summary>
        /// Taken from the projected identity column after it arrived wrapped in an alias. Same value,
        /// different shape: a joined query may alias every column it returns, and a reader that only
        /// understands the unwrapped shape would silently lose the id.
        /// </summary>
        AliasedIdColumn = 3,

        /// <summary>
        /// The row id and the projected identity column disagreed. Two ids that do not match is not a
        /// choice of convenience, it is an unreadable row: guessing either one could attach runs, filters
        /// and watchlist membership to the wrong flow, so the row is refused and counted.
        /// </summary>
        Conflicting = 4
    }
}
