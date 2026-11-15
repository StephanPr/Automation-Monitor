using System;
using System.Globalization;

namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// What a stored resource scope actually is, as far as this build can tell. The states are separate
    /// because they license different sentences: only <see cref="ResourceScopeState.Qualified"/> may be
    /// treated as an identity, and none of them says the current connection was authenticated against it.
    /// </summary>
    public enum ResourceScopeState
    {
        /// <summary>No scope was recorded. Identity is unverified, never "the environment I am in now".</summary>
        Unverified = 0,

        /// <summary>A supported kind token plus a well-formed identifier: this build knows what it names.</summary>
        Qualified = 1,

        /// <summary>
        /// Non-empty text with no kind separator - the shape a scope had before kinds were recorded.
        /// Preserved and comparable as text, never promoted to a qualified identity.
        /// </summary>
        Unqualified = 2,

        /// <summary>A kind-qualified scope whose kind this build does not implement. Preserved verbatim.</summary>
        UnrecognizedKind = 3,

        /// <summary>A supported kind whose identifier is missing or malformed. Never an identity.</summary>
        Invalid = 4
    }

    /// <summary>
    /// Which authority a qualified scope names. Every member must be a resource a build can actually
    /// resolve; a token that is only reserved stays <see cref="ResourceScopeState.UnrecognizedKind"/> until
    /// it is implemented, so a reserved value can never be mistaken for a verified identity.
    /// </summary>
    public enum ResourceScopeKind
    {
        /// <summary>Not a kind this build can interpret, or there is no kind at all.</summary>
        Unrecognized = 0,

        /// <summary>The Dataverse organization a Dataverse row belongs to.</summary>
        DataverseOrganization = 1
    }

    /// <summary>
    /// A provider-qualified resource scope: <c>dataverse-organization:&lt;canonical-guid&gt;</c> today, and the
    /// value that will one day sit in the <c>scopeId</c> and <c>environmentId</c> fields that already exist.
    ///
    /// <para>
    /// The kind token is part of the value on purpose. A Power Platform environment id and a Dataverse
    /// organization id are both GUIDs, and storing one in the other's field is a silent identity swap; a token
    /// makes the two spellings impossible to confuse. The identifier is canonicalised the way
    /// <see cref="CanonicalId"/> canonicalises a workflow id, so dashed, braced and bare spellings of one
    /// organization collapse to one scope.
    /// </para>
    ///
    /// <para>
    /// Two rules bind every use of this type. <strong>Reading is lossless:</strong> <see cref="Read"/> never
    /// fails, never empties a value and never rewrites text it cannot interpret, because a scope this build
    /// cannot classify still belongs to the operator's file. <strong>Authoring is strict:</strong> a qualified
    /// scope can only be built from a real identifier, so a malformed scope can never enter a key. And
    /// <strong>recognition is not proof:</strong> parsing says only that the text names a kind - not that this
    /// session is connected to it, and not that a stored member belongs to it. That evidence arrives through a
    /// completed read and an operator's confirmation, in a later checkpoint.
    /// </para>
    /// </summary>
    public sealed class ResourceScope : IEquatable<ResourceScope?>
    {
        /// <summary>The only kind token this build can resolve to a resource.</summary>
        public const string DataverseOrganizationToken = "dataverse-organization";

        /// <summary>Separates the kind token from the identifier. Fixed text, never derived from a label.</summary>
        public const char Separator = ':';

        private static readonly ResourceScope None = new ResourceScope(
            ResourceScopeState.Unverified, ResourceScopeKind.Unrecognized, string.Empty, string.Empty);

        private ResourceScope(ResourceScopeState state, ResourceScopeKind kind, string text, string identifier)
        {
            State = state;
            Kind = kind;
            Text = text ?? string.Empty;
            Identifier = identifier ?? string.Empty;
        }

        /// <summary>The empty scope: identity not verified. Never a wildcard and never a match.</summary>
        public static ResourceScope Unverified
        {
            get { return None; }
        }

        public ResourceScopeState State { get; }

        public ResourceScopeKind Kind { get; }

        /// <summary>
        /// The text a key or a stored field carries: canonical for a qualified scope, otherwise exactly the
        /// trimmed lower-case text that was read. Never null, empty only when <see cref="IsUnverified"/>.
        /// </summary>
        public string Text { get; }

        /// <summary>Canonical identifier text of a qualified scope; empty for every other state.</summary>
        public string Identifier { get; }

        public bool IsUnverified
        {
            get { return State == ResourceScopeState.Unverified; }
        }

        public bool IsQualified
        {
            get { return State == ResourceScopeState.Qualified; }
        }

        /// <summary>
        /// Non-empty scope text this build keeps but cannot interpret. It must survive the next save
        /// unchanged, and it must never be reported to the operator as "no environment".
        /// </summary>
        public bool IsPreservedUnreadable
        {
            get { return State != ResourceScopeState.Unverified && State != ResourceScopeState.Qualified; }
        }

        /// <summary>Build the one scope form this build can write. An all-zero id is not an organization.</summary>
        public static ResourceScope ForDataverseOrganization(Guid organizationId)
        {
            if (organizationId == Guid.Empty)
            {
                throw new ArgumentException(
                    "An all-zero identifier does not name a Dataverse organization, so it cannot be a scope.",
                    nameof(organizationId));
            }

            string identifier = CanonicalIdentifier(organizationId);
            return new ResourceScope(
                ResourceScopeState.Qualified,
                ResourceScopeKind.DataverseOrganization,
                DataverseOrganizationToken + Separator + identifier,
                identifier);
        }

        /// <summary>
        /// Interpret stored scope text. Never throws and never loses the value: text this build cannot
        /// classify comes back as <see cref="ResourceScopeState.Unqualified"/>,
        /// <see cref="ResourceScopeState.UnrecognizedKind"/> or <see cref="ResourceScopeState.Invalid"/>
        /// carrying that original text.
        /// </summary>
        public static ResourceScope Read(string? storedText)
        {
            string text = CanonicalId.Text(storedText);
            if (text.Length == 0)
            {
                return None;
            }

            int separator = text.IndexOf(Separator);
            if (separator < 0)
            {
                return new ResourceScope(ResourceScopeState.Unqualified, ResourceScopeKind.Unrecognized, text, string.Empty);
            }

            string token = text.Substring(0, separator);
            string value = text.Substring(separator + 1).Trim();
            if (token.Length == 0 || value.Length == 0)
            {
                return new ResourceScope(ResourceScopeState.Invalid, KindOf(token), text, string.Empty);
            }

            if (string.Equals(token, DataverseOrganizationToken, StringComparison.Ordinal))
            {
                Guid parsed;
                if (Guid.TryParse(value, out parsed) && parsed != Guid.Empty)
                {
                    string identifier = CanonicalIdentifier(parsed);
                    return new ResourceScope(
                        ResourceScopeState.Qualified,
                        ResourceScopeKind.DataverseOrganization,
                        token + Separator + identifier,
                        identifier);
                }

                // The kind is known and the value is not: say so rather than keeping a half-identity that a
                // later save could write back as though it had been understood.
                return new ResourceScope(ResourceScopeState.Invalid, ResourceScopeKind.DataverseOrganization, text, string.Empty);
            }

            // A kind this build does not implement. The text is kept as stored (apart from the trim and
            // lower-case the scope field already goes through) so the build that wrote it can still read it.
            return new ResourceScope(ResourceScopeState.UnrecognizedKind, ResourceScopeKind.Unrecognized, text, string.Empty);
        }

        /// <summary>
        /// The scope text a key or a stored field should hold. Only a scope this build recognizes is
        /// canonicalised; anything else keeps the existing <see cref="CanonicalId.Text"/> spelling, so no
        /// identity recorded before the scope contract can move.
        /// </summary>
        public static string NormalizeStored(string? storedText)
        {
            ResourceScope scope = Read(storedText);
            return scope.IsQualified ? scope.Text : CanonicalId.Text(storedText);
        }

        /// <summary>
        /// The gate for callers that need a usable identity rather than a preserved value: true only for a
        /// well-formed qualified scope. An unverified, unqualified, unknown-kind or damaged scope is refused
        /// with the reason to show, so a malformed value can never be treated as a scope.
        /// </summary>
        public static bool TryQualified(string? storedText, out ResourceScope scope, out string? problem)
        {
            scope = Read(storedText);
            problem = null;
            if (scope.IsQualified)
            {
                return true;
            }

            problem = scope.IsUnverified
                ? "no resource scope is recorded"
                : "the resource scope is not one this version can use (" + scope.State + ")";
            return false;
        }

        /// <summary>
        /// Whether two scopes name the same resource. False unless both are qualified, of the same kind, with
        /// the same canonical identifier: preserved or unverified text is never evidence of sameness, and two
        /// kinds that happen to hold the same GUID name different resources.
        /// </summary>
        public bool IsSameResourceAs(ResourceScope? other)
        {
            if (other == null)
            {
                return false;
            }

            return IsQualified
                && other.IsQualified
                && Kind == other.Kind
                && string.Equals(Identifier, other.Identifier, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether two stored scope values name the same environment for every consumer that has to group
        /// saved content by environment - listing a catalog, checking a name clash, finding the settings
        /// profile of the connected organization. It is one rule on purpose: two answers to "same environment"
        /// is how a list ends up named twice or an organization ends up with two profiles.
        ///
        /// <para>
        /// Three arms, and the order matters. Two unverified scopes name the same thing ("nothing was
        /// recorded"), which is what keeps a legacy file's settings together; an unverified scope never names
        /// a qualified one. Two scopes this build can canonicalise are compared on what they name, so a brace,
        /// a dash or a change of case cannot split one organization in two. Everything else - a bare legacy
        /// environment id, a kind token this build does not implement, a damaged identifier - is compared as
        /// the text it is, case-insensitively, because a value this build cannot interpret is not evidence
        /// about any other value. Unlike the catalog's listing rule, an unverified stored scope is
        /// not treated as belonging to the connected environment: a name clash and a profile are per
        /// environment, and an unrecorded environment has no identity to share.
        /// </para>
        /// </summary>
        public static bool NamesSameEnvironment(string? left, string? right)
        {
            ResourceScope a = Read(left);
            ResourceScope b = Read(right);

            if (a.IsUnverified || b.IsUnverified)
            {
                return a.IsUnverified && b.IsUnverified;
            }

            if (a.IsSameResourceAs(b))
            {
                return true;
            }

            return string.Equals(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(ResourceScope? other)
        {
            if (other == null)
            {
                return false;
            }

            return State == other.State
                && Kind == other.Kind
                && string.Equals(Text, other.Text, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as ResourceScope);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)State * 31 + (int)Kind;
                return (hash * 397) ^ Text.GetHashCode();
            }
        }

        /// <summary>The stored or canonical text. A scope is data; the human wording is <see cref="ResourceScopeLabel"/>.</summary>
        public override string ToString()
        {
            return Text;
        }

        private static ResourceScopeKind KindOf(string token)
        {
            return string.Equals(token, DataverseOrganizationToken, StringComparison.Ordinal)
                ? ResourceScopeKind.DataverseOrganization
                : ResourceScopeKind.Unrecognized;
        }

        private static string CanonicalIdentifier(Guid value)
        {
            return value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
        }
    }
}
