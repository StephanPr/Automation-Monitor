using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// What one row's environment column says. Identity lives in <see cref="ResourceScope"/> and a
    /// scope's own wording in <see cref="ResourceScopeLabel"/>; this class only answers the presentational
    /// question a grid or a status line asks - "which environment is this row from, in words a person
    /// can act on" - and it is the only place allowed to combine a stored scope with the connected
    /// organization's display context.
    ///
    /// <para>
    /// The rule that makes that safe is one direction only: a display name may be shown beside a scope
    /// this session has <em>verified</em> names the connected organization. It is never used to
    /// identify a row, never stored, never compared, and never borrowed by a member whose scope was
    /// never verified - because a legacy list opened while connected would otherwise read as though it
    /// belonged to that connection, which is the silent association this checkpoint exists to prevent.
    /// </para>
    /// </summary>
    public static class EnvironmentDisplay
    {
        /// <summary>The state label for a row with no verified environment. Short, because it is a column.</summary>
        public const string UnverifiedText = "Unverified environment";

        /// <summary>Longest display text echoed into a row. Longer is a paste, not a name.</summary>
        public const int MaximumDisplayNameLength = ConnectedOrganization.MaximumDisplayNameLength;

        /// <summary>
        /// Describe the scope a row carries, beside the connected organization this session resolved.
        /// A stored scope this build cannot interpret keeps <see cref="ResourceScopeLabel"/>'s wording
        /// verbatim, because replacing it with "no environment" would misreport preserved content.
        /// </summary>
        public static string Describe(string? storedScope, ConnectedOrganization? connected)
        {
            return Describe(ResourceScope.Read(storedScope), connected);
        }

        /// <summary>Typed form of <see cref="Describe(string?,ConnectedOrganization?)"/>.</summary>
        public static string Describe(ResourceScope? scope, ConnectedOrganization? connected)
        {
            ResourceScope value = scope ?? ResourceScope.Unverified;

            if (value.IsUnverified)
            {
                return UnverifiedText;
            }

            if (!value.IsQualified)
            {
                return ResourceScopeLabel.Describe(value);
            }

            string identity = ResourceScopeLabel.Describe(value);
            bool connectedOrganization = connected != null && value.IsSameResourceAs(connected.Scope);
            if (!connectedOrganization)
            {
                return identity + (connected != null && connected.IsResolved ? " (not this connection)" : string.Empty);
            }

            string? display = Clean(connected!.DisplayName);
            return display == null ? identity : display + " (" + identity + ")";
        }

        /// <summary>
        /// The header line for the connected environment itself: what this session's connection was
        /// verified against, or the honest statement that it was not. A display name alone is never a
        /// verified environment, so an unresolved read says so instead of showing a friendly name.
        /// </summary>
        public static string DescribeConnected(ConnectedOrganization? connected)
        {
            if (connected == null || !connected.IsResolved)
            {
                return UnverifiedText;
            }

            string identity = ResourceScopeLabel.Describe(connected.Scope, connected.Scope);
            string? display = Clean(connected.DisplayName);
            return display == null ? identity : display + " (" + identity + ")";
        }

        /// <summary>
        /// Sanitize host-supplied display text for a cell: trimmed, control characters replaced, clamped.
        /// The result is still data - it goes into a cell value and is never parsed or composed.
        /// </summary>
        public static string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string text = value!.Trim();
            if (text.Length > MaximumDisplayNameLength)
            {
                text = text.Substring(0, MaximumDisplayNameLength);
            }

            char[] characters = text.ToCharArray();
            for (int index = 0; index < characters.Length; index++)
            {
                if (char.IsControl(characters[index]))
                {
                    characters[index] = ' ';
                }
            }

            text = new string(characters).Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
