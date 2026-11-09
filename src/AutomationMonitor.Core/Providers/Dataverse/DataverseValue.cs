using System;
using System.Globalization;

namespace AutomationMonitor.Core.Providers.Dataverse
{
    /// <summary>
    /// Reads one already-unwrapped attribute value into the model, or explains why it cannot.
    ///
    /// <para>
    /// The SDK adapter reduces provider shapes (an aliased value, an option set) to a plain CLR value
    /// first, so the decision about what a value means lives here: SDK-free and testable. Three
    /// outcomes only - absent or null is an honest "the source did not say", a recognised shape is
    /// converted, and anything else raises <see cref="DataverseMappingException"/> naming the column.
    /// A value is never coerced into a shape it does not have and an unexpected type is never quietly
    /// dropped: a blank cell is as bad as a wrong row.
    /// </para>
    /// </summary>
    public static class DataverseValue
    {
        private const string TextExpected = "text";
        private const string WholeNumberExpected = "a whole number";
        private const string BooleanExpected = "a true or false value";
        private const string DateExpected = "a date and time";
        private const string IdentifierExpected = "an identifier";

        /// <summary>
        /// Text column. An absent, null or blank value is "not stated" rather than an empty string, so
        /// a caller cannot mistake a missing name for a name that happens to be empty.
        /// </summary>
        public static string? AsText(object? value, string attribute)
        {
            if (value == null)
            {
                return null;
            }

            string? text = value as string;
            if (text == null)
            {
                throw Unexpected(attribute, TextExpected, value);
            }

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>
        /// Whole-number column; an option set arrives here as its option value. A numeric string is
        /// accepted because a text column carrying a number is still unambiguous; anything else is a
        /// shape change, not a value.
        /// </summary>
        public static int? AsInt32(object? value, string attribute)
        {
            long? whole = AsWholeNumber(value, attribute);
            if (whole == null)
            {
                return null;
            }

            if (whole.Value < int.MinValue || whole.Value > int.MaxValue)
            {
                throw new DataverseMappingException(attribute, "a whole number within 32 bits", TypeOf(value));
            }

            return (int)whole.Value;
        }

        /// <summary>BigInt column - durations in milliseconds. Same tolerance as <see cref="AsInt32"/>.</summary>
        public static long? AsInt64(object? value, string attribute)
        {
            return AsWholeNumber(value, attribute);
        }
        /// <summary>A genuine Boolean column. A picklist is not one: see <see cref="AsBooleanWhenBoolean"/>.</summary>
        public static bool? AsBoolean(object? value, string attribute)
        {
            if (value == null)
            {
                return null;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            throw Unexpected(attribute, BooleanExpected, value);
        }

        /// <summary>
        /// Reads a Boolean only when the column really is one. For a column with both a picklist and a
        /// Boolean representation the numeric form travels in its own field, so null here loses
        /// nothing, while deriving "true" from an option value this tool does not recognise would
        /// invent a fact.
        /// </summary>
        public static bool? AsBooleanWhenBoolean(object? value)
        {
            return value as bool?;
        }

        /// <summary>
        /// Instant column, always returned in UTC. Dataverse hands back a <see cref="DateTime"/> with an
        /// unspecified kind whose value is the UTC instant it stores; a local-kind value is converted,
        /// never trusted as UTC, and a value with an offset is normalised so no local time is compared.
        /// </summary>
        public static DateTimeOffset? AsUtc(object? value, string attribute)
        {
            if (value == null)
            {
                return null;
            }

            if (value is DateTime)
            {
                DateTime raw = (DateTime)value;
                if (raw.Kind == DateTimeKind.Local)
                {
                    return new DateTimeOffset(raw.ToUniversalTime(), TimeSpan.Zero);
                }

                return new DateTimeOffset(DateTime.SpecifyKind(raw, DateTimeKind.Utc));
            }

            if (value is DateTimeOffset)
            {
                return new DateTimeOffset(((DateTimeOffset)value).ToUniversalTime().UtcDateTime, TimeSpan.Zero);
            }

            throw Unexpected(attribute, DateExpected, value);
        }

        /// <summary>
        /// Identifier column (<c>Uniqueidentifier</c>): the shape a workflow's identity arrives in.
        ///
        /// <para>
        /// Absent or blank is "the source did not state an id", which the caller must count and report.
        /// A <see cref="Guid.Empty"/> is returned as itself rather than as null: it is a value the source
        /// did send, and only the caller knows whether an empty identity means "not stated" or "refuse the
        /// row" (see <see cref="WorkflowIdentity"/>).
        /// </para>
        /// <para>
        /// Unlike the optional columns, a text value that is not an identifier is a defect and not a blank:
        /// silently returning null here is what let a whole flow list disappear one attribute at a time.
        /// Identity keys merging, run filtering and saved watchlists, so an unrecognised shape is named -
        /// column and CLR type only - and never guessed into a plausible value.
        /// </para>
        /// </summary>
        public static Guid? AsGuid(object? value, string attribute)
        {
            if (value == null)
            {
                return null;
            }

            if (value is Guid)
            {
                return (Guid)value;
            }

            string? text = value as string;
            if (text != null)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                Guid parsed;
                if (Guid.TryParse(text.Trim(), out parsed))
                {
                    return parsed;
                }
            }

            throw Unexpected(attribute, IdentifierExpected, value);
        }

        private static long? AsWholeNumber(object? value, string attribute)
        {
            if (value == null)
            {
                return null;
            }

            if (value is long)
            {
                return (long)value;
            }

            if (value is int)
            {
                return (int)value;
            }

            if (value is short)
            {
                return (short)value;
            }

            if (value is byte)
            {
                return (byte)value;
            }

            if (value is decimal)
            {
                decimal exact = (decimal)value;
                if (decimal.Truncate(exact) != exact || exact < long.MinValue || exact > long.MaxValue)
                {
                    throw Unexpected(attribute, WholeNumberExpected, value);
                }

                return (long)exact;
            }

            // A text column that carries a number is still unambiguous, and a text column that does not
            // carry one simply states no number. That is a null, not a defect: the value has no home in
            // the model, and failing a whole read because an optional category column chose a label over
            // an option value would hide every row the environment did answer with.
            string? text = value as string;
            if (text != null)
            {
                long parsed;
                if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }

                return null;
            }

            throw Unexpected(attribute, WholeNumberExpected, value);
        }

        /// <summary>
        /// The one diagnostic: the column, the accepted shape, and the CLR type name of what arrived.
        /// Enough to fix the mapping, and never the value itself, which may be business data.
        /// </summary>
        private static DataverseMappingException Unexpected(string attribute, string expected, object? value)
        {
            return new DataverseMappingException(attribute, expected, TypeOf(value));
        }

        private static string TypeOf(object? value)
        {
            return value == null ? "an empty value" : value.GetType().Name;
        }
    }
}
