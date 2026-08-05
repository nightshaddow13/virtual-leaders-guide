using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// Stamps one <see cref="ClaimTypes.Role"/> claim per <see cref="RoleGrantDto"/> onto the sign-in cookie
/// principal, formatted via <see cref="RoleClaimValue.Format"/> - the same formatter the internal JWT
/// (<see cref="InternalJwtProvider"/>) uses, so the cookie and the JWT can never disagree about what a role
/// claim means (P2-5, #14; ADR-0007).
/// </summary>
/// <remarks>
/// Cookie claims are generated once, here, at sign-in, and refreshed only on re-sign-in or a security-stamp
/// bump (see <see cref="Components.Account.IdentityRevalidatingAuthenticationStateProvider"/>, which checks
/// the stamp but does not regenerate claims) - unlike the JWT, which <see cref="InternalJwtProvider"/>
/// re-mints from a fresh grant lookup roughly every 5 minutes. The cookie is <c>Web</c>'s own UI hint (e.g.
/// <c>Dashboard.razor</c>'s "does this user hold any role at all" check); the JWT is what <c>Api</c> actually
/// authorizes from.
/// </remarks>
public sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor,
    AdminAllowlistSynchronizer allowlistSynchronizer)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    /// <inheritdoc/>
    /// <remarks>
    /// Adds one <see cref="ClaimTypes.Role"/> claim per grant returned by
    /// <see cref="AdminAllowlistSynchronizer.SyncAsync"/> - which promotes/demotes the platform-wide Admin
    /// grant to match the Admin allowlist (ADR-0008) before returning the up-to-date grant list, so this same
    /// sign-in already reflects any allowlist change. A <see langword="null"/> result (the user's row no
    /// longer exists on <c>Api</c>) adds no role claims rather than throwing - the existing security-stamp
    /// revalidation signs such a user out on its own within
    /// <see cref="Components.Account.IdentityRevalidatingAuthenticationStateProvider"/>'s next cycle. A
    /// genuine <c>Api</c> outage still surfaces as <see cref="AuthorizationDataUnavailableException"/>, which
    /// <see cref="AdminAllowlistSynchronizer.SyncAsync"/> throws rather than this method swallowing it.
    /// </remarks>
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);

        IReadOnlyList<RoleGrantDto>? grants =
            await allowlistSynchronizer.SyncAsync(user, CancellationToken.None);

        if (grants is not null)
        {
            identity.AddClaims(grants.Select(grant => new Claim(ClaimTypes.Role, RoleClaimValue.Format(grant))));
        }

        return identity;
    }
}
