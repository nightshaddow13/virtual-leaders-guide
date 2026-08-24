namespace VirtualLeadersGuide.Web.Events;

/// <summary>Outcomes <see cref="ApiEventClient.GetEventAsync"/> distinguishes.</summary>
public enum EventReadOutcome
{
    Success,

    /// <remarks>
    /// Api's <c>EventResourceDefinition</c> returns 403, not 404, for an Event outside the caller's access
    /// (ADR-0031) - from the caller's point of view this is indistinguishable from "no such Event".
    /// </remarks>
    Forbidden
}
