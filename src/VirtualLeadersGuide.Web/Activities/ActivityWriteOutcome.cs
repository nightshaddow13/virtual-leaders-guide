namespace VirtualLeadersGuide.Web.Activities;

/// <summary>
/// Outcomes <see cref="ApiActivityClient.CreateAsync"/>, <see cref="ApiActivityClient.UpdateAsync"/>, and
/// <see cref="ApiActivityClient.DeleteAsync"/> distinguish.
/// </summary>
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
    /// since <c>EventId</c> always comes from an already-verified route. Create-only: an update body carries no
    /// <c>eventId</c>, so <see cref="ApiActivityClient.UpdateAsync"/> never returns it.
    /// </remarks>
    Invalid,

    /// <remarks>
    /// <see cref="ApiActivityClient.DeleteAsync"/> only - the Activity was already gone (a stale list, or a
    /// concurrent delete winning the race). Treated as silent success, matching <c>InfoPageWriteOutcome.NotFound</c>.
    /// </remarks>
    NotFound
}
