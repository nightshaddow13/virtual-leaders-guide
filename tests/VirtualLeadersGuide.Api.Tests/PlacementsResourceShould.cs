using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Coverage for <c>/api/placements</c> (P5-11, #96): <c>ActivityPlacementResourceDefinition</c>'s Admin/
/// Director scoping (ADR-0069's posture extended to Placement), its resolve-or-create handling of each
/// Tier level by id or by name (ADR-0072), and its validation/uniqueness rules (ADR-0046). Grants are
/// simulated entirely via pre-formatted role claims, as in <see cref="ActivitiesResourceShould"/>.
/// </remarks>
public class PlacementsResourceShould : IAsyncLifetime
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
    public async Task SucceedWithCreatedAndCreateTheTab_WhenAdminPlacesAnActivityOnANewTabName_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabName = "Morning" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Tab tab = await db.Tabs.AsNoTracking().SingleAsync(t => t.EventId == @event.Id);
        Assert.Equal("Morning", tab.Name);
        ActivityPlacement placement = await db.ActivityPlacements.AsNoTracking().SingleAsync();
        Assert.Equal(tab.Id, placement.TabId);
        Assert.Equal(@event.Id, placement.EventId);
        Assert.Null(placement.SubTabId);
    }

    [Fact]
    public async Task ReuseTheExistingTabPreservingItsCasing_WhenTheNameMatchesCaseInsensitively_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab existing = await _factory.CreateTabAsync(@event.Id, "Morning");
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabName = "  MORNING " }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Tab tab = await db.Tabs.AsNoTracking().SingleAsync(t => t.EventId == @event.Id);
        Assert.Equal(existing.Id, tab.Id);
        Assert.Equal("Morning", tab.Name);
    }

    [Fact]
    public async Task CreateTheWholeFourLevelChain_WhenAllFourNamesAreNew_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new
            {
                tabName = "Morning", subTabName = "Round Robin", sectionName = "Waterfront", subSectionName = "Canoeing"
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        ActivityPlacement placement = await db.ActivityPlacements.AsNoTracking().SingleAsync();
        SubTab subTab = await db.SubTabs.AsNoTracking().SingleAsync();
        Section section = await db.Sections.AsNoTracking().SingleAsync();
        SubSection subSection = await db.SubSections.AsNoTracking().SingleAsync();
        Assert.Equal(subTab.Id, placement.SubTabId);
        Assert.Equal(subTab.Id, section.ParentSubTabId);
        Assert.Null(section.ParentTabId);
        Assert.Equal(section.Id, placement.SectionId);
        Assert.Equal(section.Id, subSection.SectionId);
        Assert.Equal(subSection.Id, placement.SubSectionId);
    }

    /// <remarks>The two chains skip independently (CONTEXT.md) - a Section with no Sub Tab parents to the bare Tab (ADR-0046).</remarks>
    [Fact]
    public async Task ParentTheSectionToTheBareTab_WhenNoSubTabIsSet_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabName = "Morning", sectionName = "Waterfront" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Tab tab = await db.Tabs.AsNoTracking().SingleAsync();
        Section section = await db.Sections.AsNoTracking().SingleAsync();
        Assert.Equal(tab.Id, section.ParentTabId);
        Assert.Null(section.ParentSubTabId);
    }

    [Fact]
    public async Task AcceptExistingIdsAtEveryLevel_WhenTheyBelongToTheResolvedPath_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id);
        Section section = await _factory.CreateSectionUnderSubTabAsync(@event.Id, subTab.Id);
        SubSection subSection = await _factory.CreateSubSectionAsync(@event.Id, section.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabId = tab.Id, subTabId = subTab.Id, sectionId = section.Id, subSectionId = subSection.Id }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenNeitherTabIdNorTabNameIsSupplied_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/tabId");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenASubSectionIsSetWithNoSection_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabName = "Morning", subSectionName = "Canoeing" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/subSectionId");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenTheTabIdBelongsToADifferentEvent_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab foreign = await _factory.CreateTabAsync(other.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabId = foreign.Id }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/tabId");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenTheSubTabIdIsUnderADifferentTab_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        Tab otherTab = await _factory.CreateTabAsync(@event.Id);
        SubTab foreign = await _factory.CreateSubTabAsync(@event.Id, otherTab.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabId = tab.Id, subTabId = foreign.Id }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/subTabId");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenTheSectionIdIsParentedToTheTabButThePlacementSetsASubTab_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id);
        Section underBareTab = await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabId = tab.Id, subTabId = subTab.Id, sectionId = underBareTab.Id }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/sectionId");
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenTheActivityDoesNotExist_ForPost()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(Guid.NewGuid(), new { tabName = "Morning" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/activityId");
    }

    [Fact]
    public async Task RejectWithConflict_WhenTheSameActivityIsPlacedAtTheSamePathTwice_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();
        var body = PlacementBody(activity.Id, new { tabName = "Morning", sectionName = "Waterfront" });
        await SendAsync(client, HttpMethod.Post, "/api/placements", body);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithCreated_WhenTheSameActivityIsPlacedUnderASecondTab_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();
        await SendAsync(client, HttpMethod.Post, "/api/placements", PlacementBody(activity.Id, new { tabName = "Morning" }));

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabName = "Afternoon" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AssignAscendingSortOrder_WhenASecondActivityIsPlacedInTheSameBucket_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity first = await _factory.CreateActivityAsync(@event.Id);
        Activity second = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = AdminClient();
        await SendAsync(client, HttpMethod.Post, "/api/placements", PlacementBody(first.Id, new { tabName = "Morning" }));

        await SendAsync(client, HttpMethod.Post, "/api/placements", PlacementBody(second.Id, new { tabName = "Morning" }));

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Assert.Equal(0, (await db.ActivityPlacements.AsNoTracking().SingleAsync(p => p.ActivityId == first.Id)).SortOrder);
        Assert.Equal(1, (await db.ActivityPlacements.AsNoTracking().SingleAsync(p => p.ActivityId == second.Id)).SortOrder);
    }

    [Fact]
    public async Task SucceedWithCreated_WhenAnAssignedDirectorPlacesAnActivityOnTheirEvent_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabName = "Morning" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbiddenAndCreateNothing_WhenAnUnassignedDirectorPlacesAnActivity_ForPost()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(activity.Id, new { tabName = "Morning" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Assert.False(await db.Tabs.AsNoTracking().AnyAsync());
        Assert.False(await db.ActivityPlacements.AsNoTracking().AnyAsync());
    }

    /// <remarks>A non-Admin probing a nonexistent Activity gets 403, not a distinguishing 422 (ADR-0031's posture, as in <c>ActivityResourceDefinition</c>).</remarks>
    [Fact]
    public async Task RejectWithForbidden_WhenADirectorPlacesANonexistentActivity_ForPost()
    {
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/placements",
            PlacementBody(Guid.NewGuid(), new { tabName = "Morning" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListOnlyTheAssignedEventsPlacements_WhenADirectorReadsTheCollection_ForGetCollection()
    {
        Event assigned = await _factory.CreateEventAsync();
        Event other = await _factory.CreateEventAsync();
        Activity mine = await _factory.CreateActivityAsync(assigned.Id);
        Activity theirs = await _factory.CreateActivityAsync(other.Id);
        Tab myTab = await _factory.CreateTabAsync(assigned.Id);
        Tab theirTab = await _factory.CreateTabAsync(other.Id);
        ActivityPlacement visible = await _factory.CreateActivityPlacementAsync(assigned.Id, mine.Id, myTab.Id);
        await _factory.CreateActivityPlacementAsync(other.Id, theirs.Id, theirTab.Id);
        using HttpClient client = DirectorClient(assigned.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/placements");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([visible.Id.ToString()], await IdsOfAsync(response));
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorReadsAPlacement_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        ActivityPlacement placement = await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/placements/{placement.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContentAndPersistTheNewSortOrder_WhenAnAssignedDirectorPatchesSortOrder_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        ActivityPlacement placement = await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);
        using HttpClient client = DirectorClient(@event.Id);
        var body = new { data = new { type = "placements", id = placement.Id.ToString(), attributes = new { sortOrder = 5 } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/placements/{placement.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Assert.Equal(5, (await db.ActivityPlacements.AsNoTracking().SingleAsync(p => p.Id == placement.Id)).SortOrder);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorPatchesAPlacement_ForPatch()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        ActivityPlacement placement = await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());
        var body = new { data = new { type = "placements", id = placement.Id.ToString(), attributes = new { sortOrder = 5 } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/placements/{placement.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAnAssignedDirectorDeletesAPlacement_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        ActivityPlacement placement = await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);
        using HttpClient client = DirectorClient(@event.Id);

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/placements/{placement.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorDeletesAPlacement_ForDelete()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        ActivityPlacement placement = await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/placements/{placement.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenAnAssignedDirectorReadsTheirEventsTiers_ForGetCollection()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id, "Morning");
        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id);
        Section section = await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id);
        SubSection subSection = await _factory.CreateSubSectionAsync(@event.Id, section.Id);
        using HttpClient client = DirectorClient(@event.Id);

        Assert.Equal([tab.Id.ToString()], await IdsOfAsync(await SendAsync(client, HttpMethod.Get, "/api/tabs")));
        Assert.Equal([subTab.Id.ToString()], await IdsOfAsync(await SendAsync(client, HttpMethod.Get, "/api/subTabs")));
        Assert.Equal([section.Id.ToString()], await IdsOfAsync(await SendAsync(client, HttpMethod.Get, "/api/sections")));
        Assert.Equal([subSection.Id.ToString()], await IdsOfAsync(await SendAsync(client, HttpMethod.Get, "/api/subSections")));
    }

    [Fact]
    public async Task ListNoTiers_WhenAnUnassignedDirectorReadsTheTabCollection_ForGetCollection()
    {
        Event @event = await _factory.CreateEventAsync();
        await _factory.CreateTabAsync(@event.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/tabs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await IdsOfAsync(response));
    }

    [Fact]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorReadsATabDirectly_ForGetSingle()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/tabs/{tab.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <remarks>ADR-0072: Tier resources are read-only - JsonApiDotNetCore answers a disabled endpoint with 403, even for an Admin.</remarks>
    [Theory]
    [InlineData("tabs")]
    [InlineData("subTabs")]
    [InlineData("sections")]
    [InlineData("subSections")]
    public async Task RejectWithForbidden_WhenAnAdminPostsToATierResource_ForPost(string resource)
    {
        using HttpClient client = AdminClient();
        var body = new { data = new { type = resource, attributes = new { name = "Anything" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, $"/api/{resource}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient AdminClient() =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.AdminRoleClaim()]);

    private HttpClient DirectorClient(Guid eventId) =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.DirectorRoleClaim(eventId)]);

    private static object PlacementBody(Guid activityId, object tierAttributes)
    {
        var attributes = new Dictionary<string, object?> { ["activityId"] = activityId };
        foreach (var property in tierAttributes.GetType().GetProperties())
        {
            attributes[property.Name] = property.GetValue(tierAttributes);
        }

        return new { data = new { type = "placements", attributes } };
    }

    private static async Task AssertErrorPointersAsync(HttpResponseMessage response, params string[] expectedPointers)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] actualPointers = document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("source").GetProperty("pointer").GetString()!)
            .ToArray();
        Assert.Equal(expectedPointers.Order(), actualPointers.Order());
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
