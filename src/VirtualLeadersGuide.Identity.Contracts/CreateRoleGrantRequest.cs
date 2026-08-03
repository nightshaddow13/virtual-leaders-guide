namespace VirtualLeadersGuide.Identity.Contracts;

// Request body for POST InternalAuthorizationRoutes.UserGrants. EventId null requests a platform-wide
// grant (e.g. Admin); set requests an Event-scoped grant (e.g. Director) - see ADR-0017. The endpoint does
// not validate that the pairing of RoleId and EventId makes sense (e.g. Admin with a non-null EventId) -
// no ticket has asked for that yet.
public sealed class CreateRoleGrantRequest
{
    public required int RoleId { get; set; }

    public Guid? EventId { get; set; }
}
