using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VirtualLeadersGuide.Web.InfoPages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="ApiEventClientShould"/>'s shape - response bodies are anonymous objects with
/// already-lowercase property names, reproducing Api's actual wire shape without touching
/// <see cref="ApiInfoPageClient"/>'s <see langword="internal"/> envelope types.
/// </remarks>
public class ApiInfoPageClientShould
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    [Fact]
    public async Task SendJsonApiAcceptHeaderAndTheBearerToken_WhenSendingARequest_ForGetInfoPageAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.OK, new { data = InfoPageResource() });
        });
        ApiInfoPageClient client = CreateClient(handler);

        await client.GetInfoPageAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(JsonApiMediaType, capturedRequest!.Headers.Accept.Single().MediaType);
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task ReturnTheMappedInfoPagesAndTotal_WhenApiRespondsWithOk_ForGetInfoPagesForEventAsync()
    {
        var eventId = Guid.NewGuid();
        var infoPageId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.OK, new
        {
            data = new[] { InfoPageResource(infoPageId, eventId, "Packing List", "# Bring a jacket") },
            meta = new { total = 3 }
        }));
        ApiInfoPageClient client = CreateClient(handler);

        (IReadOnlyList<InfoPageDto> infoPages, int total) =
            await client.GetInfoPagesForEventAsync(eventId, 1, 10, CancellationToken.None);

        Assert.Single(infoPages);
        Assert.Equal(infoPageId, infoPages[0].Id);
        Assert.Equal(eventId, infoPages[0].EventId);
        Assert.Equal("Packing List", infoPages[0].Title);
        Assert.Equal("# Bring a jacket", infoPages[0].MarkdownContent);
        Assert.Equal(3, total);
    }

    [Fact]
    public async Task ReturnTheInfoPagesCountAsTotal_WhenApiOmitsMeta_ForGetInfoPagesForEventAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.OK, new
        {
            data = new[] { InfoPageResource(eventId: eventId), InfoPageResource(eventId: eventId) }
        }));
        ApiInfoPageClient client = CreateClient(handler);

        (IReadOnlyList<InfoPageDto> infoPages, int total) =
            await client.GetInfoPagesForEventAsync(eventId, 1, 10, CancellationToken.None);

        Assert.Equal(2, infoPages.Count);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task IncludeTheEventIdFilterSortAndPageQueryParameters_WhenListing_ForGetInfoPagesForEventAsync()
    {
        var eventId = Guid.NewGuid();
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() });
        });
        ApiInfoPageClient client = CreateClient(handler);

        await client.GetInfoPagesForEventAsync(eventId, 2, 25, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        string decodedQuery = Uri.UnescapeDataString(capturedRequest!.RequestUri!.Query);
        Assert.Contains($"filter=equals(eventId,'{eventId}')", decodedQuery, StringComparison.Ordinal);
        Assert.Contains("sort=title", decodedQuery, StringComparison.Ordinal);
        Assert.Contains("page[number]=2", decodedQuery, StringComparison.Ordinal);
        Assert.Contains("page[size]=25", decodedQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnTheMappedInfoPage_WhenApiRespondsWithOk_ForGetInfoPageAsync()
    {
        var infoPageId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(
            _ => JsonApiResponse(HttpStatusCode.OK, new { data = InfoPageResource(infoPageId) }));
        ApiInfoPageClient client = CreateClient(handler);

        (InfoPageReadOutcome outcome, InfoPageDto? infoPage) =
            await client.GetInfoPageAsync(infoPageId, CancellationToken.None);

        Assert.Equal(InfoPageReadOutcome.Success, outcome);
        Assert.Equal(infoPageId, infoPage?.Id);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForGetInfoPageAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiInfoPageClient client = CreateClient(handler);

        (InfoPageReadOutcome outcome, InfoPageDto? infoPage) =
            await client.GetInfoPageAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(InfoPageReadOutcome.Forbidden, outcome);
        Assert.Null(infoPage);
    }

    [Fact]
    public async Task ReturnNotFound_WhenApiRespondsWithNotFound_ForGetInfoPageAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        ApiInfoPageClient client = CreateClient(handler);

        (InfoPageReadOutcome outcome, InfoPageDto? infoPage) =
            await client.GetInfoPageAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(InfoPageReadOutcome.NotFound, outcome);
        Assert.Null(infoPage);
    }

    [Fact]
    public async Task SendTheEventIdTitleAndMarkdownContentAttributes_WhenCreating_ForCreateAsync()
    {
        var eventId = Guid.NewGuid();
        string? capturedBody = null;
        string? capturedContentType = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedContentType = request.Content.Headers.ContentType?.MediaType;
            return JsonApiResponse(HttpStatusCode.Created, new { data = InfoPageResource(eventId: eventId) });
        });
        ApiInfoPageClient client = CreateClient(handler);

        await client.CreateAsync(eventId, "Packing List", "# Bring a jacket", CancellationToken.None);

        Assert.Equal(JsonApiMediaType, capturedContentType);
        Assert.NotNull(capturedBody);
        Assert.Contains($"\"eventId\":\"{eventId}\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"title\":\"Packing List\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"markdownContent\":\"# Bring a jacket\"", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnTheCreatedInfoPage_WhenApiRespondsWithCreated_ForCreateAsync()
    {
        var infoPageId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(
            HttpStatusCode.Created, new { data = InfoPageResource(infoPageId, eventId, "Packing List") }));
        ApiInfoPageClient client = CreateClient(handler);

        (InfoPageWriteOutcome outcome, InfoPageDto? infoPage, IReadOnlyList<string> pointers) =
            await client.CreateAsync(eventId, "Packing List", "content", CancellationToken.None);

        Assert.Equal(InfoPageWriteOutcome.Success, outcome);
        Assert.Equal(infoPageId, infoPage?.Id);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiInfoPageClient client = CreateClient(handler);

        (InfoPageWriteOutcome outcome, InfoPageDto? infoPage, IReadOnlyList<string> pointers) =
            await client.CreateAsync(Guid.NewGuid(), "Packing List", "content", CancellationToken.None);

        Assert.Equal(InfoPageWriteOutcome.Forbidden, outcome);
        Assert.Null(infoPage);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnInvalidWithThePointer_WhenApiRespondsWithUnprocessableEntity_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.UnprocessableEntity, new
        {
            errors = new[]
            {
                new { title = "Unknown Event.", source = new { pointer = "/data/attributes/eventId" } }
            }
        }));
        ApiInfoPageClient client = CreateClient(handler);

        (InfoPageWriteOutcome outcome, InfoPageDto? infoPage, IReadOnlyList<string> pointers) =
            await client.CreateAsync(Guid.NewGuid(), "Packing List", "content", CancellationToken.None);

        Assert.Equal(InfoPageWriteOutcome.Invalid, outcome);
        Assert.Null(infoPage);
        Assert.Equal(["/data/attributes/eventId"], pointers);
    }

    [Fact]
    public async Task SendOnlyTitleAndMarkdownContentAttributes_WhenUpdating_ForUpdateAsync()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        ApiInfoPageClient client = CreateClient(handler);

        await client.UpdateAsync(Guid.NewGuid(), "Renamed", "new content", CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"title\":\"Renamed\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"markdownContent\":\"new content\"", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("eventId", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnSuccess_WhenApiRespondsWithNoContent_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        ApiInfoPageClient client = CreateClient(handler);

        InfoPageWriteOutcome outcome =
            await client.UpdateAsync(Guid.NewGuid(), "Renamed", "content", CancellationToken.None);

        Assert.Equal(InfoPageWriteOutcome.Success, outcome);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiInfoPageClient client = CreateClient(handler);

        InfoPageWriteOutcome outcome =
            await client.UpdateAsync(Guid.NewGuid(), "Renamed", "content", CancellationToken.None);

        Assert.Equal(InfoPageWriteOutcome.Forbidden, outcome);
    }

    [Fact]
    public async Task SendADeleteRequestToTheInfoPagesIdUri_WhenDeleting_ForDeleteAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        Guid id = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        ApiInfoPageClient client = CreateClient(handler);

        await client.DeleteAsync(id, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Delete, capturedRequest.Method);
        Assert.EndsWith($"/api/infoPages/{id}", capturedRequest.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnSuccess_WhenApiRespondsWithNoContent_ForDeleteAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        ApiInfoPageClient client = CreateClient(handler);

        InfoPageWriteOutcome outcome = await client.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(InfoPageWriteOutcome.Success, outcome);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForDeleteAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiInfoPageClient client = CreateClient(handler);

        InfoPageWriteOutcome outcome = await client.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(InfoPageWriteOutcome.Forbidden, outcome);
    }

    [Fact]
    public async Task ReturnNotFound_WhenApiRespondsWithNotFound_ForDeleteAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        ApiInfoPageClient client = CreateClient(handler);

        InfoPageWriteOutcome outcome = await client.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(InfoPageWriteOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task ThrowInfoPageDataUnavailableException_WhenTheHttpCallFails_ForDeleteAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiInfoPageClient client = CreateClient(handler);

        await Assert.ThrowsAsync<InfoPageDataUnavailableException>(
            () => client.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ThrowInfoPageDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForGetInfoPagesForEventAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiInfoPageClient client = CreateClient(handler);

        await Assert.ThrowsAsync<InfoPageDataUnavailableException>(
            () => client.GetInfoPagesForEventAsync(Guid.NewGuid(), 1, 10, CancellationToken.None));
    }

    private static ApiInfoPageClient CreateClient(HttpMessageHandler apiHandler) =>
        ApiClientTestFactory.CreateInfoPageClient(apiHandler);

    private static object InfoPageResource(
        Guid? id = null, Guid? eventId = null, string title = "Packing List", string markdownContent = "content") =>
        new
        {
            type = "infoPages",
            id = (id ?? Guid.NewGuid()).ToString(),
            attributes = new { eventId = eventId ?? Guid.NewGuid(), title, markdownContent }
        };

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        return response;
    }
}
