using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;
using Microsoft.AspNetCore.Identity;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>The ASP.NET Core Identity credential row, and — per ADR-0024 — the person itself.</summary>
/// <remarks>
/// See ADR-0024 for why there's no separate domain <c>User</c> row, why <c>/api/users</c> exposure is
/// attribute-gated (the containment guarantee credential columns rely on), and why this implements
/// <see cref="IIdentifiable{T}"/> directly rather than deriving from JsonApiDotNetCore's <c>Identifiable&lt;T&gt;</c>.
/// </remarks>
[Resource(PublicName = "users", GenerateControllerEndpoints = JsonApiEndpoints.Query)]
public class ApplicationUser : IdentityUser, IIdentifiable<string>
{
    [Attr]
    public string? DisplayName { get; set; }

    /// <remarks>
    /// Re-declared here purely to attach <c>[Attr]</c> — JsonApiDotNetCore's resource-graph scan doesn't
    /// pick up attributes on a base class's own property, so the base <see cref="IdentityUser.Email"/> isn't
    /// otherwise reachable.
    /// </remarks>
    [Attr]
    public override string? Email
    {
        get => base.Email;
        set => base.Email = value;
    }

    /// <remarks>
    /// Explicit interface implementation, not a plain public property, so its nullability ties exactly to
    /// <see cref="IIdentifiable{T}"/>'s own annotations rather than approximating them.
    /// </remarks>
    string? IIdentifiable.StringId
    {
        get => Id;
        set => Id = value ?? Id;
    }

    /// <remarks>
    /// Only meaningful for JsonApiDotNetCore's atomic:operations extension (client-generated ids within one
    /// request) — unused, since nothing in this app performs atomic operations against <c>/api/users</c>.
    /// </remarks>
    string? IIdentifiable.LocalId { get; set; }
}
