using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// A workflow the operator can select for monitoring, normalized across providers.
    /// Identity comes from <see cref="Key"/>; the name is display text and is deliberately
    /// excluded from equality. Provider raw values are retained for diagnosis and links.
    /// </summary>
    public sealed class WorkflowReference
    {
        public WorkflowReference(
            WorkflowKey key,
            string? name,
            WorkflowState state,
            int? rawStateCode = null,
            int? rawStatusCode = null,
            int? rawCategory = null,
            string? description = null,
            DateTimeOffset? createdOnUtc = null,
            DateTimeOffset? modifiedOnUtc = null)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            Key = key;
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed workflow)" : name!.Trim();
            State = state;
            RawStateCode = rawStateCode;
            RawStatusCode = rawStatusCode;
            RawCategory = rawCategory;
            Description = description;
            CreatedOnUtc = createdOnUtc;
            ModifiedOnUtc = modifiedOnUtc;
        }

        public WorkflowKey Key { get; }

        public ProviderKind Provider { get { return Key.Provider; } }

        /// <summary>Provider's own workflow id, retained for portal deep links.</summary>
        public string WorkflowId { get { return Key.WorkflowId; } }

        /// <summary>Environment scope of the source, empty when unknown.</summary>
        public string EnvironmentId { get { return Key.ScopeId; } }

        public string Name { get; }

        /// <summary>Normalized enablement state; <see cref="WorkflowState.Unknown"/> is surfaced, not hidden.</summary>
        public WorkflowState State { get; }

        public int? RawStateCode { get; }

        public int? RawStatusCode { get; }

        public int? RawCategory { get; }

        public string? Description { get; }

        public DateTimeOffset? CreatedOnUtc { get; }

        public DateTimeOffset? ModifiedOnUtc { get; }

        /// <summary>Human-readable state, including the raw code when the state is unknown.</summary>
        public string StateLabel
        {
            get
            {
                switch (State)
                {
                    case WorkflowState.Enabled:
                        return "Enabled";
                    case WorkflowState.Disabled:
                        return "Disabled";
                    default:
                        return RawStateCode.HasValue
                            ? "Unknown (" + RawStateCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")"
                            : "Unknown";
                }
            }
        }

        public override string ToString()
        {
            return Name + " [" + StateLabel + "]";
        }
    }
}
