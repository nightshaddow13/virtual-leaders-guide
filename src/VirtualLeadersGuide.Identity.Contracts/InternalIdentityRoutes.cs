namespace VirtualLeadersGuide.Identity.Contracts;

// Route shape shared between Api's InternalIdentityEndpoints (which maps these) and Web's ApiUserStore
// (which calls them) so the two sides can't drift. Deliberately outside JsonApi's /api namespace (see
// ADR-0022) - these are plain internal CRUD-by-user endpoints, not a JSON:API resource.
public static class InternalIdentityRoutes
{
    public const string GroupPrefix = "/internal/identity";

    public const string Users = "/users";

    public const string UserById = "/users/{id}";

    public const string UserByNormalizedUserName = "/users/by-name/{normalizedUserName}";

    public const string UserByNormalizedEmail = "/users/by-email/{normalizedEmail}";

    public static string ForUserById(string id) => $"{GroupPrefix}/users/{Uri.EscapeDataString(id)}";

    public static string ForUserByNormalizedUserName(string normalizedUserName) =>
        $"{GroupPrefix}/users/by-name/{Uri.EscapeDataString(normalizedUserName)}";

    public static string ForUserByNormalizedEmail(string normalizedEmail) =>
        $"{GroupPrefix}/users/by-email/{Uri.EscapeDataString(normalizedEmail)}";

    public static string ForUsers() => $"{GroupPrefix}/users";
}
