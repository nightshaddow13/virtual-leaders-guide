using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Positive coverage for <c>/api/events</c> (P2-7, #16): <c>EventResourceDefinition</c>'s Admin/Director
/// scoping. Doesn't repeat <c>DomainAuthorizationEntitiesAreNotJsonApiResourcesShould</c>'s negative case for
/// <c>roles</c>/<c>userRoles</c>, or <c>InternalJwtAuthorizationShould</c>'s identity-forwarding policy tests
/// (this class only ever calls <see cref="ApiWebApplicationFactory.CreateUserClient"/>, never an
/// X-Internal-Key-only client). Grants are simulated entirely via pre-formatted role claims
/// (<see cref="ApiWebApplicationFactory.AdminRoleClaim"/>/<see cref="ApiWebApplicationFactory.DirectorRoleClaim"/>)
/// rather than real <c>UserRoles</c> rows - Api authorizes from JWT claims alone (ADR-0007's amendment).
/// </remarks>
public class EventsResourceShould : IAsyncLifetime
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
    public async Task SucceedWithCreated_WhenAdminCreatesAnEventWithOnlyAName_ForPost()
    {
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "events", attributes = new { name = $"Fall Camporee {Guid.NewGuid()}" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement attributes = await AttributesOfAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(attributes.GetProperty("slug").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(attributes.GetProperty("passcode").GetString()));
    }

    [Fact]
    public async Task SucceedWithOk_WhenAdminReadsAnyEvent_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminUpdatesAnyEvent_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { name = $"Renamed {Guid.NewGuid()}" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminDeletesAnyEvent_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenAnAssignedDirectorReadsTheirEvent_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnAssignedDirectorUpdatesTheirEvent_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { name = $"Renamed {Guid.NewGuid()}" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorAttemptsToCreateAnEvent_ForPost()
    {
        Event existing = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(existing.Id);
        var body = new { data = new { type = "events", attributes = new { name = $"New Event {Guid.NewGuid()}" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnAssignedDirectorAttemptsToDeleteTheirEvent_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorReadsAnotherEvent_ForGetSingle()
    {
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{other.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorUpdatesAnotherEvent_ForPatch()
    {
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(Guid.NewGuid());
        var body = new
        {
            data = new
            {
                type = "events", id = other.Id.ToString(),
                attributes = new { name = $"Renamed {Guid.NewGuid()}" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{other.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorDeletesAnotherEvent_ForDelete()
    {
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{other.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnOnlyTheAssignedEvent_WhenADirectorListsEvents_ForGetCollection()
    {
        Event assigned = await _factory.CreateEventAsync();
        await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(assigned.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement[] data = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Single(data);
        Assert.Equal(assigned.Id.ToString(), data[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ReturnAnEmptyCollection_WhenACallerWithNoRoleClaimsListsEvents_ForGetCollection()
    {
        await _factory.CreateEventAsync();
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsReadsAnEvent_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithConflict_WhenCreatingAnEventWhoseNameDerivesToAnAlreadyUsedSlug_ForPost()
    {
        Event existing = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new { data = new { type = "events", attributes = new { name = existing.Name.ToUpperInvariant() } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/name", "/data/attributes/slug");
    }

    [Fact]
    public async Task RejectWithConflict_WhenUpdatingAnEventsNameToOneAlreadyInUse_ForPatch()
    {
        Event existing = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = other.Id.ToString(),
                attributes = new { name = existing.Name.ToUpperInvariant() }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{other.Id}", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/name");
    }

    [Fact]
    public async Task RejectWithConflict_WhenUpdatingAnEventsSlugToOneAlreadyInUse_ForPatch()
    {
        Event existing = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = other.Id.ToString(),
                attributes = new { slug = existing.Slug }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{other.Id}", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/slug");
    }

    [Fact]
    public async Task RejectWithConflict_WhenUpdatingAnEventToANameAndSlugBothAlreadyInUse_ForPatch()
    {
        Event existing = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = other.Id.ToString(),
                attributes = new { name = existing.Name, slug = existing.Slug }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{other.Id}", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/name", "/data/attributes/slug");
    }

    [Fact]
    public async Task SucceedWithCreated_WhenAdminCreatesAnEventWithBothDates_ForPost()
    {
        using HttpClient client = AdminClient();
        var startsAt = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.FromHours(-5));
        var endsAt = new DateTimeOffset(2026, 6, 14, 17, 0, 0, TimeSpan.FromHours(-5));
        var body = new
        {
            data = new
            {
                type = "events",
                attributes = new { name = $"Fall Camporee {Guid.NewGuid()}", startsAt, endsAt }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement attributes = await AttributesOfAsync(response);
        Assert.Equal(startsAt.ToUniversalTime(), attributes.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(endsAt.ToUniversalTime(), attributes.GetProperty("endsAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminSetsAnEndOnAnEventThatAlreadyHasAStart_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync(startsAt: DateTimeOffset.UtcNow);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { endsAt = DateTimeOffset.UtcNow.AddDays(1) }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenEndEqualsStart_ForPatch()
    {
        var startsAt = DateTimeOffset.UtcNow;
        Event @event = await _factory.CreateEventAsync(startsAt: startsAt);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { endsAt = startsAt }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/endsAt");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenEndPrecedesStart_ForPatch()
    {
        var startsAt = DateTimeOffset.UtcNow;
        Event @event = await _factory.CreateEventAsync(startsAt: startsAt);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { endsAt = startsAt.AddDays(-1) }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/endsAt");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenAnEndIsSetOnAnEventWithNoStart_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { endsAt = DateTimeOffset.UtcNow }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/startsAt");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenClearingStartWhileEndRemainsSet_ForPatch()
    {
        var startsAt = DateTimeOffset.UtcNow;
        Event @event = await _factory.CreateEventAsync(startsAt: startsAt);
        using HttpClient adminClient = AdminClient();
        await SendAsync(adminClient, HttpMethod.Patch, $"/api/events/{@event.Id}",
            new { data = new { type = "events", id = @event.Id.ToString(), attributes = new { endsAt = startsAt.AddDays(1) } } });

        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { startsAt = (DateTimeOffset?)null }
            }
        };
        HttpResponseMessage response = await SendAsync(adminClient, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/startsAt");
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenClearingBothDates_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync(startsAt: DateTimeOffset.UtcNow);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { startsAt = (DateTimeOffset?)null, endsAt = (DateTimeOffset?)null }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        HttpResponseMessage getResponse = await SendAsync(client, HttpMethod.Get, $"/api/events/{@event.Id}");
        JsonElement attributes = await AttributesOfAsync(getResponse);
        Assert.Equal(JsonValueKind.Null, attributes.GetProperty("startsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, attributes.GetProperty("endsAt").ValueKind);
    }

    [Fact]
    public async Task PersistAsUtc_WhenCreatingAnEventWithANonUtcOffset_ForPost()
    {
        using HttpClient client = AdminClient();
        var startsAt = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.FromHours(9));
        var body = new
        {
            data = new { type = "events", attributes = new { name = $"Fall Camporee {Guid.NewGuid()}", startsAt } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/events", body);

        JsonElement attributes = await AttributesOfAsync(response);
        DateTimeOffset returned = attributes.GetProperty("startsAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, returned.Offset);
        Assert.Equal(startsAt.ToUniversalTime(), returned);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnAssignedDirectorSetsDates_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);
        var body = new
        {
            data = new
            {
                type = "events", id = @event.Id.ToString(),
                attributes = new { startsAt = DateTimeOffset.UtcNow }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/events/{@event.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient AdminClient() =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.AdminRoleClaim()]);

    private HttpClient DirectorClient(Guid eventId) =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.DirectorRoleClaim(eventId)]);

    private static async Task AssertErrorPointersAsync(HttpResponseMessage response, params string[] expectedPointers)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] actualPointers = document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("source").GetProperty("pointer").GetString()!)
            .ToArray();
        Assert.Equal(expectedPointers.Order(), actualPointers.Order());
    }

    private static async Task<JsonElement> AttributesOfAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").GetProperty("attributes").Clone();
    }

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
