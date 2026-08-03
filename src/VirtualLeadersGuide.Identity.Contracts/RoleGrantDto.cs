namespace VirtualLeadersGuide.Identity.Contracts;

// Wire shape for a single UserRole grant, returned by the /internal/authorization/users/{id}/grants
// endpoints (see InternalAuthorizationRoutes). No UserId here - every route that returns this is already
// scoped to one person by path, so it would be redundant on every element. EventId null means a
// platform-wide grant (e.g. Admin); set means an Event-scoped grant (e.g. Director) - see ADR-0017.
public sealed class RoleGrantDto
{
    public required Guid Id { get; set; }

    public required int RoleId { get; set; }

    public required string RoleName { get; set; }

    public Guid? EventId { get; set; }
}
