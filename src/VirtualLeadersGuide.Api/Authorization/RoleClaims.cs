using System.Security.Claims;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Authorization;

/// <summary>
/// The single place that walks a <see cref="ClaimsPrincipal"/>'s <see cref="ClaimTypes.Role"/> claims and
/// parses each one via <see cref="RoleClaimValue.TryParse"/>, silently skipping any claim that doesn't parse.
/// </summary>
/// <remarks>
/// Shared by <see cref="EventAccessPolicy"/> and <see cref="RoleGrantAccessPolicy"/> (P2-8, #17; ADR-0033) so
/// the two access-policy types - which answer different questions about the same claims - don't each carry
/// their own copy of this parsing loop.
/// </remarks>
internal static class RoleClaims
{
    /// <summary>Parses every well-formed role claim on <paramref name="user"/>.</summary>
    /// <param name="user">The authenticated caller, as populated from the internal JWT (ADR-0007).</param>
    /// <returns>
    /// One <c>(RoleName, EventId)</c> pair per claim that parses via <see cref="RoleClaimValue.TryParse"/> - a
    /// <see langword="null"/> <c>EventId</c> means a platform-wide grant. Malformed claims are skipped, not
    /// surfaced as an error.
    /// </returns>
    public static IEnumerable<(string RoleName, Guid? EventId)> Parse(ClaimsPrincipal user)
    {
        foreach (Claim claim in user.FindAll(ClaimTypes.Role))
        {
            if (RoleClaimValue.TryParse(claim.Value, out string roleName, out Guid? eventId))
            {
                yield return (roleName, eventId);
            }
        }
    }
}
