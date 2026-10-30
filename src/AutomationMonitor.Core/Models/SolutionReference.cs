using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>A Dataverse solution as offered to the operator for flow discovery.</summary>
    public sealed class SolutionReference
    {
        public SolutionReference(
            string id,
            string? uniqueName,
            string? displayName,
            string? version,
            bool isManaged,
            DateTimeOffset? installedOnUtc = null,
            DateTimeOffset? modifiedOnUtc = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A solution id is required.", nameof(id));
            }

            Id = id!.Trim();
            UniqueName = (uniqueName ?? string.Empty).Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? UniqueName : displayName!.Trim();
            Version = (version ?? string.Empty).Trim();
            IsManaged = isManaged;
            InstalledOnUtc = installedOnUtc;
            ModifiedOnUtc = modifiedOnUtc;
        }

        /// <summary>Solution table primary id. Membership is keyed on this, never on the name.</summary>
        public string Id { get; }

        /// <summary> Publisher name used in the solution component references.</summary>
        public string UniqueName { get; }

        /// <summary>Display name shown to the operator.</summary>
        public string DisplayName { get; }

        public string Version { get; }

        /// <summary>Managed solutions cannot be edited in this environment; still monitorable.</summary>
        public bool IsManaged { get; }

        public DateTimeOffset? InstalledOnUtc { get; }

        public DateTimeOffset? ModifiedOnUtc { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
