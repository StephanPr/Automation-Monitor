using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Turns provider error text into something safe to show. Provider messages are untrusted
    /// strings: they are never executed, never used to build a path or URL, and any URL, bearer
    /// token or secret-looking pair is stripped before the text reaches the screen or a log.
    /// </summary>
    public static class DiagnosticsSanitizer
    {
        private const int MaxLength = 320;

        private static readonly Regex UrlPattern = new Regex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex BearerPattern = new Regex(@"bearer\s+[A-Za-z0-9\-._~+/=%]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SecretPattern = new Regex(@"\b(password|passwd|secret|token|apikey|api_key|key|code|sessionid|assertion)\b\s*[=:]\s*[^\s;&,""]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Sanitize(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            string text = message ?? string.Empty;

            // Drop any stack trace: only the thrown message can be useful to an operator.
            int stackStart = text.IndexOf("\n   at ", StringComparison.Ordinal);
            if (stackStart < 0)
            {
                stackStart = text.IndexOf("\r\n   at ", StringComparison.Ordinal);
            }

            if (stackStart >= 0)
            {
                text = text.Substring(0, stackStart);
            }

            string cleaned = UrlPattern.Replace(text, "[url]");
            cleaned = BearerPattern.Replace(cleaned, "bearer [redacted]");
            cleaned = SecretPattern.Replace(cleaned, "$1=[redacted]");
            cleaned = cleaned.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            cleaned = MultipleSpaces.Replace(cleaned.Trim(), " ");

            if (cleaned.Length > MaxLength)
            {
                cleaned = cleaned.Substring(0, MaxLength - 1) + "...";
            }

            return cleaned;
        }

        private static readonly Regex MultipleSpaces = new Regex(@"\s{2,}", RegexOptions.CultureInvariant);

        /// <summary>Combines an optional source label with a sanitized reason, without inventing detail.</summary>
        public static string Describe(string? context, string? sanitizedMessage)
        {
            StringBuilder text = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(context))
            {
                text.Append(context!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(sanitizedMessage))
            {
                if (text.Length > 0)
                {
                    text.Append(": ");
                }

                text.Append(sanitizedMessage);
            }

            return text.ToString();
        }
    }
}
