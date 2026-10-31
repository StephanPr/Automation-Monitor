using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// Which automation platform a workflow or run was read from. Identity is always
    /// qualified by this value: the same provider-side id can exist on two platforms.
    ///
    /// <para>
    /// <see cref="LogicAppsConsumption"/> and <see cref="LogicAppsStandard"/> are declared so the
    /// normalized model, its keys and its merge rules already fit the M9/M10 providers without a
    /// rewrite. No provider implements them yet: a run carrying either kind can only come from a
    /// test fixture until those milestones land, and the capability contract below is how the UI
    /// will know to disable rather than empty those sources.
    /// </para>
    /// </summary>
    public enum ProviderKind
    {
        /// <summary>Source not known or not supported. Never treated as a successful value.</summary>
        Unknown = 0,

        /// <summary>Power Automate cloud flows held in Dataverse.</summary>
        PowerAutomate = 1,

        /// <summary>Azure Logic Apps (multi-tenant / Consumption). Not implemented before M9.</summary>
        LogicAppsConsumption = 2,

        /// <summary>Azure Logic Apps (single-tenant / Standard). Not implemented before M10.</summary>
        LogicAppsStandard = 3
    }

    public static class ProviderKinds
    {
        /// <summary>
        /// Lower-case namespace used in canonical keys. Stable text, never derived from a
        /// display name, so a renamed workflow or provider label cannot change an identity.
        /// Every member is listed explicitly: an unlisted kind must never silently share the
        /// <c>unknown</c> namespace and collide with a real provider's keys.
        /// </summary>
        public static string Prefix(ProviderKind provider)
        {
            switch (provider)
            {
                case ProviderKind.PowerAutomate:
                    return "powerautomate";
                case ProviderKind.LogicAppsConsumption:
                    return "logicapps-consumption";
                case ProviderKind.LogicAppsStandard:
                    return "logicapps-standard";
                default:
                    return "unknown";
            }
        }

        /// <summary>
        /// Whether a provider of this kind can be queried at all in the current build. Honest
        /// capability, not a guess: only Power Automate run history exists today, and a view must
        /// disable an unsupported source instead of rendering it as an empty result.
        /// </summary>
        public static bool IsImplemented(ProviderKind provider)
        {
            return provider == ProviderKind.PowerAutomate;
        }

        /// <summary>
        /// Read a provider namespace back from stored text. Only an exact namespace matches: an
        /// unrecognised value must never fall through to <see cref="ProviderKind.Unknown"/>, because
        /// that would let a member of a provider this build does not know about be merged with a
        /// genuinely unknown one. The caller reports the value instead.
        /// </summary>
        public static bool TryParse(string? token, out ProviderKind provider)
        {
            provider = ProviderKind.Unknown;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string text = token!.Trim().ToLowerInvariant();
            foreach (ProviderKind candidate in new[]
            {
                ProviderKind.PowerAutomate,
                ProviderKind.LogicAppsConsumption,
                ProviderKind.LogicAppsStandard
            })
            {
                if (string.Equals(Prefix(candidate), text, StringComparison.Ordinal))
                {
                    provider = candidate;
                    return true;
                }
            }

            // "unknown" is a namespace a file may legitimately carry, so it is matched explicitly
            // rather than by falling off the end of the loop.
            if (string.Equals(Prefix(ProviderKind.Unknown), text, StringComparison.Ordinal))
            {
                provider = ProviderKind.Unknown;
                return true;
            }

            return false;
        }
    }
}

