using System.Security.Claims;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Authorization;

/// <summary>
/// What one request's signed-in caller may do at <c>/api/users</c>, derived once from their
/// <see cref="ClaimTypes.Role"/> claims (ADR-0007) - the enforcement rule
/// <c>ApplicationUserResourceDefinition</c> applies (P2-12, #43).
/// </summary>
/// <remarks>
/// Pure and claims-only, same posture as <see cref="EventAccessPolicy"/> and
/// <see cref="RoleGrantAccessPolicy"/> - each resource definition gets its own small policy rather than
/// sharing one across unrelated resources. A non-Admin's visible set here is never partially narrowed -
/// every row is visible to an Admin or to nobody - so <c>ApplicationUserResourceDefinition</c> rejects a
/// non-Admin outright, collection or single alike, the same rule ADR-0033 established for
/// <see cref="RoleGrantAccessPolicy"/> and generalized past <c>UserRole</c> specifically.
/// </remarks>
public sealed class ApplicationUserAccessPolicy
{
    private readonly bool _isAdmin;

    /// <summary>Builds the policy for <paramref name="user"/>'s role claims.</summary>
    /// <param name="user">The authenticated caller, as populated from the internal JWT (ADR-0007).</param>
    public ApplicationUserAccessPolicy(ClaimsPrincipal user)
    {
        _isAdmin = RoleClaims.Parse(user).Any(claim => claim.RoleName == RoleNames.Admin && claim.EventId is null);
    }

    /// <summary>Whether this caller may read <c>/api/users</c> at all - Admin-only.</summary>
    public bool CanRead => _isAdmin;
}
