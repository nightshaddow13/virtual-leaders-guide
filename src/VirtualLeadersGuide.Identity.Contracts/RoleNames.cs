namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// Well-known <c>Role.Name</c> values, matching the rows seeded by Api's <c>AddRoleAndUserRoleSchema</c>
/// migration (see <see cref="RoleIds"/> for the matching <c>Role.Id</c> values).
/// </summary>
/// <remarks>
/// Shared here rather than duplicated in Api and Web - P2-5 (#14) mints JWT role claims from these names,
/// and P2-4 (#13) checks against them.
/// </remarks>
public static class RoleNames
{
    public const string Admin = "Admin";

    public const string Director = "Director";
}
