namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// Route shape shared between Api's <c>InternalAuthorizationEndpoints</c> (which maps these) and Web's
/// <c>ApiRoleGrantClient</c> (which calls them), mirroring <see cref="InternalIdentityRoutes"/>' pattern so
/// the two sides can't drift.
/// </summary>
/// <remarks>
/// Deliberately outside JsonApi's <c>/api</c> namespace - this is the identity-forwarding path Web's login
/// flow uses to mint role claims (ADR-0007), unauthenticated by role on purpose since it's what produces
/// those claims in the first place; it coexists with, and is unaffected by, <c>UserRole</c>'s separate,
/// Admin-only exposure at <c>/api/roleGrants</c> (P2-8, #17; ADR-0033). <c>{id}</c> is an
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
