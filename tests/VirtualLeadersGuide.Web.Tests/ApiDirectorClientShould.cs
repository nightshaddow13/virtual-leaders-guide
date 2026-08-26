using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Directors;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <c>ApiEventClientShould</c>'s shape - <see cref="StubHttpClientFactory"/>/<see cref="StubHttpMessageHandler"/>,
/// response bodies as anonymous objects with already-lowercase names. Unlike <c>ApiEventClient</c>,
/// <see cref="ApiDirectorClient"/> joins two resources (<c>/api/users</c>, <c>/api/roleGrants</c>) per call,
/// so most responders here dispatch on <see cref="HttpRequestMessage.RequestUri"/>'s path rather than
/// returning one fixed body.
/// </remarks>
public class ApiDirectorClientShould
{
    private const string SigningKey = "test-internal-jwt-signing-key-at-least-32-bytes-long";

    [Fact]
    public async Task ReturnUsersJoinedWithTheirGrants_WhenApiRespondsWithOk_ForGetUsersAsync()
    {
        var adminId = "user-admin";
        var directorId = "user-director";
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/users" => JsonApiResponse(HttpStatusCode.OK, new
            {
                data = new[]
                {
                    UserResource(adminId, "ash@council.org", "Ash Vance", hasCredential: true),
                    UserResource(directorId, "pat@troop12.org", "Pat Riley", hasCredential: true)
                }
            }),
            "/api/roleGrants" => JsonApiResponse(HttpStatusCode.OK, new
            {
                data = new[]
                {
                    GrantResource(adminId, RoleIds.Admin, eventId: null),
                    GrantResource(directorId, RoleIds.Director, eventId: null),
                    GrantResource(directorId, RoleIds.Director, eventId: Guid.NewGuid())
                }
            }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        ApiDirectorClient client = CreateClient(handler);

        (IReadOnlyList<UserRowDto> users, int total) =
            await client.GetUsersAsync(1, 10, search: null, state: null, CancellationToken.None);

        Assert.Equal(2, total);
        UserRowDto admin = Assert.Single(users, u => u.Id == adminId);
        Assert.True(admin.IsAdmin);
        Assert.False(admin.IsDirector);
        Assert.Equal(0, admin.EventGrantCount);

        UserRowDto director = Assert.Single(users, u => u.Id == directorId);
        Assert.False(director.IsAdmin);
        Assert.True(director.IsDirector);
        Assert.Equal(1, director.EventGrantCount);
    }

    [Fact]
    public async Task FilterBySearchText_ForGetUsersAsync()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/users" => JsonApiResponse(HttpStatusCode.OK, new
            {
                data = new[]
                {
                    UserResource("u1", "dana@troop7.org", null, hasCredential: false),
                    UserResource("u2", "jo@pack44.org", "Jo Menzies", hasCredential: false)
                }
            }),
            "/api/roleGrants" => JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        ApiDirectorClient client = CreateClient(handler);

        (IReadOnlyList<UserRowDto> users, int total) =
            await client.GetUsersAsync(1, 10, search: "jo", state: null, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal("u2", Assert.Single(users).Id);
    }

    [Fact]
    public async Task FilterByState_ForGetUsersAsync()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/users" => JsonApiResponse(HttpStatusCode.OK, new
            {
                data = new[]
                {
                    UserResource("u1", "dana@troop7.org", null, hasCredential: false),
                    UserResource("u2", "jo@pack44.org", "Jo Menzies", hasCredential: true)
                }
            }),
            "/api/roleGrants" => JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        ApiDirectorClient client = CreateClient(handler);

        (IReadOnlyList<UserRowDto> invited, int invitedTotal) =
            await client.GetUsersAsync(1, 10, search: null, state: UserState.Invited, CancellationToken.None);
        (IReadOnlyList<UserRowDto> active, int activeTotal) =
            await client.GetUsersAsync(1, 10, search: null, state: UserState.Active, CancellationToken.None);

        Assert.Equal(1, invitedTotal);
        Assert.Equal("u1", Assert.Single(invited).Id);
        Assert.Equal(1, activeTotal);
        Assert.Equal("u2", Assert.Single(active).Id);
    }

    [Fact]
    public async Task ReturnNull_WhenApiRespondsWithNotFound_ForGetUserAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        ApiDirectorClient client = CreateClient(handler);

        UserRowDto? user = await client.GetUserAsync("missing", CancellationToken.None);

        Assert.Null(user);
    }

    [Fact]
    public async Task ReturnTheJoinedUser_WhenApiRespondsWithOk_ForGetUserAsync()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/users/u1" => JsonApiResponse(HttpStatusCode.OK, new { data = UserResource("u1", "dana@troop7.org", null, hasCredential: false) }),
            "/api/roleGrants" => JsonApiResponse(HttpStatusCode.OK, new { data = new[] { GrantResource("u1", RoleIds.Director, eventId: null) } }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        ApiDirectorClient client = CreateClient(handler);

        UserRowDto? user = await client.GetUserAsync("u1", CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal("dana@troop7.org", user!.Email);
        Assert.True(user.IsDirector);
        Assert.False(user.HasCredential);
        Assert.Equal(0, user.EventGrantCount);
    }

    [Fact]
    public async Task ReturnEmpty_WithoutASecondCall_WhenNoGrantsMatchTheEvent_ForGetDirectorsForEventAsync()
    {
        var eventId = Guid.NewGuid();
        var usersRequested = false;
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/roleGrants")
            {
                return JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() });
            }

            usersRequested = true;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        ApiDirectorClient client = CreateClient(handler);

        IReadOnlyList<UserRowDto> directors = await client.GetDirectorsForEventAsync(eventId, CancellationToken.None);

        Assert.Empty(directors);
        Assert.False(usersRequested);
    }

    [Fact]
    public async Task ReturnTheAssignedDirectors_WhenGrantsMatchTheEvent_ForGetDirectorsForEventAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/roleGrants" => JsonApiResponse(HttpStatusCode.OK, new { data = new[] { GrantResource("u1", RoleIds.Director, eventId) } }),
            "/api/users" => JsonApiResponse(HttpStatusCode.OK, new { data = new[] { UserResource("u1", "pat@troop12.org", "Pat Riley", hasCredential: true) } }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        ApiDirectorClient client = CreateClient(handler);

        IReadOnlyList<UserRowDto> directors = await client.GetDirectorsForEventAsync(eventId, CancellationToken.None);

        Assert.Equal("u1", Assert.Single(directors).Id);
    }

    [Fact]
    public async Task ReturnCreated_WhenApiRespondsWithCreated_ForGrantDirectorRoleAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        ApiDirectorClient client = CreateClient(handler);

        GrantWriteOutcome outcome = await client.GrantDirectorRoleAsync("u1", CancellationToken.None);

        Assert.Equal(GrantWriteOutcome.Created, outcome);
    }

    [Fact]
    public async Task SendANullEventId_ForGrantDirectorRoleAsync()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        ApiDirectorClient client = CreateClient(handler);

        await client.GrantDirectorRoleAsync("u1", CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"userId\":\"u1\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"roleId\":2", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnAlreadyGranted_WhenApiRespondsWithConflict_ForGrantEventAccessAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        ApiDirectorClient client = CreateClient(handler);

        GrantWriteOutcome outcome = await client.GrantEventAccessAsync("u1", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(GrantWriteOutcome.AlreadyGranted, outcome);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForGrantEventAccessAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiDirectorClient client = CreateClient(handler);

        GrantWriteOutcome outcome = await client.GrantEventAccessAsync("u1", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(GrantWriteOutcome.Forbidden, outcome);
    }

    [Fact]
    public async Task ThrowDirectorDataUnavailableException_WhenTheHttpCallFails_ForGetUsersAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiDirectorClient client = CreateClient(handler);

        await Assert.ThrowsAsync<DirectorDataUnavailableException>(
            () => client.GetUsersAsync(1, 10, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowDirectorDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForGetUsersAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiDirectorClient client = CreateClient(handler);

        await Assert.ThrowsAsync<DirectorDataUnavailableException>(
            () => client.GetUsersAsync(1, 10, null, null, CancellationToken.None));
    }

    private static ApiDirectorClient CreateClient(HttpMessageHandler apiHandler)
    {
        var grantsClient = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
        var jwtProvider = new InternalJwtProvider(new FixedAuthenticationStateProvider("user-1"), grantsClient, Configuration());
        var apiClient = new InternalApiClient(new StubHttpClientFactory(apiHandler), jwtProvider);
        return new ApiDirectorClient(apiClient);
    }

    private static object UserResource(string id, string email, string? displayName, bool hasCredential) => new
    {
        type = "users",
        id,
        attributes = new { email, displayName, hasCredential }
    };

    private static object GrantResource(string userId, int roleId, Guid? eventId) => new
    {
        type = "roleGrants",
        id = Guid.NewGuid().ToString(),
        attributes = new { userId, roleId, eventId }
    };

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");
        return response;
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InternalJwtDefaults.SigningKeyConfigurationKey] = SigningKey
        })
        .Build();
}
