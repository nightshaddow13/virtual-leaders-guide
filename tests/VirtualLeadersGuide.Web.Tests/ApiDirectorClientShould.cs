using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Directors;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <c>ApiEventClientShould</c>'s shape - <see cref="StubHttpClientFactory"/>/<see cref="StubHttpMessageHandler"/>,
/// response bodies as anonymous objects with already-lowercase names. Unlike <c>ApiEventClient</c>,
/// <see cref="ApiDirectorClient"/> joins two resources (<c>/api/users</c>, <c>/api/roleGrants</c>) per call,
/// so most responders here dispatch on <see cref="HttpRequestMessage.RequestUri"/>'s path rather than
/// returning one fixed body. <c>MarkADirectorWhoAlsoHoldsAdmin_ForGetDirectorsForEventAsync</c> goes further
/// still, dispatching on query string too - <see cref="ApiDirectorClient.GetDirectorsForEventAsync"/> makes
/// two distinct <c>/api/roleGrants</c> calls (event-scoped grants, then every grant for the resolved users),
/// and a single fixed body can't represent "one Director grant for this event" and "this User also holds
/// Admin" at once without producing a spurious second row (ADR-0051's <see cref="EventDirectorDto.IsAdmin"/>
/// needs the User's <em>other</em> grants, which the event-scoped fetch alone never returns).
/// </remarks>
public class ApiDirectorClientShould
{
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

        IReadOnlyList<EventDirectorDto> directors = await client.GetDirectorsForEventAsync(eventId, CancellationToken.None);

        Assert.Empty(directors);
        Assert.False(usersRequested);
    }

    [Fact]
    public async Task ReturnTheAssignedDirectorsWithTheirGrantId_WhenGrantsMatchTheEvent_ForGetDirectorsForEventAsync()
    {
        var eventId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/roleGrants" => JsonApiResponse(HttpStatusCode.OK, new { data = new[] { GrantResource("u1", RoleIds.Director, eventId, grantId.ToString()) } }),
            "/api/users" => JsonApiResponse(HttpStatusCode.OK, new { data = new[] { UserResource("u1", "pat@troop12.org", "Pat Riley", hasCredential: true) } }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        ApiDirectorClient client = CreateClient(handler);

        IReadOnlyList<EventDirectorDto> directors = await client.GetDirectorsForEventAsync(eventId, CancellationToken.None);

        EventDirectorDto director = Assert.Single(directors);
        Assert.Equal("u1", director.UserId);
        Assert.Equal(grantId, director.GrantId);
        Assert.False(director.IsAdmin);
    }

    [Fact]
    public async Task MarkADirectorWhoAlsoHoldsAdmin_ForGetDirectorsForEventAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            string query = request.RequestUri!.Query;

            if (path == "/api/users")
            {
                return JsonApiResponse(HttpStatusCode.OK, new { data = new[] { UserResource("u1", "ash@council.org", "Ash Vance", hasCredential: true) } });
            }

            if (path == "/api/roleGrants" && query.Contains("eventId", StringComparison.Ordinal))
            {
                return JsonApiResponse(HttpStatusCode.OK, new { data = new[] { GrantResource("u1", RoleIds.Director, eventId) } });
            }

            if (path == "/api/roleGrants")
            {
                return JsonApiResponse(HttpStatusCode.OK, new
                {
                    data = new[]
                    {
                        GrantResource("u1", RoleIds.Director, eventId),
                        GrantResource("u1", RoleIds.Admin, eventId: null)
                    }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        ApiDirectorClient client = CreateClient(handler);

        IReadOnlyList<EventDirectorDto> directors = await client.GetDirectorsForEventAsync(eventId, CancellationToken.None);

        Assert.True(Assert.Single(directors).IsAdmin);
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
    public async Task ReturnRemoved_WhenApiRespondsWithNoContent_ForRemoveEventAccessAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        ApiDirectorClient client = CreateClient(handler);

        GrantWriteOutcome outcome = await client.RemoveEventAccessAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(GrantWriteOutcome.Removed, outcome);
    }

    [Fact]
    public async Task ReturnNotFound_WhenApiRespondsWithNotFound_ForRemoveEventAccessAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        ApiDirectorClient client = CreateClient(handler);

        GrantWriteOutcome outcome = await client.RemoveEventAccessAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(GrantWriteOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForRemoveEventAccessAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiDirectorClient client = CreateClient(handler);

        GrantWriteOutcome outcome = await client.RemoveEventAccessAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(GrantWriteOutcome.Forbidden, outcome);
    }

    [Fact]
    public async Task SendADeleteToTheGrantsResource_ForRemoveEventAccessAsync()
    {
        var grantId = Guid.NewGuid();
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        ApiDirectorClient client = CreateClient(handler);

        await client.RemoveEventAccessAsync(grantId, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Delete, capturedRequest!.Method);
        Assert.EndsWith($"/api/roleGrants/{grantId}", capturedRequest.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowDirectorDataUnavailableException_WhenTheHttpCallFails_ForRemoveEventAccessAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiDirectorClient client = CreateClient(handler);

        await Assert.ThrowsAsync<DirectorDataUnavailableException>(
            () => client.RemoveEventAccessAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ThrowDirectorDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForRemoveEventAccessAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiDirectorClient client = CreateClient(handler);

        await Assert.ThrowsAsync<DirectorDataUnavailableException>(
            () => client.RemoveEventAccessAsync(Guid.NewGuid(), CancellationToken.None));
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

    private static ApiDirectorClient CreateClient(HttpMessageHandler apiHandler) =>
        ApiClientTestFactory.CreateDirectorClient(apiHandler);

    private static object UserResource(string id, string email, string? displayName, bool hasCredential) => new
    {
        type = "users",
        id,
        attributes = new { email, displayName, hasCredential }
    };

    /// <remarks><paramref name="id"/> defaults to a random one - callers that need to assert against a specific grant id (e.g. <see cref="ApiDirectorClient.RemoveEventAccessAsync"/>'s target) pass one explicitly.</remarks>
    private static object GrantResource(string userId, int roleId, Guid? eventId, string? id = null) => new
    {
        type = "roleGrants",
        id = id ?? Guid.NewGuid().ToString(),
        attributes = new { userId, roleId, eventId }
    };

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");
        return response;
    }
}
