namespace VirtualLeadersGuide.Web.Events;

/// <summary>Outcomes <see cref="ApiEventClient.CreateAsync"/> and <see cref="ApiEventClient.UpdateAsync"/> distinguish.</summary>
public enum EventWriteOutcome
{
    Success,

    /// <remarks>
    /// The caller isn't an Admin (ADR-0031: only an Admin may create, update, or delete an Event) - covers
    /// both a Director attempting any write and the claim-lag case where a since-demoted Admin's cookie
    /// still says otherwise.
    /// </remarks>
    Forbidden,

    /// <remarks>
    /// Api's <c>EventResourceDefinition</c> rejected a Name/Slug collision with 409 - see the pointers a
    /// caller reads off the write method's return value to route the error to the offending field.
    /// </remarks>
    Conflict,

    /// <remarks>
    /// Api's <c>EventResourceDefinition</c> rejected the write with 422 - a well-formed request that broke a
    /// business rule rather than colliding with another Event (ADR-0042), namely <c>Event.StartsAt</c>/
    /// <c>Event.EndsAt</c>'s ordering rules. Same pointer-reading contract as <see cref="Conflict"/>.
    /// </remarks>
    Invalid
}
