namespace VirtualLeadersGuide.Web.PublicGuide;

/// <summary>Outcomes <see cref="PublicEventClient.CheckPasscodeAsync"/> distinguishes.</summary>
public enum PasscodeCheckOutcome
{
    Matched,

    /// <summary>The Event exists (and isn't <c>Draft</c>) but the guess was wrong.</summary>
    NotMatched,

    /// <remarks>
    /// An unknown Slug and a <c>Draft</c> Event are indistinguishable here by design - see
    /// <c>PublicGuideEndpoints</c>'s remarks.
    /// </remarks>
    EventNotFound
}
