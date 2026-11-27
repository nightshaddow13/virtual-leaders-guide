using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Coverage for <c>/api/infoPagePlacements</c> (P5-24, #208): scoping, Tier resolve-or-create, ADR-0047's
/// exclusive screen, the atomic move, and Tier reaping on move, removal, InfoPage delete and Event delete.
/// </remarks>
public class InfoPagePlacementsResourceShould : IAsyncLifetime
{
    private const string JsonApiMediaType = "application/vnd.api+json";
    private const string Path = "/api/infoPagePlacements";

    private ApiWebApplicationFactory _factory = null!;
    private Event _event = null!;
    private InfoPage _page = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
        _event = await _factory.CreateEventAsync();
        _page = await _factory.CreateInfoPageAsync(_event.Id);
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task CreateTheTabAndSubTab_WhenBothNamesAreNew_ForPost()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabName = "Logistics", subTabName = "Arrival" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await WithDbAsync(async db =>
        {
            InfoPagePlacement placement = await db.InfoPagePlacements.AsNoTracking().SingleAsync();
            Assert.Equal(_event.Id, placement.EventId);
            Assert.Equal("Logistics", (await db.Tabs.SingleAsync(t => t.Id == placement.TabId)).Name);
            Assert.Equal("Arrival", (await db.SubTabs.SingleAsync(s => s.Id == placement.SubTabId)).Name);
        });
    }

    [Fact]
    public async Task ReuseTheExistingTab_WhenTheNameMatchesCaseInsensitively_ForPost()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id, "Logistics");
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabName = " LOGISTICS " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await WithDbAsync(async db => Assert.Equal(tab.Id, (await db.Tabs.AsNoTracking().SingleAsync()).Id));
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenNoTabIsSupplied_ForPost()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { subTabName = "Arrival" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenTheTabBelongsToAnotherEvent_ForPost()
    {
        Event other = await _factory.CreateEventAsync();
        Tab foreign = await _factory.CreateTabAsync(other.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabId = foreign.Id });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenTheSubTabIsNotUnderTheTab_ForPost()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id);
        Tab other = await _factory.CreateTabAsync(_event.Id);
        SubTab elsewhere = await _factory.CreateSubTabAsync(_event.Id, other.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabId = tab.Id, subTabId = elsewhere.Id });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithAlreadyPlaced_WhenTheInfoPageHasAPlacement_ForPost()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id);
        await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabName = "Elsewhere" });

        await AssertConflictAsync(response, PlacementErrors.InfoPageAlreadyPlaced);
        await WithDbAsync(async db => Assert.Single(await db.Tabs.ToListAsync()));
    }

    [Fact]
    public async Task RejectWithSlotHasActivities_WhenActivitiesAreOnThatScreen_ForPost()
    {
        Activity activity = await _factory.CreateActivityAsync(_event.Id);
        Tab tab = await _factory.CreateTabAsync(_event.Id, "Morning");
        Section section = await _factory.CreateSectionUnderTabAsync(_event.Id, tab.Id);
        await _factory.CreateActivityPlacementAsync(_event.Id, activity.Id, tab.Id, sectionId: section.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabName = "Morning" });

        await AssertConflictAsync(response, PlacementErrors.SlotHasActivities);
    }

    [Fact]
    public async Task RejectWithSlotHasOtherInfoPage_WhenAnotherInfoPageFillsThatScreen_ForPost()
    {
        InfoPage other = await _factory.CreateInfoPageAsync(_event.Id);
        Tab tab = await _factory.CreateTabAsync(_event.Id, "Logistics");
        await _factory.CreateInfoPagePlacementAsync(_event.Id, other.Id, tab.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabName = "Logistics" });

        await AssertConflictAsync(response, PlacementErrors.SlotHasOtherInfoPage);
    }

    [Fact]
    public async Task Succeed_WhenActivitiesAreOnlyUnderASubTabOfTheBareTab_ForPost()
    {
        Activity activity = await _factory.CreateActivityAsync(_event.Id);
        Tab tab = await _factory.CreateTabAsync(_event.Id, "Logistics");
        SubTab subTab = await _factory.CreateSubTabAsync(_event.Id, tab.Id);
        await _factory.CreateActivityPlacementAsync(_event.Id, activity.Id, tab.Id, subTab.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabName = "Logistics" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbiddenAndCreateNothing_WhenAnUnassignedDirectorPlaces_ForPost()
    {
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabName = "Logistics" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await WithDbAsync(async db => Assert.False(await db.Tabs.AnyAsync()));
    }

    [Fact]
    public async Task Succeed_WhenAnAssignedDirectorPlaces_ForPost()
    {
        using HttpClient client = DirectorClient(_event.Id);

        HttpResponseMessage response = await PlaceAsync(client, _page.Id, new { tabName = "Logistics" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ReturnNothing_WhenAnUnassignedDirectorLists_ForGet()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id);
        await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, Path);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, document.RootElement.GetProperty("data").GetArrayLength());
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task RejectWithForbidden_WhenAnUnassignedDirectorWrites_ForPatchAndDelete(string method)
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id);
        InfoPagePlacement placement = await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id);
        using HttpClient client = DirectorClient(Guid.NewGuid());

        HttpResponseMessage response = await SendAsync(client, new HttpMethod(method), $"{Path}/{placement.Id}",
            method == "PATCH" ? MoveBody(placement.Id, new { tabName = "Elsewhere" }) : null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await WithDbAsync(async db => Assert.Equal(tab.Id, (await db.InfoPagePlacements.AsNoTracking().SingleAsync()).TabId));
    }

    [Fact]
    public async Task ReapTheOldSubTabButKeepTheTab_WhenMovedToAnotherSubTabOfTheSameTab_ForPatch()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id, "Logistics");
        SubTab arrival = await _factory.CreateSubTabAsync(_event.Id, tab.Id, "Arrival");
        InfoPagePlacement placement = await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id, arrival.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"{Path}/{placement.Id}",
            MoveBody(placement.Id, new { tabName = "Logistics", subTabName = "Departure" }));

        Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
        await WithDbAsync(async db =>
        {
            Assert.Equal(tab.Id, (await db.Tabs.AsNoTracking().SingleAsync()).Id);
            SubTab departure = await db.SubTabs.AsNoTracking().SingleAsync();
            Assert.Equal("Departure", departure.Name);
            Assert.Equal(departure.Id, (await db.InfoPagePlacements.AsNoTracking().SingleAsync()).SubTabId);
        });
    }

    [Fact]
    public async Task ReapTheWholeOldPath_WhenMovedToANewBareTab_ForPatch()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id, "Logistics");
        SubTab arrival = await _factory.CreateSubTabAsync(_event.Id, tab.Id, "Arrival");
        InfoPagePlacement placement = await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id, arrival.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"{Path}/{placement.Id}",
            MoveBody(placement.Id, new { tabName = "About" }));

        Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
        await WithDbAsync(async db =>
        {
            Assert.Equal("About", (await db.Tabs.AsNoTracking().SingleAsync()).Name);
            Assert.False(await db.SubTabs.AnyAsync());
            Assert.Null((await db.InfoPagePlacements.AsNoTracking().SingleAsync()).SubTabId);
        });
    }

    [Fact]
    public async Task LeaveThePlacementUntouched_WhenTheNewScreenIsTaken_ForPatch()
    {
        Activity activity = await _factory.CreateActivityAsync(_event.Id);
        Tab taken = await _factory.CreateTabAsync(_event.Id, "Morning");
        await _factory.CreateActivityPlacementAsync(_event.Id, activity.Id, taken.Id);
        Tab original = await _factory.CreateTabAsync(_event.Id, "Logistics");
        InfoPagePlacement placement = await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, original.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"{Path}/{placement.Id}",
            MoveBody(placement.Id, new { tabName = "Morning" }));

        await AssertConflictAsync(response, PlacementErrors.SlotHasActivities);
        await WithDbAsync(async db =>
        {
            Assert.Equal(original.Id, (await db.InfoPagePlacements.AsNoTracking().SingleAsync()).TabId);
            Assert.True(await db.Tabs.AnyAsync(t => t.Id == original.Id));
        });
    }

    [Fact]
    public async Task ChangeNothing_WhenMovedToTheSameScreen_ForPatch()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id, "Logistics");
        InfoPagePlacement placement = await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"{Path}/{placement.Id}",
            MoveBody(placement.Id, new { tabName = "logistics" }));

        Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
        await WithDbAsync(async db => Assert.Equal(tab.Id, (await db.Tabs.AsNoTracking().SingleAsync()).Id));
    }

    [Fact]
    public async Task ReapTheEmptiedTiers_WhenRemoved_ForDelete()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(_event.Id, tab.Id);
        InfoPagePlacement placement = await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id, subTab.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"{Path}/{placement.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertNoTiersOrPlacementAsync();
    }

    [Fact]
    public async Task KeepTheTab_WhenActivitiesStillUseIt_ForDelete()
    {
        Activity activity = await _factory.CreateActivityAsync(_event.Id);
        Tab tab = await _factory.CreateTabAsync(_event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(_event.Id, tab.Id);
        await _factory.CreateActivityPlacementAsync(_event.Id, activity.Id, tab.Id);
        InfoPagePlacement placement = await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id, subTab.Id);
        using HttpClient client = AdminClient();

        await SendAsync(client, HttpMethod.Delete, $"{Path}/{placement.Id}");

        await WithDbAsync(async db =>
        {
            Assert.True(await db.Tabs.AnyAsync(t => t.Id == tab.Id));
            Assert.False(await db.SubTabs.AnyAsync());
        });
    }

    [Fact]
    public async Task RemoveThePlacementAndReapItsTiers_WhenTheInfoPageIsDeleted_ForDelete()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id);
        await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/infoPages/{_page.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertNoTiersOrPlacementAsync();
    }

    [Fact]
    public async Task Succeed_WhenTheEventWithAPlacedInfoPageIsDeleted_ForDelete()
    {
        Tab tab = await _factory.CreateTabAsync(_event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(_event.Id, tab.Id);
        await _factory.CreateInfoPagePlacementAsync(_event.Id, _page.Id, tab.Id, subTab.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/events/{_event.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertNoTiersOrPlacementAsync();
    }

    private HttpClient AdminClient() =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.AdminRoleClaim()]);

    private HttpClient DirectorClient(Guid eventId) =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.DirectorRoleClaim(eventId)]);

    private static Dictionary<string, object?> AttributesOf(object values) =>
        values.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(values));

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, Guid infoPageId, object tierAttributes)
    {
        Dictionary<string, object?> attributes = AttributesOf(tierAttributes);
        attributes["infoPageId"] = infoPageId;
        return SendAsync(client, HttpMethod.Post, Path, new { data = new { type = "infoPagePlacements", attributes } });
    }

    private static object MoveBody(Guid id, object tierAttributes) =>
        new { data = new { type = "infoPagePlacements", id = id.ToString(), attributes = AttributesOf(tierAttributes) } };

    private async Task WithDbAsync(Func<VirtualLeadersGuideDbContext, Task> assert)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        await assert(scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>());
    }

    private Task AssertNoTiersOrPlacementAsync() => WithDbAsync(async db =>
    {
        Assert.False(await db.InfoPagePlacements.AnyAsync());
        Assert.False(await db.SubTabs.AnyAsync());
        Assert.False(await db.Tabs.AnyAsync());
    });

    private static async Task AssertConflictAsync(HttpResponseMessage response, string expectedCode)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
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
