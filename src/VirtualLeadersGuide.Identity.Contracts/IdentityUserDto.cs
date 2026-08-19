namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>Wire shape for the internal identity-store endpoints (see <c>InternalIdentityRoutes</c>).</summary>
/// <remarks>
/// A flat mirror of <c>Microsoft.AspNetCore.Identity.IdentityUser</c>'s columns, deliberately including
/// <see cref="PasswordHash"/> and <see cref="SecurityStamp"/> — Web verifies hashes and mints reset tokens
/// locally against these values, keeping the stock <c>SignInManager</c>/<c>DataProtectorTokenProvider</c>
/// path intact instead of hand-rolling either in Api. See ADR-0022 for why this data crosses the internal
/// Web↔Api hop at all. No EF/DbContext dependency here on purpose: this project is referenced by both Api
/// and Web, and is only the contract between them, not a data-access layer either side shares.
/// </remarks>
public sealed class IdentityUserDto
{
    public required string Id { get; set; }

    public string? UserName { get; set; }

    public string? NormalizedUserName { get; set; }

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public bool EmailConfirmed { get; set; }

    /// <remarks>
    /// Not an <c>IdentityUser</c> column - carried here so Web's <c>ApplicationUser</c> round-trips it
    /// through the same CRUD-by-user endpoints as every other property (ADR-0024's Consequences). Unset by
    /// anything in this ticket; P2-12 (#43)/account-setup is the first place expected to write it.
    /// </remarks>
    public string? DisplayName { get; set; }

    public string? PasswordHash { get; set; }

    public string? SecurityStamp { get; set; }

    public required string ConcurrencyStamp { get; set; }

    public string? PhoneNumber { get; set; }

    public bool PhoneNumberConfirmed { get; set; }

    /// <remarks>
    /// Always <see langword="false"/> / unused: Web does not implement <c>IUserTwoFactorStore</c> (ADR-0022's
    /// Consequences, issue #54), so <c>UserManager</c> never reads or writes this. Kept only for a full 1:1
    /// mirror of <c>IdentityUser</c>'s columns.
    /// </remarks>
    public bool TwoFactorEnabled { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public bool LockoutEnabled { get; set; }

    public int AccessFailedCount { get; set; }
}
