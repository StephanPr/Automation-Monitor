namespace AutomationMonitor.Core.Models
{
    /// <summary>
    /// The identifier kinds AM-42 measures. They are separate kinds on purpose: a Power Platform environment id
    /// and a Dataverse organization id name different things, and a comparison between them would be a question
    /// no operator can act on. See the identity section of <c>docs/provider-capabilities.md</c>.
    /// </summary>
    public enum IdentityKind
    {
        /// <summary>The Power Platform environment that hosts the Dataverse environment.</summary>
        PowerPlatformEnvironmentId,

        /// <summary>The Dataverse organization a request was answered by.</summary>
        DataverseOrganizationId
    }

    /// <summary>
    /// What a source said about one identifier, stated without the identifier. Four answers, because "it was
    /// blank", "it is not an identifier" and "this build could not ask" are three different findings: the first
    /// says the source does not carry the value, the second says a defect in this build may be misreading it,
    /// and the third says the question was never put. Collapsing them is how an unmeasured source starts to look
    /// like a measured one.
    /// </summary>
    public enum IdentityAvailability
    {
        /// <summary>A value arrived and is shaped like an identifier.</summary>
        Present,

        /// <summary>The source answered and offered nothing: blank, or the all-zero identifier.</summary>
        Missing,

        /// <summary>The source offered something, but it is not a well-formed identifier of that kind.</summary>
        Invalid,

        /// <summary>This build could not reach the source, so no value was requested or read.</summary>
        Unavailable
    }

    /// <summary>The answer of comparing two observations of the same kind.</summary>
    public enum IdentityMatch
    {
        /// <summary>Both usable, and the same identifier.</summary>
        Same,

        /// <summary>Both usable, and different identifiers.</summary>
        Different,

        /// <summary>Not comparable: different kinds, or one side is not a usable identifier.</summary>
        NotComparable
    }
}
