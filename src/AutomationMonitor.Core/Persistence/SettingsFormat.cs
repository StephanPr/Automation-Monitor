using System;
using System.Globalization;
using System.Text;

namespace AutomationMonitor.Core.Persistence
{
    /// <summary>
    /// The one conversion between a stored ISO 8601 string and a UTC instant. It exists because the
    /// framework JSON serializer has no ISO format for <see cref="DateTimeOffset"/>: it emits a
    /// legacy <c>/Date(milliseconds)/</c> object, which would leak a serializer detail into the file
    /// format and make the settings neither hand-editable nor portable. Keeping the conversion here
    /// means no persisted DTO anywhere holds a <see cref="DateTimeOffset"/>.
    /// </summary>
    public static class SettingsFormat
    {
        /// <summary>Round-trippable format written for every stored timestamp.</summary>
        public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz";

        /// <summary>The longest stored text value. A provider name longer than this is truncated, not rejected.</summary>
        public const int MaximumTextLength = 512;

        /// <summary>UTC text for an instant, always with an explicit offset so no local time is ever stored.</summary>
        public static string Timestamp(DateTimeOffset instantUtc)
        {
            return instantUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        public static string? Timestamp(DateTimeOffset? instantUtc)
        {
            return instantUtc.HasValue ? Timestamp(instantUtc.Value) : null;
        }

        /// <summary>
        /// Parse stored UTC text. Empty is "not recorded": true with a null result. Anything else must
        /// parse as a real instant and is normalised to UTC; unparseable text is false, so a corrupt
        /// file is reported instead of silently becoming the year 1.
        /// </summary>
        public static bool TryTimestamp(string? text, out DateTimeOffset? instantUtc)
        {
            instantUtc = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            DateTimeOffset parsed;
            if (DateTimeOffset.TryParse(
                text!.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out parsed))
            {
                instantUtc = parsed.ToUniversalTime();
                return true;
            }

            return false;
        }

        /// <summary>
        /// A text value safe to store: trimmed of outer whitespace, control characters removed, length
        /// capped. Stored strings here are often provider text (a flow name travels in a member
        /// record), so the cap and the control-character strip apply to them as much as to anything
        /// the operator typed. The text is never escaped or interpreted: it stays data for the grid,
        /// for JSON and for a future export.
        /// </summary>
        public static string Text(string? value)
        {
            return Text(value, MaximumTextLength);
        }

        public static string Text(string? value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder text = new StringBuilder(value!.Length);
            for (int i = 0; i < value!.Length && text.Length < maximumLength; i++)
            {
                char character = value[i];

                // Control characters cannot be displayed safely in a grid, a label or a JSON string
                // an operator might open in an editor, and a tab or newline inside a name is always a
                // defect rather than content worth keeping.
                if (char.IsControl(character))
                {
                    text.Append(' ');
                    continue;
                }

                text.Append(character);
            }

            return text.ToString().Trim();
        }
    }
}
