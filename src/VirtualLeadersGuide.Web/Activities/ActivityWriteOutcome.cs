namespace VirtualLeadersGuide.Web.Activities;

/// <summary>Outcomes <see cref="ApiActivityClient.CreateAsync"/> distinguishes.</summary>
public enum ActivityWriteOutcome
{
    Success,

    /// <remarks>
    /// The caller isn't an Admin or an assigned Director (ADR-0069) - covers both an unassigned Director's
    /// write and the claim-lag case where a since-removed Director's cookie still says otherwise.
    /// </remarks>
    Forbidden,

    /// <remarks>
    /// Api's <c>ActivityResourceDefinition</c> rejected the write with 422 - in practice only reachable via
    /// the unknown-Event pointer (<c>/data/attributes/eventId</c>), which normal navigation can't trigger
    /// since <c>EventId</c> always comes from an already-verified route.
    /// </remarks>
    Invalid
}
