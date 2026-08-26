namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// One row of the P2-12 (#43) Users screen (frame 3a) - an <c>ApplicationUser</c> joined against their
/// <c>UserRole</c> rows. Built by <see cref="ApiDirectorClient"/> from <c>/api/users</c> plus
/// <c>/api/roleGrants</c>, since <c>UserRole.User</c> isn't a JSON:API relationship (ADR-0024's remarks) -
/// there is no server-side <c>?include=</c> to fetch this pre-joined.
/// </summary>
public sealed class UserRowDto
{
    public required string Id { get; init; }

    public required string Email { get; init; }

    /// <remarks>Renders as "— not set yet" on the Users screen when null (frame 3a).</remarks>
    public string? DisplayName { get; init; }

    /// <summary>Whether a password has been set - <see langword="false"/> for a pending Invite.</summary>
    public required bool HasCredential { get; init; }

    /// <summary>Whether this User holds the platform-wide Admin Role (ADR-0035).</summary>
    public required bool IsAdmin { get; init; }

    /// <summary>Whether this User holds the Director Role - unscoped or with Grants, either counts (ADR-0035).</summary>
    public required bool IsDirector { get; init; }

    /// <summary>The count of Event-scoped Director Grants this User holds - zero for an unscoped Director.</summary>
    public required int EventGrantCount { get; init; }
}
