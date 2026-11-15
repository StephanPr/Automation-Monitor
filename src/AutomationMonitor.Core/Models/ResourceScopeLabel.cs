using System;
using System.Text;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// The one wording for an environment scope, shared by the grid, the watchlist lines and the log, so a
    /// scope can never be described as a different kind of identity by whichever control happens to show it.
    ///
    /// <para>
    /// Two prohibitions. A label never carries a whole identifier: a GUID-shaped value contributes at most its
    /// first eight characters as a fingerprint, which is enough for a person to recognise a row and useless for
    /// addressing anything - identity lives in <see cref="ResourceScope.Text"/>, never in this text. And a label
    /// never uses a display name, url or friendly name, because those are mutable and collide; a name that
    /// looked like a scope would be the next environment-identity defect.
    /// </para>
    /// </summary>
    public static class ResourceScopeLabel
    {
        /// <summary>How much of an identifier may be shown. Longer would be the identifier itself.</summary>
        public const int FingerprintLength = 8;

        /// <summary>The longest kind token echoed back. A longer one is not a token but somebody's text.</summary>
        public const int MaximumTokenLength = 40;

        public static string Describe(ResourceScope? scope)
        {
            ResourceScope value = scope ?? ResourceScope.Unverified;
            switch (value.State)
            {
                case ResourceScopeState.Qualified:
                    return "Dataverse organization " + Fingerprint(value.Identifier);
                case ResourceScopeState.Unqualified:
                    return LooksLikeIdentifier(value.Text)
                        ? "unqualified environment id " + Fingerprint(value.Text)
                        : "unqualified environment scope";
                case ResourceScopeState.UnrecognizedKind:
                    return "environment scope of kind " + Quote(TokenOf(value.Text));
                case ResourceScopeState.Invalid:
                    return string.IsNullOrEmpty(TokenOf(value.Text))
                        ? "unreadable environment scope"
                        : "unreadable environment scope for kind " + Quote(TokenOf(value.Text));
                default:
                    return "no verified environment";
            }
        }

        /// <summary>Label stored scope text without making the caller build the value first.</summary>
        public static string DescribeStored(string? storedScope)
        {
            return Describe(ResourceScope.Read(storedScope));
        }

        /// <summary>
        /// The label plus what this session's connection evidence says about it. The connected scope is
        /// supplied by the caller only when a read really established it: passing null (or a scope that is not
        /// qualified) claims nothing, which is why a recognized token on its own can never read as connected.
        /// </summary>
        public static string Describe(ResourceScope? scope, ResourceScope? connectedScope)
        {
            ResourceScope value = scope ?? ResourceScope.Unverified;
            bool connectedKnown = connectedScope != null && connectedScope!.IsQualified;

            if (!connectedKnown)
            {
                return Describe(value);
            }

            ResourceScope connected = connectedScope!;
            if (value.IsQualified)
            {
                return Describe(value) + (value.IsSameResourceAs(connected) ? " (this connection)" : " (not this connection)");
            }

            if (value.IsUnverified)
            {
                return "no verified environment (connected organization " + Fingerprint(connected.Identifier) + ")";
            }

            return Describe(value);
        }

        /// <summary>
        /// At most the leading characters of an identifier, for recognition on screen only. Never reverse
        /// this: a fingerprint is not an id and must never be sent to a query, a key or a link.
        /// </summary>
        public static string Fingerprint(string? value)
        {
            string text = (value ?? string.Empty).Trim();
            return text.Length <= FingerprintLength
                ? text
                : text.Substring(0, FingerprintLength);
        }

        private static bool LooksLikeIdentifier(string text)
        {
            Guid parsed;
            return Guid.TryParse(text, out parsed);
        }

        private static string TokenOf(string text)
        {
            int separator = text.IndexOf(ResourceScope.Separator);
            string token = separator > 0 ? text.Substring(0, separator) : string.Empty;
            return token.Length <= MaximumTokenLength ? token : token.Substring(0, MaximumTokenLength);
        }

        private static string Quote(string token)
        {
            StringBuilder text = new StringBuilder(token.Length + 2);
            text.Append('"');
            foreach (char character in token)
            {
                text.Append(char.IsControl(character) ? ' ' : character);
            }

            text.Append('"');
            return text.ToString();
        }
    }
}
