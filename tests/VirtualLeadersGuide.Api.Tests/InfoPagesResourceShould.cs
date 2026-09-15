using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Positive coverage for <c>/api/infoPages</c> (P5-16, #21): <c>InfoPageResourceDefinition</c>'s Admin/Director
/// scoping. Doesn't repeat <see cref="PageEntitiesAreNotJsonApiResourcesShould"/>'s negative case for
/// <c>pages</c>/<c>pageTypes</c>, or <c>InternalJwtAuthorizationShould</c>'s identity-forwarding policy tests
/// (this class only ever calls <see cref="ApiWebApplicationFactory.CreateUserClient"/>, never an
/// X-Internal-Key-only client) - the zero-role-claims tests below cover the authenticated-but-unauthorized
/// half of "no public read path in this phase" that those two classes don't. Grants are simulated entirely
/// via pre-formatted role claims, not real <c>UserRoles</c> rows - Api authorizes from JWT claims alone
/// (ADR-0007's amendment).
/// </remarks>
/// <remarks>
/// A Director's full CRUD access on an assigned Event (ADR-0059) is the deliberate divergence from
/// <see cref="EventsResourceShould"/>, where a Director's PATCH/DELETE always 403s regardless of assignment -
/// call that out explicitly wherever a test's name might otherwise read as a copy-paste of the Event version.
/// </remarks>
public class InfoPagesResourceShould : IAsyncLifetime
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
    public async Task SucceedWithCreated_WhenAdminCreatesAnInfoPageOnAnyEvent_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "infoPages",
                attributes = new { eventId = @event.Id, title = "Packing List", markdownContent = "# Bring a jacket" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/infoPages", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement attributes = await AttributesOfAsync(response);
        Assert.Equal("Packing List", attributes.GetProperty("title").GetString());
        Assert.Equal("# Bring a jacket", attributes.GetProperty("markdownContent").GetString());
    }

    [Fact]
    public async Task SucceedWithOk_WhenAdminReadsAnyInfoPage_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/infoPages/{infoPage.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminUpdatesAnyInfoPage_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "infoPages", id = infoPage.Id.ToString(), attributes = new { title = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/infoPages/{infoPage.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminDeletesAnyInfoPage_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/infoPages/{infoPage.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReturnEveryEventsInfoPages_WhenAdminListsInfoPages_ForGetCollection()
    {
        Event first = await _factory.CreateEventAsync();
        Event second = await _factory.CreateEventAsync();
        InfoPage onFirst = await _factory.CreateInfoPageAsync(first.Id);
        InfoPage onSecond = await _factory.CreateInfoPageAsync(second.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/infoPages");

        string[] ids = await IdsOfAsync(response);
        Assert.Contains(onFirst.Id.ToString(), ids);
        Assert.Contains(onSecond.Id.ToString(), ids);
    }

    [Fact]
    public async Task SucceedWithCreated_WhenAnAssignedDirectorCreatesAnInfoPageOnTheirEvent_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(@event.Id);
        var body = new
        {
            data = new
            {
                type = "infoPages",
                attributes = new { eventId = @event.Id, title = "FAQ", markdownContent = "Q: ... A: ..." }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/infoPages", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenAnAssignedDirectorReadsAnInfoPageOnTheirEvent_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/infoPages/{infoPage.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <remarks>
    /// The ADR-0059 divergence from <see cref="EventsResourceShould.RejectWithForbidden_WhenAnAssignedDirectorUpdatesTheirEvent_ForPatch"/> -
    /// a Director may edit their own Event's InfoPages even though they may never edit the Event itself.
    /// </remarks>
    [Fact]
    public async Task SucceedWithNoContent_WhenAnAssignedDirectorUpdatesAnInfoPageOnTheirEvent_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = DirectorClient(@event.Id);
        var body = new
        {
            data = new { type = "infoPages", id = infoPage.Id.ToString(), attributes = new { title = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/infoPages/{infoPage.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <remarks>
    /// The ADR-0059 divergence from <see cref="EventsResourceShould.RejectWithForbidden_WhenAnAssignedDirectorAttemptsToDeleteTheirEvent_ForDelete"/> -
    /// a Director's write authority on InfoPages includes delete, unlike Event details.
    /// </remarks>
    [Fact]
    public async Task SucceedWithNoContent_WhenAnAssignedDirectorDeletesAnInfoPageOnTheirEvent_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/infoPages/{infoPage.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorCreatesAnInfoPageOnAnotherEvent_ForPost()
    {
        Event other = await _factory.CreateEventAsync();
        using HttpClient client = DirectorClient(Guid.NewGuid());
        var body = new
        {
            data = new
            {
                type = "infoPages",
                attributes = new { eventId = other.Id, title = "FAQ", markdownContent = "Q: ... A: ..." }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/infoPages", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorReadsAnInfoPageOnAnotherEvent_ForGetSingle()
    {
        Event other = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(other.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/infoPages/{infoPage.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorUpdatesAnInfoPageOnAnotherEvent_ForPatch()
    {
        Event other = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(other.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());
        var body = new
        {
            data = new { type = "infoPages", id = infoPage.Id.ToString(), attributes = new { title = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/infoPages/{infoPage.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorDeletesAnInfoPageOnAnotherEvent_ForDelete()
    {
        Event other = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(other.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/infoPages/{infoPage.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnOnlyTheAssignedEventsInfoPages_WhenADirectorListsInfoPages_ForGetCollection()
    {
        Event assigned = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        InfoPage onAssigned = await _factory.CreateInfoPageAsync(assigned.Id);
        InfoPage onOther = await _factory.CreateInfoPageAsync(other.Id);
        using HttpClient client = DirectorClient(assigned.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/infoPages");

        string[] ids = await IdsOfAsync(response);
        Assert.Contains(onAssigned.Id.ToString(), ids);
        Assert.DoesNotContain(onOther.Id.ToString(), ids);
    }

    [Fact]
    public async Task ReturnAnEmptyCollection_WhenACallerWithNoRoleClaimsListsInfoPages_ForGetCollection()
    {
        Event @event = await _factory.CreateEventAsync();
        await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/infoPages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await IdsOfAsync(response));
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsReadsAnInfoPage_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/infoPages/{infoPage.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsCreatesAnInfoPage_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = _factory.CreateUserClient();
        var body = new
        {
            data = new
            {
                type = "infoPages",
                attributes = new { eventId = @event.Id, title = "FAQ", markdownContent = "Q: ... A: ..." }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/infoPages", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <remarks>
    /// Pins ADR-0055's "kept truthful by construction" invariant through the one path <c>InfoPage.Create</c>
    /// never sees - a POST over HTTP, which JsonApiDotNetCore routes through its own resource factory instead
    /// (see <c>InfoPage.Create</c>'s remarks and <c>InfoPageResourceDefinition.FillServerGeneratedDefaults</c>).
    /// Asserted against the DbContext, not the response body - <see cref="Page.PageTypeId"/> carries no
    /// <c>[Attr]</c>, so it's never in the JSON:API response to begin with.
    /// </remarks>
    [Fact]
    public async Task SetThePageTypeId_WhenAdminCreatesAnInfoPageOverHttp_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "infoPages",
                attributes = new { eventId = @event.Id, title = "About", markdownContent = "Welcome!" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/infoPages", body);
        Guid createdId = await IdOfAsync(response);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Page page = await db.Pages.AsNoTracking().SingleAsync(p => p.Id == createdId);

        Assert.Equal(PageTypeIds.InfoPage, page.PageTypeId);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenAPatchBodySetsEventId_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "infoPages", id = infoPage.Id.ToString(), attributes = new { eventId = other.Id } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/infoPages/{infoPage.Id}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenAdminCreatesAnInfoPageOnAnUnknownEvent_ForPost()
    {
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "infoPages",
                attributes = new { eventId = Guid.NewGuid(), title = "About", markdownContent = "Welcome!" }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/infoPages", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/eventId");
    }

    [Fact]
    public async Task RejectWithNotFound_WhenAdminReadsANonexistentInfoPage_ForGetSingle()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/infoPages/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenAdminDeletesANonexistentInfoPage_ForDelete()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/infoPages/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NotExposePageTypeIdOrRelationships_WhenReadingAnInfoPage_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        InfoPage infoPage = await _factory.CreateInfoPageAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/infoPages/{infoPage.Id}");

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("attributes").TryGetProperty("pageTypeId", out _));
        Assert.False(data.TryGetProperty("relationships", out _));
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

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Guid.Parse(document.RootElement.GetProperty("data").GetProperty("id").GetString()!);
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
