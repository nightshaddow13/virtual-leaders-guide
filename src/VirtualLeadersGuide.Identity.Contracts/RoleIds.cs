namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// Well-known <c>Role.Id</c> values, seeded via <c>HasData</c> in Api's <c>AddRoleAndUserRoleSchema</c>
/// migration so both P2-4 (#13, allowlist resync) and P2-8 (#17, grant management) can reference a
/// <see cref="Role"/> without a lookup. See <see cref="RoleNames"/> for the matching <c>Role.Name</c> values.
/// </summary>
public static class RoleIds
{
    public const int Admin = 1;

    public const int Director = 2;
}
