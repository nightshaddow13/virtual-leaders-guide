using System.Net;
using System.Net.Http.Json;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.Tests;

/// <summary>
/// A tiny in-memory grant store speaking <see cref="InternalAuthorizationRoutes"/>' wire shape - GET the
/// list, POST to create (409 on a duplicate role/Event pairing, matching Api's filtered-unique-index
/// behavior), DELETE by id (204, or 404 if already gone).
/// </summary>
/// <remarks>
/// Unlike <see cref="StubHttpMessageHandler"/>, which returns one canned response per test, this actually
/// tracks grant state across calls - needed to exercise
/// <see cref="Authorization.AdminAllowlistSynchronizer.SyncAsync"/>'s read-then-write sync.
/// </remarks>
internal sealed class FakeAuthorizationApiHandler : HttpMessageHandler
{
    private const string Prefix = "/internal/authorization/users/";

    private readonly List<RoleGrantDto> _grants = [];
    private readonly bool _userNotFound;

    public FakeAuthorizationApiHandler()
        : this(userNotFound: false)
    {
    }

    private FakeAuthorizationApiHandler(bool userNotFound) => _userNotFound = userNotFound;

    /// <summary>A handler whose every request 404s, simulating a user row Api no longer has.</summary>
    public static FakeAuthorizationApiHandler WithUserNotFound() => new(userNotFound: true);

    /// <summary>Seeds a platform-wide (EventId null) Admin grant for <paramref name="userId"/>.</summary>
    public RoleGrantDto SeedAdminGrant(string userId) =>
        Seed(userId, RoleIds.Admin, RoleNames.Admin, eventId: null);

    /// <summary>Seeds an Event-scoped Director grant for <paramref name="userId"/>.</summary>
    public RoleGrantDto SeedDirectorGrant(string userId, Guid eventId) =>
        Seed(userId, RoleIds.Director, RoleNames.Director, eventId);

    private RoleGrantDto Seed(string userId, int roleId, string roleName, Guid? eventId)
    {
        var grant = new RoleGrantDto { Id = Guid.NewGuid(), RoleId = roleId, RoleName = roleName, EventId = eventId };
        _grants.Add(grant);
        return grant;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_userNotFound)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        string path = request.RequestUri!.AbsolutePath;
        if (!path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        string[] segments = path[Prefix.Length..].Split('/');

        if (request.Method == HttpMethod.Get && segments is [_, "grants"])
        {
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, _grants));
        }

        if (request.Method == HttpMethod.Post && segments is [var userId, "grants"])
        {
            var body = request.Content!.ReadFromJsonAsync<CreateRoleGrantRequest>(cancellationToken)
                .GetAwaiter().GetResult()!;

            if (_grants.Any(g => g.RoleId == body.RoleId && g.EventId == body.EventId))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
            }

            RoleGrantDto created = Seed(
                Uri.UnescapeDataString(userId),
                body.RoleId,
                body.RoleId == RoleIds.Admin ? RoleNames.Admin : RoleNames.Director,
                body.EventId);
            return Task.FromResult(JsonResponse(HttpStatusCode.Created, created));
        }

        if (request.Method == HttpMethod.Delete && segments is [_, "grants", var grantIdSegment])
        {
            RoleGrantDto? existing = _grants.FirstOrDefault(g => g.Id == Guid.Parse(grantIdSegment));
            if (existing is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            _grants.Remove(existing);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode statusCode, T body) =>
        new(statusCode) { Content = JsonContent.Create(body) };
}
