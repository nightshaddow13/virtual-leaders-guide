namespace VirtualLeadersGuide.Identity.Contracts;

// Route shape shared between Api's InternalAuthorizationEndpoints (which maps these) and Web's
// ApiRoleGrantClient (which calls them), mirroring InternalIdentityRoutes' pattern so the two sides can't
// drift. Deliberately outside JsonApi's /api namespace - these are plain internal grant CRUD, not a
// JSON:API resource (UserRole itself only becomes one under P2-8, #17). {id} is an ApplicationUser.Id
// (string, per ADR-0024) - there is no separate domain User id.
public static class InternalAuthorizationRoutes
{
    public const string GroupPrefix = "/internal/authorization";

    public const string UserGrants = "/users/{id}/grants";

    public const string UserGrantById = "/users/{id}/grants/{grantId}";

    public static string ForUserGrants(string id) =>
        $"{GroupPrefix}/users/{Uri.EscapeDataString(id)}/grants";

    public static string ForUserGrantById(string id, Guid grantId) =>
        $"{GroupPrefix}/users/{Uri.EscapeDataString(id)}/grants/{grantId}";
}
