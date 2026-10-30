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
    }
}
