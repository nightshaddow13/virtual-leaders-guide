namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// Outcomes <see cref="ApiDirectorClient.GrantDirectorRoleAsync"/>, <see cref="ApiDirectorClient.GrantEventAccessAsync"/>,
/// and <see cref="ApiDirectorClient.RemoveEventAccessAsync"/> distinguish.
/// </summary>
public enum GrantWriteOutcome
{
    Created,

    /// <remarks>
    /// <c>/api/roleGrants</c>' duplicate-grant pre-check (<c>UserRoleResourceDefinition.CheckForConflictsAsync</c>)
    /// rejected this exact (user, role, scope) combination with 409 - the User already holds it.
    /// </remarks>
    AlreadyGranted,

    /// <remarks>
    /// <see cref="ApiDirectorClient.RemoveEventAccessAsync"/> only - the Grant was already gone (a stale
    /// page, or a concurrent removal winning the race). A caller treats this as silent success, matching
    /// <c>EventWriteOutcome.NotFound</c>'s own reasoning: the Admin's intent ("this Grant shouldn't exist")
    /// is already satisfied, so there is nothing to surface.
    /// </remarks>
    Removed,

    /// <remarks>Mirrors <see cref="Removed"/> for a delete that found nothing to delete - see its remarks.</remarks>
    NotFound,

    /// <remarks>
    /// Two distinct causes, deliberately not distinguished (P2-18, #113): the caller isn't an Admin
    /// (ADR-0033) - normally unreachable, since only the Users/EventEditor screens that already gate on
    /// Admin call this, but covers the claim-lag case where a since-demoted Admin's cookie still says
    /// otherwise (mirrors <c>EventWriteOutcome.Forbidden</c>) - or, on <see cref="ApiDirectorClient.RemoveEventAccessAsync"/>,
    /// the target User holds Admin (ADR-0051) - normally unreachable too, since <c>EventEditor.razor</c>
    /// already disables that row's button, but covers a page that went stale between render and click.
    /// Both resolve the same way (refresh), so there is no separate outcome for the second cause.
    /// </remarks>
    Forbidden
}
