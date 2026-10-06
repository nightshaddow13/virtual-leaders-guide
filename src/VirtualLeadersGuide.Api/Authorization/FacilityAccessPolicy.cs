using System.Security.Claims;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Authorization;

/// <summary>
/// What one request's signed-in caller may do to <c>Facility</c>/<c>FacilityType</c> rows - the enforcement
/// rules <c>FacilityResourceDefinition</c> and <c>FacilityTypeResourceDefinition</c> apply to
/// <c>/api/facilities</c>/<c>/api/facilityTypes</c>.
/// </summary>
/// <remarks>
/// Parses <see cref="RoleClaims"/> directly rather than delegating to <see cref="EventAccessPolicy"/>, unlike
/// <see cref="ActivityAccessPolicy"/>/<see cref="InfoPageAccessPolicy"/> - a Facility has no owning Event to
/// delegate to (ADR-0066), so there's no per-Event visibility set to check against; this policy is flat.
/// <see cref="CanRead"/> is Admin-or-Director (ADR-0070), unlike <see cref="CanWrite"/>'s Admin-only
/// (ADR-0066); <c>FacilityTypeResourceDefinition</c> reuses this same type but only ever consults
/// <see cref="IsAdmin"/>, since that resource's own posture is Admin-only end to end, read included.
/// </remarks>
public sealed class FacilityAccessPolicy
{
    private readonly bool _isAdmin;
    private readonly bool _hasAnyRoleClaim;

    /// <summary>Builds the policy for <paramref name="user"/>'s role claims.</summary>
    /// <param name="user">The authenticated caller, as populated from the internal JWT (ADR-0007).</param>
    public FacilityAccessPolicy(ClaimsPrincipal user)
    {
        bool isAdmin = false;
        bool hasAnyRoleClaim = false;

        foreach ((string roleName, Guid? eventId) in RoleClaims.Parse(user))
        {
            hasAnyRoleClaim = true;

            if (roleName == RoleNames.Admin && eventId is null)
            {
                isAdmin = true;
            }
        }

        _isAdmin = isAdmin;
        _hasAnyRoleClaim = hasAnyRoleClaim;
    }

    /// <summary>Whether this caller holds the platform-wide Admin claim.</summary>
    public bool IsAdmin => _isAdmin;

    /// <summary>
    /// Whether this caller may read every Facility - any recognized Admin or Director claim, platform-wide or
    /// Event-scoped (ADR-0070). A Facility has no owning Event, so unlike <see cref="EventAccessPolicy"/> this
    /// never narrows by assignment - it's binary, not per-row.
    /// </summary>
    public bool CanRead => _hasAnyRoleClaim;

    /// <summary>Whether this caller may create, update, or delete a Facility - Admin-only (ADR-0066).</summary>
    public bool CanWrite => _isAdmin;
}
