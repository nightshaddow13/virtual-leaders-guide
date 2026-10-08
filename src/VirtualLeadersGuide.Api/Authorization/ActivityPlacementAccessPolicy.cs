using System.Security.Claims;

namespace VirtualLeadersGuide.Api.Authorization;

/// <summary>
/// What one request's signed-in caller may do to <c>ActivityPlacement</c> rows - the enforcement rules
/// <c>ActivityPlacementResourceDefinition</c> applies to <c>/api/placements</c>.
/// </summary>
/// <remarks>
/// Delegates every check to <see cref="EventAccessPolicy"/> rather than re-parsing role claims, the same
/// shape as <see cref="ActivityAccessPolicy"/> - a Placement is visible and writable exactly when its
/// Activity's Event is (ADR-0069's posture extended here: placing an Activity is as day-to-day a write as
/// authoring its Description). A separate type from <see cref="ActivityAccessPolicy"/> nonetheless, for the
/// same reason that type is separate from <see cref="EventAccessPolicy"/> - a future story narrowing just
/// Placement's access has exactly one place to do it without touching Activity's own policy.
/// </remarks>
public sealed class ActivityPlacementAccessPolicy
{
    private readonly EventAccessPolicy _eventPolicy;

    /// <summary>Builds the policy for <paramref name="user"/>'s role claims.</summary>
    /// <param name="user">The authenticated caller, as populated from the internal JWT (ADR-0007).</param>
    public ActivityPlacementAccessPolicy(ClaimsPrincipal user) => _eventPolicy = new EventAccessPolicy(user);

    /// <summary>Whether this caller may read every Placement, not just ones on an assigned Event.</summary>
    public bool IsAdmin => _eventPolicy.IsAdmin;

    /// <summary>The Event ids a Director claim assigns this caller to - empty for an Admin or a bare User.</summary>
    public IReadOnlySet<Guid> AssignedEventIds => _eventPolicy.AssignedEventIds;

    /// <summary>Whether this caller may read a Placement on the Event identified by <paramref name="eventId"/>.</summary>
    public bool CanRead(Guid eventId) => _eventPolicy.CanRead(eventId);

    /// <summary>
    /// Whether this caller may create, update, or delete a Placement on the Event identified by
    /// <paramref name="eventId"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately <see cref="EventAccessPolicy.CanRead"/>, not <see cref="EventAccessPolicy.CanUpdate"/> -
    /// mirrors <see cref="ActivityAccessPolicy.CanWrite"/>'s own reasoning. Read and write coincide here on
    /// purpose; a future story narrowing just one of them has exactly one place to split.
    /// </remarks>
    public bool CanWrite(Guid eventId) => _eventPolicy.CanRead(eventId);
}
