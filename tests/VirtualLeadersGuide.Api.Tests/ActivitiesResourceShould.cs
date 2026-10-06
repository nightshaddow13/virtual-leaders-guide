using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Positive coverage for <c>/api/activities</c> (P5-6, #87): <c>ActivityResourceDefinition</c>'s Admin/Director
/// scoping. Grants are simulated entirely via pre-formatted role claims, not real <c>UserRoles</c> rows - Api
/// authorizes from JWT claims alone (ADR-0007's amendment).
/// </remarks>
/// <remarks>
/// A Director's full CRUD access on an assigned Event (ADR-0069) is the deliberate divergence from
/// <see cref="EventsResourceShould"/>, where a Director's PATCH/DELETE always 403s regardless of assignment -
/// the same call ADR-0059 already made for <c>InfoPagesResourceShould</c>. Full GET/PATCH/DELETE coverage is
/// included here even though P5-6's own UI only exercises POST - see this story's plan's "Domain decisions"
/// section for why: <c>ActivityResourceDefinition</c> authorizes all four verbs the moment it exists.
/// </remarks>
public class ActivitiesResourceShould : IAsyncLifetime
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
    public async Task SucceedWithCreated_WhenAdminCreatesAnActivityOnAnyEvent_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "activities",
                attributes = new { eventId = @event.Id, name = "Canoe Basics", description = "Paddle strokes and the buddy system." }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/activities", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement attributes = await AttributesOfAsync(response);
        Assert.Equal("Canoe Basics", attributes.GetProperty("name").GetString());
        Assert.Equal("Paddle strokes and the buddy system.", attributes.GetProperty("description").GetString());
    }

    [Fact]
    public async Task SucceedWithOk_WhenAdminReadsAnyActivity_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/activities/{activity.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminUpdatesAnyActivity_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "activities", id = activity.Id.ToString(), attributes = new { name = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/activities/{activity.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminDeletesAnyActivity_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/activities/{activity.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReturnEveryEventsActivities_WhenAdminListsActivities_ForGetCollection()
    {
        Event first = await _factory.CreateEventAsync();
        Event second = await _factory.CreateEventAsync();
        Activity onFirst = await _factory.CreateActivityAsync(first.Id);
        Activity onSecond = await _factory.CreateActivityAsync(second.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/activities");

        string[] ids = await IdsOfAsync(response);
        Assert.Contains(onFirst.Id.ToString(), ids);
        Assert.Contains(onSecond.Id.ToString(), ids);
    }

    [Fact]
    public async Task SucceedWithCreated_WhenAnAssignedDirectorCreatesAnActivityOnTheirEvent_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);
        var body = new
        {
            data = new
            {
                type = "activities",
                attributes = new { eventId = @event.Id, name = "Archery Range", description = "Range safety first." }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/activities", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenAnAssignedDirectorReadsAnActivityOnTheirEvent_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/activities/{activity.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAnAssignedDirectorUpdatesAnActivityOnTheirEvent_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = DirectorClient(@event.Id);
        var body = new
        {
            data = new { type = "activities", id = activity.Id.ToString(), attributes = new { name = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/activities/{activity.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAnAssignedDirectorDeletesAnActivityOnTheirEvent_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/activities/{activity.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorCreatesAnActivityOnAnotherEvent_ForPost()
    {
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(Guid.NewGuid());
        var body = new
        {
            data = new
            {
                type = "activities",
                attributes = new { eventId = other.Id, name = "Archery Range", description = "Range safety first." }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/activities", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorReadsAnActivityOnAnotherEvent_ForGetSingle()
    {
        Event other = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(other.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/activities/{activity.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorUpdatesAnActivityOnAnotherEvent_ForPatch()
    {
        Event other = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(other.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());
        var body = new
        {
            data = new { type = "activities", id = activity.Id.ToString(), attributes = new { name = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/activities/{activity.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorDeletesAnActivityOnAnotherEvent_ForDelete()
    {
        Event other = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(other.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/activities/{activity.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnOnlyTheAssignedEventsActivities_WhenADirectorListsActivities_ForGetCollection()
    {
        Event assigned = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        Activity onAssigned = await _factory.CreateActivityAsync(assigned.Id);
        Activity onOther = await _factory.CreateActivityAsync(other.Id);
        using HttpClient client = DirectorClient(assigned.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/activities");

        string[] ids = await IdsOfAsync(response);
        Assert.Contains(onAssigned.Id.ToString(), ids);
        Assert.DoesNotContain(onOther.Id.ToString(), ids);
    }

    [Fact]
    public async Task ReturnAnEmptyCollection_WhenACallerWithNoRoleClaimsListsActivities_ForGetCollection()
    {
        Event @event = await _factory.CreateEventAsync();
        await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/activities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await IdsOfAsync(response));
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsReadsAnActivity_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/activities/{activity.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsCreatesAnActivity_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = _factory.CreateUserClient();
        var body = new
        {
            data = new
            {
                type = "activities",
                attributes = new { eventId = @event.Id, name = "Archery Range", description = "Range safety first." }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/activities", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenAPatchBodySetsEventId_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "activities", id = activity.Id.ToString(), attributes = new { eventId = other.Id } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/activities/{activity.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenAdminCreatesAnActivityOnAnUnknownEvent_ForPost()
    {
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "activities",
                attributes = new { eventId = Guid.NewGuid(), name = "Archery Range", description = "Range safety first." }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/activities", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/eventId");
    }

    [Fact]
    public async Task RejectWithNotFound_WhenAdminReadsANonexistentActivity_ForGetSingle()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/activities/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenAdminDeletesANonexistentActivity_ForDelete()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/activities/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private static async Task<string[]> IdsOfAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToArray();
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
