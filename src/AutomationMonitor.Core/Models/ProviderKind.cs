namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Which automation platform a workflow or run was read from. Identity is always
    /// qualified by this value: the same provider-side id can exist on two platforms.
    /// </summary>
    public enum ProviderKind
    {
        /// <summary>Source not known or not supported. Never treated as a successful value.</summary>
        Unknown = 0,

        /// <summary>Power Automate cloud flows held in Dataverse.</summary>
        PowerAutomate = 1
    }

    public static class ProviderKinds
    {
        /// <summary>
        /// Lower-case namespace used in canonical keys. Stable text, never derived from a
        /// display name, so a renamed workflow or provider label cannot change an identity.
        /// </summary>
        public static string Prefix(ProviderKind provider)
        {
            return provider == ProviderKind.PowerAutomate ? "powerautomate" : "unknown";
        }
    }
}
