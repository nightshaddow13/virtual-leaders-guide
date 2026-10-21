namespace VirtualLeadersGuide.Web.Activities;

/// <summary>Outcomes <see cref="ApiActivityClient.GetActivityAsync"/> distinguishes.</summary>
public enum ActivityReadOutcome
{
    Success,

    /// <remarks>
    /// Api's <c>ActivityResourceDefinition</c> returns 403 for an Activity outside the caller's access
    /// (ADR-0069) - from the caller's point of view this is indistinguishable from "no such Activity",
    /// matching <c>EventReadOutcome.Forbidden</c>'s posture for Event.
    /// </remarks>
    Forbidden,

    /// <remarks>
    /// Unlike Event, an Admin gets a real 404 for an unknown Activity id - ADR-0069's single-resource lookup
    /// only 403s a *non-Admin*; an Admin short-circuits before that check runs.
    /// </remarks>
    NotFound
}
