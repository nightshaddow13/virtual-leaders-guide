namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// Route shape shared between Api's <c>InternalAuthorizationEndpoints</c> (which maps these) and Web's
/// <c>ApiRoleGrantClient</c> (which calls them), mirroring <see cref="InternalIdentityRoutes"/>' pattern so
/// the two sides can't drift.
/// </summary>
/// <remarks>
/// Deliberately outside JsonApi's <c>/api</c> namespace - these are plain internal grant CRUD, not a
/// JSON:API resource (<see cref="UserRole"/> itself only becomes one under P2-8, #17). <c>{id}</c> is an
/// <c>ApplicationUser.Id</c> (string, per ADR-0024) - there is no separate domain User id.
/// </remarks>
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
