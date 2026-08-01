namespace VirtualLeadersGuide.Identity.Contracts;

// Wire shape for the internal identity-store endpoints (see InternalIdentityRoutes). A flat mirror of
// Microsoft.AspNetCore.Identity.IdentityUser's columns, deliberately including PasswordHash and
// SecurityStamp — Web verifies hashes and mints reset tokens locally against these values, which is what
// keeps the stock SignInManager/DataProtectorTokenProvider path intact instead of hand-rolling either in
// Api. See ADR-0022 for why this data crosses the internal Web<->Api hop at all.
//
// No EF/DbContext dependency here on purpose: this project is referenced by both Api and Web, and is only
// the contract between them, not a data-access layer either side shares.
public sealed class IdentityUserDto
{
    public required string Id { get; set; }

    public string? UserName { get; set; }

    public string? NormalizedUserName { get; set; }

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public bool EmailConfirmed { get; set; }

    public string? PasswordHash { get; set; }

    public string? SecurityStamp { get; set; }

    public required string ConcurrencyStamp { get; set; }

    public string? PhoneNumber { get; set; }

    public bool PhoneNumberConfirmed { get; set; }

    // Always false / unused: Web does not implement IUserTwoFactorStore (see ADR-0022's Consequences and
    // issue #54), so UserManager never reads or writes this. Kept only for a full 1:1 mirror of
    // IdentityUser's columns.
    public bool TwoFactorEnabled { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public bool LockoutEnabled { get; set; }

    public int AccessFailedCount { get; set; }
}
