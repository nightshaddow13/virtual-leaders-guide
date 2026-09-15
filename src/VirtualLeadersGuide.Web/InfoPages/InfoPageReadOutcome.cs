namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>Outcomes <see cref="ApiInfoPageClient.GetInfoPageAsync"/> distinguishes.</summary>
public enum InfoPageReadOutcome
{
    Success,

    /// <remarks>
    /// Api's <c>InfoPageResourceDefinition</c> returns 403 for an InfoPage outside the caller's access
    /// (ADR-0059) - from the caller's point of view this is indistinguishable from "no such InfoPage",
    /// matching <c>EventReadOutcome.Forbidden</c>'s posture for Event.
    /// </remarks>
    Forbidden,

    /// <remarks>
    /// Unlike Event, an Admin gets a real 404 for an unknown InfoPage id - ADR-0059's single-resource lookup
    /// only 403s a *non-Admin*; an Admin short-circuits before that check runs.
    /// </remarks>
    NotFound
}
