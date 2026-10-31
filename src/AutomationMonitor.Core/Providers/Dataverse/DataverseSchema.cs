namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// Dataverse schema facts used by both the plugin's queries and the core's normalization.
    /// They live here so the read-only provider and the tested logic can never drift apart.
    ///
    /// <para>
    /// <c>ComponentTypeWorkflow</c> is the <c>solutioncomponent.componenttype</c> choice value and
    /// is documented by Microsoft (Solution Component table reference: 29 = Workflow). The
    /// workflow <c>category</c> and <c>statecode</c> values are platform option-set metadata: no
    /// SDK enum exists for them, so they are named constants here and must be re-confirmed
    /// against a real environment by the Windows/M0 spike (see docs/provider-capabilities.md).
    /// </para>
    /// </summary>
    public static class DataverseSchema
    {
        public const string SolutionEntity = "solution";
        public const string SolutionComponentEntity = "solutioncomponent";
        public const string WorkflowEntity = "workflow";

        // solution columns (all read-only reads; the plugin never writes).
        public const string SolutionId = "solutionid";
        public const string SolutionUniqueName = "uniquename";
        public const string SolutionDisplayName = "friendlyname";
        public const string SolutionVersion = "version";
        public const string SolutionIsManaged = "ismanaged";
        public const string SolutionIsVisible = "isvisible";
        public const string SolutionInstalledOn = "installedon";
        public const string SolutionModifiedOn = "modifiedon";

        // solutioncomponent columns.
        public const string ComponentType = "componenttype";
        public const string ComponentObjectId = "objectid";
        public const string ComponentSolutionId = "solutionid";

        /// <summary>solutioncomponent.componenttype value that identifies a workflow component.</summary>
        public const int ComponentTypeWorkflow = 29;

        // workflow columns.
        public const string WorkflowId = "workflowid";
        public const string WorkflowName = "name";
        public const string WorkflowCategory = "category";
        public const string WorkflowStateCode = "statecode";
        public const string WorkflowStatusCode = "statuscode";
        public const string WorkflowPrimaryCategory = "primarycategory";
        public const string WorkflowSecondaryCategory = "secondarycategory";
        public const string WorkflowDescription = "description";
        public const string WorkflowCreatedOn = "createdon";
        public const string WorkflowModifiedOn = "modifiedon";

        /// <summary>
        /// workflow.category value for a modern Power Automate cloud flow. Anything else in the
        /// workflow table is a classic process, dialog, rule or business process flow and is
        /// excluded from the cloud-flow catalogue.
        /// </summary>
        public const int WorkflowCategoryCloudFlow = 5;

        /// <summary>Dataverse statecode for a disabled workflow.</summary>
        public const int StateCodeDisabled = 0;

        /// <summary>Dataverse statecode for an enabled workflow.</summary>
        public const int StateCodeEnabled = 1;

        // ---- flowrun: execution history (M3) ------------------------------------------------
        //
        // Run history for solution-aware cloud flows is replicated into this table. Every fact
        // below comes from the Microsoft Learn FlowRun entity reference and is repeated in
        // docs/provider-capabilities.md; none of it has been executed against a tenant from this
        // host, so the run provider ships behind the spike checklist until the first Windows
        // session confirms it.

        /// <summary>Entity holding one row per solution-aware cloud flow execution.</summary>
        public const string RunEntity = "flowrun";

        /// <summary>Primary id of the row (Uniqueidentifier). Not the Logic Apps run id.</summary>
        public const string RunFlowRunId = "flowrunid";

        /// <summary>
        /// Primary name attribute (String 100): the Logic Apps run name of the execution. This is
        /// the value <c>parentrunid</c> refers to, so it is the run id the model keys on.
        /// </summary>
        public const string RunName = "name";

        /// <summary>Cloud flow this execution belongs to (String 100), matched to <see cref="WorkflowId"/>.</summary>
        public const string RunWorkflowId = "workflowid";

        /// <summary>Start instant (DateTime, UserLocal behaviour: read as the UTC Dataverse stores).</summary>
        public const string RunStartTime = "starttime";

        /// <summary>End instant; absent while the run is still going.</summary>
        public const string RunEndTime = "endtime";

        /// <summary>BigInt duration in milliseconds. The Learn property page calls it DurationInMs.</summary>
        public const string RunDuration = "duration";

        /// <summary>Free-text provider status (String 100, RequiredLevel None): absent is possible.</summary>
        public const string RunStatus = "status";

        /// <summary>Short error code (String 100).</summary>
        public const string RunErrorCode = "errorcode";

        /// <summary>Error description (Memo 20000). Untrusted text: never a payload, always truncated.</summary>
        public const string RunErrorMessage = "errormessage";

        /// <summary>Parent run name (String 100): the execution evidence for a parent/child link.</summary>
        public const string RunParentRunId = "parentrunid";

        /// <summary>Tracking id propagated by the caller (String 255): correlation on evidence only.</summary>
        public const string RunClientTrackingId = "clienttrackingid";

        /// <summary>Primary-run marker. A <c>Picklist</c>, not a Boolean: read it type-tolerantly.</summary>
        public const string RunIsPrimary = "isprimary";

        /// <summary>Trigger kind as text (String 100).</summary>
        public const string RunTriggerType = "triggertype";

        /// <summary>Picklist value of <see cref="RunIsPrimary"/> meaning "not the primary run".</summary>
        public const int IsPrimaryValueFalse = 0;

        /// <summary>Picklist value of <see cref="RunIsPrimary"/> meaning "the primary run".</summary>
        public const int IsPrimaryValueTrue = 1;

        /// <summary>
        /// The columns the run provider asks the source for, in the order the gateway builds them.
        /// A guard test pins this list against <see cref="RunAttributeCount"/>: a column added here
        /// and forgotten in the query (or the reverse) would otherwise mean a silently missing
        /// value on every row.
        /// </summary>
        public static readonly string[] RunAttributes =
        {
            RunFlowRunId,
            RunName,
            RunWorkflowId,
            RunStartTime,
            RunEndTime,
            RunDuration,
            RunStatus,
            RunErrorCode,
            RunErrorMessage,
            RunParentRunId,
            RunClientTrackingId,
            RunIsPrimary,
            RunTriggerType
        };
    }
}
