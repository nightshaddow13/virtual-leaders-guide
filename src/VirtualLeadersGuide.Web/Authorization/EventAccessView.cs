using System.Security.Claims;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.Authorization;

/// <summary>
/// The Web-side read of a signed-in user's Event access, built from their sign-in cookie's
/// <see cref="ClaimTypes.Role"/> claims - a rendering hint for what UI to show, never the authority.
/// </summary>
/// <remarks>
/// Mirrors <c>Api.Authorization.EventAccessPolicy</c>'s claim parsing, but can't reuse it directly - that
/// type (and the <c>RoleClaims</c> loop it shares with <c>RoleGrantAccessPolicy</c>) is
/// <see langword="internal"/> to Api, and Web has no project reference to Api. Both sides read the same
/// claim shape via <see cref="RoleClaimValue"/>, so they can't disagree about how to parse a claim, only
/// about what to conclude from one - and Api's response is always the authority (ADR-0031). The sign-in
/// cookie's claims are stamped once at sign-in and can lag a grant change by up to a sign-in cycle
/// (<c>ApplicationUserClaimsPrincipalFactory</c>'s remarks) - a demoted Admin still reads as an Admin here
/// until they sign in again, which is exactly the gap <c>ApiEventClient</c>'s
/// <see cref="Events.EventWriteOutcome.Forbidden"/> exists to catch at write time.
/// </remarks>
public sealed class EventAccessView
{
    private readonly bool _isAdmin;
    private readonly IReadOnlySet<Guid> _assignedEventIds;

    /// <summary>Builds the view from <paramref name="user"/>'s role claims.</summary>
    /// <param name="user">The signed-in user, as populated from the sign-in cookie.</param>
    public EventAccessView(ClaimsPrincipal user)
    {
        var assignedEventIds = new HashSet<Guid>();
        var isAdmin = false;

        foreach (Claim claim in user.FindAll(ClaimTypes.Role))
        {
            if (!RoleClaimValue.TryParse(claim.Value, out string roleName, out Guid? eventId))
            {
                continue;
            }

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

    /// <summary>Whether this user is an Admin - can read and edit every Event, and create or delete any.</summary>
    public bool IsAdmin => _isAdmin;

    /// <summary>The Event ids a Director claim assigns this user to - empty for an Admin or a bare User.</summary>
    public IReadOnlySet<Guid> AssignedEventIds => _assignedEventIds;

    /// <summary>Whether this user should see the Event identified by <paramref name="eventId"/> in their dashboard.</summary>
    public bool CanReadEvent(Guid eventId) => _isAdmin || _assignedEventIds.Contains(eventId);

    /// <summary>Whether this user should see edit controls for Event details - Admin-only (ADR-0031).</summary>
    public bool CanEditEventDetails => _isAdmin;
}
