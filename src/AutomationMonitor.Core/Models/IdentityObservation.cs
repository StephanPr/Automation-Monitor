using System;
using System.Globalization;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// One measured identifier source, recorded as shape only (AM-42).
    ///
    /// <para>
    /// There is deliberately no member that could hold the value. The evaluation that produces this object sees
    /// the value as a parameter and keeps nothing from it but the derived facts: whether it was there, whether
    /// it parsed as an identifier, and how long it was. Length is reported because it separates a dashed GUID
    /// (36 characters) from a brace-wrapped one (38) from a name (anything else), which is exactly the
    /// distinction this probe exists to make; on its own it contributes nothing to reconstructing a value.
    /// </para>
    /// </summary>
    public sealed class IdentityObservation
    {
        public IdentityObservation(
            IdentityKind kind,
            string source,
            IdentityAvailability availability,
            bool isIdentifierShape,
            bool isAllZeroIdentifier,
            int characterCount)
        {
            Kind = kind;
            Source = string.IsNullOrWhiteSpace(source) ? "the source" : source.Trim();
            Availability = availability;
            IsIdentifierShape = isIdentifierShape;
            IsAllZeroIdentifier = isAllZeroIdentifier;
            CharacterCount = characterCount < 0 ? 0 : characterCount;
        }

        /// <summary>Which identifier this observation is about.</summary>
        public IdentityKind Kind { get; }

        /// <summary>
        /// A fixed label this build owns ("the connection record"), never text that came from the host or the
        /// environment: provider-supplied strings have no business being copied into a diagnostic line.
        /// </summary>
        public string Source { get; }

        public IdentityAvailability Availability { get; }

        /// <summary>The value parsed as a GUID, the shape both identifier kinds are documented to have.</summary>
        public bool IsIdentifierShape { get; }

        /// <summary>
        /// The source stated the all-zero identifier. That is "not stated", not an environment named zero - the
        /// same rule <see cref="WorkflowIdentity"/> applies to a row's id.
        /// </summary>
        public bool IsAllZeroIdentifier { get; }

        public int CharacterCount { get; }

        /// <summary>Only a present, well-formed identifier may take part in a comparison.</summary>
        public bool IsUsable
        {
            get { return Availability == IdentityAvailability.Present; }
        }

        /// <summary>
        /// Observe one source. The value is a parameter and is never stored: every kept fact is derived from it
        /// before this method returns.
        /// </summary>
        /// <param name="reachable">
        /// False when the source could not be read at all. That is reported as Unavailable rather than being
        /// written up as a blank, which would read as "this connection has no environment id".
        /// </param>
        public static IdentityObservation Observe(IdentityKind kind, string source, string? value, bool reachable = true)
        {
            if (!reachable)
            {
                return new IdentityObservation(kind, source, IdentityAvailability.Unavailable, false, false, 0);
            }

            string? text = string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
            if (text == null)
            {
                return new IdentityObservation(kind, source, IdentityAvailability.Missing, false, false, 0);
            }

            if (!Guid.TryParse(text, out Guid parsed))
            {
                return new IdentityObservation(kind, source, IdentityAvailability.Invalid, false, false, text.Length);
            }

            if (parsed == Guid.Empty)
            {
                return new IdentityObservation(kind, source, IdentityAvailability.Missing, true, true, text.Length);
            }

            return new IdentityObservation(kind, source, IdentityAvailability.Present, true, false, text.Length);
        }

        /// <summary>
        /// The one wording for an observation: the kind of answer, then the shape. No other Core type should
        /// spell these out, so a report line and a status line cannot drift apart.
        /// </summary>
        public string Describe()
        {
            string answer;
            switch (Availability)
            {
                case IdentityAvailability.Present:
                    answer = "present";
                    break;
                case IdentityAvailability.Invalid:
                    answer = "present but not a valid identifier";
                    break;
                case IdentityAvailability.Unavailable:
                    answer = "not read by this build";
                    break;
                default:
                    answer = IsAllZeroIdentifier
                        ? "not stated (the source returned the all-zero identifier)"
                        : "not stated";
                    break;
            }

            if (Availability == IdentityAvailability.Unavailable)
            {
                return answer;
            }

            // A blank offers no shape to describe, and saying "non-GUID shape, 0 character(s)" would read as
            // though something were measured and found wanting. Only a value that arrived has a shape.
            if (Availability == IdentityAvailability.Missing && !IsAllZeroIdentifier)
            {
                return answer;
            }

            string shape = IsIdentifierShape ? "GUID" : "non-GUID";

            return answer + ", " + shape + " shape, "
                + CharacterCount.ToString(CultureInfo.InvariantCulture) + " character(s)";
        }
    }
}
