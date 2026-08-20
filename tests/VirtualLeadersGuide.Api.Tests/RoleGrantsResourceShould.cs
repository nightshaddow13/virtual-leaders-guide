using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Positive coverage for <c>/api/roleGrants</c> (P2-8, #17; ADR-0033): <c>UserRoleResourceDefinition</c>'s
/// Admin-only scoping, and the Admin-role-grant write restriction. Doesn't repeat
/// <c>DomainAuthorizationEntitiesAreNotJsonApiResourcesShould</c>'s negative case for <c>roles</c>, or
/// <c>InternalJwtAuthorizationShould</c>'s identity-forwarding policy tests (this class only ever calls
/// <see cref="ApiWebApplicationFactory.CreateUserClient"/>, never an X-Internal-Key-only client). Unlike
/// <see cref="EventsResourceShould"/>, a non-Admin caller gets 403 on every shape including
/// <c>GetCollection</c> - see <c>UserRoleResourceDefinition</c>'s remarks and ADR-0033.
/// </remarks>
public class RoleGrantsResourceShould : IAsyncLifetime
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task SucceedWithCreated_WhenAdminCreatesAnEventScopedDirectorGrant_ForPost()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "roleGrants",
                attributes = new { userId = user.Id, roleId = RoleIds.Director, eventId = @event.Id }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/roleGrants", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement attributes = await AttributesOfAsync(response);
        Assert.Equal(user.Id, attributes.GetProperty("userId").GetString());
        Assert.Equal(RoleIds.Director, attributes.GetProperty("roleId").GetInt32());
        Assert.Equal(@event.Id.ToString(), attributes.GetProperty("eventId").GetString());
    }

    [Fact]
    public async Task SucceedWithOk_WhenAdminReadsAnAdminRoleGrant_ForGetSingle()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        UserRole grant = await _factory.CreateGrantAsync(user.Id, RoleIds.Admin);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/roleGrants/{grant.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOkIncludingAnAdminRoleGrant_WhenAdminListsGrants_ForGetCollection()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        UserRole grant = await _factory.CreateGrantAsync(user.Id, RoleIds.Admin);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/roleGrants");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement[] data = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Contains(data, element => element.GetProperty("id").GetString() == grant.Id.ToString());
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminDeletesADirectorGrant_ForDelete()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        Event @event = await _factory.CreateEventAsync();
        UserRole grant = await _factory.CreateGrantAsync(user.Id, RoleIds.Director, @event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/roleGrants/{grant.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAdminAttemptsToCreateAnAdminRoleGrant_ForPost()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "roleGrants", attributes = new { userId = user.Id, roleId = RoleIds.Admin } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/roleGrants", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAdminAttemptsToDeleteAnAdminRoleGrant_ForDelete()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        UserRole grant = await _factory.CreateGrantAsync(user.Id, RoleIds.Admin);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/roleGrants/{grant.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorListsGrants_ForGetCollection()
    {
        using HttpClient client = DirectorClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/roleGrants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorReadsAGrant_ForGetSingle()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        UserRole grant = await _factory.CreateGrantAsync(user.Id, RoleIds.Admin);
        using HttpClient client = DirectorClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/roleGrants/{grant.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorAttemptsToCreateAGrant_ForPost()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        using HttpClient client = DirectorClient();
        var body = new
        {
            data = new { type = "roleGrants", attributes = new { userId = user.Id, roleId = RoleIds.Director } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/roleGrants", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorAttemptsToDeleteAGrant_ForDelete()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        UserRole grant = await _factory.CreateGrantAsync(user.Id, RoleIds.Director);
        using HttpClient client = DirectorClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/roleGrants/{grant.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsListsGrants_ForGetCollection()
    {
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/roleGrants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithConflict_WhenCreatingADuplicateEventScopedGrant_ForPost()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        Event @event = await _factory.CreateEventAsync();
        await _factory.CreateGrantAsync(user.Id, RoleIds.Director, @event.Id);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "roleGrants",
                attributes = new { userId = user.Id, roleId = RoleIds.Director, eventId = @event.Id }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/roleGrants", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithConflict_WhenCreatingADuplicatePlatformWideGrantForTheSameRole_ForPost()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        await _factory.CreateGrantAsync(user.Id, RoleIds.Director);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "roleGrants", attributes = new { userId = user.Id, roleId = RoleIds.Director } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/roleGrants", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ExposeAGrantCreatedViaApi_ThroughInternalAuthorizationEndpoints_ForGetGrants()
    {
        ApplicationUser user = await _factory.CreateUserAsync();
        Event @event = await _factory.CreateEventAsync();
        using HttpClient adminClient = AdminClient();
        var body = new
        {
            data = new
            {
                type = "roleGrants",
                attributes = new { userId = user.Id, roleId = RoleIds.Director, eventId = @event.Id }
            }
        };
        await SendAsync(adminClient, HttpMethod.Post, "/api/roleGrants", body);

        using HttpClient internalClient = _factory.CreateAuthenticatedClient();
        HttpResponseMessage response =
            await internalClient.GetAsync(InternalAuthorizationRoutes.ForUserGrants(user.Id));

        response.EnsureSuccessStatusCode();
        List<RoleGrantDto>? grants = await response.Content.ReadFromJsonAsync<List<RoleGrantDto>>();
        Assert.Contains(grants!, grant => grant.RoleId == RoleIds.Director && grant.EventId == @event.Id);
    }

    private HttpClient AdminClient() =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.AdminRoleClaim()]);

    private HttpClient DirectorClient() =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.DirectorRoleClaim(Guid.NewGuid())]);

    private static async Task<JsonElement> AttributesOfAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").GetProperty("attributes").Clone();
    }

    /// <remarks>
    /// JSON:API content negotiation rejects a Content-Type with parameters (e.g. the charset
    /// <see cref="StringContent"/>'s 3-arg constructor would add) with 415, so the header is set explicitly
    /// below instead of going through that overload.
    /// </remarks>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string requestUri, object? body = null)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        }

        return await client.SendAsync(request);
    }
}
