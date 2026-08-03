using Microsoft.AspNetCore.Identity;

namespace VirtualLeadersGuide.Web.Identity;

// Web's own IdentityUser subclass - deliberately a separate type from Api's ApplicationUser (Data/), not a
// shared model. UserManager/SignInManager only need the shape IdentityUser already provides, and
// duplicating it across the Web<->Api boundary keeps the two sides independently deployable - see
// ADR-0022. ApiUserStore maps to/from IdentityUserDto (VirtualLeadersGuide.Identity.Contracts) rather than
// this type crossing the wire directly.
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
