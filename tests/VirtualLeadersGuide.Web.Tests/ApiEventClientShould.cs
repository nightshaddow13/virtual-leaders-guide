using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Response bodies are built as anonymous objects with already-lowercase property names, so
/// <see cref="System.Net.Http.Json.JsonContent"/>'s default serialization reproduces Api's actual wire
/// shape without needing access to <see cref="ApiEventClient"/>'s <see langword="internal"/> envelope
/// types - the same approach <c>EventsResourceShould</c> uses for request bodies on the Api side.
/// </remarks>
public class ApiEventClientShould
{
    private const string JsonApiMediaType = "application/vnd.api+json";
    private const string SigningKey = "test-internal-jwt-signing-key-at-least-32-bytes-long";

    [Fact]
    public async Task SendJsonApiAcceptHeaderAndTheBearerToken_WhenSendingARequest_ForGetEventAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.OK, new { data = EventResource() });
        });
        ApiEventClient client = CreateClient(handler);

        await client.GetEventAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(JsonApiMediaType, capturedRequest!.Headers.Accept.Single().MediaType);
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task ReturnTheMappedEventsAndTotal_WhenApiRespondsWithOk_ForGetEventsAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.OK, new
        {
            data = new[] { EventResource(eventId, "Fall Camporee", "fall-camporee") },
            meta = new { total = 4 }
        }));
        ApiEventClient client = CreateClient(handler);

        (IReadOnlyList<EventDto> events, int total) = await client.GetEventsAsync(1, 10, null, CancellationToken.None);

        Assert.Single(events);
        Assert.Equal(eventId, events[0].Id);
        Assert.Equal("Fall Camporee", events[0].Name);
        Assert.Equal(4, total);
    }

    [Fact]
    public async Task ReturnTheEventsCountAsTotal_WhenApiOmitsMeta_ForGetEventsAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.OK, new
        {
            data = new[] { EventResource(), EventResource() }
        }));
        ApiEventClient client = CreateClient(handler);

        (IReadOnlyList<EventDto> events, int total) = await client.GetEventsAsync(1, 10, null, CancellationToken.None);

        Assert.Equal(2, events.Count);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task IncludePageAndSortQueryParameters_WhenListingEvents_ForGetEventsAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() });
        });
        ApiEventClient client = CreateClient(handler);

        await client.GetEventsAsync(2, 25, "-name", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        string query = capturedRequest!.RequestUri!.Query;
        Assert.Contains("page[number]=2", query, StringComparison.Ordinal);
        Assert.Contains("page[size]=25", query, StringComparison.Ordinal);
        Assert.Contains("sort=-name", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnTheMappedEvent_WhenApiRespondsWithOk_ForGetEventAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(
            _ => JsonApiResponse(HttpStatusCode.OK, new { data = EventResource(eventId) }));
        ApiEventClient client = CreateClient(handler);

        (EventReadOutcome outcome, EventDto? @event) = await client.GetEventAsync(eventId, CancellationToken.None);

        Assert.Equal(EventReadOutcome.Success, outcome);
        Assert.Equal(eventId, @event?.Id);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForGetEventAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiEventClient client = CreateClient(handler);

        (EventReadOutcome outcome, EventDto? @event) = await client.GetEventAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(EventReadOutcome.Forbidden, outcome);
        Assert.Null(@event);
    }

    [Fact]
    public async Task SendOnlyTheNameAttribute_WhenCreatingAnEvent_ForCreateAsync()
    {
        string? capturedBody = null;
        string? capturedContentType = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedContentType = request.Content.Headers.ContentType?.MediaType;
            return JsonApiResponse(HttpStatusCode.Created, new { data = EventResource() });
        });
        ApiEventClient client = CreateClient(handler);

        await client.CreateAsync("Fall Camporee", CancellationToken.None);

        Assert.Equal(JsonApiMediaType, capturedContentType);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"name\":\"Fall Camporee\"", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("slug", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("passcode", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnTheCreatedEvent_WhenApiRespondsWithCreated_ForCreateAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(
            HttpStatusCode.Created, new { data = EventResource(eventId, "Fall Camporee", "fall-camporee") }));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, EventDto? @event, IReadOnlyList<string> pointers) =
            await client.CreateAsync("Fall Camporee", CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Success, outcome);
        Assert.Equal(eventId, @event?.Id);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, EventDto? @event, IReadOnlyList<string> pointers) =
            await client.CreateAsync("Fall Camporee", CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Forbidden, outcome);
        Assert.Null(@event);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnBothConflictPointers_WhenApiRespondsWithConflict_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.Conflict, new
        {
            errors = new[]
            {
                new { title = "Resource conflict.", source = new { pointer = "/data/attributes/name" } },
                new { title = "Resource conflict.", source = new { pointer = "/data/attributes/slug" } }
            }
        }));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, EventDto? @event, IReadOnlyList<string> pointers) =
            await client.CreateAsync("Fall Camporee", CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Conflict, outcome);
        Assert.Null(@event);
        Assert.Equal(
            new[] { "/data/attributes/name", "/data/attributes/slug" }.Order(),
            pointers.Order());
    }

    [Fact]
    public async Task SendOnlyProvidedAttributes_WhenUpdatingAnEventWithSomeFieldsOmitted_ForUpdateAsync()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        ApiEventClient client = CreateClient(handler);

        await client.UpdateAsync(Guid.NewGuid(), name: null, slug: "fall-camporee", passcode: null, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"slug\":\"fall-camporee\"", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("name", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("passcode", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnSuccess_WhenApiRespondsWithNoContent_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, IReadOnlyList<string> pointers) = await client.UpdateAsync(
            Guid.NewGuid(), "Renamed", null, null, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Success, outcome);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, IReadOnlyList<string> pointers) = await client.UpdateAsync(
            Guid.NewGuid(), "Renamed", null, null, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Forbidden, outcome);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnTheConflictPointer_WhenApiRespondsWithConflict_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.Conflict, new
        {
            errors = new[] { new { title = "Resource conflict.", source = new { pointer = "/data/attributes/slug" } } }
        }));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, IReadOnlyList<string> pointers) = await client.UpdateAsync(
            Guid.NewGuid(), null, "taken-slug", null, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Conflict, outcome);
        Assert.Equal(["/data/attributes/slug"], pointers);
    }

    [Fact]
    public async Task ThrowEventDataUnavailableException_WhenTheHttpCallFails_ForGetEventsAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiEventClient client = CreateClient(handler);

        await Assert.ThrowsAsync<EventDataUnavailableException>(
            () => client.GetEventsAsync(1, 10, null, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowEventDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForGetEventsAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiEventClient client = CreateClient(handler);

        await Assert.ThrowsAsync<EventDataUnavailableException>(
            () => client.GetEventsAsync(1, 10, null, CancellationToken.None));
    }

    private static ApiEventClient CreateClient(HttpMessageHandler apiHandler)
    {
        var grantsClient = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
        var jwtProvider = new InternalJwtProvider(new FixedAuthenticationStateProvider("user-1"), grantsClient, Configuration());
        var apiClient = new InternalApiClient(new StubHttpClientFactory(apiHandler), jwtProvider);
        return new ApiEventClient(apiClient);
    }

    private static object EventResource(Guid? id = null, string name = "Fall Camporee", string slug = "fall-camporee") =>
        new
        {
            type = "events",
            id = (id ?? Guid.NewGuid()).ToString(),
            attributes = new { name, slug, passcode = "TigerLantern" }
        };

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        return response;
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InternalJwtDefaults.SigningKeyConfigurationKey] = SigningKey
        })
        .Build();
}
