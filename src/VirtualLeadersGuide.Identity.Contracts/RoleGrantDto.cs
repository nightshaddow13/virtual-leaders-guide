namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// Wire shape for a single <c>UserRole</c> grant, returned by the
/// <c>/internal/authorization/users/{id}/grants</c> endpoints (see
/// <see cref="InternalAuthorizationRoutes"/>).
/// </summary>
/// <remarks>
/// No <c>UserId</c> here - every route that returns this is already scoped to one person by path, so it
/// would be redundant on every element.
/// </remarks>
public sealed class RoleGrantDto
{
    public required Guid Id { get; set; }

    public required int RoleId { get; set; }

    public required string RoleName { get; set; }

    /// <remarks>
    /// <see langword="null"/> means a platform-wide grant (e.g. Admin); set means an Event-scoped grant
    /// (e.g. Director) - see ADR-0017.
    /// </remarks>
    public Guid? EventId { get; set; }
}
