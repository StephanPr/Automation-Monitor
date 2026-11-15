using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Globally unique run identity: the provider-scoped <see cref="WorkflowKey"/> plus the
    /// provider's own run id. This is the key every merge, de-duplication and cache decision uses.
    ///
    /// <para>
    /// The workflow key is part of the identity on purpose. Run ids are only unique inside their
    /// workflow, and the same run id can reappear under a different flow or in a different
    /// environment; dropping either qualifier would merge two unrelated executions into one row.
    /// Status, timestamps and names never participate in equality.
    /// </para>
    /// </summary>
    public sealed class RunKey : IEquatable<RunKey?>
    {
        private RunKey(WorkflowKey workflow, string runId)
        {
            Workflow = workflow;
            RunId = CanonicalId.Identifier(runId);
        }

        /// <summary>Provider, environment and workflow this run belongs to.</summary>
        public WorkflowKey Workflow { get; }

        /// <summary>The provider's own run id, in canonical form.</summary>
        public string RunId { get; }

        public ProviderKind Provider { get { return Workflow.Provider; } }

        public string EnvironmentId { get { return Workflow.ScopeId; } }

        /// <summary>
        /// The workflow key's resource scope, offered here so a run reader never has to re-parse the stored
        /// text and cannot invent a second rule for what an empty or unknown scope means.
        /// </summary>
        public ResourceScope Scope { get { return Workflow.Scope; } }

        public string WorkflowId { get { return Workflow.WorkflowId; } }

        /// <summary>Text form used for de-duplication and cache keys, e.g. <c>powerautomate:env:wf:run</c>.</summary>
        public string Canonical { get { return Workflow.Canonical + ":" + RunId; } }

        public static RunKey Create(WorkflowKey workflow, string runId)
        {
            if (workflow == null)
            {
                throw new ArgumentNullException(nameof(workflow));
            }

            if (string.IsNullOrWhiteSpace(runId))
            {
                throw new ArgumentException("A provider run id is required to build a run identity.", nameof(runId));
            }

            return new RunKey(workflow, runId);
        }

        /// <summary>
        /// Try-build without throwing. A row that cannot carry a run id must stay visible as an
        /// unusable record in the caller's counts; it must never enter a merge under a made-up key.
        /// </summary>
        public static bool TryCreate(WorkflowKey? workflow, string? runId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RunKey? key)
        {
            key = null;
            if (workflow == null || string.IsNullOrWhiteSpace(runId))
            {
                return false;
            }

            key = new RunKey(workflow, runId!);
            return true;
        }

        public bool Equals(RunKey? other)
        {
            if (other == null)
            {
                return false;
            }

            return Workflow.Equals(other.Workflow) && string.Equals(RunId, other.RunId, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as RunKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Workflow.GetHashCode() * 397) ^ RunId.GetHashCode();
            }
        }

        public override string ToString()
        {
            return Canonical;
        }
    }
}
