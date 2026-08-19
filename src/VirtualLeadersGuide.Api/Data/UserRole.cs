namespace VirtualLeadersGuide.Api.Data;

/// <summary>A <see cref="User"/>→<see cref="Role"/> grant, optionally scoped to an <see cref="Event"/>.</summary>
/// <remarks>
/// See ADR-0017 for the three-table shape and why a null <see cref="EventId"/> means platform-wide (Admin)
/// versus a set one scoping the grant to that Event (Director, and future Event-scoped roles), and
/// ADR-0024 for why <see cref="UserId"/> is <see cref="ApplicationUser.Id"/> directly rather than a
/// separate domain <c>User</c> row's id. Not a JsonApiDotNetCore resource yet, same posture as
/// <see cref="Role"/> — P2-8 (#17) is what turns this into one.
/// </remarks>
public class UserRole
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public int RoleId { get; set; }

    public Role? Role { get; set; }

    /// <remarks>
    /// A real FK against <see cref="Event.Id"/> (P2-6, #15), with cascade delete
    /// (<see cref="VirtualLeadersGuideDbContext"/>) — provisional, since no ticket builds Event deletion yet
    /// (only archiving is planned near-term). Whoever eventually builds it should revisit this behavior
    /// rather than assume it was a considered choice for that feature specifically.
    /// </remarks>
    public Guid? EventId { get; set; }

    /// <summary>The Event this grant is scoped to, or <see langword="null"/> for a platform-wide grant.</summary>
    public Event? Event { get; set; }
}
