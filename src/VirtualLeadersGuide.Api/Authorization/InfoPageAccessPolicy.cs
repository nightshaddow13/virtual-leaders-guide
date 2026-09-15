using System.Security.Claims;

namespace VirtualLeadersGuide.Api.Authorization;

/// <summary>
/// What one request's signed-in caller may do to <c>InfoPage</c> rows - the enforcement rules
/// <c>InfoPageResourceDefinition</c> applies to <c>/api/infoPages</c>.
/// </summary>
/// <remarks>
/// Delegates every check to <see cref="EventAccessPolicy"/> rather than re-parsing role claims: the domain
/// rule is "an InfoPage is visible and writable exactly when its Event is visible" (ADR-0059), so expressing
/// that as delegation makes the two structurally unable to drift apart. A separate type from
/// <see cref="EventAccessPolicy"/> nonetheless, because that type's <c>CanCreate</c>/<c>CanUpdate</c>/
/// <c>CanDelete</c> encode Event-<em>details</em> semantics (Admin-only, ADR-0031) that are deliberately wrong
/// here - the same reason <see cref="RoleGrantAccessPolicy"/>/<see cref="ApplicationUserAccessPolicy"/> are
/// their own types rather than reusing <see cref="EventAccessPolicy"/> directly.
/// </remarks>
public sealed class InfoPageAccessPolicy
{
    private readonly EventAccessPolicy _eventPolicy;

    /// <summary>Builds the policy for <paramref name="user"/>'s role claims.</summary>
    /// <param name="user">The authenticated caller, as populated from the internal JWT (ADR-0007).</param>
    public InfoPageAccessPolicy(ClaimsPrincipal user) => _eventPolicy = new EventAccessPolicy(user);

    /// <summary>Whether this caller may read every InfoPage, not just ones on an assigned Event.</summary>
    public bool IsAdmin => _eventPolicy.IsAdmin;

    /// <summary>The Event ids a Director claim assigns this caller to - empty for an Admin or a bare User.</summary>
    public IReadOnlySet<Guid> AssignedEventIds => _eventPolicy.AssignedEventIds;

    /// <summary>Whether this caller may read an InfoPage on the Event identified by <paramref name="eventId"/>.</summary>
    public bool CanRead(Guid eventId) => _eventPolicy.CanRead(eventId);

    /// <summary>
    /// Whether this caller may create, update, or delete an InfoPage on the Event identified by
    /// <paramref name="eventId"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately <see cref="EventAccessPolicy.CanRead"/>, not <see cref="EventAccessPolicy.CanUpdate"/> - a
    /// Director assigned to an Event may fully author its InfoPages, unlike Event details themselves
    /// (ADR-0059). Read and write coincide here on purpose; a future story narrowing just one of them has
    /// exactly one place to split.
    /// </remarks>
    public bool CanWrite(Guid eventId) => _eventPolicy.CanRead(eventId);
}
