namespace VirtualLeadersGuide.Identity.Contracts;

// Well-known Role.Id values, seeded via HasData in Api's AddRoleAndUserRoleSchema migration so both
// P2-4 (#13, allowlist resync) and P2-8 (#17, grant management) can reference a Role without a lookup.
// See RoleNames for the matching Role.Name values.
public static class RoleIds
{
    public const int Admin = 1;

    public const int Director = 2;
}
