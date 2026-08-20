using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>An <see cref="ApplicationUser"/>→<see cref="Role"/> grant, optionally scoped to an <see cref="Event"/>.</summary>
/// <remarks>
/// See ADR-0017 for the three-table shape and why a null <see cref="EventId"/> means platform-wide (Admin)
/// versus a set one scoping the grant to that Event (Director, and future Event-scoped roles), and
/// ADR-0024 for why <see cref="UserId"/> is <see cref="ApplicationUser.Id"/> directly rather than a
/// separate domain <c>User</c> row's id. Exposed at <c>/api/roleGrants</c>, Admin-only (P2-8, #17; ADR-0033),
/// superseding ADR-0017's "never exposed" clause for this type - <see cref="Role"/> stays unexposed.
/// </remarks>
[Resource(PublicName = "roleGrants",
    GenerateControllerEndpoints = JsonApiEndpoints.Query | JsonApiEndpoints.Post | JsonApiEndpoints.Delete)]
public class UserRole : Identifiable<Guid>
{
    /// <summary>The <see cref="ApplicationUser.Id"/> this grant belongs to.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate
        | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required string UserId { get; set; }

    public ApplicationUser? User { get; set; }

    /// <summary>The <see cref="Role.Id"/> this grant holds (see <see cref="RoleIds"/>).</summary>
    /// <remarks>
    /// Not a relationship to a <c>role</c> resource - <see cref="Role"/> itself stays unexposed (ADR-0017's
    /// Consequences, unchanged by ADR-0033), so a caller resolves this against the well-known
    /// <see cref="VirtualLeadersGuide.Identity.Contracts.RoleIds"/> constants instead of an <c>?include=</c>.
    /// </remarks>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate
        | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public int RoleId { get; set; }

    public Role? Role { get; set; }

    /// <remarks>
    /// A real FK against <see cref="Event.Id"/> (P2-6, #15), with cascade delete
    /// (<see cref="VirtualLeadersGuideDbContext"/>) — provisional, since no ticket builds Event deletion yet
    /// (only archiving is planned near-term). Whoever eventually builds it should revisit this behavior
    /// rather than assume it was a considered choice for that feature specifically.
    /// </remarks>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate
        | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public Guid? EventId { get; set; }

    /// <summary>The Event this grant is scoped to, or <see langword="null"/> for a platform-wide grant.</summary>
    public Event? Event { get; set; }
}
