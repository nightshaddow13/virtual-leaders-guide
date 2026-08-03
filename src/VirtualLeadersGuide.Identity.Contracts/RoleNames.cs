namespace VirtualLeadersGuide.Identity.Contracts;

// Well-known Role.Name values, matching the rows seeded by Api's AddRoleAndUserRoleSchema migration
// (see RoleIds for the matching Role.Id values). Shared here rather than duplicated in Api and Web -
// P2-5 (#14) mints JWT role claims from these names, and P2-4 (#13) checks against them.
public static class RoleNames
{
    public const string Admin = "Admin";

    public const string Director = "Director";
}
