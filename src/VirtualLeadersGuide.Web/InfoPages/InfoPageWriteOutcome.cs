namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>
/// Outcomes <see cref="ApiInfoPageClient.CreateAsync"/>, <see cref="ApiInfoPageClient.UpdateAsync"/>, and
/// <see cref="ApiInfoPageClient.DeleteAsync"/> distinguish.
/// </summary>
public enum InfoPageWriteOutcome
{
    Success,

    /// <remarks>
    /// The caller isn't an Admin or an assigned Director (ADR-0059) - covers both an unassigned Director's
    /// write and the claim-lag case where a since-removed Director's cookie still says otherwise.
    /// </remarks>
    Forbidden,

    /// <remarks>
    /// Api's <c>InfoPageResourceDefinition</c> rejected the write with 422 - in practice only reachable via
    /// the unknown-Event pointer (<c>/data/attributes/eventId</c>), which normal navigation can't trigger
    /// since <c>EventId</c> always comes from an already-verified route.
    /// </remarks>
    Invalid,

    /// <remarks>
    /// <see cref="ApiInfoPageClient.DeleteAsync"/> only - the InfoPage was already gone (a stale grid, or a
    /// concurrent delete winning the race). Treated as silent success, matching <c>EventWriteOutcome.NotFound</c>.
    /// </remarks>
    NotFound
}
