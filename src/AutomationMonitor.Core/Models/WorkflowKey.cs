using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Provider-scoped workflow identity: provider + scope (environment) + the provider's own
    /// globally unique id. Display names never participate in equality, hashing or cache keys
    /// because they are mutable and collide between solutions.
    /// </summary>
    public sealed class WorkflowKey : IEquatable<WorkflowKey?>
    {
        private WorkflowKey(ProviderKind provider, string? scopeId, string workflowId)
        {
            Provider = provider;
            ScopeId = NormalizeText(scopeId);
            WorkflowId = NormalizeId(workflowId);
        }

        public ProviderKind Provider { get; }

        /// <summary>
        /// Environment identifier for Power Automate, empty when the source cannot report one.
        /// An empty scope is deliberately a different key from a known scope: results read from an
        /// unidentified environment must never merge with identified ones.
        /// </summary>
        public string ScopeId { get; }

        /// <summary>The provider's own stable workflow id, in canonical lower-case form.</summary>
        public string WorkflowId { get; }

        /// <summary>Text form used for membership lists and cache keys, e.g. <c>powerautomate:env:wf</c>.</summary>
        public string Canonical
        {
            get { return ProviderKinds.Prefix(Provider) + ":" + ScopeId + ":" + WorkflowId; }
        }

        public static WorkflowKey Create(ProviderKind provider, string? scopeId, string workflowId)
        {
            if (string.IsNullOrWhiteSpace(workflowId))
            {
                throw new ArgumentException("A provider workflow id is required to build an identity.", nameof(workflowId));
            }

            return new WorkflowKey(provider, scopeId, workflowId);
        }

        public static bool TryCreate(ProviderKind provider, string? scopeId, string? workflowId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WorkflowKey? key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(workflowId))
            {
                return false;
            }

            key = new WorkflowKey(provider, scopeId, workflowId!);
            return true;
        }

        public bool Equals(WorkflowKey? other)
        {
            if (other == null)
            {
                return false;
            }

            return Provider == other.Provider
                && string.Equals(ScopeId, other.ScopeId, StringComparison.Ordinal)
                && string.Equals(WorkflowId, other.WorkflowId, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as WorkflowKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Provider;
                hash = (hash * 397) ^ ScopeId.GetHashCode();
                hash = (hash * 397) ^ WorkflowId.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return Canonical;
        }

        private static string NormalizeText(string? value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value!.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Ids arrive as GUIDs in different formats (with or without dashes) depending on the
        /// reader. Canonicalising them keeps one workflow from appearing twice.
        /// </summary>
        private static string NormalizeId(string value)
        {
            string text = NormalizeText(value);
            Guid parsed;
            if (Guid.TryParse(text, out parsed))
            {
                return parsed.ToString("D").ToLowerInvariant();
            }

            return text;
        }
    }
}
