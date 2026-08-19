namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>Request body for <c>POST InternalAuthorizationRoutes.UserGrants</c>.</summary>
/// <remarks>
/// The endpoint does not validate that the pairing of <see cref="RoleId"/> and <see cref="EventId"/> makes
/// sense (e.g. Admin with a non-null EventId) - no ticket has asked for that yet.
/// </remarks>
public sealed class CreateRoleGrantRequest
{
    public required int RoleId { get; set; }

    /// <remarks>
    /// <see langword="null"/> requests a platform-wide grant (e.g. Admin); set requests an Event-scoped
    /// grant (e.g. Director) - see ADR-0017.
    /// </remarks>
    public Guid? EventId { get; set; }
}
