using System.Security.Claims;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Authorization;

/// <summary>
/// What one request's signed-in caller may do to <c>UserRole</c> grants, derived once from their
/// <see cref="ClaimTypes.Role"/> claims (ADR-0007, ADR-0033) - the enforcement rules
/// <c>UserRoleResourceDefinition</c> applies to <c>/api/roleGrants</c>.
/// </summary>
/// <remarks>
/// Pure and claims-only, deliberately, same posture as <see cref="EventAccessPolicy"/> - the two share
/// <see cref="RoleClaims.Parse"/> rather than each walking <see cref="ClaimTypes.Role"/> claims independently.
/// Unlike <see cref="EventAccessPolicy"/>, this type has no per-row scoping: an Admin may read and manage
/// every grant (except an Admin-role one, see <see cref="CanWrite"/>), a non-Admin may read or manage none.
/// Because a non-Admin's visible set is never partially narrowed - only ever all or nothing - a request from
/// one is rejected outright rather than silently filtered, even for a collection read (ADR-0033's
/// generalization of ADR-0031's asymmetry rule).
/// </remarks>
public sealed class RoleGrantAccessPolicy
{
    private readonly bool _isAdmin;

    /// <summary>Builds the policy for <paramref name="user"/>'s role claims.</summary>
    /// <param name="user">The authenticated caller, as populated from the internal JWT (ADR-0007).</param>
    public RoleGrantAccessPolicy(ClaimsPrincipal user)
    {
        _isAdmin = RoleClaims.Parse(user).Any(claim => claim.RoleName == RoleNames.Admin && claim.EventId is null);
    }

    /// <summary>Whether this caller may read every <c>UserRole</c> grant, including Admin grants.</summary>
    public bool IsAdmin => _isAdmin;

    /// <summary>Whether this caller may read grants at all - Admin-only (ADR-0033).</summary>
    public bool CanRead => _isAdmin;

    /// <summary>
    /// Whether this caller may create or delete a grant for <paramref name="roleId"/>: Admin-only, and never
    /// for <see cref="RoleIds.Admin"/> itself - Admin grants stay owned by ADR-0008's config allowlist, which
    /// silently reverts any Admin <c>UserRole</c> row this resource could otherwise write on the grantee's
    /// next login, and this resource should never be a path to granting Admin regardless (ADR-0033).
    /// </summary>
    /// <param name="roleId">The <c>Role.Id</c> the write targets (<see cref="RoleIds"/>).</param>
    public bool CanWrite(int roleId) => _isAdmin && roleId != RoleIds.Admin;
}
