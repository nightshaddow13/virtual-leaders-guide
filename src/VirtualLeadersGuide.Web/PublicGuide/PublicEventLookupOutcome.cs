namespace VirtualLeadersGuide.Web.PublicGuide;

/// <summary>Outcomes <see cref="PublicEventClient.GetBySlugAsync"/> distinguishes.</summary>
public enum PublicEventLookupOutcome
{
    Success,

    /// <remarks>
    /// An unknown Slug and a <c>Draft</c> Event are indistinguishable here by design - see
    /// <c>PublicGuideEndpoints</c>'s remarks.
    /// </remarks>
    NotFound
}
