using System;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// A source returned a value this tool cannot interpret for a column it asked for. Distinct from
    /// an environment failure on purpose: the environment answered, so "check the connection" would be
    /// a lie, and the operator is told which column and which shape instead of seeing a blank cell.
    ///
    /// <para>
    /// Lives in the core rather than the SDK adapter because the classifier that turns a thrown error
    /// into an honest coverage state is core logic, and because the rule "never guess a value from a
    /// shape you do not understand" must be stated once for every provider.
    /// </para>
    /// </summary>
    public sealed class DataverseMappingException : InvalidOperationException
    {
        public DataverseMappingException(string attribute, string expected, string actualType)
            : base(Describe(attribute, expected, actualType))
        {
            Attribute = string.IsNullOrWhiteSpace(attribute) ? "unknown" : attribute!.Trim();
            Expected = expected ?? "a value this tool understands";
            ActualType = actualType ?? "an unsupported type";
        }

        /// <summary>The column whose value could not be read. Never a value, never a payload.</summary>
        public string Attribute { get; }

        /// <summary>The shape the reader accepts, in operator words.</summary>
        public string Expected { get; }

        /// <summary>CLR type name of what actually arrived. Type only: never the content.</summary>
        public string ActualType { get; }

        /// <summary>
        /// Wording is deliberate: it names this tool so the error classifier can tell a local mapping
        /// defect apart from an environment complaint, and it never quotes the value, which may be
        /// business data.
        /// </summary>
        private static string Describe(string attribute, string? expected, string? actualType)
        {
            return "Automation Monitor could not read the '" + attribute
                + "' value returned by the environment (expected " + (expected ?? "a supported shape")
                + ", received " + (actualType ?? "an unsupported type") + ").";
        }
    }
}
