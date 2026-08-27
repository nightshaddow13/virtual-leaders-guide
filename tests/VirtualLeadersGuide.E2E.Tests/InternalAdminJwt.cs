using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Mints a short-lived internal JWT carrying an Admin role claim - shared by <see cref="EventsApiClient"/>
/// and <see cref="UsersApiClient"/>, the two test-side clients that call Admin-gated <c>/api/*</c> resources
/// directly rather than through <see cref="IdentityApiClient"/>'s plain <c>X-Internal-Key</c> channel
/// (<c>/internal/*</c> sits outside <c>/api</c>'s internal-JWT policy - see <see cref="InternalJwtDefaults"/>'s
/// own remarks).
/// </summary>
internal static class InternalAdminJwt
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    /// <summary>
    /// Mints a token good for <see cref="InternalJwtDefaults.Lifetime"/>, with a fresh <see cref="Guid"/>
    /// subject - callers here only ever need Api to accept "some Admin," never a specific person's identity.
    /// </summary>
    /// <param name="signingKey">
    /// The same signing key <c>Api</c> validates against for this run - <see cref="AspireE2EFixture"/>'s own
    /// <c>InternalJwtKey</c> constant.
    /// </param>
    /// <returns>A signed, encoded JWT ready to use as a bearer token.</returns>
    public static string Mint(string signingKey)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, RoleClaimValue.Format(
                new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin }))
        };

        DateTime now = DateTime.UtcNow;
        return TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = InternalJwtDefaults.Issuer,
            Audience = InternalJwtDefaults.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            Expires = now.Add(InternalJwtDefaults.Lifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)
        });
    }
}
