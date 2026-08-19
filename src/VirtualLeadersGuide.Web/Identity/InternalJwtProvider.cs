using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// Mints and caches the short-lived internal JWT (ADR-0007, P2-5/#14) that <see cref="InternalApiClient"/>
/// attaches to outbound calls to <c>Api</c>, carrying the signed-in user's id and current role grants.
/// </summary>
/// <remarks>
/// Registered scoped, which in Blazor Server means one instance per circuit - exactly ADR-0007's caching
/// unit. <see cref="GetTokenAsync"/> is the only entry point: it reuses the cached token until it's within
/// <see cref="InternalJwtDefaults.RefreshSkew"/> of expiry, then mints a fresh one inline. There is
/// deliberately no background refresh timer.
/// </remarks>
public sealed class InternalJwtProvider(
    AuthenticationStateProvider authenticationStateProvider,
    ApiRoleGrantClient roleGrantClient,
    IConfiguration configuration)
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private string? _cachedToken;
    private DateTimeOffset _cachedTokenExpiresAt;

    /// <summary>
    /// Returns a valid internal JWT for the current circuit's signed-in user, minting a fresh one if none is
    /// cached or the cached one is within <see cref="InternalJwtDefaults.RefreshSkew"/> of expiry.
    /// </summary>
    /// <param name="cancellationToken">Propagated to the underlying role-grant lookup, if one is needed.</param>
    /// <returns>A signed, encoded JWT ready to use as a bearer token.</returns>
    /// <exception cref="InvalidOperationException">
    /// The current circuit has no signed-in user, or <see cref="InternalJwtDefaults.SigningKeyConfigurationKey"/>
    /// is not configured.
    /// </exception>
    /// <exception cref="AuthorizationDataUnavailableException">
    /// <c>Api</c>'s role-grants endpoint is unreachable or returned an unexpected response. Propagated as-is
    /// from <see cref="ApiRoleGrantClient.GetGrantsAsync"/> rather than degraded into a stale or zero-role
    /// token - whatever outbound call needed this token should fail loudly instead of silently proceeding as
    /// the wrong user.
    /// </exception>
    /// <remarks>
    /// A <see langword="null"/> grants result (as opposed to <see cref="AuthorizationDataUnavailableException"/>)
    /// means the user's row no longer exists on Api (e.g. deleted mid-session), not a genuine outage.
    /// Minting a zero-role token here is the honest representation: the person is still authenticated, they
    /// just hold nothing - and it's self-healing, since the existing security-stamp revalidation
    /// (<c>IdentityRevalidatingAuthenticationStateProvider</c>) signs them out within its own cycle once
    /// <c>UserManager.GetUserAsync</c> also returns null for the same missing row.
    /// </remarks>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null
            && DateTimeOffset.UtcNow < _cachedTokenExpiresAt - InternalJwtDefaults.RefreshSkew)
        {
            return _cachedToken;
        }

        AuthenticationState authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        string? userId = authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            throw new InvalidOperationException(
                "Cannot mint an internal JWT: the current circuit has no signed-in user.");
        }

        IReadOnlyList<RoleGrantDto>? grants = await roleGrantClient.GetGrantsAsync(userId, cancellationToken);

        (string token, DateTimeOffset expiresAt) = Mint(userId, grants);

        _cachedToken = token;
        _cachedTokenExpiresAt = expiresAt;
        return token;
    }

    private (string Token, DateTimeOffset ExpiresAt) Mint(string userId, IReadOnlyList<RoleGrantDto>? grants)
    {
        string? signingKey = configuration[InternalJwtDefaults.SigningKeyConfigurationKey];
        if (string.IsNullOrEmpty(signingKey))
        {
            throw new InvalidOperationException(
                $"{InternalJwtDefaults.SigningKeyConfigurationKey} is not configured.");
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (grants is not null)
        {
            claims.AddRange(grants.Select(grant => new Claim(ClaimTypes.Role, RoleClaimValue.Format(grant))));
        }

        DateTime now = DateTime.UtcNow;
        DateTime expires = now.Add(InternalJwtDefaults.Lifetime);

        string token = TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = InternalJwtDefaults.Issuer,
            Audience = InternalJwtDefaults.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)
        });

        return (token, expires);
    }
}
