using System;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// The one canonicalisation rule for provider identifiers. Workflow keys and run keys must
    /// normalise ids the same way or the same record observed through two readers would merge
    /// into two rows (or two records would merge into one).
    ///
    /// <para>
    /// Ids reach us as GUIDs in several spellings (dashed, braced, bare) or as opaque run names,
    /// depending on the reader and on the column that carried them. Trimming and lower-casing is
    /// safe for both; a value that parses as a GUID is re-rendered in the canonical dashed form so
    /// <c>BBBBBBBB-…-01</c>, <c>{bbbbbbbb-…-01}</c> and <c>bbbbbbbb…01</c> collapse to one key.
    /// Anything that is not a GUID is kept as trimmed lower-case text: an opaque Logic Apps or run
    /// name is identity, and re-writing it would be inventing a value.
    /// </para>
    /// </summary>
    internal static class CanonicalId
    {
        /// <summary>Empty for null/blank, so an absent scope stays representable but distinct.</summary>
        public static string Text(string? value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value!.Trim().ToLowerInvariant();
        }

        /// <summary>Canonical id text; never empty unless the input was empty.</summary>
        public static string Identifier(string? value)
        {
            string text = Text(value);
            Guid parsed;
            if (Guid.TryParse(text, out parsed))
            {
                return parsed.ToString("D").ToLowerInvariant();
            }

            return text;
        }
    }
}
