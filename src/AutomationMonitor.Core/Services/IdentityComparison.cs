using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Compares two identity observations (AM-42).
    ///
    /// <para>
    /// The caller supplies the equality fact instead of the values, so no identifier has to be retained anywhere
    /// to answer "do these two sources agree". The kind check is the point of this class: a Power Platform
    /// environment id and a Dataverse organization id are different kinds, and comparing them would produce a
    /// "they differ" line that reads like a defect when it is only two different things. Different kinds are
    /// therefore not comparable at all, whatever the values say.
    /// </para>
    /// </summary>
    public static class IdentityComparison
    {
        public static IdentityMatch Evaluate(IdentityObservation? left, IdentityObservation? right, bool valuesAreEqual)
        {
            if (left == null || right == null)
            {
                return IdentityMatch.NotComparable;
            }

            if (left.Kind != right.Kind)
            {
                return IdentityMatch.NotComparable;
            }

            if (!left.IsUsable || !right.IsUsable)
            {
                return IdentityMatch.NotComparable;
            }

            return valuesAreEqual ? IdentityMatch.Same : IdentityMatch.Different;
        }

        /// <summary>The wording for a comparison result. Never names a value.</summary>
        public static string Describe(IdentityMatch match)
        {
            switch (match)
            {
                case IdentityMatch.Same:
                    return "the same identifier";
                case IdentityMatch.Different:
                    return "two different identifiers";
                default:
                    return "not comparable - at least one source did not state a usable identifier";
            }
        }
    }
}
