namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// Well-known values for the short-lived internal JWT <c>Web</c> mints to forward a signed-in user's identity
/// to <c>Api</c> (ADR-0007), shared here so the minting side (<c>Web</c>'s <c>InternalJwtProvider</c>) and the
/// validating side (<c>Api</c>'s <c>Program.cs</c>) cannot drift on scheme name, issuer, audience, or lifetime.
/// </summary>
/// <remarks>
/// This token is separate from, and travels alongside, the static <c>X-Internal-Key</c> header (ADR-0002) -
/// the two answer different trust questions ("this call came from Web" vs. "this call is on behalf of this
/// signed-in user") and are validated by two independent authentication schemes on <c>Api</c> (see
/// <c>docs/adr/0015-internal-key-validated-via-authentication-handler.md</c>'s amendment for how the two
/// compose under one authorization policy).
/// </remarks>
public static class InternalJwtDefaults
{
    /// <summary>
    /// The name of the <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> authentication scheme
    /// <c>Api</c> registers to validate this token, distinct from <c>InternalApiKeyDefaults.AuthenticationScheme</c>.
    /// </summary>
    public const string AuthenticationScheme = "InternalJwt";

    /// <summary>
    /// The name of the authorization policy that requires a request be authenticated via
    /// <see cref="AuthenticationScheme"/> specifically, not merely via <c>X-Internal-Key</c> alone. Applied to
    /// <c>Api</c>'s JSON:API resource controllers only (<c>/api/*</c>) - see the ADR-0015 amendment for why
    /// <c>/internal/*</c> endpoints deliberately stay off this policy.
    /// </summary>
    public const string PolicyName = "RequireInternalUser";

    /// <summary>
    /// The token's <c>iss</c> (issuer) claim value - always <c>Web</c>, the only project that mints this token.
    /// </summary>
    public const string Issuer = "vlg-web";

    /// <summary>
    /// The token's <c>aud</c> (audience) claim value - always <c>Api</c>, the only project that validates this token.
    /// </summary>
    public const string Audience = "vlg-api";

    /// <summary>
    /// How long a minted token remains valid. Kept short (per ADR-0007) because <c>Api</c> authorizes purely
    /// from the token's claims and never re-queries role/Event-assignment data per request - a grant or
    /// revocation can take up to this long to reach an already-connected session.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How far ahead of a cached token's expiry <c>Web</c>'s <c>InternalJwtProvider</c> re-mints a fresh one,
    /// rather than risking a request going out with a token that expires mid-flight.
    /// </summary>
    public static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The configuration key both <c>Api</c> (validation) and <c>Web</c> (minting) read the shared symmetric
    /// signing key from, delivered via the <c>internal-jwt-key</c> Aspire secret parameter (see
    /// <c>AppHost.cs</c>), mirroring how <c>InternalApi:Key</c> delivers <c>internal-api-key</c>.
    /// </summary>
    public const string SigningKeyConfigurationKey = "InternalJwt:SigningKey";
}
