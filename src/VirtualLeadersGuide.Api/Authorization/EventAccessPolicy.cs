using System.Security.Claims;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Authorization;

/// <summary>
/// What one request's signed-in caller may do to <c>Event</c> rows, derived once from their
/// <see cref="ClaimTypes.Role"/> claims (ADR-0007, ADR-0017) - the enforcement rules
/// <c>EventResourceDefinition</c> applies to <c>/api/events</c>.
/// </summary>
/// <remarks>
/// Pure and claims-only, deliberately: no database round-trip re-checks assignment (ADR-0007's amendment) -
/// a Director's <see cref="AssignedEventIds"/> can lag a grant/revocation by up to the token's lifetime,
/// which is the accepted trade-off, not a bug here. Mirrors CONTEXT.md's Admin/Director entries: Admin is
/// a superset that can create/edit/delete any Event; Director gets read/edit on only the Events they're
/// assigned to, never create or delete. Claim parsing itself is <see cref="RoleClaims.Parse"/>, shared with
/// <see cref="RoleGrantAccessPolicy"/> (P2-8, #17) rather than duplicated here.
/// </remarks>
public sealed class EventAccessPolicy
{
    private readonly bool _isAdmin;
    private readonly IReadOnlySet<Guid> _assignedEventIds;

    /// <summary>Builds the policy for <paramref name="user"/>'s role claims.</summary>
    /// <param name="user">The authenticated caller, as populated from the internal JWT (ADR-0007).</param>
    public EventAccessPolicy(ClaimsPrincipal user)
    {
        var assignedEventIds = new HashSet<Guid>();
        var isAdmin = false;

        foreach ((string roleName, Guid? eventId) in RoleClaims.Parse(user))
        {
            if (roleName == RoleNames.Admin && eventId is null)
            {
                isAdmin = true;
            }
            else if (roleName == RoleNames.Director && eventId is Guid directorEventId)
            {
                assignedEventIds.Add(directorEventId);
            }
        }

        _isAdmin = isAdmin;
        _assignedEventIds = assignedEventIds;
    }

    /// <summary>The Event ids a Director claim assigns this caller to - empty for an Admin or a bare User.</summary>
    public IReadOnlySet<Guid> AssignedEventIds => _assignedEventIds;

    /// <summary>Whether this caller may read every Event, not just ones in <see cref="AssignedEventIds"/>.</summary>
    public bool IsAdmin => _isAdmin;

    /// <summary>Whether this caller may read the Event identified by <paramref name="eventId"/>.</summary>
    public bool CanRead(Guid eventId) => _isAdmin || _assignedEventIds.Contains(eventId);

    /// <summary>Whether this caller may update the Event identified by <paramref name="eventId"/>.</summary>
    public bool CanUpdate(Guid eventId) => _isAdmin || _assignedEventIds.Contains(eventId);

    /// <summary>Whether this caller may create a new Event - Admin-only (CONTEXT.md's Director entry).</summary>
    public bool CanCreate => _isAdmin;

    /// <summary>Whether this caller may delete an Event - Admin-only (CONTEXT.md's Director entry).</summary>
    public bool CanDelete => _isAdmin;
}
