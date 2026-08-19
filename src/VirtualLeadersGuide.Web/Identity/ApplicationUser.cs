using Microsoft.AspNetCore.Identity;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>Web's own <see cref="IdentityUser"/> subclass.</summary>
/// <remarks>
/// Deliberately a separate type from Api's <c>ApplicationUser</c> (<c>Data/</c>), not a shared model -
/// <c>UserManager</c>/<c>SignInManager</c> only need the shape <see cref="IdentityUser"/> already provides,
/// and duplicating it across the Web↔Api boundary keeps the two sides independently deployable (ADR-0022).
/// <c>ApiUserStore</c> maps to/from <c>IdentityUserDto</c> (<c>VirtualLeadersGuide.Identity.Contracts</c>)
/// rather than this type crossing the wire directly.
/// </remarks>
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
