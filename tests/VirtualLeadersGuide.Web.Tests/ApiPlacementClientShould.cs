using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="ApiActivityClientShould"/>'s shape for <see cref="ApiPlacementClient"/> (P5-11, #96).
/// Response bodies are anonymous objects with already-camelCase property names, reproducing Api's actual wire
/// shape without touching the client's <see langword="internal"/> envelope types.
/// </remarks>
public class ApiPlacementClientShould
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    [Fact]
    public async Task MapEveryCollectionIntoTheTree_WhenApiRespondsWithOk_ForGetTreeForEventAsync()
    {
        var eventId = Guid.NewGuid();
        Guid tabId = Guid.NewGuid(), subTabId = Guid.NewGuid(), sectionId = Guid.NewGuid(), subSectionId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid(), placementId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/tabs" => Collection(Resource("tabs", tabId, new { eventId, name = "Morning", sortOrder = 1 })),
            "/api/subTabs" => Collection(Resource("subTabs", subTabId, new { eventId, tabId, name = "Round Robin", sortOrder = 0 })),
            "/api/sections" => Collection(Resource("sections", sectionId, new { eventId, parentSubTabId = subTabId, name = "Waterfront", sortOrder = 2 })),
            "/api/subSections" => Collection(Resource("subSections", subSectionId, new { eventId, sectionId, name = "Canoeing", sortOrder = 3 })),
            "/api/placements" => Collection(Resource("placements", placementId, new
            {
                eventId, activityId, tabId, subTabId, sectionId, subSectionId, sortOrder = 4
            })),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        ApiPlacementClient client = CreateClient(handler);

        PlacementTreeDto tree = await client.GetTreeForEventAsync(eventId, CancellationToken.None);

        Assert.Equal("Morning", Assert.Single(tree.Tabs).Name);
        Assert.Equal(1, tree.Tabs[0].SortOrder);
        Assert.Equal(tabId, Assert.Single(tree.SubTabs).TabId);
        SectionDto section = Assert.Single(tree.Sections);
        Assert.Equal(subTabId, section.ParentSubTabId);
        Assert.Null(section.ParentTabId);
        Assert.Equal(sectionId, Assert.Single(tree.SubSections).SectionId);
        PlacementDto placement = Assert.Single(tree.Placements);
        Assert.Equal(placementId, placement.Id);
        Assert.Equal(activityId, placement.ActivityId);
        Assert.Equal(subSectionId, placement.SubSectionId);
        Assert.Equal(4, placement.SortOrder);
    }

    [Fact]
    public async Task FilterEveryRequestByEventAndAskForAWholePage_WhenLoadingTheTree_ForGetTreeForEventAsync()
    {
        var eventId = Guid.NewGuid();
        var requests = new List<Uri>();
        var handler = new StubHttpMessageHandler(request =>
        {
            lock (requests)
            {
                requests.Add(request.RequestUri!);
            }

            return Collection();
        });
        ApiPlacementClient client = CreateClient(handler);

        await client.GetTreeForEventAsync(eventId, CancellationToken.None);

        Assert.Equal(5, requests.Count);
        Assert.All(requests, uri =>
        {
            string decoded = Uri.UnescapeDataString(uri.Query);
            Assert.Contains($"filter=equals(eventId,'{eventId}')", decoded, StringComparison.Ordinal);
            Assert.Contains("page[size]=9999", decoded, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ThrowActivityDataUnavailableException_WhenAnyCollectionRespondsWithAnUnexpectedStatus_ForGetTreeForEventAsync()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath == "/api/sections"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Collection());
        ApiPlacementClient client = CreateClient(handler);

        await Assert.ThrowsAsync<ActivityDataUnavailableException>(
            () => client.GetTreeForEventAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SendTierNamesNotIdsAndReturnThePlacement_WhenApiRespondsWithCreated_ForCreateAsync()
    {
        var activityId = Guid.NewGuid();
        var tabId = Guid.NewGuid();
        string? sentBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            sentBody = request.Content!.ReadAsStringAsync().Result;
            return JsonApiResponse(HttpStatusCode.Created, new
            {
                data = Resource("placements", Guid.NewGuid(), new { eventId = Guid.NewGuid(), activityId, tabId, sortOrder = 0 })
            });
        });
        ApiPlacementClient client = CreateClient(handler);

        (PlacementWriteOutcome outcome, PlacementDto? placement, _) =
            await client.CreateAsync(activityId, "Morning", null, "Waterfront", null, CancellationToken.None);

        Assert.Equal(PlacementWriteOutcome.Success, outcome);
        Assert.Equal(tabId, placement!.TabId);
        using JsonDocument body = JsonDocument.Parse(sentBody!);
        JsonElement attributes = body.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Equal("placements", body.RootElement.GetProperty("data").GetProperty("type").GetString());
        Assert.Equal(activityId, attributes.GetProperty("activityId").GetGuid());
        Assert.Equal("Morning", attributes.GetProperty("tabName").GetString());
        Assert.Equal("Waterfront", attributes.GetProperty("sectionName").GetString());
        Assert.False(attributes.TryGetProperty("subTabName", out _));
        Assert.False(attributes.TryGetProperty("tabId", out _));
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForCreateAsync()
    {
        ApiPlacementClient client = CreateClient(StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden));

        (PlacementWriteOutcome outcome, _, _) =
            await client.CreateAsync(Guid.NewGuid(), "Morning", null, null, null, CancellationToken.None);

        Assert.Equal(PlacementWriteOutcome.Forbidden, outcome);
    }

    [Fact]
    public async Task ReturnConflict_WhenApiRespondsWithConflict_ForCreateAsync()
    {
        ApiPlacementClient client = CreateClient(StubHttpMessageHandler.RespondingWith(HttpStatusCode.Conflict));

        (PlacementWriteOutcome outcome, _, _) =
            await client.CreateAsync(Guid.NewGuid(), "Morning", null, null, null, CancellationToken.None);

        Assert.Equal(PlacementWriteOutcome.Conflict, outcome);
    }

    [Fact]
    public async Task ReturnInvalidWithThePointer_WhenApiRespondsWithUnprocessableEntity_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.UnprocessableEntity, new
        {
            errors = new[] { new { title = "Sub Section requires a Section.", source = new { pointer = "/data/attributes/subSectionId" } } }
        }));
        ApiPlacementClient client = CreateClient(handler);

        (PlacementWriteOutcome outcome, _, IReadOnlyList<string> pointers) =
            await client.CreateAsync(Guid.NewGuid(), "Morning", null, null, "Canoeing", CancellationToken.None);

        Assert.Equal(PlacementWriteOutcome.Invalid, outcome);
        Assert.Equal(["/data/attributes/subSectionId"], pointers);
    }

    [Fact]
    public async Task ThrowActivityDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForCreateAsync()
    {
        ApiPlacementClient client = CreateClient(StubHttpMessageHandler.RespondingWith(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<ActivityDataUnavailableException>(
            () => client.CreateAsync(Guid.NewGuid(), "Morning", null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowActivityDataUnavailableException_WhenTheTransportFails_ForCreateAsync()
    {
        ApiPlacementClient client = CreateClient(
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("connection refused")));

        await Assert.ThrowsAsync<ActivityDataUnavailableException>(
            () => client.CreateAsync(Guid.NewGuid(), "Morning", null, null, null, CancellationToken.None));
    }

    private static ApiPlacementClient CreateClient(HttpMessageHandler apiHandler) =>
        ApiClientTestFactory.CreatePlacementClient(apiHandler);

    private static object Resource(string type, Guid id, object attributes) =>
        new { type, id = id.ToString(), attributes };

    private static HttpResponseMessage Collection(params object[] resources) =>
        JsonApiResponse(HttpStatusCode.OK, new { data = resources, meta = new { total = resources.Length } });

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        return response;
    }
}
