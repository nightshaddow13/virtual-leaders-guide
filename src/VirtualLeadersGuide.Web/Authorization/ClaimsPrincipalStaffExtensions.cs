using System.Security.Claims;

namespace VirtualLeadersGuide.Web.Authorization;

/// <summary>Staff checks on a signed-in user's sign-in cookie principal.</summary>
public static class ClaimsPrincipalStaffExtensions
{
    /// <summary>
    /// Whether <paramref name="user"/> is Staff - holds any Role, Admin or Director (CONTEXT.md's Staff).
    /// </summary>
    /// <param name="user">The user, as populated from the sign-in cookie.</param>
    /// <remarks>
    /// Only tests that some <see cref="ClaimTypes.Role"/> claim is present, deliberately without parsing it:
    /// an unscoped Director (ADR-0035) is Staff even though their Role grants access to no Event yet, and
    /// what that Role actually allows is <see cref="EventAccessView"/>'s question, not this one's. Like
    /// <see cref="EventAccessView"/>, it's a routing/rendering hint read from claims stamped at sign-in, never
    /// the authority - Api's response is (ADR-0031).
    /// </remarks>
    public static bool IsStaff(this ClaimsPrincipal user) => user.HasClaim(c => c.Type == ClaimTypes.Role);
}
