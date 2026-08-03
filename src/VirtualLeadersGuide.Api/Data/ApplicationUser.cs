using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;
using Microsoft.AspNetCore.Identity;

namespace VirtualLeadersGuide.Api.Data;

// The ASP.NET Core Identity credential row - password hash, security stamp, lockout state - and, per
// ADR-0024, the person itself. ADR-0017 originally split these into a separate domain User table because
// Entra owned credentials; ADR-0019 brought credentials in-house, which made that split's justification
// moot, so ADR-0024 collapsed it back onto this one row. UserRole.UserId (see UserRole.cs) is this class's
// Id - there is no separate domain User row to link to.
//
// [Resource(..., GenerateControllerEndpoints = JsonApiEndpoints.Query)] exposes ONLY [Attr]-marked
// properties, read-only, at /api/users - see UsersResourceShould. Every other IdentityUser property
// (PasswordHash, SecurityStamp, ConcurrencyStamp, lockout state, ...) stays unmarked and therefore
// unreachable through JsonApiDotNetCore; that is the entire containment guarantee ADR-0022 depends on for
// credential material never crossing into the public-facing /api namespace. See
// DomainAuthorizationEntitiesAreNotJsonApiResourcesShould for the sibling entities (Role, UserRole) that
// stay fully private for now.
//
// Implements IIdentifiable<string> directly rather than deriving from JsonApiDotNetCore's Identifiable<T>
// base class, since C# can't stack that on top of IdentityUser. Id already satisfies IIdentifiable<string>
// implicitly (IdentityUser<string>.Id is already `string Id { get; set; }`); StringId/LocalId are added
// explicitly below, kept as explicit interface implementations (not plain public properties) purely to tie
// their nullability exactly to IIdentifiable's own annotations rather than approximate them.
[Resource(PublicName = "users", GenerateControllerEndpoints = JsonApiEndpoints.Query)]
public class ApplicationUser : IdentityUser, IIdentifiable<string>
{
    [Attr]
    public string? DisplayName { get; set; }

    // [Attr] only applies where it's declared - JsonApiDotNetCore's resource-graph scan doesn't pick up
    // annotations on a base class's own property, so Email (declared on IdentityUser, not here) needs this
    // override purely to attach the attribute. get/set trivially delegate to the base implementation.
    [Attr]
    public override string? Email
    {
        get => base.Email;
        set => base.Email = value;
    }

    // Explicit interface implementation (rather than plain public properties) so the compiler ties these
    // exactly to IIdentifiable's own nullability annotations instead of guessing at ones that happen to
    // match.
    string? IIdentifiable.StringId
    {
        get => Id;
        set => Id = value ?? Id;
    }

    // Only meaningful for JsonApiDotNetCore's atomic:operations extension (client-generated ids within one
    // request) - unused, since nothing in this app performs atomic operations against /api/users.
    string? IIdentifiable.LocalId { get; set; }
}
